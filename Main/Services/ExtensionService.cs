using SaveVaultApp.Models;
using SaveVaultApp.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SaveVaultApp.Services;

/// <summary>
/// Response model for server API calls
/// </summary>
public class ServerApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public List<Extension>? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Canonical capability names an extension can request in its manifest <c>permissions</c> array
/// and that the host enforces at the Lua API boundary (see <see cref="ExtensionService.HasPermission"/>).
/// </summary>
public static class ExtensionPermissions
{
    public const string Network = "network";     // httpRequest, openUrl
    public const string Files = "files";         // readExtensionFile / writeExtensionFile
    public const string Clipboard = "clipboard"; // copyToClipboard
    public const string Backups = "backups";     // getBackups / createBackupNow / restoreBackup
    public const string Games = "games";         // getGames / getSavePath

    /// <summary>Capabilities that did not exist before the permissions model and therefore always
    /// require an explicit manifest declaration (even for legacy extensions).</summary>
    public static readonly string[] RequireExplicitDeclaration = { Backups, Games };
}

public class ExtensionService
{
    private static readonly Lazy<ExtensionService> _instance = new(() => new ExtensionService());
    public static ExtensionService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _extensionsPath;
    private readonly string _extensionCachePath;
    private readonly List<Extension> _availableExtensions = new();
    private readonly List<Extension> _installedExtensions = new();    private const string GITHUB_BASE_URL = "https://raw.githubusercontent.com/EtkaPerry/SaveVaultExtensions/main";
    private const string GITHUB_LOCALIZATION_URL = "https://raw.githubusercontent.com/EtkaPerry/SaveVaultExtensions/main/Localization";
    private const string GITHUB_THEMES_URL = "https://raw.githubusercontent.com/EtkaPerry/SaveVaultExtensions/main/Themes";
    private const string GITHUB_FIXES_URL = "https://raw.githubusercontent.com/EtkaPerry/SaveVaultExtensions/main/Fixes";
    private const string SERVER_API_URL = "https://vault.etka.co.uk/extensions_api.php";

    public event EventHandler<Extension>? ExtensionInstalled;
    public event EventHandler<Extension>? ExtensionUninstalled;
    public event EventHandler<Extension>? ExtensionEnabled;
    public event EventHandler<Extension>? ExtensionDisabled;    private ExtensionService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SaveVault-Extension-Manager/1.0");

        var configManager = ExtensionConfigManager.Instance;
        var userExtensionsPath = configManager.GetUserExtensionsPath();
        
        _extensionsPath = userExtensionsPath.Replace("/", "\\"); // Normalize path separators for Windows
        _extensionCachePath = Path.Combine(Path.GetDirectoryName(_extensionsPath) ?? "", "ExtensionCache");

        Directory.CreateDirectory(_extensionsPath);
        Directory.CreateDirectory(_extensionCachePath);

        // Forward extension lifecycle changes to the Lua event bus so extensions can react to each
        // other being installed/enabled/etc. (subscribe before any extensions are loaded below).
        ExtensionInstalled += (_, ext) => TriggerLifecycleEvent(ExtensionEventService.SystemEvents.ExtensionInstalled, ext);
        ExtensionUninstalled += (_, ext) => TriggerLifecycleEvent(ExtensionEventService.SystemEvents.ExtensionUninstalled, ext);
        ExtensionEnabled += (_, ext) => TriggerLifecycleEvent(ExtensionEventService.SystemEvents.ExtensionEnabled, ext);
        ExtensionDisabled += (_, ext) => TriggerLifecycleEvent(ExtensionEventService.SystemEvents.ExtensionDisabled, ext);

        // Clean up any invalid extensions in settings
        Settings.Instance.CleanupInvalidExtensions();

