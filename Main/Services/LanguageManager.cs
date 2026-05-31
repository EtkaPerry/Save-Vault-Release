using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SaveVaultApp.Models;

namespace SaveVaultApp.Services;

/// <summary>
/// Manages available languages and language selection for the application
/// </summary>
public class LanguageManager
{
    private static readonly Lazy<LanguageManager> _instance = new(() => new LanguageManager());
    public static LanguageManager Instance => _instance.Value;

    private readonly Dictionary<string, LanguageInfo> _availableLanguages = new();
    private string _currentLanguage = "en-US";

    public event EventHandler<string>? LanguageChanged;
    public event EventHandler<LanguageInfo>? LanguageRegistered;
    public event EventHandler<LanguageInfo>? LanguageUnregistered;

    private LanguageManager()
    {
        // Initialize with English (built-in)
        RegisterLanguage("en-US", "English", "built-in", isBuiltIn: true);
        
        // Set initial language from settings or system
        try
        {
            var settings = Settings.Instance;
            if (!string.IsNullOrEmpty(settings.Language))
            {
                _currentLanguage = settings.Language;
            }
            else
            {
                // Fallback to system language if supported, otherwise English
                var systemLanguage = CultureInfo.CurrentUICulture.Name;
                if (_availableLanguages.ContainsKey(systemLanguage))
                {
                    _currentLanguage = systemLanguage;
                    settings.Language = systemLanguage;
                }
                else
                {
                    _currentLanguage = "en-US";
                    settings.Language = "en-US";
                }
            }
        }
        catch
        {
            _currentLanguage = "en-US";
        }
        
        LoggingService.Instance.Info($"LanguageManager initialized with language: {_currentLanguage}");
    }    /// <summary>
    /// Register a language (typically called by extensions)
    /// </summary>
    public bool RegisterLanguage(string languageCode, string displayName, string extensionId, bool isBuiltIn = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(languageCode) || string.IsNullOrWhiteSpace(displayName))
                return false;

            var languageInfo = new LanguageInfo
            {
                Code = languageCode,
                DisplayName = displayName,
                ExtensionId = extensionId,
                IsBuiltIn = isBuiltIn
            };

            _availableLanguages[languageCode] = languageInfo;
            LoggingService.Instance.Info($"Registered language: {displayName} ({languageCode}) from {extensionId}");
            
            // Check if this newly registered language should be activated based on saved settings
            var settings = Settings.Instance;
            if (!string.IsNullOrEmpty(settings.Language) && settings.Language == languageCode && _currentLanguage != languageCode)
            {
                LoggingService.Instance.Info($"Activating previously saved language '{languageCode}' now that it's available");
                SetCurrentLanguage(languageCode);
            }
            
