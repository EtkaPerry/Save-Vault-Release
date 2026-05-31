using NLua;
using SaveVaultApp.Models;
using SaveVaultApp.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveVaultApp.Services;

public class LuaEngine
{
    private static readonly Lazy<LuaEngine> _instance = new(() => new LuaEngine());
    public static LuaEngine Instance => _instance.Value;

    private Lua? _lua;
    private readonly Dictionary<string, Extension> _loadedExtensions = new();

    // Theme resource keys that extensions have overridden at the Application level.
    // Tracked so we can revert them cleanly when switching themes (no restart needed).
    private readonly HashSet<string> _themeOverrideKeys = new();

    private LuaEngine()
    {
        InitializeLua();
    }

    private void InitializeLua()
    {
        try
        {
            _lua = new Lua();
            _lua.State.Encoding = System.Text.Encoding.UTF8;
            
            // Register safe API functions for extensions
            RegisterApiMethods();
            
            LoggingService.Instance.Info("Lua engine initialized successfully");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to initialize Lua engine: {ex.Message}");
        }
    }

    private void RegisterApiMethods()
    {
        if (_lua == null) return;

        // Register logging functions
        _lua.RegisterFunction("logInfo", this, typeof(LuaEngine).GetMethod(nameof(LogInfo)));
        _lua.RegisterFunction("logError", this, typeof(LuaEngine).GetMethod(nameof(LogError)));
        _lua.RegisterFunction("logWarning", this, typeof(LuaEngine).GetMethod(nameof(LogWarning)));
          // Register settings functions
        _lua.RegisterFunction("getSetting", this, typeof(LuaEngine).GetMethod(nameof(GetSetting)));
        _lua.RegisterFunction("setSetting", this, typeof(LuaEngine).GetMethod(nameof(SetSetting)));
        
        // Register file system functions (sandboxed)
        _lua.RegisterFunction("readExtensionFile", this, typeof(LuaEngine).GetMethod(nameof(ReadExtensionFile)));
        _lua.RegisterFunction("writeExtensionFile", this, typeof(LuaEngine).GetMethod(nameof(WriteExtensionFile)));

        // Register UI functions
        _lua.RegisterFunction("addMenuItem", this, typeof(LuaEngine).GetMethod(nameof(AddMenuItem)));
        _lua.RegisterFunction("addButton", this, typeof(LuaEngine).GetMethod(nameof(AddButton)));
        _lua.RegisterFunction("createWindow", this, typeof(LuaEngine).GetMethod(nameof(CreateWindow)));
        _lua.RegisterFunction("addLabel", this, typeof(LuaEngine).GetMethod(nameof(AddLabel)));
        _lua.RegisterFunction("addWindowButton", this, typeof(LuaEngine).GetMethod(nameof(AddWindowButton)));
        _lua.RegisterFunction("addTextBox", this, typeof(LuaEngine).GetMethod(nameof(AddTextBox)));
        _lua.RegisterFunction("getControlValue", this, typeof(LuaEngine).GetMethod(nameof(GetControlValue)));        // Register translation functions
        _lua.RegisterFunction("addTranslation", this, typeof(LuaEngine).GetMethod(nameof(AddTranslation)));
        _lua.RegisterFunction("addTranslationDefault", this, typeof(LuaEngine).GetMethod(nameof(AddTranslationWithDefaultLanguage)));
        _lua.RegisterFunction("getTranslation", this, typeof(LuaEngine).GetMethod(nameof(GetTranslation)));
        _lua.RegisterFunction("getCurrentLanguage", this, typeof(LuaEngine).GetMethod(nameof(GetCurrentLanguage)));
        
        // Register UI text replacement function
        _lua.RegisterFunction("replaceUIText", this, typeof(LuaEngine).GetMethod(nameof(ReplaceUIText)));
        _lua.RegisterFunction("clearUITextReplacements", this, typeof(LuaEngine).GetMethod(nameof(ClearUITextReplacements)));
        
        // Register language management functions
        _lua.RegisterFunction("registerLanguage", this, typeof(LuaEngine).GetMethod(nameof(RegisterLanguage)));
        _lua.RegisterFunction("unregisterLanguage", this, typeof(LuaEngine).GetMethod(nameof(UnregisterLanguage)));
        _lua.RegisterFunction("getAvailableLanguages", this, typeof(LuaEngine).GetMethod(nameof(GetAvailableLanguages)));
        
        // Register extension language context function
        _lua.RegisterFunction("setExtensionLanguage", this, typeof(LuaEngine).GetMethod(nameof(SetExtensionLanguage)));
        
        // Register event functions
        _lua.RegisterFunction("subscribeToEvent", this, typeof(LuaEngine).GetMethod(nameof(SubscribeToEvent)));
        _lua.RegisterFunction("triggerEvent", this, typeof(LuaEngine).GetMethod(nameof(TriggerEvent)));
        _lua.RegisterFunction("unsubscribeFromEvent", this, typeof(LuaEngine).GetMethod(nameof(UnsubscribeFromEvent)));

        // Register system functions
        _lua.RegisterFunction("httpRequest", this, typeof(LuaEngine).GetMethod(nameof(HttpRequest)));
        _lua.RegisterFunction("showNotification", this, typeof(LuaEngine).GetMethod(nameof(ShowNotification)));
        _lua.RegisterFunction("showDialog", this, typeof(LuaEngine).GetMethod(nameof(ShowDialog)));
        _lua.RegisterFunction("copyToClipboard", this, typeof(LuaEngine).GetMethod(nameof(CopyToClipboard)));
        _lua.RegisterFunction("openUrl", this, typeof(LuaEngine).GetMethod(nameof(OpenUrl)));

        // Register theming functions
        _lua.RegisterFunction("setThemeResource", this, typeof(LuaEngine).GetMethod(nameof(SetThemeResource)));
    }    public bool LoadExtension(Extension extension, string scriptContent)
    {
        if (_lua == null)
        {
            LoggingService.Instance.Error("Lua engine not initialized");
            return false;
        }

        try
        {
            LoggingService.Instance.Info($"Loading extension '{extension.Name}' (ID: {extension.Id})");
            
            // Set extension context
            _lua["currentExtensionId"] = extension.Id;
            _lua["currentExtensionName"] = extension.Name;
            _lua["currentExtensionVersion"] = extension.Version;
            
            // Execute the extension script
            LoggingService.Instance.Info($"Executing Lua script for extension '{extension.Name}'");
            _lua.DoString(scriptContent);
            
            // Call initialization function if it exists
            var initFunction = _lua["onLoad"];
            if (initFunction is LuaFunction initFunc)
            {
                LoggingService.Instance.Info($"Calling onLoad() for extension '{extension.Name}'");
                initFunc.Call();
                LoggingService.Instance.Info($"onLoad() completed for extension '{extension.Name}'");
            }
            else
            {
                LoggingService.Instance.Warning($"No onLoad() function found for extension '{extension.Name}'");
            }
            
            _loadedExtensions[extension.Id] = extension;
            LoggingService.Instance.Info($"Extension '{extension.Name}' loaded and registered successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load extension '{extension.Name}': {ex.Message}");
            LoggingService.Instance.Error($"Script content preview: {scriptContent.Substring(0, Math.Min(200, scriptContent.Length))}...");
            return false;
        }
    }    public void UnloadExtension(string extensionId)
    {
        if (_lua == null)
        {
            LoggingService.Instance.Warning($"Cannot unload extension '{extensionId}': Lua engine not initialized");
            return;
        }

        if (!_loadedExtensions.ContainsKey(extensionId))
        {
            LoggingService.Instance.Warning($"Extension '{extensionId}' is not currently loaded");
            return;
        }

        try
        {
            var extension = _loadedExtensions[extensionId];
            LoggingService.Instance.Info($"Unloading extension '{extension.Name}' (ID: {extensionId})");
            
            // Set extension context
            _lua["currentExtensionId"] = extensionId;
            _lua["currentExtensionName"] = extension.Name;
            
            // Call unload function if it exists
            var unloadFunction = _lua["onUnload"];
            if (unloadFunction is LuaFunction unloadFunc)
            {
                LoggingService.Instance.Info($"Calling onUnload() for extension '{extension.Name}'");
                unloadFunc.Call();
                LoggingService.Instance.Info($"onUnload() completed for extension '{extension.Name}'");
            }
            else
            {
                LoggingService.Instance.Info($"No onUnload() function found for extension '{extension.Name}'");
            }

            // If this was a theme extension, revert any theme overrides it applied
            if (extension.Category == ExtensionCategory.Theming)
            {
                ClearThemeOverrides();
            }

            // Clean up UI elements
            ExtensionUIService.Instance.RemoveExtensionUI(extensionId);
            
            // Clean up event subscriptions
            ExtensionEventService.Instance.RemoveExtensionSubscriptions(extensionId);
            
            // Clean up translations
            ExtensionTranslationService.Instance.RemoveExtensionTranslations(extensionId);
            
            _loadedExtensions.Remove(extensionId);
            LoggingService.Instance.Info($"Extension '{extension.Name}' unloaded successfully");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to unload extension '{extensionId}': {ex.Message}");
        }
    }