        // Load both built-in and user-installed extensions
        LoadBuiltInExtensions();
        LoadInstalledExtensions();
    }    /// <summary>
    /// Load all enabled extensions into the Lua engine
    /// </summary>
    public void LoadEnabledExtensions()
    {
        try
        {
            LoggingService.Instance.Info("Loading all enabled extensions...");
            
            var enabledExtensions = _installedExtensions.Where(e => e.IsEnabled).ToList();
            LoggingService.Instance.Info($"Found {enabledExtensions.Count} enabled extensions");
            
            foreach (var extension in enabledExtensions)
            {
                try
                {
                    if (!File.Exists(extension.ScriptPath))
                    {
                        LoggingService.Instance.Warning($"Script file not found for extension {extension.Id}: {extension.ScriptPath}");
                        continue;
                    }
                    
                    var scriptContent = File.ReadAllText(extension.ScriptPath);
                    LoggingService.Instance.Info($"Loading extension {extension.Id} from {extension.ScriptPath}");
                    
                    bool loadResult = LuaEngine.Instance.LoadExtension(extension, scriptContent);
                    if (loadResult)
                    {
                        LoggingService.Instance.Info($"Successfully loaded extension: {extension.Id}");
                    }
                    else
                    {
                        LoggingService.Instance.Error($"Failed to load extension: {extension.Id}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Error loading extension {extension.Id}: {ex.Message}");
                }
            }
              // After all extensions are loaded, recheck the saved language preference
            LoggingService.Instance.Info("Extensions loaded, rechecking saved language preference...");
            LanguageManager.Instance.RecheckSavedLanguage();
            
            // Force UI translation application to ensure current language is properly displayed
            LoggingService.Instance.Info("Forcing UI translation application after extension loading...");
            UITranslationService.Instance.ForceApplyTranslations();
            
            LoggingService.Instance.Info("Finished loading enabled extensions");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error in LoadEnabledExtensions: {ex.Message}");
        }
    }    public async Task<List<Extension>> GetAvailableExtensionsAsync()
    {
        try
        {
            LoggingService.Instance.Info("Fetching available extensions from server");

            var allExtensions = new List<Extension>();
            
            // Always include built-in extensions first
            allExtensions.AddRange(GetBuiltInExtensions());

            // Try to get external extensions from the server
            var serverExtensions = await GetExtensionsFromServer();
            if (serverExtensions.Count > 0)
            {
                LoggingService.Instance.Info($"Loaded {serverExtensions.Count} extensions from server");
                
                // Update installation status
                foreach (var extension in serverExtensions)
                {
                    extension.IsInstalled = Settings.Instance.IsExtensionInstalled(extension.Id);
                    extension.IsEnabled = Settings.Instance.IsExtensionEnabled(extension.Id);
                }

                // Add server extensions that aren't already in built-in list
                foreach (var serverExt in serverExtensions)
                {
                    var existing = allExtensions.FirstOrDefault(e => e.Id == serverExt.Id);
                    if (existing == null)
                    {
                        allExtensions.Add(serverExt);
                    }
                }

                _availableExtensions.Clear();
                _availableExtensions.AddRange(allExtensions);
                return allExtensions;
            }

            LoggingService.Instance.Info("Server extensions not available, falling back to GitHub");

            // Fallback to GitHub catalog
            var catalogUrl = $"{GITHUB_BASE_URL}/catalog.json";
            var response = await _httpClient.GetAsync(catalogUrl);

            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.Warning($"Failed to fetch extension catalog: {response.StatusCode}");
                return allExtensions; // Return just built-in extensions
            }

            var catalogJson = await response.Content.ReadAsStringAsync();
            var extensions = JsonSerializer.Deserialize<List<Extension>>(catalogJson) ?? new List<Extension>();

            // Update installation status
            foreach (var extension in extensions)
            {
                extension.IsInstalled = Settings.Instance.IsExtensionInstalled(extension.Id);
                extension.IsEnabled = Settings.Instance.IsExtensionEnabled(extension.Id);
            }

            // Add catalog extensions that aren't already in built-in list
            foreach (var catalogExt in extensions)
            {
                var existing = allExtensions.FirstOrDefault(e => e.Id == catalogExt.Id);
                if (existing == null)
                {
                    allExtensions.Add(catalogExt);
                }
            }

            _availableExtensions.Clear();
            _availableExtensions.AddRange(allExtensions);

            LoggingService.Instance.Info($"Loaded {allExtensions.Count} total extensions ({extensions.Count} from catalog)");
            return allExtensions;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get available extensions: {ex.Message}");
            return GetBuiltInExtensions();
        }
    }

    /// <summary>
    /// Get only remote extensions from server/GitHub without built-in ones
    /// Used for background loading to avoid duplicating built-in extensions
    /// </summary>
    public async Task<List<Extension>> GetRemoteExtensionsAsync()
    {
        try
        {
            LoggingService.Instance.Info("Fetching remote extensions from server");

            // First try to get extensions from the server
            var serverExtensions = await GetExtensionsFromServer();
            if (serverExtensions.Count > 0)
            {                LoggingService.Instance.Info($"Loaded {serverExtensions.Count} remote extensions from server");
                
                // Update installation status
                foreach (var extension in serverExtensions)
                {
                    extension.IsInstalled = Settings.Instance.IsExtensionInstalled(extension.Id);
                    extension.IsEnabled = Settings.Instance.IsExtensionEnabled(extension.Id);
                    
                    // Ensure correct URLs are set
                    SetExtensionUrls(extension);
                }

                return serverExtensions;
            }

            LoggingService.Instance.Info("Server extensions not available, falling back to GitHub");

            // Fallback to GitHub catalog
            var catalogUrl = $"{GITHUB_BASE_URL}/catalog.json";
            var response = await _httpClient.GetAsync(catalogUrl);

            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.Warning($"Failed to fetch remote extension catalog: {response.StatusCode}");
                return new List<Extension>();
            }

            var catalogJson = await response.Content.ReadAsStringAsync();
            var extensions = JsonSerializer.Deserialize<List<Extension>>(catalogJson) ?? new List<Extension>();            // Update installation status
            foreach (var extension in extensions)
            {
                extension.IsInstalled = Settings.Instance.IsExtensionInstalled(extension.Id);
                extension.IsEnabled = Settings.Instance.IsExtensionEnabled(extension.Id);
                
                // Ensure correct URLs are set for catalog extensions too
                SetExtensionUrls(extension);
            }

            LoggingService.Instance.Info($"Loaded {extensions.Count} remote extensions from catalog");
            return extensions;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get remote extensions: {ex.Message}");
            return new List<Extension>();
        }
    }/// <summary>
    /// Get extensions from the SaveVault server
    /// </summary>
    private async Task<List<Extension>> GetExtensionsFromServer()
    {        try
        {
            var response = await _httpClient.GetAsync($"{SERVER_API_URL}?action=catalog");
            
            LoggingService.Instance.Info($"Server API request to: {SERVER_API_URL}?action=catalog");
            LoggingService.Instance.Info($"Response status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Instance.Warning($"Server API responded with status: {response.StatusCode}");
                return new List<Extension>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            LoggingService.Instance.Info($"Server API response length: {jsonContent.Length} characters");
            LoggingService.Instance.Info($"Server API response preview: {jsonContent.Substring(0, Math.Min(200, jsonContent.Length))}...");
            
            var apiResponse = JsonSerializer.Deserialize<ServerApiResponse>(jsonContent);
            LoggingService.Instance.Info($"Deserialized ServerApiResponse: Success={apiResponse?.Success}, Data count={apiResponse?.Data?.Count ?? 0}");            if (apiResponse?.Success == true && apiResponse.Data != null)
            {
                LoggingService.Instance.Info($"Successfully parsed {apiResponse.Data.Count} extensions from server API response");
                
                // Process each extension to set correct URLs
                foreach (var ext in apiResponse.Data)
                {
                    LoggingService.Instance.Info($"Extension from server: {ext.Id} - {ext.Name} (Category: {ext.Category})");
                    
                    // Set correct download and icon URLs based on extension category
                    SetExtensionUrls(ext);
                }
                return apiResponse.Data;
            }
            
            // Handle case where server returns data directly (for backward compatibility)
            var directData = JsonSerializer.Deserialize<List<Extension>>(jsonContent);
            if (directData != null)
            {
                LoggingService.Instance.Info($"Successfully parsed {directData.Count} extensions from direct array response");
                return directData;
            }

            LoggingService.Instance.Warning("Server API returned unsuccessful response");
            return new List<Extension>();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Failed to fetch extensions from server: {ex.Message}");
            return new List<Extension>();
        }
    }

    /// <summary>
    /// Record extension download with the server
    /// </summary>
    private async Task RecordDownloadAsync(string extensionId)
    {
        try
        {
            var downloadData = new { extension_id = extensionId };
            var jsonContent = JsonSerializer.Serialize(downloadData);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            await _httpClient.PostAsync($"{SERVER_API_URL}?action=download", content);
            LoggingService.Instance.Info($"Recorded download for extension: {extensionId}");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Failed to record download for {extensionId}: {ex.Message}");
        }
    }

    public List<Extension> GetBuiltInExtensions()
    {
        var builtInExtensions = new List<Extension>();
        
        // Load built-in extensions from the Extensions folder
        var builtInPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions");
        if (Directory.Exists(builtInPath))
        {
            foreach (var extensionDir in Directory.GetDirectories(builtInPath))
            {
                try
                {
                    var manifestPath = Path.Combine(extensionDir, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var manifestJson = File.ReadAllText(manifestPath);
                        var extension = JsonSerializer.Deserialize<Extension>(manifestJson);                        if (extension != null)
                        {
                            // Check if this built-in extension was disabled by the user
                            bool isDisabledByUser = Settings.Instance.IsBuiltInExtensionDisabled(extension.Id);
                              extension.IsInstalled = !isDisabledByUser && Settings.Instance.IsExtensionInstalled(extension.Id);
                            extension.IsEnabled = Settings.Instance.IsExtensionEnabled(extension.Id);
                            extension.ScriptPath = Path.Combine(extensionDir, "main.lua");
                            // Category should come from manifest.json, no need to override// Set icon URL if icon file exists
                            if (!string.IsNullOrEmpty(extension.IconUrl))
                            {
                                var iconPath = Path.Combine(extensionDir, extension.IconUrl);
                                if (File.Exists(iconPath))
                                {
                                    extension.IconUrl = iconPath;
                                }
                                else
                                {
                                    extension.IconUrl = string.Empty;
                                }
                            }
                            
                            builtInExtensions.Add(extension);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Failed to load built-in extension from {extensionDir}: {ex.Message}");
                }
            }
        }
        
        return builtInExtensions;
    }

    public async Task<bool> InstallExtensionAsync(Extension extension)
    {
        try
        {
            extension.IsDownloading = true;
            LoggingService.Instance.Info($"Installing extension: {extension.Name}");            // Handle built-in extensions
            if (extension.Id.StartsWith("savevault.") && extension.IsOfficial)
            {
                return InstallBuiltInExtension(extension);
            }

            // Record download with server (fire and forget)
            _ = Task.Run(async () => await RecordDownloadAsync(extension.Id));

            // Create extension directory
            var extensionDir = Path.Combine(_extensionsPath, extension.Id);
            Directory.CreateDirectory(extensionDir);            // Download extension files
            var downloadUrl = string.IsNullOrEmpty(extension.DownloadUrl)
                ? (extension.Category == ExtensionCategory.Localization 
                    ? $"{GITHUB_LOCALIZATION_URL}/{extension.Id}/"
                    : $"{GITHUB_BASE_URL}/Official/{extension.Id}/")
                : extension.DownloadUrl;

            // Download manifest
            var manifestUrl = $"{downloadUrl}manifest.json";
            var manifestResponse = await _httpClient.GetAsync(manifestUrl);
            if (manifestResponse.IsSuccessStatusCode)
            {
                var manifestPath = Path.Combine(extensionDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, await manifestResponse.Content.ReadAsStringAsync());
            }

            // Download main script
            var scriptUrl = $"{downloadUrl}main.lua";
            var scriptResponse = await _httpClient.GetAsync(scriptUrl);
            if (scriptResponse.IsSuccessStatusCode)
            {
                var scriptPath = Path.Combine(extensionDir, "main.lua");
                await File.WriteAllTextAsync(scriptPath, await scriptResponse.Content.ReadAsStringAsync());
                extension.ScriptPath = scriptPath;
            }            // Download additional files if needed
            await DownloadAdditionalFiles(downloadUrl, extensionDir);            // Update icon URL to point to local file if it was downloaded
            var localIconFiles = new[] { "logo.png", "icon.png", "preview.png", "icon.jpg", "icon.jpeg" };
            foreach (var iconFile in localIconFiles)
            {
                var localIconPath = Path.Combine(extensionDir, iconFile);
                if (File.Exists(localIconPath))
                {
                    extension.IconUrl = localIconPath;
                    LoggingService.Instance.Info($"Updated icon URL for {extension.Id} to local file: {iconFile}");
                    break;
                }
            }

            // Mark as installed
            Settings.Instance.AddInstalledExtension(extension.Id);
            Settings.Instance.ForceSave(); // Ensure settings are immediately saved
            extension.IsInstalled = true;
            extension.IsDownloading = false;

            if (!_installedExtensions.Any(e => e.Id == extension.Id))
            {
                _installedExtensions.Add(extension);
            }

            ExtensionInstalled?.Invoke(this, extension);
            LoggingService.Instance.Info($"Extension '{extension.Name}' installed successfully");
            return true;
        }
        catch (Exception ex)
        {
            extension.IsDownloading = false;
            LoggingService.Instance.Error($"Failed to install extension '{extension.Name}': {ex.Message}");
            return false;
        }
    }    private bool InstallBuiltInExtension(Extension extension)
    {
        try
        {
            // Remove from disabled list if it was previously disabled
            Settings.Instance.RemoveDisabledBuiltInExtension(extension.Id);
            
            // For built-in extensions, just copy from the Extensions folder to user extensions folder
            var builtInExtensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions", extension.Id);
            var userExtensionPath = Path.Combine(_extensionsPath, extension.Id);
            
            if (!Directory.Exists(builtInExtensionPath))
            {
                LoggingService.Instance.Warning($"Built-in extension not found: {extension.Id}");
                return false;
            }
            
            // Create user extension directory
            Directory.CreateDirectory(userExtensionPath);
            
            // Copy all files from built-in extension to user extension folder
            foreach (var file in Directory.GetFiles(builtInExtensionPath))
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(userExtensionPath, fileName);
                File.Copy(file, destPath, true);
            }
            
            extension.ScriptPath = Path.Combine(userExtensionPath, "main.lua");

            // Mark as installed
            Settings.Instance.AddInstalledExtension(extension.Id);
            Settings.Instance.ForceSave(); // Ensure settings are immediately saved
            extension.IsInstalled = true;
            extension.IsDownloading = false;

            if (!_installedExtensions.Any(e => e.Id == extension.Id))
            {
                _installedExtensions.Add(extension);
            }

            ExtensionInstalled?.Invoke(this, extension);
            LoggingService.Instance.Info($"Built-in extension '{extension.Name}' installed successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to install built-in extension '{extension.Name}': {ex.Message}");
            return false;
        }
    }    private async Task DownloadAdditionalFiles(string baseUrl, string extensionDir)
    {
        try
        {
            // Try to download common additional files
            var additionalFiles = new[] { 
                "readme.md", "README.md",
                "icon.png", "logo.png", "preview.png", // Various icon formats
                "icon.jpg", "icon.jpeg", 
                "screenshot.png", "screenshot.jpg"
            };

            foreach (var file in additionalFiles)
            {
                try
                {
                    var fileUrl = $"{baseUrl}{file}";
                    var response = await _httpClient.GetAsync(fileUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsByteArrayAsync();
                        var filePath = Path.Combine(extensionDir, file);
                        await File.WriteAllBytesAsync(filePath, content);
                        LoggingService.Instance.Info($"Downloaded additional file: {file}");
                    }
                }
                catch
                {
                    // Ignore errors for optional files
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Failed to download additional files: {ex.Message}");
        }
    }public bool UninstallExtension(Extension extension)
    {
        try
        {
            LoggingService.Instance.Info($"Uninstalling extension: {extension.Name}");

            // Disable the extension first
            if (extension.IsEnabled)
            {
                SetExtensionEnabled(extension, false);
            }            // Check if this is a built-in extension (by savevault. prefix or IsOfficial property)
            bool isBuiltIn = extension.Id.StartsWith("savevault.") || extension.IsOfficial;
            
            if (isBuiltIn)
            {
                // For built-in extensions, don't delete files but mark as disabled
                Settings.Instance.AddDisabledBuiltInExtension(extension.Id);
                LoggingService.Instance.Info($"Built-in extension '{extension.Name}' marked as disabled by user");
            }            else
            {
                // For user-installed extensions, remove files as before
                var extensionDir = Path.Combine(_extensionsPath, extension.Id);
                if (Directory.Exists(extensionDir))
                {
                    try
                    {
                        Directory.Delete(extensionDir, true);
                        LoggingService.Instance.Info($"Successfully deleted extension directory: {extensionDir}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Failed to delete extension directory {extensionDir}: {ex.Message}");
                        // Continue with the uninstall process even if directory deletion fails
                        // The loading logic will now clean up leftover directories
                    }
                }
            }

            // Remove from settings
            Settings.Instance.RemoveInstalledExtension(extension.Id);
            Settings.Instance.ForceSave(); // Ensure settings are immediately saved
            extension.IsInstalled = false;

            _installedExtensions.RemoveAll(e => e.Id == extension.Id);

            ExtensionUninstalled?.Invoke(this, extension);
            LoggingService.Instance.Info($"Extension '{extension.Name}' uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to uninstall extension '{extension.Name}': {ex.Message}");
            return false;
        }
    }

    public bool SetExtensionEnabled(Extension extension, bool enabled)
    {
        try
        {
            LoggingService.Instance.Info($"SetExtensionEnabled called for '{extension.Name}': {enabled} (currently {extension.IsEnabled}, installed: {extension.IsInstalled})");
            
            if (enabled && !extension.IsInstalled)
            {
                LoggingService.Instance.Warning($"Cannot enable uninstalled extension: {extension.Name}");
                return false;
            }

            if (enabled)
            {
                // Load the extension - use the extension's ScriptPath if available, otherwise fall back to AppData path
                var scriptPath = !string.IsNullOrEmpty(extension.ScriptPath) && File.Exists(extension.ScriptPath)
                    ? extension.ScriptPath
                    : Path.Combine(_extensionsPath, extension.Id, "main.lua");
                
                LoggingService.Instance.Info($"Attempting to load extension from: {scriptPath}");
                
                if (File.Exists(scriptPath))
                {
                    var scriptContent = File.ReadAllText(scriptPath);
                    if (LuaEngine.Instance.LoadExtension(extension, scriptContent))
                    {
                        Settings.Instance.SetExtensionEnabled(extension.Id, true);
                        Settings.Instance.ForceSave(); // Ensure settings are immediately saved
                        extension.IsEnabled = true;
                        
                        // Also update the installed extensions list to keep them in sync
                        var installedExt = _installedExtensions.FirstOrDefault(e => e.Id == extension.Id);
                        if (installedExt != null && installedExt != extension)
                        {
                            installedExt.IsEnabled = true;
                        }
                        
                        ExtensionEnabled?.Invoke(this, extension);
                        LoggingService.Instance.Info($"Extension '{extension.Name}' enabled from: {scriptPath}");
                        return true;
                    }

                    // LoadExtension failed (script error, etc.) — tell the user instead of failing silently.
                    LoggingService.Instance.Error($"Extension '{extension.Name}' failed to load; it will remain disabled");
                    NotificationService.Instance.AddLocalNotification($"Extension '{extension.Name}' failed to load. Check the Log Viewer for details.", "error");
                }
                else
                {
                    LoggingService.Instance.Warning($"Script file not found for extension '{extension.Name}' at: {scriptPath}");
                    NotificationService.Instance.AddLocalNotification($"Extension '{extension.Name}' could not be enabled: its script file is missing.", "error");
                }
            }
            else
            {
                // Unload the extension
                LoggingService.Instance.Info($"Unloading extension '{extension.Name}'");
                LuaEngine.Instance.UnloadExtension(extension.Id);
                Settings.Instance.SetExtensionEnabled(extension.Id, false);
                Settings.Instance.ForceSave(); // Ensure settings are immediately saved
                extension.IsEnabled = false;
                
                // Also update the installed extensions list to keep them in sync
                var installedExt = _installedExtensions.FirstOrDefault(e => e.Id == extension.Id);
                if (installedExt != null && installedExt != extension)
                {
                    installedExt.IsEnabled = false;
                }
                
                ExtensionDisabled?.Invoke(this, extension);
                LoggingService.Instance.Info($"Extension '{extension.Name}' disabled successfully");
                
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to {(enabled ? "enable" : "disable")} extension '{extension.Name}': {ex.Message}");
            return false;
        }
    }public async Task<bool> ImportExtensionAsync(string filePath)
    {
        try
        {
            LoggingService.Instance.Info($"Importing extension from: {filePath}");

            if (!File.Exists(filePath))
            {
                LoggingService.Instance.Error("Extension file does not exist");
                return false;
            }

            var extension = Path.GetExtension(filePath).ToLower();
            if (extension == ".zip")
            {
                return await ImportZipExtension(filePath);
            }
            else if (extension == ".lua")
            {
                return await ImportLuaScript(filePath);
            }
            else
            {
                LoggingService.Instance.Error("Unsupported extension file format. Supported formats: .zip, .lua");
                return false;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import extension: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ImportZipExtension(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var manifestEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifestEntry == null)
            {
                LoggingService.Instance.Error("Extension archive missing manifest.json");
                return false;
            }

            // Read manifest
            using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(manifestJson);

            if (manifest == null || !manifest.TryGetValue("id", out var idObj))
            {
                LoggingService.Instance.Error("Invalid manifest.json");
                return false;
            }

            // The id becomes a directory name and is fully attacker-controlled, so validate it
            // before it can be used to escape the extensions folder (zip-slip via the id).
            var extensionId = SanitizeExtensionId(idObj.ToString());
            if (extensionId == null)
            {
                LoggingService.Instance.Error($"Refusing to import extension: unsafe id '{idObj}'");
                return false;
            }

            var extensionsRoot = Path.GetFullPath(_extensionsPath);
            var extensionDir = Path.GetFullPath(Path.Combine(extensionsRoot, extensionId));

            // Defence in depth: even with a sanitized id, confirm the target stays under the root.
            if (!extensionDir.StartsWith(extensionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.Instance.Error($"Refusing to import extension '{extensionId}': resolved path escapes the extensions folder");
                return false;
            }

            // Validate every entry's destination before extracting, so a crafted entry name such as
            // "..\\..\\evil.exe" cannot be written outside the extension's own directory (zip-slip).
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/"))
                    continue; // directory entry

                var destination = Path.GetFullPath(Path.Combine(extensionDir, entry.FullName));
                if (!destination.StartsWith(extensionDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(destination, extensionDir, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.Instance.Error($"Refusing to import extension '{extensionId}': archive entry '{entry.FullName}' escapes the extension directory");
                    return false;
                }
            }

            // Extract all files (paths already validated above)
            archive.ExtractToDirectory(extensionDir, true);            // Create extension object
            var importedCategory = Enum.TryParse<ExtensionCategory>(
                manifest.TryGetValue("category", out var catObj) ? catObj.ToString() : "Other",
                ignoreCase: true, out var parsedCategory) ? parsedCategory : ExtensionCategory.Other;

            var importedExtension = new Extension
            {
                Id = extensionId,
                Name = manifest.TryGetValue("name", out var name) ? name.ToString()! : extensionId,
                Version = manifest.TryGetValue("version", out var version) ? version.ToString()! : "1.0.0",
                Description = manifest.TryGetValue("description", out var desc) ? desc.ToString()! : "Imported extension",
                Author = manifest.TryGetValue("author", out var author) ? author.ToString()! : "Unknown",
                Category = importedCategory,
                IsInstalled = true,
                IsOfficial = false, // Imported extensions are never official
                Permissions = ParseManifestPermissions(manifest),
                ScriptPath = Path.Combine(extensionDir, "main.lua"),
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            Settings.Instance.AddInstalledExtension(extensionId);
            Settings.Instance.ForceSave(); // Ensure settings are immediately saved
            _installedExtensions.Add(importedExtension);

            ExtensionInstalled?.Invoke(this, importedExtension);
            LoggingService.Instance.Info($"Extension '{importedExtension.Name}' imported successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import ZIP extension: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ImportLuaScript(string luaPath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(luaPath);
            var extensionId = $"imported.{fileName}";
            var extensionDir = Path.Combine(_extensionsPath, extensionId);
            Directory.CreateDirectory(extensionDir);

            // Copy the Lua script
            var scriptPath = Path.Combine(extensionDir, "main.lua");
            File.Copy(luaPath, scriptPath, true);

            // Create basic manifest
            var manifest = new
            {
                id = extensionId,
                name = fileName,
                version = "1.0.0",
                description = "Imported Lua script",
                author = "Unknown",
                category = "Other",
                main = "main.lua"
            };

            var manifestPath = Path.Combine(extensionDir, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));            var importedExtension = new Extension
            {
                Id = extensionId,
                Name = fileName,
                Version = "1.0.0",
                Description = "Imported Lua script",
                Author = "Unknown",
                Category = ExtensionCategory.Other,
                IsInstalled = true,
                IsOfficial = false, // Imported scripts are never official
                ScriptPath = scriptPath,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            Settings.Instance.AddInstalledExtension(extensionId);
            Settings.Instance.ForceSave(); // Ensure settings are immediately saved
            _installedExtensions.Add(importedExtension);

            ExtensionInstalled?.Invoke(this, importedExtension);
            LoggingService.Instance.Info($"Lua script '{fileName}' imported successfully");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import Lua script: {ex.Message}");
            return false;
        }
    }    public List<Extension> GetInstalledExtensions()
    {
        return _installedExtensions.Where(e => !string.IsNullOrWhiteSpace(e.Id) && 
                                             !string.IsNullOrWhiteSpace(e.Name) && 
                                             !string.IsNullOrWhiteSpace(e.Author)).ToList();
    }

    /// <summary>
    /// Find an installed extension by its ID.
    /// </summary>
    public Extension? FindInstalledExtensionById(string extensionId)
    {
        return _installedExtensions.FirstOrDefault(e => string.Equals(e.Id, extensionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Decide whether an extension is allowed to use a sensitive capability. The rules:
    /// <list type="bullet">
    /// <item>Official / built-in extensions are trusted and may use anything.</item>
    /// <item>If the manifest declares a <c>permissions</c> array, it is enforced strictly — only the
    /// listed capabilities are granted.</item>
    /// <item>If the manifest declares nothing (legacy extension), the capabilities that existed
    /// before the permissions model (network, files, clipboard) stay granted, but brand-new
    /// capabilities (backups, games) require an explicit declaration.</item>
    /// </list>
    /// Low-risk capabilities (logging, settings, translation, events, generic UI) are never routed
    /// through here — they are always available.
    /// </summary>
    public bool HasPermission(string extensionId, string permission)
    {
        var extension = FindInstalledExtensionById(extensionId);
        return extension != null && EvaluatePermission(extension, permission);
    }

    /// <summary>
    /// Pure policy evaluation for an extension's capability (see <see cref="HasPermission"/> for the
    /// rules). Exposed so a caller that already holds the <see cref="Extension"/> (e.g. the Lua engine
    /// during initial load) can decide without re-entering this singleton.
    /// </summary>
    public static bool EvaluatePermission(Extension extension, string permission)
    {
        // Trust first-party content shipped/curated with the app.
        if (extension.IsOfficial || extension.Id.StartsWith("savevault.", StringComparison.OrdinalIgnoreCase))
            return true;

        // Manifest declared an explicit set -> strict allow-list.
        if (extension.Permissions != null)
            return extension.Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));

        // Legacy extension (no permissions key): grant historically-available capabilities, but
        // require an explicit opt-in for capabilities introduced alongside the permissions model.
        return !ExtensionPermissions.RequireExplicitDeclaration
            .Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validate an extension id taken from an untrusted manifest so it can be used as a single
    /// folder name. Rejects path separators, traversal segments and characters illegal in a file
    /// name. Returns null when the id is unusable.
    /// </summary>
    private static string? SanitizeExtensionId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return null;

        var id = rawId.Trim();

        // Must be a single path segment: no directory separators and not a traversal token.
        if (id is "." or ".." ||
            id.Contains('/') || id.Contains('\\') ||
            id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal))
        {
            return null;
        }

        return id;
    }

    /// <summary>
    /// Pull the optional <c>permissions</c> array out of a manifest deserialized as a loose
    /// dictionary. Returns null when the key is absent (legacy extension) so the distinction between
    /// "declared nothing" and "declared an empty set" is preserved.
    /// </summary>
    private static List<string>? ParseManifestPermissions(Dictionary<string, object> manifest)
    {
        if (!manifest.TryGetValue("permissions", out var permsObj) || permsObj is not JsonElement el ||
            el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return el.EnumerateArray()
            .Select(p => p.GetString() ?? "")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
    }

    /// <summary>
    /// Dispatch an extension-lifecycle system event (with a small JSON payload) to subscribed Lua
    /// extensions. Best-effort: a failure here must never break install/enable flows.
    /// </summary>
    private static void TriggerLifecycleEvent(string eventName, Extension ext)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { id = ext.Id, name = ext.Name, version = ext.Version });
            ExtensionEventService.Instance.TriggerEvent(eventName, payload);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Failed to dispatch '{eventName}' to extensions: {ex.Message}");
        }
    }

    /// <summary>
    /// Get only local extensions (built-in and installed) without network requests
    /// This is fast and can be used to show extensions immediately
    /// </summary>
    public List<Extension> GetLocalExtensions()
    {
        var localExtensions = new List<Extension>();
        
        // Get all installed extensions (which includes built-in ones that were loaded)
        var installedExtensions = GetInstalledExtensions();
        localExtensions.AddRange(installedExtensions);
        
        // Add any built-in extensions that aren't already installed
        var builtInExtensions = GetBuiltInExtensions();
        foreach (var builtIn in builtInExtensions)
        {
            var existing = localExtensions.FirstOrDefault(e => e.Id == builtIn.Id);
            if (existing == null)
            {
                localExtensions.Add(builtIn);
            }
            else
            {
                // Update existing with any additional built-in metadata
                if (string.IsNullOrEmpty(existing.IconUrl) && !string.IsNullOrEmpty(builtIn.IconUrl))
                    existing.IconUrl = builtIn.IconUrl;
                if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(builtIn.Description))
                    existing.Description = builtIn.Description;
            }
        }
        
        return localExtensions.Where(e => e.IsValid).ToList();
    }private void LoadInstalledExtensions()
    {
        try
        {
            _installedExtensions.Clear();

            // First, load built-in extensions from the project directory
            LoadBuiltInExtensions();

            // Log the contents of the installed extensions set for debugging
            var installedExtensions = Settings.Instance.InstalledExtensions;
            var enabledExtensions = Settings.Instance.EnabledExtensions;
            LoggingService.Instance.Info($"Found {installedExtensions.Count} extensions in settings, {enabledExtensions.Count} enabled");
            
            foreach (var extensionId in installedExtensions)
            {
                LoggingService.Instance.Info($"Extension in settings: {extensionId}, enabled: {enabledExtensions.Contains(extensionId)}");
            }

            // Then, load user-installed extensions from AppData
            if (!Directory.Exists(_extensionsPath))
            {
                LoggingService.Instance.Info($"Extensions directory not found: {_extensionsPath}");
                return;
            }

            foreach (var extensionDir in Directory.GetDirectories(_extensionsPath))
            {
                try
                {
                    var extensionFolderName = Path.GetFileName(extensionDir);
                    LoggingService.Instance.Info($"Found extension directory: {extensionFolderName}");
                    
                    var manifestPath = Path.Combine(extensionDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        LoggingService.Instance.Warning($"Manifest file not found in {extensionDir}, skipping");
                        continue;
                    }

                    var manifestJson = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(manifestJson);

                    if (manifest == null)
                    {
                        LoggingService.Instance.Warning($"Failed to parse manifest in {extensionDir}, skipping");
                        continue;
                    }

                    // Skip directories that don't represent valid extensions
                    if (!manifest.ContainsKey("id") || !manifest.ContainsKey("name"))
                    {
                        LoggingService.Instance.Warning($"Skipping invalid extension at {extensionDir}: Missing required fields in manifest");
                        continue;
                    }
                    
                    var extensionId = manifest.TryGetValue("id", out var id) ? id.ToString()! : Path.GetFileName(extensionDir);
                    
                    // Skip if we already loaded this as a built-in extension
                    if (_installedExtensions.Any(e => e.Id == extensionId))
                    {
                        LoggingService.Instance.Info($"Extension {extensionId} already loaded as built-in, skipping");
                        continue;
                    }
                      // Check if this extension is actually installed according to settings
                    bool isReallyInstalled = Settings.Instance.IsExtensionInstalled(extensionId);
                    LoggingService.Instance.Info($"Extension {extensionId} installed according to settings: {isReallyInstalled}");
                      // If the extension directory exists but it's not marked as installed in settings,
                    // it means it was uninstalled but the directory cleanup failed or was incomplete.
                    // Instead of re-adding it to settings, clean up the leftover directory.
                    if (!isReallyInstalled)
                    {
                        LoggingService.Instance.Warning($"Extension {extensionId} directory exists but not marked as installed in settings. This appears to be leftover from an uninstall. Cleaning up directory.");
                        try
                        {
                            // Additional safety check: only clean up directories in the user extensions path
                            // and ensure the directory is actually under our control
                            if (extensionDir.StartsWith(_extensionsPath, StringComparison.OrdinalIgnoreCase) && 
                                Path.GetFileName(extensionDir) == extensionId)
                            {
                                Directory.Delete(extensionDir, true);
                                LoggingService.Instance.Info($"Successfully cleaned up leftover extension directory: {extensionDir}");
                            }
                            else
                            {
                                LoggingService.Instance.Warning($"Skipping cleanup of directory outside expected path: {extensionDir}");
                            }
                        }
                        catch (Exception cleanupEx)
                        {
                            LoggingService.Instance.Error($"Failed to clean up leftover extension directory {extensionDir}: {cleanupEx.Message}");
                        }
                        continue; // Skip loading this extension since it should be uninstalled
                    }

                    var extension = new Extension
                    {
                        Id = extensionId,
                        Name = manifest.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name?.ToString()) 
                            ? name.ToString()! 
                            : Path.GetFileName(extensionDir),
                        Version = manifest.TryGetValue("version", out var version) && !string.IsNullOrWhiteSpace(version?.ToString()) 
                            ? version.ToString()! 
                            : "1.0.0",
                        Description = manifest.TryGetValue("description", out var desc) 
                            ? desc.ToString()! 
                            : "",
                        Author = manifest.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author?.ToString()) 
                            ? author.ToString()! 
                            : "Unknown",                        Category = Enum.TryParse<ExtensionCategory>(manifest.TryGetValue("category", out var cat) ? cat.ToString()! : "Other", out var category) ? category : ExtensionCategory.Other,
                        IsInstalled = true,
                        IsEnabled = Settings.Instance.IsExtensionEnabled(extensionId),                        ScriptPath = Path.Combine(extensionDir, "main.lua"),
                        IsOfficial = manifest.TryGetValue("isOfficial", out var isOfficialObj) && isOfficialObj is JsonElement isOfficialElement && isOfficialElement.GetBoolean(),
                        Permissions = ParseManifestPermissions(manifest)
                    };

                    _installedExtensions.Add(extension);
                    LoggingService.Instance.Info($"Added extension to installed list: {extension.Id}, enabled: {extension.IsEnabled}");

                    // Auto-enable if marked as enabled in settings
                    if (extension.IsEnabled && File.Exists(extension.ScriptPath))
                    {
                        var scriptContent = File.ReadAllText(extension.ScriptPath);
                        bool loadResult = LuaEngine.Instance.LoadExtension(extension, scriptContent);
                        LoggingService.Instance.Info($"Auto-enabled extension {extension.Id}: {loadResult}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Failed to load extension from {extensionDir}: {ex.Message}");
                }
            }

            LoggingService.Instance.Info($"Loaded {_installedExtensions.Count} installed extensions");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load installed extensions: {ex.Message}");
        }
    }    private void LoadBuiltInExtensions()
    {
        try
        {
            var configManager = ExtensionConfigManager.Instance;
            
            // Get the built-in extensions directory relative to the executable
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = Path.GetDirectoryName(executablePath);
            var builtInExtensionsPath = Path.Combine(executableDir ?? "", configManager.GetBuiltInExtensionsPath());

            // Fallback to the project structure during development
            if (!Directory.Exists(builtInExtensionsPath))
            {
                var currentDir = Directory.GetCurrentDirectory();
                builtInExtensionsPath = Path.Combine(currentDir, "Main", configManager.GetBuiltInExtensionsPath());
            }

            if (!Directory.Exists(builtInExtensionsPath))
            {
                LoggingService.Instance.Info("No built-in extensions directory found");
                return;
            }

            LoggingService.Instance.Info($"Loading built-in extensions from: {builtInExtensionsPath}");

            foreach (var extensionDir in Directory.GetDirectories(builtInExtensionsPath))
            {
                try
                {
                    var extensionFolderName = Path.GetFileName(extensionDir);
                    LoggingService.Instance.Info($"Found built-in extension directory: {extensionFolderName}");
                    
                    var manifestPath = Path.Combine(extensionDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        LoggingService.Instance.Warning($"Built-in manifest file not found in {extensionDir}, skipping");
                        continue;
                    }

                    var manifestJson = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(manifestJson);

                    if (manifest == null)
                    {
                        LoggingService.Instance.Warning($"Failed to parse built-in manifest in {extensionDir}, skipping");
                        continue;
                    }

                    // Skip directories that don't represent valid extensions
                    if (!manifest.ContainsKey("id") || !manifest.ContainsKey("name"))
                    {
                        LoggingService.Instance.Warning($"Skipping invalid built-in extension at {extensionDir}: Missing required fields in manifest");
                        continue;
                    }
                      var extensionId = manifest.TryGetValue("id", out var id) ? id.ToString()! : Path.GetFileName(extensionDir);
                    
                    // Check if this built-in extension was explicitly disabled by the user
                    bool isDisabledByUser = Settings.Instance.IsBuiltInExtensionDisabled(extensionId);
                    
                    if (isDisabledByUser)
                    {
                        LoggingService.Instance.Info($"Skipping built-in extension {extensionId} - disabled by user");
                        continue;
                    }
                    
                    bool isInstalledInSettings = Settings.Instance.IsExtensionInstalled(extensionId);
                    bool isEnabledInSettings = Settings.Instance.IsExtensionEnabled(extensionId);
                    bool shouldAutoEnable = configManager.ShouldAutoEnable(extensionId);
                    
                    LoggingService.Instance.Info($"Built-in extension {extensionId}: " +
                        $"Installed in settings: {isInstalledInSettings}, " +
                        $"Enabled in settings: {isEnabledInSettings}, " +
                        $"Auto-enable in config: {shouldAutoEnable}");

                    var extension = new Extension
                    {
                        Id = extensionId,
                        Name = manifest.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name?.ToString()) 
                            ? name.ToString()! 
                            : Path.GetFileName(extensionDir),
                        Version = manifest.TryGetValue("version", out var version) && !string.IsNullOrWhiteSpace(version?.ToString()) 
                            ? version.ToString()! 
                            : "1.0.0",
                        Description = manifest.TryGetValue("description", out var desc) 
                            ? desc.ToString()! 
                            : "",                        Author = manifest.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author?.ToString()) 
                            ? author.ToString()! 
                            : "SaveVault Team",
                        Category = Enum.TryParse<ExtensionCategory>(manifest.TryGetValue("category", out var cat) ? cat.ToString()! : "Other", out var category) ? category : ExtensionCategory.Other,
                        IsInstalled = isInstalledInSettings,
                        IsEnabled = isEnabledInSettings || (isInstalledInSettings && shouldAutoEnable),
                        ScriptPath = Path.Combine(extensionDir, "main.lua"),                        Tags = manifest.TryGetValue("tags", out var tagsObj) && tagsObj is JsonElement tagsElement 
                            ? tagsElement.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => !string.IsNullOrEmpty(t)).ToList()                            : new List<string>(),
                        IsOfficial = manifest.TryGetValue("isOfficial", out var isOfficialObj) && isOfficialObj is JsonElement isOfficialElement && isOfficialElement.GetBoolean(),
                        Permissions = ParseManifestPermissions(manifest)
                    };// Set icon URL if icon file exists
                    if (manifest.TryGetValue("icon", out var iconObj) && !string.IsNullOrWhiteSpace(iconObj?.ToString()))
                    {
                        var iconFileName = iconObj.ToString()!;
                        var iconPath = Path.Combine(extensionDir, iconFileName);
                        if (File.Exists(iconPath))
                        {
                            extension.IconUrl = iconPath;
                        }
                    }                    _installedExtensions.Add(extension);
                    LoggingService.Instance.Info($"Added built-in extension to list: {extension.Id}, installed: {extension.IsInstalled}, enabled: {extension.IsEnabled}");

                    // SECURITY FIX: Only mark as installed if user explicitly enabled it or it was previously installed
                    // Built-in extensions should NOT auto-install themselves
                    if (shouldAutoEnable && !extension.IsInstalled)
                    {
                        Settings.Instance.AddInstalledExtension(extension.Id);
                        extension.IsInstalled = true;
                        LoggingService.Instance.Info($"Marked built-in extension as installed due to auto-enable config: {extension.Id}");
                    }

                    // Enable if configured for auto-enable and now installed
                    if (shouldAutoEnable && extension.IsInstalled && !extension.IsEnabled)
                    {
                        Settings.Instance.SetExtensionEnabled(extension.Id, true);
                        extension.IsEnabled = true;
                        LoggingService.Instance.Info($"Auto-enabled built-in extension: {extension.Id}");
                    }

                    LoggingService.Instance.Info($"Loaded built-in extension: {extension.Name} (Installed: {extension.IsInstalled}, Enabled: {extension.IsEnabled})");
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Failed to load built-in extension from {extensionDir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load built-in extensions: {ex.Message}");
        }
    }    /// <summary>
    /// Set correct download and icon URLs for an extension based on its category and ID
    /// </summary>
    private void SetExtensionUrls(Extension extension)
    {
        try
        {
            string baseUrl;
            string iconFileName;
              // Determine folder structure and icon file based on category
            switch (extension.Category)
            {
                case ExtensionCategory.Localization:
                    baseUrl = $"{GITHUB_LOCALIZATION_URL}/{extension.Id}/";
                    iconFileName = "logo.png"; // Localization extensions typically use logo.png
                    break;
                    
                case ExtensionCategory.Theming:
                    baseUrl = $"{GITHUB_THEMES_URL}/{extension.Id}/";
                    iconFileName = "preview.png"; // Theme extensions might use preview.png
                    break;
                    
                case ExtensionCategory.Fixes:
                    baseUrl = $"{GITHUB_FIXES_URL}/{extension.Id}/";
                    iconFileName = "icon.png";
                    break;

                case ExtensionCategory.Official:
                    baseUrl = $"{GITHUB_BASE_URL}/Official/{extension.Id}/";
                    iconFileName = "icon.png";
                    break;

                default: // ExtensionCategory.Other and any future categories
                    baseUrl = $"{GITHUB_BASE_URL}/Official/{extension.Id}/";
                    iconFileName = "icon.png";
                    break;
            }
            
            extension.DownloadUrl = baseUrl;
            extension.IconUrl = $"{baseUrl}{iconFileName}";
            
            LoggingService.Instance.Info($"Set URLs for {extension.Category} extension {extension.Id}: Download={extension.DownloadUrl}, Icon={extension.IconUrl}");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to set URLs for extension {extension.Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually clean up any orphaned extension directories that are not marked as installed
    /// </summary>
    public void CleanupOrphanedExtensions()
    {
        try
        {
            LoggingService.Instance.Info("Starting cleanup of orphaned extension directories...");
            
            if (!Directory.Exists(_extensionsPath))
            {
                LoggingService.Instance.Info("Extensions directory does not exist, nothing to clean up");
                return;
            }

            var cleanedCount = 0;
            foreach (var extensionDir in Directory.GetDirectories(_extensionsPath))
            {
                try
                {
                    var extensionId = Path.GetFileName(extensionDir);
                    
                    // Skip if this extension is marked as installed
                    if (Settings.Instance.IsExtensionInstalled(extensionId))
                    {
                        continue;
                    }
                    
                    LoggingService.Instance.Info($"Found orphaned extension directory: {extensionId}");
                    
                    // Additional safety checks
                    if (extensionDir.StartsWith(_extensionsPath, StringComparison.OrdinalIgnoreCase) && 
                        !string.IsNullOrWhiteSpace(extensionId) &&
                        extensionId != "." && extensionId != "..")
                    {
                        Directory.Delete(extensionDir, true);
                        LoggingService.Instance.Info($"Cleaned up orphaned extension directory: {extensionDir}");
                        cleanedCount++;
                    }
                    else
                    {
                        LoggingService.Instance.Warning($"Skipping cleanup of suspicious directory: {extensionDir}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Failed to clean up extension directory {extensionDir}: {ex.Message}");
                }
            }
            
            LoggingService.Instance.Info($"Cleanup completed. Removed {cleanedCount} orphaned extension directories.");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error during orphaned extensions cleanup: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}