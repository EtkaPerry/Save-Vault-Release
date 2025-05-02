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
        "settings.json"
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
    }

    public static Settings Load()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(SettingsPath))
            {
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
            // Make sure this instance is set as the static instance
            if (_instance != this)
            {
                _instance = this;
            }
            
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
            
            Debug.WriteLine($"Settings saved successfully to {SettingsPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving settings: {ex.Message}");
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