    // API Methods exposed to Lua scripts

    public void LogInfo(string message)
    {
        if (_lua?["currentExtensionName"] is string extensionName)
        {
            LoggingService.Instance.Info($"[{extensionName}] {message}");
        }
    }

    public void LogError(string message)
    {
        if (_lua?["currentExtensionName"] is string extensionName)
        {
            LoggingService.Instance.Error($"[{extensionName}] {message}");
        }
    }

    public void LogWarning(string message)
    {
        if (_lua?["currentExtensionName"] is string extensionName)
        {
            LoggingService.Instance.Warning($"[{extensionName}] {message}");
        }
    }

    public string? GetSetting(string key)
    {
        try
        {
            var settings = Settings.Load();
            return settings.GetExtensionSetting(key);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get setting '{key}': {ex.Message}");
            return null;
        }
    }

    public void SetSetting(string key, string value)
    {
        try
        {
            var settings = Settings.Load();
            settings.SetExtensionSetting(key, value);
            settings.Save();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to set setting '{key}': {ex.Message}");
        }
    }

    public string? ReadExtensionFile(string fileName)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return null;

        try
        {
            var extensionPath = GetExtensionPath(extensionId);
            var filePath = Path.Combine(extensionPath, fileName);
            
            // Security check: ensure file is within extension directory
            if (!filePath.StartsWith(extensionPath))
            {
                LoggingService.Instance.Warning($"Extension '{extensionId}' attempted to access file outside its directory: {fileName}");
                return null;
            }

            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to read extension file '{fileName}': {ex.Message}");
            return null;
        }
    }

    public bool WriteExtensionFile(string fileName, string content)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        try
        {
            var extensionPath = GetExtensionPath(extensionId);
            var filePath = Path.Combine(extensionPath, fileName);
            
            // Security check: ensure file is within extension directory
            if (!filePath.StartsWith(extensionPath))
            {
                LoggingService.Instance.Warning($"Extension '{extensionId}' attempted to write file outside its directory: {fileName}");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to write extension file '{fileName}': {ex.Message}");
            return false;
        }
    }

    private static string GetExtensionPath(string extensionId)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SaveVault",
            "Extensions",
            extensionId);
        
        Directory.CreateDirectory(appDataPath);
        return appDataPath;
    }

    // UI API Methods

    public bool AddMenuItem(string menuName, string itemText, string? tooltip = null)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddMenuItem(extensionId, menuName, itemText, tooltip);
    }

    public bool AddButton(string location, string buttonText, string? tooltip = null)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddButton(extensionId, location, buttonText, tooltip);
    }

    public bool CreateWindow(string windowTitle, double width = 400, double height = 300)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.CreateWindow(extensionId, windowTitle, (int)width, (int)height);
    }

    public bool AddLabel(string windowTitle, string text)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddLabel(extensionId, windowTitle, text);
    }

    public bool AddWindowButton(string windowTitle, string text, string callbackFunction)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddWindowButton(extensionId, windowTitle, text, callbackFunction);
    }

    public bool AddTextBox(string windowTitle, string name, string placeholder = "")
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddTextBox(extensionId, windowTitle, name, placeholder);
    }

    public string GetControlValue(string windowTitle, string controlName)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return "";

        return ExtensionUIService.Instance.GetControlValue(extensionId, windowTitle, controlName);
    }    // Translation API Methods
    public bool AddTranslation(string language, string key, string value)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionTranslationService.Instance.AddTranslation(extensionId, language, key, value);
    }

    /// <summary>
    /// Add a translation using the extension's default language
    /// </summary>
    public bool AddTranslationWithDefaultLanguage(string key, string value)
    {
        try
        {
            if (_lua?["currentExtensionId"] is not string extensionId)
                return false;

            // Use extension language or fall back to current language
            var targetLanguage = (_lua?["currentExtensionLanguage"] as string) ?? 
                                LanguageManager.Instance.GetCurrentLanguage();
            
            return ExtensionTranslationService.Instance.AddTranslation(extensionId, targetLanguage, key, value);        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add translation with default language: {ex.Message}");
            return false;
        }
    }

    public string GetTranslation(string key, string? fallbackValue = null)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return fallbackValue ?? key;

        return ExtensionTranslationService.Instance.GetTranslation(extensionId, key, fallbackValue);
    }

    public string GetCurrentLanguage()
    {
        return ExtensionTranslationService.Instance.GetCurrentLanguage();
    }    /// <summary>
    /// Set the default language for the current extension
    /// </summary>
    public bool SetExtensionLanguage(string languageCode)
    {
        try
        {
            if (_lua == null) return false;
            _lua["currentExtensionLanguage"] = languageCode;
            LoggingService.Instance.Info($"Set extension language to: {languageCode}");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to set extension language: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Register a UI text replacement for a specific language
    /// </summary>
    public bool ReplaceUIText(string originalText, string translatedText, string? languageCode = null)
    {
        try
        {
            // Use specified language, extension language, or fall back to current language
            var targetLanguage = languageCode ?? 
                                (_lua?["currentExtensionLanguage"] as string) ?? 
                                LanguageManager.Instance.GetCurrentLanguage();
            UITranslationService.Instance.RegisterTextReplacement(targetLanguage, originalText, translatedText);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to replace UI text: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clear all UI text replacements for a specific language (or current language if not specified)
    /// </summary>
    public bool ClearUITextReplacements(string? languageCode = null)
    {
        try
        {
            // Use specified language or fall back to current language
            var targetLanguage = languageCode ?? LanguageManager.Instance.GetCurrentLanguage();
            UITranslationService.Instance.ClearLanguageReplacements(targetLanguage);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to clear UI text replacements: {ex.Message}");
            return false;
        }
    }

    public bool RegisterLanguage(string languageCode, string displayName)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return LanguageManager.Instance.RegisterLanguage(languageCode, displayName, extensionId);
    }

    public bool UnregisterLanguage(string languageCode)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return LanguageManager.Instance.UnregisterLanguage(languageCode, extensionId);
    }

    public string GetAvailableLanguages()
    {
        try
        {
            var languages = LanguageManager.Instance.GetAvailableLanguages();
            return string.Join(";", languages.Select(l => $"{l.Code}|{l.DisplayName}|{l.ExtensionId}"));
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get available languages: {ex.Message}");
            return "en-US|English|built-in";
        }
    }

    // Event API Methods

    public bool SubscribeToEvent(string eventName, string callbackFunction)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionEventService.Instance.SubscribeToEvent(extensionId, eventName, callbackFunction);
    }

    public void TriggerEvent(string eventName, string? data = null)
    {
        ExtensionEventService.Instance.TriggerEvent(eventName, data);
    }

    public bool UnsubscribeFromEvent(string eventName)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionEventService.Instance.UnsubscribeFromEvent(extensionId, eventName);
    }

    /// <summary>
    /// Trigger a callback function in an extension
    /// </summary>
    public void TriggerExtensionCallback(string extensionId, string functionName, params string[] args)
    {
        if (_lua == null || !_loadedExtensions.ContainsKey(extensionId))
            return;

        try
        {
            // Set extension context
            _lua["currentExtensionId"] = extensionId;
            _lua["currentExtensionName"] = _loadedExtensions[extensionId].Name;

            // Get the callback function
            var callbackFunction = _lua[functionName];
            if (callbackFunction is LuaFunction callback)
            {
                // Call with arguments
                callback.Call(args);
                LoggingService.Instance.Info($"Called callback '{functionName}' for extension '{extensionId}'");
            }
            else
            {
                LoggingService.Instance.Warning($"Callback function '{functionName}' not found for extension '{extensionId}'");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to trigger callback '{functionName}' for extension '{extensionId}': {ex.Message}");
        }    }

    public void Dispose()
    {
        // Unload all extensions and clean up their resources
        foreach (var extensionId in _loadedExtensions.Keys.ToList())
        {
            UnloadExtension(extensionId);
        }
        
        _lua?.Dispose();
        _lua = null;
    }

    // System API Methods

    public void HttpRequest(string url, string method, string? body, string? headersJson, string callbackFunction)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return;

        Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient();
                
                // Add headers
                if (!string.IsNullOrEmpty(headersJson))
                {
                    try
                    {
                        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                        if (headers != null)
                        {
                            foreach (var header in headers)
                            {
                                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warning($"Failed to parse headers for extension '{extensionId}': {ex.Message}");
                    }
                }

                HttpResponseMessage response;
                var httpMethod = new HttpMethod(method.ToUpper());

                if (httpMethod == HttpMethod.Get)
                {
                    response = await client.GetAsync(url);
                }
                else if (httpMethod == HttpMethod.Post)
                {
                    var content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json");
                    response = await client.PostAsync(url, content);
                }
                else if (httpMethod == HttpMethod.Put)
                {
                    var content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json");
                    response = await client.PutAsync(url, content);
                }
                else if (httpMethod == HttpMethod.Delete)
                {
                    response = await client.DeleteAsync(url);
                }
                else
                {
                    LoggingService.Instance.Warning($"Unsupported HTTP method '{method}' for extension '{extensionId}'");
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;
                var isSuccess = response.IsSuccessStatusCode;

                // Trigger callback on main thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TriggerExtensionCallback(extensionId, callbackFunction, isSuccess.ToString().ToLower(), statusCode.ToString(), responseBody);
                });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"HTTP request failed for extension '{extensionId}': {ex.Message}");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TriggerExtensionCallback(extensionId, callbackFunction, "false", "0", ex.Message);
                });
            }
        });
    }

    public void ShowNotification(string title, string message, string type = "info")
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NotificationService.Instance.AddLocalNotification($"{title}: {message}", type);
        });
    }

    public void ShowDialog(string title, string message, string type = "info")
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await DialogHelper.ShowInfoAsync(null, title, message);
        });
    }

    public void CopyToClipboard(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            }
        });
    }

    public void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to open URL '{url}': {ex.Message}");
        }
    }

    public void SetThemeResource(string key, string colorCode)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (Avalonia.Application.Current != null)
                {
                    // Look up extension metadata to enforce safety rules
                    var extension = ExtensionService.Instance.FindInstalledExtensionById(extensionId);
                    if (extension == null)
                    {
                        LoggingService.Instance.Warning($"SetThemeResource called by unknown extension '{extensionId}', ignoring");
                        return;
                    }

                    // Allow theme-category extensions (or any official extension) to modify theme resources
                    if (extension.Category != ExtensionCategory.Theming && !extension.IsOfficial)
                    {
                        LoggingService.Instance.Warning($"Extension '{extensionId}' is not a theme extension; SetThemeResource call blocked for key '{key}'");
                        return;
                    }

                    // Prevent any theme from touching settings-specific resources to avoid breaking options UI
                    if (key.StartsWith("Settings", StringComparison.OrdinalIgnoreCase))
                    {
                        LoggingService.Instance.Warning($"Extension '{extensionId}' attempted to modify settings UI resource '{key}', blocked");
                        return;
                    }

                    if (Avalonia.Media.Color.TryParse(colorCode, out var color))
                    {
                        Avalonia.Application.Current.Resources[key] = color;
                        _themeOverrideKeys.Add(key);
                        LoggingService.Instance.Info($"Extension '{extensionId}' set theme resource '{key}' to '{colorCode}'");
                    }
                    else
                    {
                        LoggingService.Instance.Warning($"Extension '{extensionId}' tried to set invalid color '{colorCode}' for resource '{key}'");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Failed to set theme resource '{key}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Revert every theme resource an extension overrode via setThemeResource, so the
    /// active built-in ThemeDictionary (Light/Dark) takes over again. No restart needed.
    /// </summary>
    public void ClearThemeOverrides()
    {
        if (_themeOverrideKeys.Count == 0)
            return;

        void DoClear()
        {
            var app = Avalonia.Application.Current;
            if (app == null) return;

            foreach (var key in _themeOverrideKeys.ToList())
            {
                try { app.Resources.Remove(key); }
                catch (Exception ex) { LoggingService.Instance.Warning($"Failed to revert theme resource '{key}': {ex.Message}"); }
            }

            _themeOverrideKeys.Clear();
            LoggingService.Instance.Info("Reverted extension theme resource overrides");
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            DoClear();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(DoClear);
    }

    /// <summary>
    /// Apply a theme extension by (re)running its script and invoking its applyTheme() function.
    /// The script is re-executed first so that, in the shared Lua state, applyTheme() belongs
    /// to this extension before we call it.
    /// </summary>
    public bool ApplyThemeExtension(Extension extension)
    {
        if (_lua == null)
        {
            LoggingService.Instance.Error("Cannot apply theme extension: Lua engine not initialized");
            return false;
        }

        try
        {
            if (string.IsNullOrEmpty(extension.ScriptPath) || !File.Exists(extension.ScriptPath))
            {
                LoggingService.Instance.Warning($"Cannot apply theme extension '{extension.Id}': script not found");
                return false;
            }

            // Set context so setThemeResource attributes overrides to this extension
            _lua["currentExtensionId"] = extension.Id;
            _lua["currentExtensionName"] = extension.Name;
            _lua["currentExtensionVersion"] = extension.Version;

            // Re-run the script so this extension's globals (incl. applyTheme) are current
            _lua.DoString(File.ReadAllText(extension.ScriptPath));

            var applyFunction = _lua["applyTheme"];
            if (applyFunction is LuaFunction applyFunc)
            {
                applyFunc.Call();
                LoggingService.Instance.Info($"Applied theme extension '{extension.Name}'");
                return true;
            }

            LoggingService.Instance.Warning($"Theme extension '{extension.Name}' has no applyTheme() function");
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to apply theme extension '{extension.Id}': {ex.Message}");
            return false;
        }
    }
}