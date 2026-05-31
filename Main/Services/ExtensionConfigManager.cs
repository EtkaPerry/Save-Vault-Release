using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SaveVaultApp.Services;

namespace SaveVaultApp.Services;

/// <summary>
/// Manages extension configuration for both built-in and external extensions
/// </summary>
public class ExtensionConfigManager
{
    private static readonly Lazy<ExtensionConfigManager> _instance = new(() => new ExtensionConfigManager());
    public static ExtensionConfigManager Instance => _instance.Value;

    private ExtensionConfig? _config;
    private readonly string _configPath;

    private ExtensionConfigManager()
    {
        var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var executableDir = Path.GetDirectoryName(executablePath);
        _configPath = Path.Combine(executableDir ?? "", "Extensions", "extension-config.json");

        // Fallback to project structure during development
        if (!File.Exists(_configPath))
        {
            var currentDir = Directory.GetCurrentDirectory();
            _configPath = Path.Combine(currentDir, "Main", "Extensions", "extension-config.json");
        }

        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var configJson = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<ExtensionConfig>(configJson);
                LoggingService.Instance.Info($"Extension configuration loaded from: {_configPath}");
            }
            else
            {
                _config = CreateDefaultConfiguration();
                LoggingService.Instance.Info("Using default extension configuration");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load extension configuration: {ex.Message}");
            _config = CreateDefaultConfiguration();
        }
    }

    private ExtensionConfig CreateDefaultConfiguration()
    {
        return new ExtensionConfig
        {
            InternalExtensions = new InternalExtensionsConfig
            {
                LoadOrder = new List<string> { "built-in", "user-installed" },
                AutoEnable = new Dictionary<string, bool>
                {
                    { "savevault.complete-dark-mode", false }
                },
                ExtensionPaths = new ExtensionPathsConfig
                {
                    BuiltIn = "Extensions",
                    UserInstalled = "%APPDATA%/SaveVault/Extensions"
                }
            },
            ExtensionApi = new ExtensionApiConfig
            {
                AllowedNamespaces = new List<string> { "theme", "settings", "logging", "file" },
                Security = new SecurityConfig
                {
                    Sandboxed = true,
                    AllowFileAccess = "extensionOnly",
                    AllowNetworkAccess = false
                }
            },
            DeveloperMode = new DeveloperModeConfig
            {
                Enabled = false,
                AllowUnsignedExtensions = false,
                DebugLogging = true
            }
        };
    }

    public bool ShouldAutoEnable(string extensionId)
    {
        return _config?.InternalExtensions?.AutoEnable?.TryGetValue(extensionId, out var autoEnable) == true && autoEnable;
    }

    public bool IsDeveloperModeEnabled => _config?.DeveloperMode?.Enabled == true;

    public bool AllowUnsignedExtensions => _config?.DeveloperMode?.AllowUnsignedExtensions == true;

    public bool IsDebugLoggingEnabled => _config?.DeveloperMode?.DebugLogging == true;

    public List<string> GetLoadOrder()
    {
        return _config?.InternalExtensions?.LoadOrder ?? new List<string> { "built-in", "user-installed" };
    }

    public string GetBuiltInExtensionsPath()
    {
        return _config?.InternalExtensions?.ExtensionPaths?.BuiltIn ?? "Extensions";
    }

    public string GetUserExtensionsPath()
    {
        var path = _config?.InternalExtensions?.ExtensionPaths?.UserInstalled ?? "%APPDATA%/SaveVault/Extensions";
        return Environment.ExpandEnvironmentVariables(path);
    }

    public bool IsNamespaceAllowed(string namespaceName)
    {
        return _config?.ExtensionApi?.AllowedNamespaces?.Contains(namespaceName.ToLowerInvariant()) == true;
    }
}

public class ExtensionConfig
{
    public InternalExtensionsConfig? InternalExtensions { get; set; }
    public ExtensionApiConfig? ExtensionApi { get; set; }
    public DeveloperModeConfig? DeveloperMode { get; set; }
}

public class InternalExtensionsConfig
{
    public List<string>? LoadOrder { get; set; }
    public Dictionary<string, bool>? AutoEnable { get; set; }
    public ExtensionPathsConfig? ExtensionPaths { get; set; }
}

public class ExtensionPathsConfig
{
    public string? BuiltIn { get; set; }
    public string? UserInstalled { get; set; }
}

public class ExtensionApiConfig
{
    public List<string>? AllowedNamespaces { get; set; }
    public SecurityConfig? Security { get; set; }
}

public class SecurityConfig
{
    public bool Sandboxed { get; set; }
    public string? AllowFileAccess { get; set; }
    public bool AllowNetworkAccess { get; set; }
}

public class DeveloperModeConfig
{
    public bool Enabled { get; set; }
    public bool AllowUnsignedExtensions { get; set; }
    public bool DebugLogging { get; set; }
}
