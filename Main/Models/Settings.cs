using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;

namespace SaveVaultApp.Models;

// Define SaveBackupInfo here for serialization
public class SaveBackupInfo
{
    public string BackupPath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Description { get; set; } = string.Empty;
    public bool IsAutoBackup { get; set; } = true;
}

public class Settings
{
    // Add static instance
    private static Settings? _instance;
    public static Settings? Instance => _instance;    
      private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SaveVault",
        "settings.json" // Make sure this is lowercase to match other files
    );
    
    // User authentication settings
    public string? LoggedInUser { get; set; }
    public string? AuthToken { get; set; }

    public string SortOption { get; set; } = "A-Z";
    public bool HiddenGamesExpanded { get; set; } = true;
    
    // Theme setting
    public string? Theme { get; set; } = "System";
    
    // Auto-save settings
    public int AutoSaveInterval { get; set; } = 15;
    public bool GlobalAutoSaveEnabled { get; set; } = true;
    public bool StartSaveEnabled { get; set; } = true;
    public int MaxAutoSaves { get; set; } = 5; // Maximum number of auto-saves to keep
    public int MaxStartSaves { get; set; } = 3; // Maximum number of start saves to keep
      // Backup storage location
    public string BackupStorageLocation { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveVault", "Backups");
        
    // Update settings
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
    public int UpdateCheckInterval { get; set; } = 24; // hours
    
    // Window settings
    public double WindowWidth { get; set; } = 800;
    public double WindowHeight { get; set; } = 450;
    public double WindowPositionX { get; set; } = double.NaN;
    public double WindowPositionY { get; set; } = double.NaN;
    public bool IsMaximized { get; set; } = false;

    // Options Window settings
    public double OptionsWindowWidth { get; set; } = 600;
    public double OptionsWindowHeight { get; set; } = 400;
    public double OptionsWindowPositionX { get; set; } = double.NaN;
    public double OptionsWindowPositionY { get; set; } = double.NaN;
    public bool IsOptionsMaximized { get; set; } = false;

    // Application specific settings
    public Dictionary<string, DateTime> LastUsedTimes { get; set; } = new();
    public Dictionary<string, DateTime> LastBackupTimes { get; set; } = new(); // Added to track last backup time
    public Dictionary<string, string> CustomNames { get; set; } = new();
    public Dictionary<string, string> CustomSavePaths { get; set; } = new();
    public HashSet<string> HiddenApps { get; set; } = new();
    public HashSet<string> KnownApplicationPaths { get; set; } = new();
    public Dictionary<string, AppSpecificSettings> AppSettings { get; set; } = new();

    // Add backup history storage
    public Dictionary<string, List<SaveBackupInfo>> BackupHistory { get; set; } = new();
    
    // Constructor that ensures this instance is the current static instance
    public Settings()
    {
        // Set this instance as the current static instance
        // This ensures that any instance creation updates the static reference
        _instance = this;
    }    public static Settings Load()
    {
        // Call debug method to verify paths
        DebugEnvironmentPaths();
        
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            Debug.WriteLine($"Loading settings from directory: {directory}");
            
            if (!Directory.Exists(directory) && directory != null)
            {
                Debug.WriteLine($"Creating settings directory during load: {directory}");
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(SettingsPath))
            {
                Debug.WriteLine($"Settings file exists at: {SettingsPath}");
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                
                // Initialize collections if they're null to prevent null reference exceptions
                settings.LastUsedTimes ??= new();
                settings.LastBackupTimes ??= new(); // Initialize LastBackupTimes
                settings.CustomNames ??= new();
                settings.CustomSavePaths ??= new();
                settings.HiddenApps ??= new();
                settings.KnownApplicationPaths ??= new();
                settings.AppSettings ??= new();
                settings.BackupHistory ??= new(); // Ensure BackupHistory is initialized

                _instance = settings;
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading settings: {ex.Message}");
        }
        
        // Return new settings if file doesn't exist or loading fails
        var newSettings = new Settings();
        _instance = newSettings;
        return newSettings;
    }    public void Save()
    {
        try
        {
            // Use the ForceSave method to ensure the file is created properly
            ForceSave();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in Save method: {ex.Message}");
            try
            {
                // Log more detailed information through the logging service
                if (Services.LoggingService.Instance != null)
                {
                    Services.LoggingService.Instance.Error($"Failed to save settings: {ex.Message}");
                    Services.LoggingService.Instance.Error($"Stack trace: {ex.StackTrace}");
                }
            }
            catch
            {
                // Just in case logging fails too
                Debug.WriteLine("Failed to log through LoggingService");
            }
        }
    }
      // Force save with retry logic
    public void ForceSave()
    {
        int maxRetries = 3;
        int retryCount = 0;
        bool saved = false;
        
        // Use logging service for better visibility in dotnet run
        var logger = SaveVaultApp.Services.LoggingService.Instance;
        logger.Debug($"ForceSave called for settings.json");
        
        while (!saved && retryCount < maxRetries)
        {
            try
            {
                retryCount++;
                logger.Debug($"Force save attempt #{retryCount}");
                
                // Make sure this instance is set as the static instance
                if (_instance != this)
                {
                    _instance = this;
                    logger.Debug("Updated static instance reference");
                }
                
                var directory = Path.GetDirectoryName(SettingsPath);
                logger.Debug($"Settings directory: {directory}");
                
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                    logger.Debug($"Created settings directory: {directory}");
                }

                logger.Debug("Serializing settings to JSON");
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                logger.Debug($"JSON serialization complete, length: {json.Length}");
                
                // Write directly to settings file first as a test
                logger.Debug($"Testing direct write to settings file: {SettingsPath}");
                File.WriteAllText(SettingsPath, json);
                
                if (File.Exists(SettingsPath))
                {
                    logger.Debug($"Settings file exists after direct write, size: {new FileInfo(SettingsPath).Length} bytes");
                    saved = true;
                }
                else 
                {
                    logger.Warning("Direct write failed, trying with temp file approach");
                    
                    // Use a temporary file to write to first, then move it
                    string tempFile = Path.Combine(
                        Path.GetDirectoryName(SettingsPath) ?? string.Empty, 
                        $"temp_settings_{Guid.NewGuid():N}.json");
                    
                    logger.Debug($"Writing to temporary file: {tempFile}");
                    File.WriteAllText(tempFile, json);
                    
                    if (File.Exists(SettingsPath))
                    {
                        logger.Debug($"Deleting existing settings file");
                        File.Delete(SettingsPath);
                    }
                    
                    logger.Debug($"Moving temporary file to settings path");
                    File.Move(tempFile, SettingsPath);
                    
                    if (File.Exists(SettingsPath))
                    {
                        logger.Debug("Settings file successfully saved using temp file approach");
                        saved = true;
                    }
                    else
                    {
                        logger.Warning("WARNING: Settings file was not created after move operation");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error in force save attempt #{retryCount}: {ex.Message}");
                logger.Debug($"Stack trace: {ex.StackTrace}");
                
                // Wait before retrying
                System.Threading.Thread.Sleep(100);
            }
        }
        
        if (!saved)
        {
            logger.Error($"CRITICAL: Failed to save settings after {maxRetries} attempts!");
            
            // As a last resort, try a very simple approach
            try {
                logger.Debug("Attempting emergency save with minimal JSON");
                File.WriteAllText(SettingsPath, "{\"Theme\":\"System\"}");
                logger.Debug("Emergency save attempt complete");
            } 
            catch (Exception ex) {
                logger.Error($"Even emergency save failed: {ex.Message}");
            }
        }
    }
      // Debug method to verify paths
    public static void DebugEnvironmentPaths()
    {
        var logger = SaveVaultApp.Services.LoggingService.Instance;
        logger.Debug("Settings.DebugEnvironmentPaths called");
        
        try
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            logger.Debug($"AppData path: {appDataPath}");
            logger.Debug($"LocalAppData path: {localAppDataPath}");
            logger.Debug($"UserProfile path: {userProfilePath}");
            logger.Debug($"Settings file path: {SettingsPath}");
            
            // Check if SaveVault directory exists
            string saveVaultDir = Path.Combine(appDataPath, "SaveVault");
            logger.Debug($"SaveVault directory exists: {Directory.Exists(saveVaultDir)}");
            
            // List files in SaveVault directory if it exists
            if (Directory.Exists(saveVaultDir))
            {
                logger.Debug("Files in SaveVault directory:");
                string fileList = "";
                foreach (string file in Directory.GetFiles(saveVaultDir))
                {
                    fileList += $"{Path.GetFileName(file)}, ";
                }
                logger.Debug($"Files: {fileList.TrimEnd(',', ' ')}");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error in DebugEnvironmentPaths: {ex.Message}");
        }
    }
}

public class AppSpecificSettings
{
    private bool _hasCustomSettings;
    public bool HasCustomSettings
    {
        get => _hasCustomSettings;
        set
        {
            _hasCustomSettings = value;
            SaveSettings();
        }
    }

    private int _autoSaveInterval = 15;
    public int AutoSaveInterval
    {
        get => _autoSaveInterval;
        set
        {
            _autoSaveInterval = value;
            SaveSettings();
        }
    }

    private bool _autoSaveEnabled = true;
    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set
        {
            _autoSaveEnabled = value;
            SaveSettings();
        }
    }

    private bool _startSaveEnabled = true;
    public bool StartSaveEnabled
    {
        get => _startSaveEnabled;
        set
        {
            _startSaveEnabled = value;
            SaveSettings();
        }
    }

    private int _maxAutoSaves = 5;
    public int MaxAutoSaves
    {
        get => _maxAutoSaves;
        set
        {
            _maxAutoSaves = value;
            SaveSettings();
        }
    }
    
    private int _maxStartSaves = 3;
    public int MaxStartSaves
    {
        get => _maxStartSaves;
        set
        {
            _maxStartSaves = value;
            SaveSettings();
        }
    }    private void SaveSettings()
    {
        // Get the instance of Settings class if available
        var settings = SaveVaultApp.Models.Settings.Instance;
        if (settings != null)
        {
            // Ensure the app settings get saved
            settings.Save();
        }
        else
        {
            // If no instance, try to load or create a new one and save
            settings = Settings.Load();
            if (settings != null)
            {
                settings.Save();
            }
        }
    }
}