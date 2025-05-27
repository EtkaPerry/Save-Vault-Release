using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
    // Static instance handling
    private static Settings? _instance;
    public static Settings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Load();
                Debug.WriteLine("Created new Settings instance via Instance property");
            }
            return _instance;
        }
        private set => _instance = value;
    }

    // Static constructor to ensure initialization
    static Settings()
    {
        if (_instance == null)
        {
            _instance = Load();
            Debug.WriteLine("Static constructor: Settings instance initialized");
        }
    }

    // Changed to use property to ensure initialization
    private static string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SaveVault",
        "settings.json"
    );

    // Changed to use the _settingsPath field
    private static string SettingsPath => _settingsPath;
    
    // Method to use alternative settings path
    public static void UseAlternativeSettingsPath(string alternativePath)
    {
        _settingsPath = alternativePath;
    }
    
    // Public method to get the current settings path
    public static string GetSettingsPath()
    {
        return _settingsPath;
    }
    
    // User authentication settings
    private string? _loggedInUser;
    public string? LoggedInUser
    {
        get => _loggedInUser;
        set
        {
            _loggedInUser = value;
            QueueSave();
        }
    }

    private string? _authToken;
    public string? AuthToken
    {
        get => _authToken;
        set
        {
            _authToken = value;
            QueueSave();
        }
    }

    private string _sortOption = "Last Used";
    public string SortOption
    {
        get => _sortOption;
        set
        {
            _sortOption = value;
            QueueSave();
        }
    }

    // Sidebar visibility setting
    private bool _isSidebarVisible = true;
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            _isSidebarVisible = value;
            QueueSave();
        }
    }

    private bool _hiddenGamesExpanded = true;
    public bool HiddenGamesExpanded
    {
        get => _hiddenGamesExpanded;
        set
        {
            _hiddenGamesExpanded = value;
            QueueSave();
        }
    }
    
    // Theme setting
    private string? _theme = "System";
    public string? Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            QueueSave();
        }
    }
    
    // Auto-save settings
    private int _autoSaveInterval = 15;
    public int AutoSaveInterval
    {
        get => _autoSaveInterval;
        set
        {
            _autoSaveInterval = value;
            QueueSave();
        }
    }

    private bool _globalAutoSaveEnabled = false;
    public bool GlobalAutoSaveEnabled
    {
        get => _globalAutoSaveEnabled;
        set
        {
            _globalAutoSaveEnabled = value;
            QueueSave();
        }
    }

    private bool _startSaveEnabled = true;
    public bool StartSaveEnabled
    {
        get => _startSaveEnabled;
        set
        {
            _startSaveEnabled = value;
            QueueSave();
        }
    }

    private int _maxAutoSaves = 3;
    public int MaxAutoSaves
    {
        get => _maxAutoSaves;
        set
        {
            _maxAutoSaves = value;
            QueueSave();
        }
    }

    private int _maxStartSaves = 2;
    public int MaxStartSaves
    {
        get => _maxStartSaves;
        set
        {
            _maxStartSaves = value;
            QueueSave();
        }
    }

    // Backup storage location
    private string _backupStorageLocation = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveVault", "Backups");
    public string BackupStorageLocation
    {
        get => _backupStorageLocation;
        set
        {
            _backupStorageLocation = value;
            QueueSave();
        }
    }
        
    // Update settings
    private bool _autoCheckUpdates = true;
    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            _autoCheckUpdates = value;
            QueueSave();
        }
    }

    // Add change detection before backup setting
    private bool _changeDetectionEnabled = true;
    public bool ChangeDetectionEnabled
    {
        get => _changeDetectionEnabled;
        set
        {
            _changeDetectionEnabled = value;
            QueueSave();
        }
    }    private DateTime _lastUpdateCheck = DateTime.MinValue;
    public DateTime LastUpdateCheck
    {
        get => _lastUpdateCheck;
        set
        {
            _lastUpdateCheck = value;
            QueueSave();
        }
    }
    
    // Last check for notifications
    private DateTime _lastNotificationCheck = DateTime.MinValue;
    public DateTime LastNotificationCheck
    {
        get => _lastNotificationCheck;
        set
        {
            _lastNotificationCheck = value;
            QueueSave();
        }
    }

    private DateTime _legalAcceptanceDate = DateTime.Parse("2025-05-24");  // Default to initial acceptance date
    public DateTime LegalAcceptanceDate
    {
        get => _legalAcceptanceDate;
        set
        {
            _legalAcceptanceDate = value;
            QueueSave();
        }
    }

    private int _updateCheckInterval = 24;
    public int UpdateCheckInterval
    {
        get => _updateCheckInterval;
        set
        {
            _updateCheckInterval = value;
            QueueSave();
        }
    }
    
    // Window settings
    private double _windowWidth = 1200;
    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            _windowWidth = value;
            QueueSave();
        }
    }

    private double _windowHeight = 800;
    public double WindowHeight
    {
        get => _windowHeight;
        set
        {
            _windowHeight = value;
            QueueSave();
        }
    }

    private double _windowPositionX = double.NaN;
    public double WindowPositionX
    {
        get => _windowPositionX;
        set
        {
            _windowPositionX = value;
            QueueSave();
        }
    }

    private double _windowPositionY = double.NaN;
    public double WindowPositionY
    {
        get => _windowPositionY;
        set
        {
            _windowPositionY = value;
            QueueSave();
        }
    }

    private bool _isMaximized = false;
    public bool IsMaximized
    {
        get => _isMaximized;
        set
        {
            _isMaximized = value;
            QueueSave();
        }
    }

    // Options Window settings
    private double _optionsWindowWidth = 750;
    public double OptionsWindowWidth
    {
        get => _optionsWindowWidth;
        set
        {
            _optionsWindowWidth = value;
            QueueSave();
        }
    }

    private double _optionsWindowHeight = 500;
    public double OptionsWindowHeight
    {
        get => _optionsWindowHeight;
        set
        {
            _optionsWindowHeight = value;
            QueueSave();
        }
    }

    private double _optionsWindowPositionX = double.NaN;
    public double OptionsWindowPositionX
    {
        get => _optionsWindowPositionX;
        set
        {
            _optionsWindowPositionX = value;
            QueueSave();
        }
    }

    private double _optionsWindowPositionY = double.NaN;
    public double OptionsWindowPositionY
    {
        get => _optionsWindowPositionY;
        set
        {
            _optionsWindowPositionY = value;
            QueueSave();
        }
    }

    private bool _isOptionsMaximized = false;
    public bool IsOptionsMaximized
    {
        get => _isOptionsMaximized;
        set
        {
            _isOptionsMaximized = value;
            QueueSave();
        }
    }
    
    // Terms and Conditions acceptance
    private bool _termsAccepted = false;
    public bool TermsAccepted
    {
        get => _termsAccepted;
        set
        {
            _termsAccepted = value;
            QueueSave();
        }
    }

    // Application specific settings
    // For collections, we'll need special handling since they can be modified directly
    private Dictionary<string, DateTime> _lastUsedTimes = new();
    public Dictionary<string, DateTime> LastUsedTimes
    {
        get => _lastUsedTimes;
        set
        {
            _lastUsedTimes = value;
            QueueSave();
        }
    }

    private Dictionary<string, DateTime> _lastBackupTimes = new();
    public Dictionary<string, DateTime> LastBackupTimes
    {
        get => _lastBackupTimes;
        set
        {
            _lastBackupTimes = value;
            QueueSave();
        }
    }

    private Dictionary<string, string> _customNames = new();
    public Dictionary<string, string> CustomNames
    {
        get => _customNames;
        set
        {
            _customNames = value;
            QueueSave();
        }
    }

    private Dictionary<string, string> _customSavePaths = new();
    public Dictionary<string, string> CustomSavePaths
    {
        get => _customSavePaths;
        set
        {
            _customSavePaths = value;
            QueueSave();
        }
    }

    private HashSet<string> _hiddenApps = new();
    public HashSet<string> HiddenApps
    {
        get => _hiddenApps;
        set
        {
            _hiddenApps = value;
            QueueSave();
        }
    }

    private HashSet<string> _knownApplicationPaths = new();
    public HashSet<string> KnownApplicationPaths
    {
        get => _knownApplicationPaths;
        set
        {
            _knownApplicationPaths = value;
            QueueSave();
        }
    }

    private Dictionary<string, AppSpecificSettings> _appSettings = new();
    public Dictionary<string, AppSpecificSettings> AppSettings
    {
        get => _appSettings;
        set
        {
            _appSettings = value;
            QueueSave();
        }
    }
    
    // Notification storage
    private List<Notification> _notifications = new();
    public List<Notification> Notifications
    {
        get => _notifications;
        set
        {
            _notifications = value;
            QueueSave();
        }
    }

    // Backup history storage
    private Dictionary<string, List<SaveBackupInfo>> _backupHistory = new();
    public Dictionary<string, List<SaveBackupInfo>> BackupHistory
    {
        get => _backupHistory;
        set
        {
            _backupHistory = value;
            QueueSave();
        }
    }
    
    // File state tracking for change detection
    private Dictionary<string, Dictionary<string, string>> _lastFileStates = new();
    public Dictionary<string, Dictionary<string, string>> LastFileStates
    {
        get => _lastFileStates;
        set
        {
            _lastFileStates = value;
            QueueSave();
        }
    }    // Constructor that ensures this instance is the current static instance
    public Settings()
    {
        // Set this instance as the current static instance
        // This ensures that any instance creation updates the static reference
        _instance = this;
        
        // Initialize the save timer - checks every 2 seconds if there are pending saves
        _saveTimer = new System.Threading.Timer(_ => 
        {
            if (_hasUnsavedChanges && DateTime.Now - _lastSaveTime >= _saveThrottleInterval)
            {
                try
                {
                    Save();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in save timer: {ex.Message}");
                }
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        
        // Register single app exit handler to ensure settings are saved
        // Only register this once to avoid multiple handlers
        if (_instance == null)
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => {
                Debug.WriteLine("Application exiting, saving settings...");
                ForceSave();
            };
        }
    }

    public static Settings Load()
    {
        // If we already have a valid instance, use it
        if (_instance != null)
        {
            Debug.WriteLine("Load(): Returning existing Settings instance");
            return _instance;
        }

        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            Debug.WriteLine($"Loading settings from directory: {directory}");
            
            if (!Directory.Exists(directory) && directory != null)
            {
                Debug.WriteLine($"Creating settings directory during load: {directory}");
                Directory.CreateDirectory(directory);
            }
            
            Settings settings;
            
            if (File.Exists(SettingsPath))
            {
                Debug.WriteLine($"Settings file exists at: {SettingsPath}");
                var json = File.ReadAllText(SettingsPath);

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    IncludeFields = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                };

                settings = JsonSerializer.Deserialize<Settings>(json, options) ?? new Settings();
            }
            else
            {
                Debug.WriteLine("No settings file found, creating new settings");
                settings = new Settings();
            }

            // Ensure all collections are initialized
            settings.LastUsedTimes ??= new();
            settings.LastBackupTimes ??= new();
            settings.CustomNames ??= new();
            settings.CustomSavePaths ??= new();
            settings.HiddenApps ??= new();
            settings.KnownApplicationPaths ??= new();
            settings.AppSettings ??= new();
            settings.BackupHistory ??= new();
            settings.LastFileStates ??= new();
            settings.Notifications ??= new();

            // Set default values for any unset properties
            if (string.IsNullOrEmpty(settings.SortOption))
                settings.SortOption = "Last Used";
            
            if (string.IsNullOrEmpty(settings.Theme))
                settings.Theme = "System";
            
            if (settings.AutoSaveInterval <= 0)
                settings.AutoSaveInterval = 15;
            
            if (settings.MaxAutoSaves <= 0)
                settings.MaxAutoSaves = 3;
            
            if (settings.MaxStartSaves <= 0)
                settings.MaxStartSaves = 2;
            
            if (string.IsNullOrEmpty(settings.BackupStorageLocation))
                settings.BackupStorageLocation = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SaveVault", "Backups");
            
            if (settings.UpdateCheckInterval <= 0)
                settings.UpdateCheckInterval = 24;
            
            // Set default for change detection (on by default)
            settings.ChangeDetectionEnabled = true;

            // Set this as the static instance
            _instance = settings;
            Debug.WriteLine("New settings instance created and set as static instance");

            // Save the settings if they were just created
            if (!File.Exists(SettingsPath))
            {
                settings.ForceSave();
            }

            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading settings: {ex.Message}");
            
            // Return new settings instance if loading fails
            var newSettings = new Settings();
            _instance = newSettings;
            
            try
            {
                Debug.WriteLine("Saving new default settings");
                newSettings.ForceSave();
            }
            catch (Exception saveEx)
            {
                Debug.WriteLine($"Failed to save new settings: {saveEx.Message}");
            }
            
            return newSettings;
        }
    }

    // Add method to update a collection item and save changes
    public void UpdateDictionary<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key, TValue value) 
        where TKey : notnull
    {
        dict[key] = value;
        QueueSave();
    }
    
    // Add method to remove from a collection and save changes
    public bool RemoveFromDictionary<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key) 
        where TKey : notnull
    {
        var result = dict.Remove(key);
        if (result)
        {
            QueueSave();
        }
        return result;
    }
    
    // Add/Update methods for collections with immediate save option
    public void AddOrUpdateLastUsedTime(string key, DateTime value, bool saveImmediately = false)
    {
        LastUsedTimes[key] = value;
        if (saveImmediately)
        {
            ForceSave();
        }
        else
        {
            QueueSave();
        }
    }
    
    public void AddOrUpdateLastBackupTime(string key, DateTime value, bool saveImmediately = false)
    {
        LastBackupTimes[key] = value;
        if (saveImmediately)
        {
            ForceSave();
        }
        else
        {
            QueueSave();
        }
    }
    
    // Changed from static to instance fields for better resource management
    private System.Threading.Timer? _saveTimer;
    private readonly object _saveLock = new object();
    private DateTime _lastSaveTime = DateTime.MinValue;
    private bool _hasUnsavedChanges = false;
    private readonly TimeSpan _saveThrottleInterval = TimeSpan.FromSeconds(2); // Reduced from 5 to 2 seconds
    
    public void QueueSave()
    {
        lock (_saveLock)
        {
            _hasUnsavedChanges = true;
            Debug.WriteLine("Settings change queued for save");
            
            // If we're the static instance, save now
            if (this == _instance)
            {
                Debug.WriteLine("Settings is static instance, saving immediately");
                Save();
            }
            else
            {
                Debug.WriteLine("WARNING: Settings change on non-static instance");
                // Try to save through the static instance
                if (_instance != null)
                {
                    _instance.Save();
                }
                else
                {
                    Debug.WriteLine("CRITICAL: No static settings instance available!");
                }
            }
        }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            if (!_hasUnsavedChanges)
            {
                Debug.WriteLine("No changes to save");
                return;
            }

            try
            {
                Debug.WriteLine($"Saving settings to: {SettingsPath}");
                
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                // Always use atomic save to prevent corruption
                var tempPath = Path.Combine(
                    Path.GetDirectoryName(SettingsPath) ?? string.Empty,
                    $"settings.{Guid.NewGuid():N}.tmp"
                );

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    IncludeFields = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                };

                // First verify we can serialize
                var json = JsonSerializer.Serialize(this, options);
                
                // Write to temp file
                File.WriteAllText(tempPath, json);

                // Create backup of current settings if it exists
                if (File.Exists(SettingsPath))
                {
                    var backupPath = SettingsPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(SettingsPath, backupPath);
                }

                // Move temp file to final location
                File.Move(tempPath, SettingsPath);

                _lastSaveTime = DateTime.Now;
                _hasUnsavedChanges = false;

                Debug.WriteLine("Settings saved successfully");

                var logger = Services.LoggingService.Instance;
                if (logger != null)
                {
                    // Log key settings values after save
                    logger.Debug($"Settings saved - AutoSaveInterval: {AutoSaveInterval}, GlobalAutoSaveEnabled: {GlobalAutoSaveEnabled}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
                if (Services.LoggingService.Instance != null)
                {
                    Services.LoggingService.Instance.Error($"Failed to save settings: {ex.Message}");
                }
            }
        }
    }

    // Enhanced ForceSave that always saves immediately
    public void ForceSave()
    {
        lock (_saveLock)
        {
            _hasUnsavedChanges = true;
            Save();
        }
    }    // Method to debug settings by showing all set properties
    public void LogActiveSettings()
    {
        var logger = SaveVaultApp.Services.LoggingService.Instance;
        logger.Debug("Active Settings Properties:");
        
        // Log basic properties
        logger.Debug($"Theme: {Theme}");
        logger.Debug($"SortOption: {SortOption}");
        logger.Debug($"HiddenGamesExpanded: {HiddenGamesExpanded}");
        logger.Debug($"GlobalAutoSaveEnabled: {GlobalAutoSaveEnabled}");
        logger.Debug($"StartSaveEnabled: {StartSaveEnabled}");
        logger.Debug($"ChangeDetectionEnabled: {ChangeDetectionEnabled}");
        logger.Debug($"AutoSaveInterval: {AutoSaveInterval}");
        logger.Debug($"MaxAutoSaves: {MaxAutoSaves}");
        logger.Debug($"MaxStartSaves: {MaxStartSaves}");
        
        // Log collections sizes
        logger.Debug($"LastUsedTimes count: {LastUsedTimes?.Count ?? 0}");
        logger.Debug($"LastBackupTimes count: {LastBackupTimes?.Count ?? 0}");
        logger.Debug($"CustomNames count: {CustomNames?.Count ?? 0}");
        logger.Debug($"HiddenApps count: {HiddenApps?.Count ?? 0}");
        logger.Debug($"AppSettings count: {AppSettings?.Count ?? 0}");
        logger.Debug($"BackupHistory count: {BackupHistory?.Count ?? 0}");
        logger.Debug($"LastFileStates count: {LastFileStates?.Count ?? 0}");
    }
    
    // File state tracking and change detection methods
    
    /// <summary>
    /// Updates the stored state of files for a specific application
    /// </summary>
    /// <param name="appId">The application identifier</param>
    /// <param name="fileStates">Dictionary of file paths and their hash/state</param>
    public void UpdateFileStates(string appId, Dictionary<string, string> fileStates)
    {
        LastFileStates[appId] = fileStates;
        QueueSave();
    }
    
    /// <summary>
    /// Compares the current file states with the previously stored states
    /// </summary>
    /// <param name="appId">The application identifier</param>
    /// <param name="currentStates">Current file states (path->hash dictionary)</param>
    /// <returns>True if changes are detected, false otherwise</returns>
    public bool HasChanges(string appId, Dictionary<string, string> currentStates)
    {
        // If change detection is disabled, always return true to force backup
        if (!ChangeDetectionEnabled)
        {
            return true;
        }
        
        // If we don't have previous states, it's a change
        if (!LastFileStates.ContainsKey(appId))
        {
            return true;
        }
        
        var previousStates = LastFileStates[appId];
        
        // If file count differs, something changed
        if (previousStates.Count != currentStates.Count)
        {
            return true;
        }
        
        // Check each file's state
        foreach (var file in currentStates)
        {
            if (!previousStates.ContainsKey(file.Key) || previousStates[file.Key] != file.Value)
            {
                return true;  // File is new or changed
            }
        }
        
        return false;  // No changes detected
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
                
                // Check write permissions by creating a test file
                try {
                    string testPath = Path.Combine(saveVaultDir, $"test_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(testPath, "Test write permissions");
                    logger.Debug($"✅ Successfully wrote test file: {testPath}");
                    
                    if (File.Exists(testPath)) {
                        File.Delete(testPath);
                        logger.Debug("✅ Successfully deleted test file");
                    }
                }
                catch (Exception ex) {
                    logger.Error($"❌ Directory permission test failed: {ex.Message}");
                }
            }
            else {
                logger.Warning("SaveVault directory does not exist in AppData");
                try {
                    Directory.CreateDirectory(saveVaultDir);
                    logger.Debug($"Created SaveVault directory: {Directory.Exists(saveVaultDir)}");
                }
                catch (Exception ex) {
                    logger.Error($"Failed to create SaveVault directory: {ex.Message}");
                }
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
            QueueSettingsSave();
        }
    }

    private int _autoSaveInterval = 15;
    public int AutoSaveInterval
    {
        get => _autoSaveInterval;
        set
        {
            _autoSaveInterval = value;
            QueueSettingsSave();
        }
    }

    private bool _autoSaveEnabled = true;
    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set
        {
            _autoSaveEnabled = value;
            QueueSettingsSave();
        }
    }

    private bool _startSaveEnabled = true;
    public bool StartSaveEnabled
    {
        get => _startSaveEnabled;
        set
        {
            _startSaveEnabled = value;
            QueueSettingsSave();
        }
    }

    private int _maxAutoSaves = 5;
    public int MaxAutoSaves
    {
        get => _maxAutoSaves;
        set
        {
            _maxAutoSaves = value;
            QueueSettingsSave();
        }
    }
    
    private int _maxStartSaves = 3;
    public int MaxStartSaves
    {
        get => _maxStartSaves;
        set
        {
            _maxStartSaves = value;
            QueueSettingsSave();
        }
    }
    
    private bool _changeDetectionEnabled = true;
    public bool ChangeDetectionEnabled
    {
        get => _changeDetectionEnabled;
        set
        {
            _changeDetectionEnabled = value;
            QueueSettingsSave();
        }
    }

    private void QueueSettingsSave()
    {
        var settings = Settings.Instance;
        if (settings != null)
        {
            settings.QueueSave();
        }
    }
}