            // Trigger language registered event
            LanguageRegistered?.Invoke(this, languageInfo);
            
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to register language '{languageCode}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unregister a language (typically called when extensions are disabled)
    /// </summary>
    public bool UnregisterLanguage(string languageCode, string extensionId)
    {
        try
        {
            if (_availableLanguages.TryGetValue(languageCode, out var language))
            {
                // Only allow unregistering if the extension owns this language and it's not built-in
                if (language.ExtensionId == extensionId && !language.IsBuiltIn)
                {
                    _availableLanguages.Remove(languageCode);
                    LoggingService.Instance.Info($"Unregistered language: {language.DisplayName} ({languageCode})");
                    
                    // Trigger language unregistered event
                    LanguageUnregistered?.Invoke(this, language);
                    
                    // If the current language was unregistered, fallback to English
                    if (_currentLanguage == languageCode)
                    {
                        SetCurrentLanguage("en-US");
                    }
                    
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to unregister language '{languageCode}': {ex.Message}");
            return false;
        }
    }    /// <summary>
    /// Set the current application language
    /// </summary>
    public bool SetCurrentLanguage(string languageCode)
    {
        if (!_availableLanguages.ContainsKey(languageCode))
        {
            LoggingService.Instance.Warning($"Language '{languageCode}' is not available");
            return false;
        }

        try
        {
            var previousLanguage = _currentLanguage;
            _currentLanguage = languageCode;

            LoggingService.Instance.Info($"Setting current language from '{previousLanguage}' to '{languageCode}'");

            // Update settings (this will trigger its own events but we want our events to be primary)
            var settings = Settings.Instance;
            // Temporarily store the current value to avoid triggering settings events
            var currentSettingsLang = settings.Language;
            if (currentSettingsLang != languageCode)
            {
                settings.Language = languageCode;
            }

            // Update extension translation service
            ExtensionTranslationService.Instance.SetLanguage(languageCode);

            // Trigger language change events from LanguageManager (primary source)
            LoggingService.Instance.Info($"Triggering language change events for '{languageCode}'");
            ExtensionEventService.Instance.TriggerEvent("app.language.changed", languageCode);
            LanguageChanged?.Invoke(this, languageCode);

            LoggingService.Instance.Info($"Language successfully changed from '{previousLanguage}' to '{languageCode}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to set language '{languageCode}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get the current language code
    /// </summary>
    public string GetCurrentLanguage()
    {
        return _currentLanguage;
    }

    /// <summary>
    /// Get current language display name
    /// </summary>
    public string GetCurrentLanguageDisplayName()
    {
        if (_availableLanguages.TryGetValue(_currentLanguage, out var language))
        {
            return language.DisplayName;
        }
        return "English"; // Fallback
    }

    /// <summary>
    /// Get all available languages
    /// </summary>
    public LanguageInfo[] GetAvailableLanguages()
    {
        return _availableLanguages.Values.OrderBy(l => l.DisplayName).ToArray();
    }

    /// <summary>
    /// Get available language display names for UI binding
    /// </summary>
    public string[] GetLanguageDisplayNames()
    {
        return _availableLanguages.Values
            .OrderBy(l => l.DisplayName)
            .Select(l => l.DisplayName)
            .ToArray();
    }

    /// <summary>
    /// Get language code by display name
    /// </summary>
    public string? GetLanguageCodeByDisplayName(string displayName)
    {
        var language = _availableLanguages.Values.FirstOrDefault(l => l.DisplayName == displayName);
        return language?.Code;
    }

    /// <summary>
    /// Check if a language is available
    /// </summary>
    public bool IsLanguageAvailable(string languageCode)
    {
        return _availableLanguages.ContainsKey(languageCode);
    }    /// <summary>
    /// Initialize the language manager (should be called during app startup)
    /// </summary>
    public void Initialize()
    {
        // Set the language from settings
        var settings = Settings.Instance;
        var savedLanguage = settings.Language;
        
        LoggingService.Instance.Info($"LanguageManager.Initialize: savedLanguage='{savedLanguage}', available languages: [{string.Join(", ", _availableLanguages.Keys)}]");
        
        if (!string.IsNullOrEmpty(savedLanguage) && _availableLanguages.ContainsKey(savedLanguage))
        {
            _currentLanguage = savedLanguage;
            ExtensionTranslationService.Instance.SetLanguage(savedLanguage);
            LoggingService.Instance.Info($"Restored language: {savedLanguage}");
        }
        else if (!string.IsNullOrEmpty(savedLanguage))
        {
            // Language is saved but not available yet (extension might load later)
            LoggingService.Instance.Info($"Language '{savedLanguage}' not available yet, waiting for extensions to load");
            _currentLanguage = "en-US"; // Temporarily use English
            ExtensionTranslationService.Instance.SetLanguage("en-US");
        }
        else
        {
            _currentLanguage = "en-US";
            settings.Language = "en-US";
            ExtensionTranslationService.Instance.SetLanguage("en-US");
            LoggingService.Instance.Info("Using default language: English");
        }
        
        LoggingService.Instance.Info($"LanguageManager initialized with current language: {_currentLanguage}");
    }/// <summary>
    /// Recheck the saved language preference and activate it if now available (called after extensions load)
    /// </summary>
    public void RecheckSavedLanguage()
    {
        try
        {
            var settings = Settings.Instance;
            var savedLanguage = settings.Language;
            
            LoggingService.Instance.Info($"RecheckSavedLanguage: savedLanguage='{savedLanguage}', currentLanguage='{_currentLanguage}'");
            LoggingService.Instance.Info($"Available languages: [{string.Join(", ", _availableLanguages.Keys)}]");
            
            if (!string.IsNullOrEmpty(savedLanguage) && 
                savedLanguage != _currentLanguage && 
                _availableLanguages.ContainsKey(savedLanguage))
            {
                LoggingService.Instance.Info($"Re-activating saved language '{savedLanguage}' now that extensions have loaded");
                bool success = SetCurrentLanguage(savedLanguage);
                if (success)
                {
                    LoggingService.Instance.Info($"Successfully re-activated language '{savedLanguage}'");
                }
                else
                {
                    LoggingService.Instance.Error($"Failed to re-activate language '{savedLanguage}'");
                }
            }
            else if (!string.IsNullOrEmpty(savedLanguage) && savedLanguage == _currentLanguage)
            {
                LoggingService.Instance.Info($"Language '{savedLanguage}' is already current, no change needed");
            }
            else if (string.IsNullOrEmpty(savedLanguage))
            {
                LoggingService.Instance.Info("No saved language preference found");
            }
            else if (!_availableLanguages.ContainsKey(savedLanguage))
            {
                LoggingService.Instance.Warning($"Saved language '{savedLanguage}' is still not available after extension loading");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error rechecking saved language: {ex.Message}");
        }
    }
}

/// <summary>
/// Information about an available language
/// </summary>
public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExtensionId { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;
}
