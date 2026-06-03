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

    // Per-extension Lua environment (its _ENV). Each extension's script runs in its
    // own table that falls through to _G for shared API functions / stdlib / the json
    // helper, so two extensions can define the same global name (onLoad, applyTheme,
    // event callbacks, state) without overwriting each other in a single shared VM.
    private readonly Dictionary<string, LuaTable> _extensionEnvironments = new();

    // Theme resource keys that extensions have overridden at the Application level.
    // Tracked so we can revert them cleanly when switching themes (no restart needed).
    private readonly HashSet<string> _themeOverrideKeys = new();

    // One HttpClient for all extension HTTP traffic (avoids per-request socket exhaustion).
    private static readonly HttpClient _sharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    // --- Execution watchdog: aborts runaway Lua (e.g. `while true do end`) so a misbehaving
    // extension can't freeze the app. Implemented with a KeraLua instruction-count hook that
    // raises a Lua error once a wall-clock deadline passes. Note: this only interrupts Lua VM
    // execution, not time spent inside a blocking host API call.
    private const int ExecutionTimeoutMs = 5000;
    private const int WatchdogInstructionInterval = 1000; // check the deadline every N VM instructions
    private readonly KeraLua.LuaHookFunction _watchdogHook;
    private System.Diagnostics.Stopwatch? _executionStopwatch;
    private int _executionLimitMs;
    private int _guardDepth;

    private LuaEngine()
    {
        _watchdogHook = OnWatchdogTick;
        InitializeLua();
    }

    private void OnWatchdogTick(IntPtr luaState, IntPtr ar)
    {
        var sw = _executionStopwatch;
        if (sw != null && sw.ElapsedMilliseconds > _executionLimitMs)
        {
            // Raise a Lua error; NLua surfaces it as an exception caught by the guarded caller.
            // The message has no '%' so it is safe to pass as the luaL_error format string.
            var message = $"Extension aborted: exceeded {_executionLimitMs} ms execution limit";
            KeraLua.Lua.FromIntPtr(luaState).Error(message, Array.Empty<object>());
        }
    }

    /// <summary>
    /// Run a piece of Lua execution (script load or callback) under the time watchdog. Nested calls
    /// share the outermost deadline. The hook is cleared when the outermost call returns.
    /// </summary>
    private void RunGuarded(Action action)
    {
        if (_lua == null) { action(); return; }

        bool top = _guardDepth == 0;
        if (top)
        {
            _executionLimitMs = ExecutionTimeoutMs;
            _executionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _lua.State.SetHook(_watchdogHook, KeraLua.LuaHookMask.Count, WatchdogInstructionInterval);
        }

        _guardDepth++;
        try
        {
            action();
        }
        finally
        {
            _guardDepth--;
            if (_guardDepth == 0)
            {
                try { _lua.State.SetHook(_watchdogHook, KeraLua.LuaHookMask.Disabled, 0); } catch { /* best effort */ }
                _executionStopwatch = null;
            }
        }
    }

    /// <summary>Create a fresh extension environment that reads through to _G but keeps writes local.</summary>
    private LuaTable? CreateExtensionEnv()
    {
        if (_lua == null) return null;
        var result = _lua.DoString("return setmetatable({}, { __index = _G })");
        return result.Length > 0 ? result[0] as LuaTable : null;
    }

    /// <summary>
    /// Compile and run <paramref name="code"/> with <paramref name="env"/> as its _ENV
    /// (Lua 5.4 <c>load(chunk, name, 't', env)</c>), so the script's globals are isolated
    /// to that environment instead of leaking into the shared _G. Call inside <see cref="RunGuarded"/>.
    /// </summary>
    private void RunScriptInEnv(string code, string chunkName, LuaTable env)
    {
        if (_lua == null) return;
        _lua["__sv_src"] = code;
        _lua["__sv_env"] = env;
        _lua["__sv_name"] = "@" + chunkName;
        try
        {
            _lua.DoString("local f, e = load(__sv_src, __sv_name, 't', __sv_env); if not f then error(e) end; f()");
        }
        finally
        {
            _lua["__sv_src"] = null;
            _lua["__sv_env"] = null;
            _lua["__sv_name"] = null;
        }
    }

    private void InitializeLua()
    {
        try
        {
            _lua = new Lua();
            _lua.State.Encoding = System.Text.Encoding.UTF8;

            // Register safe API functions for extensions
            RegisterApiMethods();

            // Provide a `json` helper so extensions can parse the JSON payloads carried by host
            // events (backup created, scan completed, ...) and build JSON for their own use.
            InjectRuntimeLibrary();

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

        // Register host-data functions (gated by the "games" / "backups" manifest permissions)
        _lua.RegisterFunction("getGames", this, typeof(LuaEngine).GetMethod(nameof(GetGames)));
        _lua.RegisterFunction("getSavePath", this, typeof(LuaEngine).GetMethod(nameof(GetSavePath)));
        _lua.RegisterFunction("getBackups", this, typeof(LuaEngine).GetMethod(nameof(GetBackups)));
        _lua.RegisterFunction("createBackupNow", this, typeof(LuaEngine).GetMethod(nameof(CreateBackupNow)));
        _lua.RegisterFunction("restoreBackup", this, typeof(LuaEngine).GetMethod(nameof(RestoreBackup)));
    }    public bool LoadExtension(Extension extension, string scriptContent)
    {
        if (_lua == null)
        {
            LoggingService.Instance.Error("Lua engine not initialized");
            return false;
        }

        var lua = _lua!; // non-null: checked above
        try
        {
            LoggingService.Instance.Info($"Loading extension '{extension.Name}' (ID: {extension.Id})");

            // Set extension context
            lua["currentExtensionId"] = extension.Id;
            lua["currentExtensionName"] = extension.Name;
            lua["currentExtensionVersion"] = extension.Version;

            // Register before running the script so permission checks made by API calls during
            // onLoad() can resolve this extension's manifest policy.
            _loadedExtensions[extension.Id] = extension;

            // Give the extension its own isolated environment so its globals do not
            // collide with other extensions sharing this Lua VM.
            var env = CreateExtensionEnv();
            if (env == null)
            {
                _loadedExtensions.Remove(extension.Id);
                LoggingService.Instance.Error($"Failed to create isolated environment for extension '{extension.Name}'");
                return false;
            }
            _extensionEnvironments[extension.Id] = env;

            RunGuarded(() =>
            {
                // Execute the extension script inside its own environment
                LoggingService.Instance.Info($"Executing Lua script for extension '{extension.Name}'");
                RunScriptInEnv(scriptContent, extension.Id, env);

                // Call initialization function if it exists (looked up in the extension's env)
                var initFunction = env["onLoad"];
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
            });

            LoggingService.Instance.Info($"Extension '{extension.Name}' loaded and registered successfully");
            return true;
        }
        catch (Exception ex)
        {
            _loadedExtensions.Remove(extension.Id); // roll back the early registration
            _extensionEnvironments.Remove(extension.Id);
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

            // Call unload function if it exists (looked up in the extension's own env)
            var unloadFunction = _extensionEnvironments.TryGetValue(extensionId, out var env)
                ? env["onUnload"]
                : null;
            if (unloadFunction is LuaFunction unloadFunc)
            {
                LoggingService.Instance.Info($"Calling onUnload() for extension '{extension.Name}'");
                RunGuarded(() => unloadFunc.Call());
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
            _extensionEnvironments.Remove(extensionId);
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
        if (DenyMissing(ExtensionPermissions.Files, "readExtensionFile"))
            return null;

        try
        {
            if (!TryResolveSandboxedPath(extensionId, fileName, out var filePath))
            {
                LoggingService.Instance.Warning($"Extension '{extensionId}' attempted to access file outside its directory: {fileName}");
                return null;
            }

            return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
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
        if (DenyMissing(ExtensionPermissions.Files, "writeExtensionFile"))
            return false;

        try
        {
            if (!TryResolveSandboxedPath(extensionId, fileName, out var filePath))
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

    /// <summary>
    /// Resolve <paramref name="fileName"/> against the extension's private directory and confirm the
    /// fully-normalized result stays inside it. This defeats "..\\.." traversal, which
    /// <see cref="Path.Combine(string, string)"/> leaves un-normalized (so a naive StartsWith check
    /// would pass). Returns false when the path escapes the sandbox.
    /// </summary>
    private static bool TryResolveSandboxedPath(string extensionId, string fileName, out string fullPath)
    {
        fullPath = "";
        var root = Path.GetFullPath(GetExtensionPath(extensionId));
        var combined = Path.GetFullPath(Path.Combine(root, fileName));

        if (!string.Equals(combined, root, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = combined;
        return true;
    }

    /// <summary>True when the current extension is allowed to use the given capability.</summary>
    private bool CurrentExtensionAllowed(string permission)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        // Prefer the already-loaded Extension (avoids re-entering the ExtensionService singleton
        // while it is still constructing during initial load); fall back to the service otherwise.
        var ext = _loadedExtensions.TryGetValue(extensionId, out var loaded)
            ? loaded
            : ExtensionService.Instance.FindInstalledExtensionById(extensionId);

        return ext != null && ExtensionService.EvaluatePermission(ext, permission);
    }

    /// <summary>Returns true (and logs) when the current extension lacks the required capability.</summary>
    private bool DenyMissing(string permission, string apiName)
    {
        if (CurrentExtensionAllowed(permission))
            return false;

        var id = _lua?["currentExtensionId"] as string ?? "?";
        LoggingService.Instance.Warning($"Extension '{id}' denied '{apiName}': missing '{permission}' permission");
        return true;
    }

    /// <summary>Only http/https URLs may be fetched by extensions (blocks file://, data:, etc.).</summary>
    private static bool IsAllowedWebUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Extensions may only open http/https/mailto links externally (no local files/exes).</summary>
    private static bool IsAllowedExternalUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);
    }

    // UI API Methods

    public bool AddMenuItem(string menuName, string itemText, string? tooltip = null)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddMenuItem(extensionId, menuName, itemText, tooltip);
    }

    public bool AddButton(string location, string buttonText, string callbackFunction, string? tooltip = null)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return false;

        return ExtensionUIService.Instance.AddButton(extensionId, location, buttonText, callbackFunction, tooltip);
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

            // Get the callback function from the extension's own environment, so a
            // callback name defined by another extension can never be invoked here.
            var callbackFunction = _extensionEnvironments.TryGetValue(extensionId, out var env)
                ? env[functionName]
                : null;
            if (callbackFunction is LuaFunction callback)
            {
                // Call with arguments under the execution watchdog
                RunGuarded(() => callback.Call(args));
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

    // Host data API Methods (games & backups) — gated by manifest permissions

    /// <summary>Returns a JSON array of installed games/apps. Requires the "games" permission.</summary>
    public string GetGames()
    {
        if (DenyMissing(ExtensionPermissions.Games, "getGames"))
            return "[]";
        return ExtensionHostService.Instance.GetGamesJson();
    }

    /// <summary>Returns the save-folder path for the named game (empty if unknown). Requires "games".</summary>
    public string GetSavePath(string gameName)
    {
        if (DenyMissing(ExtensionPermissions.Games, "getSavePath"))
            return "";
        return ExtensionHostService.Instance.GetSavePath(gameName ?? "");
    }

    /// <summary>Returns a JSON array of backups for the named game. Requires the "backups" permission.</summary>
    public string GetBackups(string gameName)
    {
        if (DenyMissing(ExtensionPermissions.Backups, "getBackups"))
            return "[]";
        return ExtensionHostService.Instance.GetBackupsJson(gameName ?? "");
    }

    /// <summary>Triggers an immediate backup of the named game. Requires the "backups" permission.</summary>
    public bool CreateBackupNow(string gameName)
    {
        if (DenyMissing(ExtensionPermissions.Backups, "createBackupNow"))
            return false;
        return ExtensionHostService.Instance.TriggerBackup(gameName ?? "");
    }

    /// <summary>Restores the named game from the given backup path. Requires the "backups" permission.</summary>
    public bool RestoreBackup(string gameName, string backupPath)
    {
        if (DenyMissing(ExtensionPermissions.Backups, "restoreBackup"))
            return false;
        return ExtensionHostService.Instance.TriggerRestore(gameName ?? "", backupPath ?? "");
    }

    /// <summary>
    /// Inject the small <c>json</c> helper (encode/decode) so extensions can work with the JSON
    /// payloads carried by host events. Pure Lua — avoids fragile C#-to-Lua table marshaling. The
    /// source deliberately uses only single-quoted strings and char codes (no double quotes or
    /// backslashes) so it embeds cleanly in a C# verbatim string.
    /// </summary>
    private void InjectRuntimeLibrary()
    {
        if (_lua == null) return;
        try
        {
            _lua.DoString(JsonLibrarySource);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to inject extension runtime library: {ex.Message}");
        }
    }

    private const string JsonLibrarySource = @"
json = (function()
  local lib = {}
  local q = string.char(34)   -- double quote
  local bs = string.char(92)  -- backslash
  local WS = string.char(9, 10, 13)

  local escape_map = {}
  escape_map[q]  = bs .. q
  escape_map[bs] = bs .. bs
  escape_map[string.char(10)] = bs .. 'n'
  escape_map[string.char(13)] = bs .. 'r'
  escape_map[string.char(9)]  = bs .. 't'

  local function escape_str(s)
    local out = (s:gsub('[%c' .. q .. bs .. ']', function(c)
      return escape_map[c] or string.format(bs .. 'u%04x', string.byte(c))
    end))
    return q .. out .. q
  end

  local function is_array(t)
    local n = 0
    for k in pairs(t) do
      if type(k) ~= 'number' then return false end
      n = n + 1
    end
    return n == #t
  end

  local encode_value
  encode_value = function(v)
    local tv = type(v)
    if v == nil then return 'null'
    elseif tv == 'string' then return escape_str(v)
    elseif tv == 'number' then return tostring(v)
    elseif tv == 'boolean' then return tostring(v)
    elseif tv == 'table' then
      local parts = {}
      if is_array(v) then
        for _, item in ipairs(v) do parts[#parts+1] = encode_value(item) end
        return '[' .. table.concat(parts, ',') .. ']'
      end
      for k, item in pairs(v) do parts[#parts+1] = escape_str(tostring(k)) .. ':' .. encode_value(item) end
      return '{' .. table.concat(parts, ',') .. '}'
    end
    return 'null'
  end

  function lib.encode(v) return encode_value(v) end

  local parse
  local function skip_ws(s, i) return s:find('[^ ' .. WS .. ']', i) or (#s + 1) end

  local function parse_str(s, i)
    local res, j = {}, i + 1
    while j <= #s do
      local c = s:sub(j, j)
      if c == q then return table.concat(res), j + 1
      elseif c == bs then
        local n = s:sub(j + 1, j + 1)
        if n == 'n' then res[#res+1] = string.char(10)
        elseif n == 't' then res[#res+1] = string.char(9)
        elseif n == 'r' then res[#res+1] = string.char(13)
        elseif n == 'b' then res[#res+1] = string.char(8)
        elseif n == 'f' then res[#res+1] = string.char(12)
        elseif n == 'u' then
          local code = tonumber(s:sub(j + 2, j + 5), 16) or 0
          if code < 0x80 then res[#res+1] = string.char(code)
          elseif code < 0x800 then res[#res+1] = string.char(0xC0 + math.floor(code / 0x40), 0x80 + (code % 0x40))
          else res[#res+1] = string.char(0xE0 + math.floor(code / 0x1000), 0x80 + (math.floor(code / 0x40) % 0x40), 0x80 + (code % 0x40)) end
          j = j + 4
        else res[#res+1] = n end
        j = j + 2
      else
        res[#res+1] = c
        j = j + 1
      end
    end
    error('unterminated string')
  end

  parse = function(s, i)
    i = skip_ws(s, i)
    local c = s:sub(i, i)
    if c == '{' then
      local obj = {}
      i = skip_ws(s, i + 1)
      if s:sub(i, i) == '}' then return obj, i + 1 end
      while true do
        local key
        key, i = parse_str(s, skip_ws(s, i))
        i = skip_ws(s, i) + 1
        local val
        val, i = parse(s, i)
        obj[key] = val
        i = skip_ws(s, i)
        local ch = s:sub(i, i)
        if ch == ',' then i = i + 1
        elseif ch == '}' then return obj, i + 1
        else error('expected , or } in object') end
      end
    elseif c == '[' then
      local arr = {}
      i = skip_ws(s, i + 1)
      if s:sub(i, i) == ']' then return arr, i + 1 end
      while true do
        local val
        val, i = parse(s, i)
        arr[#arr+1] = val
        i = skip_ws(s, i)
        local ch = s:sub(i, i)
        if ch == ',' then i = i + 1
        elseif ch == ']' then return arr, i + 1
        else error('expected , or ] in array') end
      end
    elseif c == q then
      return parse_str(s, i)
    elseif c == 't' then return true, i + 4
    elseif c == 'f' then return false, i + 5
    elseif c == 'n' then return nil, i + 4
    else
      local e = s:find('[^%-+0-9.eE]', i) or (#s + 1)
      return tonumber(s:sub(i, e - 1)), e
    end
  end

  function lib.decode(str)
    if type(str) ~= 'string' or str == '' then return nil end
    local ok, result = pcall(function() local v = parse(str, 1); return v end)
    if ok then return result end
    return nil
  end

  return lib
end)()
";

    public void Dispose()
    {
        // Unload all extensions and clean up their resources
        foreach (var extensionId in _loadedExtensions.Keys.ToList())
        {
            UnloadExtension(extensionId);
        }

        _extensionEnvironments.Clear();
        _lua?.Dispose();
        _lua = null;
    }

    // System API Methods

    public void HttpRequest(string url, string method, string? body, string? headersJson, string callbackFunction)
    {
        if (_lua?["currentExtensionId"] is not string extensionId)
            return;
        if (DenyMissing(ExtensionPermissions.Network, "httpRequest"))
            return;

        if (!IsAllowedWebUrl(url))
        {
            LoggingService.Instance.Warning($"Extension '{extensionId}' blocked httpRequest to disallowed URL: {url}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                TriggerExtensionCallback(extensionId, callbackFunction, "false", "0", "Blocked: only http/https URLs are allowed"));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod((method ?? "GET").ToUpperInvariant()), url);

                if (!string.IsNullOrEmpty(body) &&
                    (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put || request.Method == HttpMethod.Patch))
                {
                    request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                }

                if (!string.IsNullOrEmpty(headersJson))
                {
                    try
                    {
                        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                        if (headers != null)
                        {
                            foreach (var header in headers)
                            {
                                // Header may belong on the request or the content; try both.
                                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warning($"Failed to parse headers for extension '{extensionId}': {ex.Message}");
                    }
                }

                using var response = await _sharedHttpClient.SendAsync(request);
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
        if (DenyMissing(ExtensionPermissions.Clipboard, "copyToClipboard"))
            return;

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
        if (DenyMissing(ExtensionPermissions.Network, "openUrl"))
            return;

        if (!IsAllowedExternalUrl(url))
        {
            LoggingService.Instance.Warning($"Extension blocked from opening a non-web URL: {url}");
            return;
        }

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
                    var extension = _loadedExtensions.TryGetValue(extensionId, out var loadedExt)
                        ? loadedExt
                        : ExtensionService.Instance.FindInstalledExtensionById(extensionId);
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

            // Get or create this theme extension's isolated environment.
            if (!_extensionEnvironments.TryGetValue(extension.Id, out var env) || env == null)
            {
                env = CreateExtensionEnv();
                if (env == null)
                {
                    LoggingService.Instance.Error($"Failed to create environment for theme extension '{extension.Id}'");
                    return false;
                }
                _extensionEnvironments[extension.Id] = env;
            }

            // Re-run the script in its own env so this extension's applyTheme() is
            // current, under the execution watchdog.
            var scriptText = File.ReadAllText(extension.ScriptPath);
            bool applied = false;
            RunGuarded(() =>
            {
                RunScriptInEnv(scriptText, extension.Id, env);
                if (env["applyTheme"] is LuaFunction applyFunc)
                {
                    applyFunc.Call();
                    applied = true;
                }
            });

            if (applied)
            {
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