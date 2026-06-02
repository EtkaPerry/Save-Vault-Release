using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveVaultApp.Services;

/// <summary>
/// Event system for extensions to communicate with the application and each other
/// </summary>
public class ExtensionEventService
{
    private static readonly Lazy<ExtensionEventService> _instance = new(() => new ExtensionEventService());
    public static ExtensionEventService Instance => _instance.Value;

    private readonly Dictionary<string, List<ExtensionEventHandler>> _eventHandlers = new();
    private readonly Dictionary<string, List<string>> _extensionSubscriptions = new();

    // Guards against synchronous re-entrant dispatch of the same event on one thread (e.g. a
    // handler that re-triggers the event it is handling), which would otherwise recurse until the
    // stack overflows. Thread-static so it never interferes with legitimate concurrent dispatch.
    [ThreadStatic]
    private static HashSet<string>? _dispatchingOnThread;

    private ExtensionEventService()
    {
    }

    /// <summary>
    /// Subscribe an extension to an event
    /// </summary>
    public bool SubscribeToEvent(string extensionId, string eventName, string callbackFunction)
    {
        try
        {
            var handler = new ExtensionEventHandler
            {
                ExtensionId = extensionId,
                EventName = eventName,
                CallbackFunction = callbackFunction
            };

            if (!_eventHandlers.ContainsKey(eventName))
                _eventHandlers[eventName] = new List<ExtensionEventHandler>();

            _eventHandlers[eventName].Add(handler);

            // Track subscriptions per extension for cleanup
            if (!_extensionSubscriptions.ContainsKey(extensionId))
                _extensionSubscriptions[extensionId] = new List<string>();

            if (!_extensionSubscriptions[extensionId].Contains(eventName))
                _extensionSubscriptions[extensionId].Add(eventName);

            LoggingService.Instance.Info($"Extension '{extensionId}' subscribed to event '{eventName}' with callback '{callbackFunction}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to subscribe extension '{extensionId}' to event '{eventName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Trigger an event, calling all subscribed extensions
    /// </summary>
    public void TriggerEvent(string eventName, object? data = null)
    {
        var dispatching = _dispatchingOnThread ??= new HashSet<string>();
        if (!dispatching.Add(eventName))
        {
            LoggingService.Instance.Warning($"Skipping re-entrant trigger of event '{eventName}' (a handler re-triggered it)");
            return;
        }

        try
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                LoggingService.Instance.Info($"Triggering event '{eventName}' for {handlers.Count} subscribers");
                
                foreach (var handler in handlers.ToList()) // ToList to avoid modification during iteration
                {
                    try
                    {
                        // Call the Lua callback function
                        LuaEngine.Instance.TriggerExtensionCallback(
                            handler.ExtensionId, 
                            handler.CallbackFunction, 
                            eventName, 
                            data?.ToString() ?? "");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Error calling event handler for extension '{handler.ExtensionId}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to trigger event '{eventName}': {ex.Message}");
        }
        finally
        {
            dispatching.Remove(eventName);
        }
    }

    /// <summary>
    /// Unsubscribe an extension from an event
    /// </summary>
    public bool UnsubscribeFromEvent(string extensionId, string eventName)
    {
        try
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                var removed = handlers.RemoveAll(h => h.ExtensionId == extensionId);
                
                if (removed > 0)
                {
                    LoggingService.Instance.Info($"Unsubscribed extension '{extensionId}' from event '{eventName}'");
                    
                    // Update subscription tracking
                    if (_extensionSubscriptions.TryGetValue(extensionId, out var subscriptions))
                    {
                        subscriptions.Remove(eventName);
                    }
                    
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to unsubscribe extension '{extensionId}' from event '{eventName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove all event subscriptions for an extension
    /// </summary>
    public void RemoveExtensionSubscriptions(string extensionId)
    {
        try
        {
            if (_extensionSubscriptions.TryGetValue(extensionId, out var subscriptions))
            {
                foreach (var eventName in subscriptions.ToList())
                {
                    UnsubscribeFromEvent(extensionId, eventName);
                }
                
                _extensionSubscriptions.Remove(extensionId);
                LoggingService.Instance.Info($"Removed all event subscriptions for extension '{extensionId}'");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to remove event subscriptions for extension '{extensionId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Get all available events
    /// </summary>
    public string[] GetAvailableEvents()
    {
        return _eventHandlers.Keys.ToArray();
    }

    /// <summary>
    /// Get subscribers for an event
    /// </summary>
    public string[] GetEventSubscribers(string eventName)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            return handlers.Select(h => h.ExtensionId).Distinct().ToArray();
        }
        return Array.Empty<string>();
    }

    // Predefined system events that extensions can subscribe to
    public static class SystemEvents
    {
        public const string ApplicationStartup = "app.startup";
        public const string ApplicationShutdown = "app.shutdown";
        public const string ThemeChanged = "app.theme.changed";
        public const string LanguageChanged = "app.language.changed";
        public const string SettingsChanged = "app.settings.changed";
        public const string WindowOpened = "app.window.opened";
        public const string WindowClosed = "app.window.closed";
        public const string ExtensionInstalled = "extension.installed";
        public const string ExtensionUninstalled = "extension.uninstalled";
        public const string ExtensionEnabled = "extension.enabled";
        public const string ExtensionDisabled = "extension.disabled";
        public const string GameScanCompleted = "games.scan.completed";
        public const string GameAdded = "games.added";
        public const string GameRemoved = "games.removed";
        public const string SaveBackupCreated = "saves.backup.created";
        public const string SaveRestored = "saves.restored";
    }
}

public class ExtensionEventHandler
{
    public string ExtensionId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string CallbackFunction { get; set; } = "";
}