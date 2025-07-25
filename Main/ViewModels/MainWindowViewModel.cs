using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Timers;
using ReactiveUI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia;

namespace SaveVaultApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{    private readonly Settings _settings;
    public Settings Settings => _settings;    
    
    // Changed from readonly with null! to just private field that can be properly initialized
    private AppData _appData;
    public AppData AppData => _appData; // This now serves the purpose of the former AppData
    
    public ObservableCollection<ApplicationInfo> InstalledApps { get; } = new();
    public ObservableCollection<ApplicationInfo> HiddenGames { get; } = new();    // Add backup timer
    private readonly System.Timers.Timer _backupTimer;
    private readonly string _backupRootFolder;
    
    // Add timer for UI updates
    private readonly System.Timers.Timer _uiRefreshTimer;
    
    private bool _isHiddenGamesExpanded;
    public bool IsHiddenGamesExpanded
    {
        get => _isHiddenGamesExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _isHiddenGamesExpanded, value);
            _settings.HiddenGamesExpanded = value;
            _settings.Save();
        }
    }    private ApplicationInfo? _selectedApp;
    public ApplicationInfo? SelectedApp
    {
        get => _selectedApp;
        set 
        {
            // Check if we need to refresh the save path when changing selection
            if (value != null && value != _selectedApp && 
                (string.IsNullOrEmpty(value.SavePath) || value.SavePath == "Unknown"))
            {
                // Try to detect the save path if it's missing
                string newPath = DetectSavePath(value);
                if (!string.IsNullOrEmpty(newPath) && newPath != "Unknown")
                {
                    value.SavePath = newPath;
                }
            }
            
            this.RaiseAndSetIfChanged(ref _selectedApp, value);
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private ObservableCollection<ApplicationInfo> _runningApps = new();
    public ObservableCollection<ApplicationInfo> RunningApps
    {
        get => _runningApps;
        set => this.RaiseAndSetIfChanged(ref _runningApps, value);
    }
    
    [RelayCommand]
    private void SelectRunningApp(ApplicationInfo app)
    {
        if (app != null)
        {
            SelectedApp = InstalledApps.FirstOrDefault(a => 
                string.Equals(a.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }
    }
    
    private readonly System.Timers.Timer _processCheckTimer;
    
    // Cancellation token for scanning operations
    private CancellationTokenSource? _scanCancellationTokenSource;
      private string _nextSaveText = string.Empty;
    public string NextSaveText
    {
        get => _nextSaveText;
        set => this.RaiseAndSetIfChanged(ref _nextSaveText, value);
    }

    private bool _isHoveringNextSave;
    public bool IsHoveringNextSave
    {
        get => _isHoveringNextSave;
        set => this.RaiseAndSetIfChanged(ref _isHoveringNextSave, value);
    }

    private bool _isSidebarVisible;
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set 
        {
            this.RaiseAndSetIfChanged(ref _isSidebarVisible, value);
            if (_settings != null)
            {
                _settings.IsSidebarVisible = value;
                _settings.Save();
            }
        }
    }
      private bool _isLoginPopupOpen;
    public bool IsLoginPopupOpen
    {
        get => _isLoginPopupOpen;
        set => this.RaiseAndSetIfChanged(ref _isLoginPopupOpen, value);
    }
    
    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }
    
    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }
    
    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }
    
    public bool IsLoggedIn => !string.IsNullOrEmpty(_settings.LoggedInUser) && !_settings.OfflineMode;
    
    public string LoginStatusText => 
        _settings.OfflineMode ? "Go Online" : 
        (IsLoggedIn ? $"Logged in as {_settings.LoggedInUser}" : "Login");
    
    // Property to get the first letter of the logged-in username for the avatar
    public string LoggedInInitial => IsLoggedIn && !string.IsNullOrEmpty(_settings.LoggedInUser) 
        ? _settings.LoggedInUser.Substring(0, 1).ToUpper() 
        : "?";
        
    // Property to check if we're in offline mode
    public bool IsOfflineMode => _settings.OfflineMode;

    // Method to refresh properties related to offline status
    public void RefreshOfflineStatus()
    {
        this.RaisePropertyChanged(nameof(IsOfflineMode));
        this.RaisePropertyChanged(nameof(IsLoggedIn));
        this.RaisePropertyChanged(nameof(LoginStatusText));
    }

    // Changed to internal to allow access from App.axaml.cs
    internal Window? _mainWindow; 
    public bool IsExiting { get; set; }
    
    // Property to control whether program search is enabled
    public bool IsSearchEnabled { get; set; } = false;
    
    // Constructor to initialize the application
    public MainWindowViewModel()
    {
        // Load settings and make sure static instance is updated
        _settings = Settings.Load();
        
        // If for some reason the static instance is not set, set it now
        if (Settings.Instance == null)
        {
            // This will update the static instance
            new Settings();
            
            // Now reload the settings from file
            _settings = Settings.Load();
        }        // Load or initialize appdata with proper null handling
        try
        {
            _appData = AppData.Load();
            
            // Ensure we have a valid instance
            if (_appData == null)
            {
                _appData = new AppData();
                Debug.WriteLine("Created new AppData instance because Load() returned null");
            }
            
            // Make sure we have valid collections in AppData
            _appData.LastBackupTimes ??= new Dictionary<string, DateTime>();
            _appData.CustomNames ??= new Dictionary<string, string>();
            _appData.CustomSavePaths ??= new Dictionary<string, string>();
            _appData.HiddenApps ??= new HashSet<string>();
            _appData.KnownApplicationPaths ??= new HashSet<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading AppData: {ex.Message}");
            
            // Create a new instance if anything fails
            _appData = new AppData();
            _appData.LastBackupTimes = new Dictionary<string, DateTime>();
            _appData.CustomNames = new Dictionary<string, string>();
            _appData.CustomSavePaths = new Dictionary<string, string>();
            _appData.HiddenApps = new HashSet<string>();
            _appData.KnownApplicationPaths = new HashSet<string>();
        }
        
        // If this is the first run or appdata is empty, migrate settings to app data
        var appDataExists = File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SaveVault", "appdata.json"));
            
        if (!appDataExists || 
            (_appData.KnownApplicationPaths.Count == 0 && 
             _appData.CustomNames.Count == 0 && 
             _appData.CustomSavePaths.Count == 0 &&
             _appData.LastBackupTimes.Count == 0))
        {
            AppData.MigrateFromSettings(_settings);
        }
        
        _selectedSortOption = _settings.SortOption;
        _isHiddenGamesExpanded = _settings.HiddenGamesExpanded;
        _isSidebarVisible = _settings.IsSidebarVisible; // Initialize sidebar visibility
        
        // Initialize update service
        var updateService = UpdateService.Instance;
        updateService.UpdateStatusChanged += (s, status) => {
            UpdateStatus = status;
        };
        updateService.UpdateAvailabilityChanged += (s, available) => {
            UpdateAvailable = available;
            if (available && updateService.LatestVersion != null) {
                UpdateVersion = updateService.LatestVersion.Version;
                IsUpdateNotificationVisible = true;
            }
        };
        updateService.DownloadProgressChanged += (s, progress) => {
            DownloadProgress = progress;
        };
          // Initialize notification service
        var notificationService = NotificationService.Instance;
        notificationService.UnreadNotificationsChanged += (s, hasUnread) => {
            HasUnreadNotifications = hasUnread;
        };        
        
        // Set up property changed handler to watch for offline mode changes
        this.PropertyChanged += (sender, args) => {
            if (args.PropertyName == nameof(IsOfflineMode) && IsOfflineMode && IsNotificationsVisible) {
                // If we go offline while viewing notifications, go back to home
                IsNotificationsVisible = false;
                SelectedApp = null;
                StatusMessage = "Notifications are disabled in offline mode. Returned to home screen.";
            }
            // Note: Save Carrier works offline, so we don't need to hide it when going offline
        };
        
        notificationService.NotificationsUpdated += (s, notifications) => {
            Notifications.Clear();
            
            // Add notifications from the list which is already sorted (newest first)
            foreach (var notification in notifications)
            {
                Notifications.Add(notification);
            }
        };
        
        // Load initial notifications from notification service
        var initialNotifications = notificationService.GetNotifications();
        if (initialNotifications.Any())
        {
            Notifications.Clear();
            foreach (var notification in initialNotifications)
            {
                Notifications.Add(notification);
            }
            HasUnreadNotifications = initialNotifications.Any(n => !n.IsRead);
        }
        
        // Initialize backup root folder
        _backupRootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SaveVault", "Backups");
            
        // Create backup directory if it doesn't exist
        if (!Directory.Exists(_backupRootFolder))
        {
            Directory.CreateDirectory(_backupRootFolder);
        }
          // Initialize process check timer with shorter interval
        _processCheckTimer = new System.Timers.Timer(3000); // Check every 3 seconds
        _processCheckTimer.Elapsed += OnProcessCheckTimerElapsed;
        _processCheckTimer.AutoReset = true;
        
        // Initialize backup timer - check every second
        _backupTimer = new System.Timers.Timer(1000); // Check every sec
        _backupTimer.Elapsed += OnBackupTimerElapsed;
        _backupTimer.AutoReset = true;
        // Always start the backup timer regardless of global settings
        // We need to keep checking for apps with custom settings
        _backupTimer.Start(); 
        Debug.WriteLine("Auto-save timer started with interval: " + _settings.AutoSaveInterval + " minutes");
        
        // Initialize UI refresh timer - update every second
        _uiRefreshTimer = new System.Timers.Timer(1000); // Exactly 1 second
        _uiRefreshTimer.Elapsed += OnUIRefreshTimerElapsed;
        _uiRefreshTimer.AutoReset = true;
        _uiRefreshTimer.Start(); // Start this timer immediately
        
        // Program search will be initialized after Terms are accepted
        // See App.axaml.cs for the logic that enables search and calls InitializeApplicationSearch()
        
        ExitApplicationCommand = new RelayCommand(() => 
        {
            IsExiting = true;
            _mainWindow?.Close();
            
            // Force exit the application process
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            
            // Ensure complete exit
            Environment.Exit(0);
        });
    }

    // Add a method to initialize the application search
    public void InitializeApplicationSearch()
    {
        // Check if search is enabled
        if (!IsSearchEnabled)
        {
            Services.LoggingService.Instance.Info("Program search is disabled, skipping initialization");
            return;
        }
        
        Services.LoggingService.Instance.Info("Initializing application search");
        
        // Load the installed applications
        Task.Run(LoadInstalledAppsAsync).ContinueWith(_ => 
        {
            // Start process monitoring after apps are loaded
            _processCheckTimer.Start();
            if (_settings.GlobalAutoSaveEnabled)
            {
                _backupTimer.Start();
                Debug.WriteLine($"Auto-save enabled with {_settings.AutoSaveInterval} minute interval");
            }
            UpdateRunningApplications();
            StartBackgroundAppRefresh(); // Start background refresh after initial load
        });
    }
    
    public void Initialize(Window window)
    {
        _mainWindow = window;
        _mainWindow.WindowState = WindowState.Normal;
    }

    private void ShowWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }
    
    private void OnProcessCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        UpdateRunningApplications();
    }
    
    private void OnBackupTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            var currentTime = DateTime.Now;
            // Find all currently running apps that have a valid save path
            var runningApps = AllInstalledApps
                .Where(app => app.IsRunning && 
                              !string.IsNullOrEmpty(app.SavePath) && 
                              app.SavePath != "Unknown" && 
                              Directory.Exists(app.SavePath))
                .ToList();
            
            if (runningApps.Count > 0)
            {
                foreach (var app in runningApps)
                {
                    try 
                    {
                        // Get auto-save settings based on whether app has custom settings
                        bool isAutoSaveEnabled = app.HasCustomSettings
                            ? app.CustomSettings.AutoSaveEnabled  // If app has custom settings, use those
                            : _settings.GlobalAutoSaveEnabled;    // Otherwise use global setting

                        int effectiveInterval = app.HasCustomSettings
                            ? app.CustomSettings.AutoSaveInterval
                            : _settings.AutoSaveInterval;

                        // If app has custom settings and auto-save is enabled, proceed regardless of global setting
                        // Otherwise, use global setting
                        if (!(app.HasCustomSettings ? app.CustomSettings.AutoSaveEnabled : _settings.GlobalAutoSaveEnabled))
                        {
                            continue; // Skip if auto-save is disabled for this app
                        }
                        
                        // Get the last backup time
                        DateTime lastBackup;
                        if (app.LastBackupTime != DateTime.MinValue)
                        {
                            lastBackup = app.LastBackupTime;
                        }                        else if (_appData.LastBackupTimes.TryGetValue(app.ExecutablePath, out DateTime appDataLastBackup))
                        {
                            lastBackup = appDataLastBackup;
                            app.LastBackupTime = appDataLastBackup; // Sync the LastBackupTime
                        }                        else if (_settings.LastBackupTimes.TryGetValue(app.ExecutablePath, out DateTime settingsLastBackup))
                        {
                            // Legacy fallback to settings if not in _appData yet
                            lastBackup = settingsLastBackup;
                            app.LastBackupTime = settingsLastBackup; // Sync the LastBackupTime
                            // Store in _appData for future use
                            _appData.LastBackupTimes[app.ExecutablePath] = settingsLastBackup;
                            _appData.Save();
                        }
                        else
                        {
                            lastBackup = DateTime.MinValue;
                        }

                        // Ensure interval is at least 1 minute
                        if (effectiveInterval < 1) effectiveInterval = 1;

                        // Calculate time difference in total minutes, rounding down to ensure full minutes have passed
                        var timeSinceLastBackup = Math.Floor((currentTime - lastBackup).TotalMinutes);
                        
                        if (timeSinceLastBackup >= effectiveInterval)
                        {
                            Debug.WriteLine($"Creating backup for {app.Name}. Time since last backup: {timeSinceLastBackup:F0} minutes, Interval: {effectiveInterval} minutes");
                            CreateBackup(app);
                        }
                        else
                        {
                            Debug.WriteLine($"Not yet time for backup of {app.Name}. Time since last: {timeSinceLastBackup:F0} minutes, Need: {effectiveInterval} minutes");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing backup for app {app.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during automatic backup check: {ex.Message}");
        }
    }
      private void CreateBackup(ApplicationInfo app)
    {
        try
        {
            // Skip if save path is invalid
            if (string.IsNullOrEmpty(app.SavePath) || app.SavePath == "Unknown" || !Directory.Exists(app.SavePath))
                return;

            // Check auto-save settings based on custom settings
            bool shouldAutoSave = app.HasCustomSettings
                ? app.CustomSettings.AutoSaveEnabled
                : _settings.GlobalAutoSaveEnabled;

            if (!shouldAutoSave)
                return;

            // Get all files in the save directory for change detection
            var saveFiles = Directory.GetFiles(app.SavePath, "*", SearchOption.AllDirectories);
            
            // Check if change detection is enabled (global or app-specific setting)
            bool changeDetectionEnabled = app.HasCustomSettings
                ? app.CustomSettings.ChangeDetectionEnabled
                : _settings.ChangeDetectionEnabled;
                  // Use change detection if enabled
            if (changeDetectionEnabled)
            {
                Debug.WriteLine($"Change detection enabled for {app.Name}, checking {saveFiles.Length} files...");                // Check if there are changes since last backup - pass app-specific settings if available
                bool hasChanges = Utilities.FileChangeDetector.HaveFilesChanged(
                    app.ExecutablePath, 
                    saveFiles, 
                    app.HasCustomSettings ? app.CustomSettings : null
                );
                  // Log the change detection result
                Utilities.LoggingExtensions.LogChangeDetection(app.ExecutablePath, hasChanges, saveFiles.Length);
                  // If no changes detected, reset the timer and skip the backup
                if (!hasChanges)
                {
                    Debug.WriteLine($"No changes detected for {app.Name}, skipping backup and resetting timer");
                    Services.LoggingService.Instance?.Debug($"Skipped backup for {app.Name} - No changes detected");
                    
                    // Reset the last backup time to 'now' to make the timer start over
                    DateTime resetTime = DateTime.Now;
                    app.LastBackupTime = resetTime;
                    
                    // Update timer in app data to ensure it persists
                    _appData.LastBackupTimes[app.ExecutablePath] = resetTime;
                    _appData.Save();
                    
                    // Update next save text immediately since we've reset the timer
                    if (SelectedApp == app)
                    {
                        UpdateNextSaveText();
                    }
                    
                    return;
                }
                
                Debug.WriteLine($"Changes detected for {app.Name}, creating backup");
                Services.LoggingService.Instance?.Debug($"Creating backup for {app.Name} - Changes detected in save files");
            }
            else
            {
                Debug.WriteLine($"Change detection disabled for {app.Name}, creating backup regardless of changes");
            }

            // Get the effective max auto-saves setting
            int maxAutoSaves = app.HasCustomSettings
                ? app.CustomSettings.MaxAutoSaves
                : _settings.MaxAutoSaves;

            // Count existing auto-saves and remove oldest ones if over limit
            var autoSaves = app.BackupHistory
                .Where(b => b.IsAutoBackup)
                .OrderByDescending(b => b.Timestamp)
                .ToList();

            if (autoSaves.Count >= maxAutoSaves)
            {
                // Remove oldest auto-saves that exceed the limit
                foreach (var oldSave in autoSaves.Skip(maxAutoSaves - 1))
                {
                    try
                    {
                        // Delete the backup files
                        if (Directory.Exists(oldSave.BackupPath))
                        {
                            Directory.Delete(oldSave.BackupPath, true);
                        }

                        // Remove from UI collection
                        app.BackupHistory.Remove(oldSave);

                        // Remove from settings
                        if (_settings.BackupHistory.TryGetValue(app.ExecutablePath, out var backupsInSettings))
                        {
                            backupsInSettings.RemoveAll(b => b.BackupPath == oldSave.BackupPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error removing old backup {oldSave.BackupPath}: {ex.Message}");
                    }
                }
            }

            // Generate backup folder path (app-specific)
            string appBackupFolder = Path.Combine(_backupRootFolder, SanitizePathName(app.Name));
            if (!Directory.Exists(appBackupFolder))
            {
                Directory.CreateDirectory(appBackupFolder);
            }
            
            // Create timestamp folder for this specific backup
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupFolder = Path.Combine(appBackupFolder, timestamp);
            Directory.CreateDirectory(backupFolder);
            
            // Count actual files copied
            int filesCopied = 0;
            
            // Copy all files from the save directory to the backup folder
            CopyDirectory(app.SavePath, backupFolder, ref filesCopied);
            
            // Only create a backup entry if files were actually copied
            if (filesCopied > 0)
            {
                DateTime backupTimestamp = DateTime.Now; // Capture the exact time

                // Create backup info (using ViewModel's SaveBackupInfo for UI)
                var backupInfoVM = new ViewModels.SaveBackupInfo
                {
                    BackupPath = backupFolder,
                    Timestamp = backupTimestamp,
                    Description = $"Auto save ({timestamp})",
                    IsAutoBackup = true
                };
                
                // Create backup info for settings (using Model's SaveBackupInfo)
                var backupInfoModel = new Models.SaveBackupInfo
                {
                    BackupPath = backupInfoVM.BackupPath,
                    Timestamp = backupInfoVM.Timestamp,
                    Description = backupInfoVM.Description,
                    IsAutoBackup = backupInfoVM.IsAutoBackup
                };
                
                // Update app's backup history and last backup time (in memory)
                app.LastBackupTime = backupTimestamp;
                  // Update the last backup time in _appData *before* saving
                _appData.LastBackupTimes[app.ExecutablePath] = backupTimestamp;
                _appData.Save();
                
                // Add to history on UI thread
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    app.BackupHistory.Insert(0, backupInfoVM); // Add ViewModel version to UI at the beginning, not the end
                    
                    // Save Model version to settings
                    if (!_settings.BackupHistory.ContainsKey(app.ExecutablePath))
                    {
                        _settings.BackupHistory[app.ExecutablePath] = new List<Models.SaveBackupInfo>();
                    }
                    _settings.BackupHistory[app.ExecutablePath].Add(backupInfoModel);
                    
                    // Update status message if this is the selected app
                    if (SelectedApp == app)
                    {
                        int interval = app.HasCustomSettings ? 
                            app.CustomSettings.AutoSaveInterval : _settings.AutoSaveInterval;
                        StatusMessage = $"Created auto-save for '{app.Name}' (every {interval} minutes)";

                        // Also update next save text immediately
                        UpdateNextSaveText();
                    }
                      // Save settings first to ensure backup history is persisted
                    _settings.Save();
                    
                    // Update file states for change detection after successful backup
                    bool changeDetectionEnabled = app.HasCustomSettings
                        ? app.CustomSettings.ChangeDetectionEnabled
                        : _settings.ChangeDetectionEnabled;
                        
                    if (changeDetectionEnabled)
                    {
                        try
                        {
                            // Get all files in the save directory
                            var saveFiles = Directory.GetFiles(app.SavePath, "*", SearchOption.AllDirectories);
                            
                            // Update the file states for future comparisons
                            Utilities.FileChangeDetector.UpdateFileStates(app.ExecutablePath, saveFiles);
                            
                            // Force save settings again to ensure file states are persisted immediately
                            _settings.Save();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating file states: {ex.Message}");
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating backup for {app.Name}: {ex.Message}");
        }
    }
    
    private void CopyDirectory(string sourceDir, string destDir, ref int filesCopied)
    {
        // Get the subdirectories for the specified directory
        var dir = new DirectoryInfo(sourceDir);
        
        // Skip if directory doesn't exist
        if (!dir.Exists)
            return;
            
        // Create all subdirectories
        DirectoryInfo[] dirs = dir.GetDirectories();
        foreach (DirectoryInfo subdir in dirs)
        {
            string newDestinationDir = Path.Combine(destDir, subdir.Name);
            CopyDirectory(subdir.FullName, newDestinationDir, ref filesCopied);
        }
        
        // Copy all files
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destDir, file.Name);
            Directory.CreateDirectory(destDir); // Ensure destination directory exists
            file.CopyTo(tempPath, true);
            filesCopied++;
        }
    }
      private string SanitizePathName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unknown";
            
        // Remove invalid characters from path
        string invalidChars = new string(Path.GetInvalidFileNameChars());
        string sanitized = string.Join("_", name.Split(invalidChars.ToCharArray(), 
            StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

        // Replace spaces with underscores
        sanitized = sanitized.Replace(' ', '_');
        
        // Make sure we have something valid
        if (string.IsNullOrEmpty(sanitized))
            return "Unknown";
            
        return sanitized;
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set 
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            HandleSearchTextChanged(value);
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private string _selectedSortOption = "Last Used";
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (_selectedSortOption != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedSortOption, value);
                _settings.SortOption = value;
                _settings.ForceSave(); // Force immediate save
                ApplySort();
                Debug.WriteLine($"Sort option changed to {value} and saved.");
            }
        }
    }

    // This command is called from the UI
    [RelayCommand]
    private void SetSortOption(string sortOption)
    {
        if (sortOption != null && sortOption != SelectedSortOption)
        {
            SelectedSortOption = sortOption;
            // Log to verify it was set
            Debug.WriteLine($"SetSortOption command executed with value: {sortOption}");
        }
    }

    [RelayCommand]
    private void LaunchApp()
    {
        if (SelectedApp == null || string.IsNullOrEmpty(SelectedApp.ExecutablePath))
            return;

        try
        {
            // Check if start-save is enabled (either globally or in custom settings)
            bool shouldCreateStartSave = SelectedApp.HasCustomSettings ? 
                SelectedApp.CustomSettings.StartSaveEnabled : 
                _settings.StartSaveEnabled;

            // Create a start save before launching if enabled and save path exists
            if (shouldCreateStartSave && 
                !string.IsNullOrEmpty(SelectedApp.SavePath) && 
                SelectedApp.SavePath != "Unknown" && 
                Directory.Exists(SelectedApp.SavePath))
            {
                // Get the effective max start-saves setting
                int maxStartSaves = SelectedApp.HasCustomSettings
                    ? SelectedApp.CustomSettings.MaxStartSaves
                    : _settings.MaxStartSaves;

                // Count existing start saves and remove oldest ones if over limit
                var startSaves = SelectedApp.BackupHistory
                    .Where(b => b.Description.StartsWith("Start Save"))
                    .OrderByDescending(b => b.Timestamp)
                    .ToList();

                if (startSaves.Count >= maxStartSaves)
                {
                    // Remove oldest start-saves that exceed the limit
                    foreach (var oldSave in startSaves.Skip(maxStartSaves - 1))
                    {
                        try
                        {
                            // Delete the backup files
                            if (Directory.Exists(oldSave.BackupPath))
                            {
                                Directory.Delete(oldSave.BackupPath, true);
                            }

                            // Remove from UI collection
                            SelectedApp.BackupHistory.Remove(oldSave);

                            // Remove from settings
                            if (_settings.BackupHistory.TryGetValue(SelectedApp.ExecutablePath, out var backupsInSettings))
                            {
                                backupsInSettings.RemoveAll(b => b.BackupPath == oldSave.BackupPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error removing old start save {oldSave.BackupPath}: {ex.Message}");
                        }
                    }
                }

                // Generate backup folder path (app-specific)
                string appBackupFolder = Path.Combine(_backupRootFolder, SanitizePathName(SelectedApp.Name));
                if (!Directory.Exists(appBackupFolder))
                {
                    Directory.CreateDirectory(appBackupFolder);
                }
                
                // Create timestamp folder for this specific backup
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupFolder = Path.Combine(appBackupFolder, timestamp);
                Directory.CreateDirectory(backupFolder);
                
                // Count actual files copied
                int filesCopied = 0;
                
                // Copy all files from the save directory to the backup folder
                CopyDirectory(SelectedApp.SavePath, backupFolder, ref filesCopied);
                
                // Only create a backup entry if files were actually copied
                if (filesCopied > 0)
                {
                    // Create backup info (ViewModel version for UI)
                    var backupInfoVM = new ViewModels.SaveBackupInfo
                    {
                        BackupPath = backupFolder,
                        Timestamp = DateTime.Now,
                        Description = $"Start Save ({timestamp})",
                        IsAutoBackup = false
                    };
                    
                    // Create backup info (Model version for settings)
                    var backupInfoModel = new Models.SaveBackupInfo
                    {
                        BackupPath = backupInfoVM.BackupPath,
                        Timestamp = backupInfoVM.Timestamp,
                        Description = backupInfoVM.Description,
                        IsAutoBackup = backupInfoVM.IsAutoBackup
                    };
                    
                    // Update app's backup history and last backup time
                    SelectedApp.LastBackupTime = DateTime.Now;
                    SelectedApp.BackupHistory.Insert(0, backupInfoVM); // Add ViewModel version to UI at the top
                    
                    // Save Model version to settings
                    if (!_settings.BackupHistory.ContainsKey(SelectedApp.ExecutablePath))
                    {
                        _settings.BackupHistory[SelectedApp.ExecutablePath] = new List<Models.SaveBackupInfo>();
                    }
                    _settings.BackupHistory[SelectedApp.ExecutablePath].Add(backupInfoModel);
                    _settings.Save(); // Persist the new start save
                    
                    StatusMessage = $"Created start save for '{SelectedApp.Name}'";
                }
            }

            // Launch the application
            ProcessStartInfo startInfo = new ProcessStartInfo(SelectedApp.ExecutablePath)
            {
                UseShellExecute = true
            };
            var process = Process.Start(startInfo);

            // Wait a short moment and check if process is running
            Task.Delay(500).ContinueWith(_ =>
            {
                UpdateRunningApplications();
            });
            
            // Update LastUsed time and persist it
            SelectedApp.LastUsed = DateTime.Now;
            _settings.LastUsedTimes[SelectedApp.ExecutablePath] = SelectedApp.LastUsed;
            _settings.ForceSave();
            
            // If sorted by last used, refresh the sort
            if (SelectedSortOption == "Last Used")
            {
                ApplySort();
            }
            
            // Immediately check running applications after launching
            UpdateRunningApplications();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching application: {ex.Message}");
            StatusMessage = $"Error launching application: {ex.Message}";
        }
    }

    private void ApplySort()
    {
        var previouslySelectedApp = SelectedApp;

        var sortedApps = SelectedSortOption switch
        {
            "A-Z" => InstalledApps.OrderBy(x => x.Name).ToList(),
            "Z-A" => InstalledApps.OrderByDescending(x => x.Name).ToList(),
            "Last Used" => InstalledApps.OrderByDescending(x => x.LastUsed).ToList(),
            _ => InstalledApps.OrderBy(x => x.Name).ToList()
        };

        InstalledApps.Clear();
        foreach (var app in sortedApps)
        {
            InstalledApps.Add(app);
        }

        // Restore the selection
        if (previouslySelectedApp != null)
        {
            SelectedApp = InstalledApps.FirstOrDefault(a => a.ExecutablePath == previouslySelectedApp.ExecutablePath);
        }
    }    private void HandleSearchTextChanged(string value)
    {
        var previouslySelectedApp = SelectedApp;

        if (string.IsNullOrWhiteSpace(value))
        {
            // Reset to show all apps in their respective collections
            LoadInstalledApps();
            return;
        }

        // Tokenize the search query for multi-term searching
        string[] searchTerms = Utilities.FuzzySearch.TokenizeQuery(value);
        
        // Create a sorted list with scoring for each app
        var scoredApps = AllInstalledApps.Select(app => 
        {
            // Get best match score for app name against all search terms
            var nameScores = searchTerms.Select(term => 
                Utilities.FuzzySearch.Match(app.Name, term)).ToList();
            
            // Get best match score for executable path against all search terms
            var pathScores = searchTerms.Select(term => 
                Utilities.FuzzySearch.Match(Path.GetFileName(app.ExecutablePath), term)).ToList();
            
            // Use the best match from either name or path
            var bestScore = nameScores.Concat(pathScores)
                .Where(score => score.IsMatch)
                .OrderBy(score => score.MatchType)
                .ThenBy(score => score.Score)
                .FirstOrDefault();
            
            return new 
            {
                App = app,
                MatchResult = bestScore,
                // Require all terms to match for multi-term queries
                IsMatch = bestScore != null && bestScore.IsMatch && 
                          searchTerms.All(term => 
                              nameScores.Any(s => s.IsMatch) || pathScores.Any(s => s.IsMatch))
            };
        })
        .Where(result => result.IsMatch)
        .OrderBy(result => result.MatchResult?.MatchType ?? Utilities.FuzzySearch.MatchType.NoMatch)
        .ThenBy(result => result.MatchResult?.Score ?? int.MaxValue)
        .ToList();
        
        var matchingApps = scoredApps.Select(result => result.App).ToList();

        // Update both collections
        InstalledApps.Clear();
        HiddenGames.Clear();

        // Add all matching apps to main list
        foreach (var app in matchingApps)
        {
            InstalledApps.Add(app);
            
            // Also add to hidden games if hidden
            if (app.IsHidden)
            {
                HiddenGames.Add(app);
            }
        }

        // Apply sort to all apps
        ApplySort();

        // Restore the selection
        if (previouslySelectedApp != null)
        {
            SelectedApp = InstalledApps.FirstOrDefault(a => a.ExecutablePath == previouslySelectedApp.ExecutablePath);
        }
    }

    private ObservableCollection<ApplicationInfo> AllInstalledApps { get; } = new();    
    private void LoadInstalledApps()
    {
        InstalledApps.Clear();
        HiddenGames.Clear();
        foreach (var app in AllInstalledApps)
        {
            // Always add to InstalledApps to keep everything in main list
            InstalledApps.Add(app);
            
            // Add to hidden games if hidden
            if (app.IsHidden)
            {
                HiddenGames.Add(app);
            }
        }
        ApplySort();
    }    private async Task LoadInstalledAppsAsync()
    {
        // Cancel any existing scan
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _scanCancellationTokenSource.Token;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            InstalledApps.Clear();
            HiddenGames.Clear();
            AllInstalledApps.Clear();
            IsLoading = true;
            StatusMessage = "Starting application search...";
        });          // Make sure we have a valid KnownApplicationPaths collection
        _appData.KnownApplicationPaths ??= new HashSet<string>();
        
        // If there is no cache in AppData, do a full search immediately
        if (_appData.KnownApplicationPaths.Count == 0)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    Services.LoggingService.Instance.Info("Starting enhanced application discovery scan...");
                    
                    // Create progress reporter to update UI
                    var progress = new Progress<string>(message =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            StatusMessage = message;
                        });
                    });

                    // Create a temporary collection for discovered apps
                    var tempApps = new ObservableCollection<ApplicationInfo>();
                    
                    // Set up event handler to add discovered apps to UI
                    tempApps.CollectionChanged += (sender, e) =>
                    {
                        if (e.NewItems != null)
                        {
                            foreach (ApplicationInfo app in e.NewItems)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                        return;
                                        
                                    AllInstalledApps.Add(app);
                                    if (app.IsHidden)
                                        HiddenGames.Add(app);
                                    else
                                        InstalledApps.Add(app);                                    // Save to AppData cache for next time
                                    if (!_appData.KnownApplicationPaths.Contains(app.ExecutablePath))
                                    {
                                        _appData.KnownApplicationPaths.Add(app.ExecutablePath);
                                        // Save AppData to persist the new app path
                                        _appData.Save();
                                    }

                                    ApplySort();
                                    // Update status message with progress
                                    int total = AllInstalledApps.Count;
                                    StatusMessage = $"Found {total} programs so far...";
                                });
                            }
                        }
                    };

                    // Start the async scan
                    await Helpers.ApplicationScanner.FindInstalledApplicationsAsync(tempApps, _settings, progress, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Services.LoggingService.Instance.Info("Application scan was cancelled");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "Application scan cancelled";
                        IsLoading = false;
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Services.LoggingService.Instance.Error($"Error in enhanced application scan: {ex.Message}");
                    // Fall back to original method if the enhanced one fails
                    try
                    {
                        var tempApps = new ObservableCollection<ApplicationInfo>();
                        tempApps.CollectionChanged += (sender, e) =>
                        {
                            if (e.NewItems != null)
                            {
                                foreach (ApplicationInfo app in e.NewItems)
                                {
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                            
                                        AllInstalledApps.Add(app);
                                        if (app.IsHidden)
                                            HiddenGames.Add(app);
                                        else
                                            InstalledApps.Add(app);
                                    });
                                }
                            }
                        };
                        SearchForExecutables(tempApps);
                    }
                    catch (Exception fallbackEx)
                    {
                        Services.LoggingService.Instance.Error($"Fallback scan also failed: {fallbackEx.Message}");
                    }
                }
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                    
                IsLoading = false;
                  // Count final stats
                int totalPrograms = AllInstalledApps.Count;
                int withSaveLocations = AllInstalledApps.Count(app => !string.IsNullOrEmpty(app.SavePath) && app.SavePath != "Unknown");
                
                // Update status message with final stats
                string statusMessage = $"Found {totalPrograms} programs, {withSaveLocations} with save location";
                StatusMessage = statusMessage;
                Debug.WriteLine(statusMessage);
                  // Log to the log viewer
                string logMessage = $"Application scan completed: {totalPrograms} programs, {withSaveLocations} with save location";
                Services.LoggingService.Instance.Info(logMessage);                // Log only known games with no save location to reduce log noise
                var knownGamesWithNoSaveLocation = AllInstalledApps
                    .Where(app => (string.IsNullOrEmpty(app.SavePath) || app.SavePath == "Unknown"))
                    // Only include apps that match a known game in KnownGames.cs
                    .Where(app => {
                        var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                            g.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) ||
                            app.ExecutablePath.EndsWith(g.Executable, StringComparison.OrdinalIgnoreCase));
                        return knownGame != null;
                    })
                    .ToList();
                
                if (knownGamesWithNoSaveLocation.Any())
                {
                    string noSaveLocationList = string.Join(", ", knownGamesWithNoSaveLocation
                        .Select(app => app.Name)
                        .OrderBy(name => name));
                
                    if (noSaveLocationList.Length > 500)
                    {                            
                        Services.LoggingService.Instance.Info($"Known games with no save location detected:");
                        foreach (var game in knownGamesWithNoSaveLocation.OrderBy(app => app.Name))
                        {
                            Services.LoggingService.Instance.Info($"  - {game.Name}");
                        }
                    }
                    else
                    {
                        Services.LoggingService.Instance.Info($"Known games with no save location detected: {noSaveLocationList}");
                    }
                }
            });
            
            // Start background scan as usual
            StartBackgroundAppRefresh();
            return;
        }

        // 1. Instantly show all known apps (no file existence check)
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
            StatusMessage = "Loading applications...";

            InstalledApps.Clear();
            HiddenGames.Clear();
            AllInstalledApps.Clear();

                        // Prioritize AppData over older locations
            IEnumerable<string> knownPaths;
            if (_appData.KnownApplicationPaths != null && _appData.KnownApplicationPaths.Count > 0)
            {
                knownPaths = _appData.KnownApplicationPaths;
            }
            else
            {
                knownPaths = _settings.KnownApplicationPaths;
            }

            foreach (var exePath in knownPaths)
            {
                var appName = Path.GetFileNameWithoutExtension(exePath);
                var app = new ApplicationInfo(_settings)
                {
                    Name = appName,
                    ExecutablePath = exePath,
                    Path = Path.GetDirectoryName(exePath) ?? string.Empty
                };

                // Restore last used time
                if (_settings.LastUsedTimes.TryGetValue(exePath, out DateTime lastUsed))
                    app.LastUsed = lastUsed;

                // Restore backup history
                if (_settings.BackupHistory.TryGetValue(exePath, out var backupList))
                {
                    app.BackupHistory.Clear();
                    foreach (var backup in backupList.OrderByDescending(b => b.Timestamp))
                    {
                        app.BackupHistory.Add(new SaveBackupInfo
                        {
                            BackupPath = backup.BackupPath,
                            Timestamp = backup.Timestamp,
                            Description = backup.Description,
                            IsAutoBackup = backup.IsAutoBackup
                        });
                    }
                    if (app.BackupHistory.Count > 0)
                        app.LastBackupTime = app.BackupHistory.Max(b => b.Timestamp);
                }
                // Apply customizations from AppData (with fallbacks to older locations)
                string originalName = app.Name; // Remember original name before customization
                bool hasCustomName = false;
                if (_appData.CustomNames != null && _appData.CustomNames.TryGetValue(exePath, out string? appListCustomName) && !string.IsNullOrWhiteSpace(appListCustomName))
                {
                    app.Name = appListCustomName;
                    hasCustomName = true;
                }
                else if (_appData.CustomNames != null && _appData.CustomNames.TryGetValue(exePath, out string? appDataCustomName) && !string.IsNullOrWhiteSpace(appDataCustomName))
                {
                    app.Name = appDataCustomName;
                    hasCustomName = true;
                }
                else if (_settings.CustomNames != null && _settings.CustomNames.TryGetValue(exePath, out string? settingsCustomName) && !string.IsNullOrWhiteSpace(settingsCustomName))
                {
                    app.Name = settingsCustomName;
                    hasCustomName = true;
                }

                // If no custom name, try to get a nice name from KnownGames
                if (!hasCustomName)
                {
                    var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                        string.Equals(g.Executable, System.IO.Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(g.GameFolder) && (app.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                        System.IO.Path.GetFileName(app.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    if (knownGame != null && !string.IsNullOrWhiteSpace(knownGame.Name))
                    {
                        app.Name = knownGame.Name;
                        // Store the known game ID for save carrier functionality
                        app.KnownGameId = knownGame.Name;
                    }
                }

                // Ensure backup paths match the current name if needed
                if (originalName != app.Name)
                {
                    EnsureBackupPathsMatchAppName(app, originalName);
                }

                if (_appData.CustomSavePaths != null && _appData.CustomSavePaths.TryGetValue(exePath, out string? appListCustomSavePath) && !string.IsNullOrWhiteSpace(appListCustomSavePath))
                    app.SavePath = appListCustomSavePath;
                else if (_appData.CustomSavePaths != null && _appData.CustomSavePaths.TryGetValue(exePath, out string? appDataCustomSavePath) && !string.IsNullOrWhiteSpace(appDataCustomSavePath))
                    app.SavePath = appDataCustomSavePath;
                else if (_settings.CustomSavePaths != null && _settings.CustomSavePaths.TryGetValue(exePath, out string? settingsCustomSavePath) && !string.IsNullOrWhiteSpace(settingsCustomSavePath))
                    app.SavePath = settingsCustomSavePath;

                if (_appData.HiddenApps != null && _appData.HiddenApps.Contains(exePath))
                    app.IsHidden = true;
                else if (_appData.HiddenApps != null && _appData.HiddenApps.Contains(exePath))
                    app.IsHidden = true;
                else if (_settings.HiddenApps != null && _settings.HiddenApps.Contains(exePath))
                    app.IsHidden = true;

                LoadAppSettings(app);

                AllInstalledApps.Add(app);
                if (app.IsHidden)
                    HiddenGames.Add(app);
                else
                    InstalledApps.Add(app);
            }
            
            ApplySort();
            IsLoading = false;
              
            // Count programs with detected save locations
            int totalPrograms = AllInstalledApps.Count;
            int withSaveLocations = AllInstalledApps.Count(app => !string.IsNullOrEmpty(app.SavePath) && app.SavePath != "Unknown");
            
            // Update status message with detailed stats
            string statusMessage = $"Found {totalPrograms} programs, {withSaveLocations} with save location";
            StatusMessage = statusMessage;
            Debug.WriteLine(statusMessage);
              // Log to the log viewer with additional details about apps with no save location
            string logMessage = $"Application scan completed: {totalPrograms} programs, {withSaveLocations} with save location";
            Services.LoggingService.Instance.Info(logMessage);
            
            // Log only known games with no save location to reduce log noise
            var knownAppsWithNoSaveLocation = AllInstalledApps
                .Where(app => (string.IsNullOrEmpty(app.SavePath) || app.SavePath == "Unknown"))
                // Only include apps that match a known game in KnownGames.cs
                .Where(app => {
                    var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                        g.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) ||
                        app.ExecutablePath.EndsWith(g.Executable, StringComparison.OrdinalIgnoreCase));
                    return knownGame != null;
                })
                .ToList();
            
            if (knownAppsWithNoSaveLocation.Any())
            {
                // Get the list of known apps with no save location, sorted alphabetically
                string noSaveLocationList = string.Join(", ", knownAppsWithNoSaveLocation
                    .Select(app => app.Name)
                    .OrderBy(name => name));
                
                // Split into multiple log messages if the list is too long
                if (noSaveLocationList.Length > 500)
                {
                    Services.LoggingService.Instance.Info($"Known games with no save location detected:");
                    
                    // Log apps in groups to avoid very long lines
                    foreach (var app in knownAppsWithNoSaveLocation.OrderBy(app => app.Name))
                    {
                        Services.LoggingService.Instance.Info($"  - {app.Name}");
                    }
                }
                else
                {
                    Services.LoggingService.Instance.Info($"Known games with no save location detected: {noSaveLocationList}");
                }
            }
        });

        // 2. Start background scan to validate and update the list
        StartBackgroundAppRefresh();
    }
    
    [SupportedOSPlatform("windows")]
    private void SearchForExecutables(ObservableCollection<ApplicationInfo> installedApps)
    {
        var searchPaths = new List<string>();
        var processedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // First scan registry for applications if our utility is available
        try
        {
            Services.LoggingService.Instance.Info("Starting registry scan for installed applications...");
            var registryExecutables = Utilities.RegistryScanner.ScanRegistryForApplications();
            
            foreach (var exePath in registryExecutables)
            {
                if (!processedExecutables.Contains(exePath) && File.Exists(exePath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(exePath);
                        if (!ShouldSkipExecutable(fileInfo))
                        {
                            processedExecutables.Add(exePath);
                            
                            // Add to apps if not already present
                            string appName = Path.GetFileNameWithoutExtension(exePath);
                            if (!installedApps.Any(a => string.Equals(a.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                installedApps.Add(new ApplicationInfo(_settings)
                                {
                                    Name = appName,
                                    Path = Path.GetDirectoryName(exePath) ?? string.Empty,
                                    ExecutablePath = exePath
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing registry executable {exePath}: {ex.Message}");
                    }
                }
            }
            
            Services.LoggingService.Instance.Info($"Registry scan found {registryExecutables.Count} executables");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during registry scan: {ex.Message}");
            Services.LoggingService.Instance.Error($"Error during registry scan: {ex.Message}");
        }
        
        // Check all possible drive letters from A to Z
        for (char driveLetter = 'A'; driveLetter <= 'Z'; driveLetter++)
        {
            string drivePath = $"{driveLetter}:\\";

            try
            {
                // Check if drive exists and is ready
                var driveInfo = new DriveInfo(drivePath);
                if (!driveInfo.IsReady)
                {
                    Debug.WriteLine($"Drive {drivePath} is not ready or doesn't exist");
                    continue;
                }

                // Skip network drives for performance
                if (driveInfo.DriveType == DriveType.Network)
                {
                    Debug.WriteLine($"Skipping network drive {drivePath}");
                    continue;
                }

                Debug.WriteLine($"Scanning drive {drivePath} (Type: {driveInfo.DriveType})");

                // Add common paths for this drive
                var commonPaths = new[]
                {
                    "Program Files",
                    "Program Files (x86)",
                    "Games",
                    "Downloaded Games",
                    "GameDownloads",
                    "MyGames",
                    "GameSetup",
                    "GameInstall",
                    "GameLibrary",
                    // Common game stores
                    "Steam",
                    "SteamLibrary",
                    "Steam Library",
                    "Epic Games",
                    "GOG Games",
                    "Origin Games",
                    "EA Games",
                    "Ubisoft",
                    "Battle.net",
                    "Xbox Games",
                    // Publisher folders
                    "2K Games",
                    "Bethesda Softworks",
                    "Square Enix",
                    "THQ Nordic",
                    "Deep Silver",
                    "CD Projekt Red",
                    "Devolver Digital",
                    "Focus Home",
                    "Paradox Interactive",
                    "Rockstar Games",
                    "SEGA",
                    "Capcom",
                    "Bandai Namco",
                    "Warner Bros",
                    "505 Games",
                    "Team17",
                    "Apogee",
                    "Activision",
                    "Konami",
                    "LucasArts",
                    "Codemasters",
                    // Common user-created game folders
                    "Downloaded Games",
                    "Installed Games",
                    "Old Games",
                    "Classic Games",
                    "Indie Games",
                    "Game Collections",
                    "Retro Games",
                    "Strategy Games",
                    "RPG Games",
                    "Adventure Games",
                    "Backup Games",
                    "Offline Games"
                };

                foreach (var commonPath in commonPaths)
                {
                    // Check root level
                    var rootPath = Path.Combine(drivePath, commonPath);
                    if (Directory.Exists(rootPath))
                    {
                        searchPaths.Add(rootPath);
                        
                        try
                        {
                            // Add immediate subfolders of common game paths
                            var subDirs = Directory.GetDirectories(rootPath);
                            foreach (var subDir in subDirs)
                            {
                                if (!ShouldSkipDirectory(subDir))
                                {
                                    searchPaths.Add(subDir);
                                    
                                    // For game-related paths, go one level deeper
                                    if (IsLikelyGamePath(subDir))
                                    {
                                        try
                                        {
                                            var gameSubDirs = Directory.GetDirectories(subDir);
                                            searchPaths.AddRange(gameSubDirs.Where(dir => !ShouldSkipDirectory(dir)));
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error accessing subdirectories in {subDir}: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error accessing subdirectories in {rootPath}: {ex.Message}");
                        }
                    }

                    // Check nested paths
                    var nestedPaths = new[]
                    {
                        Path.Combine(drivePath, "Games", commonPath),
                        Path.Combine(drivePath, "Program Files", commonPath),
                        Path.Combine(drivePath, "Program Files (x86)", commonPath)
                    };

                    foreach (var nestedPath in nestedPaths)
                    {
                        if (Directory.Exists(nestedPath))
                        {
                            searchPaths.Add(nestedPath);
                        }
                    }
                }

                // Special handling for optical drives (CD/DVD)
                if (driveInfo.DriveType == DriveType.CDRom)
                {
                    try
                    {
                        // Add the root of the CD/DVD
                        searchPaths.Add(drivePath);
                        
                        // Add all first-level directories on the CD/DVD
                        var cdDirs = Directory.GetDirectories(drivePath);
                        foreach (var cdDir in cdDirs)
                        {
                            if (!ShouldSkipDirectory(cdDir))
                            {
                                searchPaths.Add(cdDir);
                                
                                // Also add subdirectories for deeper scanning
                                try
                                {
                                    var subDirs = Directory.GetDirectories(cdDir);
                                    searchPaths.AddRange(subDirs.Where(dir => !ShouldSkipDirectory(dir)));
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error accessing CD/DVD subdirectories in {cdDir}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error accessing CD/DVD drive {drivePath}: {ex.Message}");
                    }
                }

                // For fixed and removable drives, also check Users folder
                if (driveInfo.DriveType == DriveType.Fixed || driveInfo.DriveType == DriveType.Removable)
                {
                    string usersFolder = Path.Combine(drivePath, "Users");
                    if (Directory.Exists(usersFolder))
                    {
                        foreach (var userDir in Directory.GetDirectories(usersFolder))
                        {
                            // Add common user program locations
                            string[] userProgramPaths = {
                                Path.Combine(userDir, "AppData", "Local", "Programs"),
                                Path.Combine(userDir, "AppData", "Local", "Games"),
                                Path.Combine(userDir, "Documents", "My Games"),
                                Path.Combine(userDir, "Games"),
                                Path.Combine(userDir, "Downloads"),
                                Path.Combine(userDir, "Desktop")
                            };

                            searchPaths.AddRange(userProgramPaths.Where(Directory.Exists));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error accessing drive {drivePath}: {ex.Message}");
                continue;
            }
        }        // Add user profile paths that might be on any drive
        var userPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "My Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Xbox Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
        };
        searchPaths.AddRange(userPaths.Where(Directory.Exists));

        // Log the search paths
        Services.LoggingService.Instance.Info($"Searching {searchPaths.Count} paths for applications...");
            
        // Process each search path, using the existing processedExecutables to avoid duplicates
        foreach (var searchPath in searchPaths.Distinct())
        {
            if (!Directory.Exists(searchPath))
            {
                continue;
            }
              try
            {
                SearchDirectoryForExecutables(searchPath, installedApps, processedExecutables);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching path {searchPath}: {ex.Message}");
            }
        }
    }

    private bool IsLikelyGamePath(string path)
    {
        string pathLower = path.ToLowerInvariant();
        return pathLower.Contains("game") ||
               pathLower.Contains("steam") ||
               pathLower.Contains("gog") ||
               pathLower.Contains("epic") ||
               pathLower.Contains("origin") ||
               pathLower.Contains("ubisoft") ||
               pathLower.Contains("bethesda") ||
               pathLower.Contains("rockstar") ||
               pathLower.EndsWith("bin") ||
               pathLower.EndsWith("binaries");
    }    private bool ShouldSkipExecutable(FileInfo fileInfo)
    {
        string fileName = fileInfo.Name.ToLowerInvariant();
        string dirName = Path.GetFileName(fileInfo.DirectoryName ?? string.Empty).ToLowerInvariant();
        string fullPath = fileInfo.FullName.ToLowerInvariant();
        
        // Don't skip files in game-related directories
        bool isInGamesDir = IsLikelyGamePath(fileInfo.DirectoryName ?? string.Empty);
        if (isInGamesDir)
        {
            return false; // Never skip executables in game directories
        }

        // Skip very small files unless they're in a games directory
        // Check if file is smaller than 50 KB
        if (fileInfo.Length < 50 * 1024 && !isInGamesDir) // 50 KB minimum unless in games directory
            return true;

        // Skip browser-related executables with more specific matching to avoid false positives
        // Check exact filename matches or specific endings rather than substring matches
        if ((fileName == "chrome.exe" ||
            fileName == "firefox.exe" ||
            fileName == "msedge.exe" ||
            fileName == "opera.exe" ||
            fileName == "brave.exe" ||
            fileName == "iexplore.exe" ||
            fileName.EndsWith("browser.exe")) ||
            fileName.EndsWith("webview.exe") ||
            fileName.EndsWith("extension.exe") ||
            fileName.EndsWith("plugin.exe") ||
            fileName.EndsWith("crashreport.exe") ||
            fileName.EndsWith("updater.exe") ||
            fileName.EndsWith("notification.exe") ||
            fileName.EndsWith("broker.exe") ||
            fileName.EndsWith("sandbox.exe") ||
            fileName.EndsWith("helper.exe") ||
            fileName.EndsWith("gpu.exe") ||
            fileName.EndsWith("utility.exe") ||
            fileName.EndsWith("crashpad.exe") ||
            fileName.EndsWith("render.exe") ||
            fileName.EndsWith("plugin-container.exe") ||
            fileName.EndsWith("notification-helper.exe") ||
            fileName.EndsWith("service.exe"))
        {
            return true;
        }

        // Skip if in browser-related directories
        if (fullPath.Contains("\\chrome\\") ||
            fullPath.Contains("\\firefox\\") ||
            fullPath.Contains("\\edge\\") ||
            fullPath.Contains("\\opera\\") ||
            fullPath.Contains("\\brave\\") ||
            fullPath.Contains("\\mozilla\\") ||
            fullPath.Contains("\\internet explorer\\") ||
            fullPath.Contains("\\microsoft\\edge\\"))
        {
            return true;
        }

        // Skip basic installer/uninstaller patterns, but be more selective
        if ((fileName.StartsWith("unins") && fileName.EndsWith(".exe")) ||
            (fileName == "setup.exe" && fileInfo.Length < 5 * 1024 * 1024) || // Small setup files
            (fileName == "install.exe" && fileInfo.Length < 5 * 1024 * 1024) || // Small install files
            fileName == "vcredist.exe" ||
            fileName.EndsWith("_setup.exe") || 
            fileName.EndsWith("_installer.exe"))
        {
            return true;
        }

        // Skip executables in directories specifically for installers
        if (dirName == "installer" ||
            dirName == "temp" ||
            dirName == "tmp" ||
            dirName == "cache" ||
            dirName == "vcredist" ||
            dirName == "redist" ||
            dirName == "uninstall")
        {
            return true;
        }

        // If it's a likely game executable, don't skip it
        if (IsLikelyGameExecutable(fileName, dirName))
        {
            return false;
        }
        
        // By default, don't skip - we want to be inclusive rather than exclusive
        return false;
    }

    private bool IsLikelyGameExecutable(string fileName, string dirName)
    {
        fileName = fileName.ToLowerInvariant();
        dirName = dirName.ToLowerInvariant();

        // Common game executable patterns
        return fileName.EndsWith("game.exe") ||
               fileName == "game.exe" ||
               fileName.Contains("start") ||
               fileName.Contains("launch") ||
               fileName == "client.exe" ||
               fileName == "app.exe" ||
               dirName.Contains("bin") ||
               dirName.Contains("game") ||
               // Common game engine executables
               fileName.Contains("unreal") ||
               fileName.Contains("unity") ||
               fileName.Contains("cryengine") ||
               fileName.Contains("godot") ||
               // Common game executable names
               fileName == "play.exe" ||
               fileName == "run.exe" ||
               fileName == "main.exe" ||
               fileName == "default.exe" ||
               fileName == "launcher.exe" && dirName.Contains("game");
    }

    private void SearchDirectoryForExecutables(string directory, ObservableCollection<ApplicationInfo> apps, 
        HashSet<string> processedExecutables, int maxDepth = 6, int currentDepth = 0)
    {
        if (currentDepth > maxDepth)
            return;
            
        try
        {
            if (ShouldSkipDirectory(directory))
                return;

            // First check binary folders specifically
            var binFolders = Directory.GetDirectories(directory)
                .Where(d => Path.GetFileName(d).ToLowerInvariant().Contains("bin") ||
                           Path.GetFileName(d).ToLowerInvariant().Contains("binary"))
                .ToList();

            foreach (var binFolder in binFolders)
            {
                ProcessExecutablesInDirectory(binFolder, apps, processedExecutables);
            }

            // Then process current directory
            ProcessExecutablesInDirectory(directory, apps, processedExecutables);
            
            // Recursively search subdirectories
            foreach (var subDir in Directory.GetDirectories(directory))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(subDir);
                    
                    // Skip hidden and system folders
                    if (dirInfo.Attributes.HasFlag(FileAttributes.System) || 
                        dirInfo.Attributes.HasFlag(FileAttributes.Hidden))
                    {
                        continue;
                    }
                    
                    SearchDirectoryForExecutables(subDir, apps, processedExecutables, maxDepth, currentDepth + 1);
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we don't have access to
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error accessing directory {subDir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error searching directory {directory}: {ex.Message}");
        }
    }    private void ProcessExecutablesInDirectory(string directory, ObservableCollection<ApplicationInfo> apps, HashSet<string> processedExecutables)
    {
        // Track executables by their filename (without path) to avoid multiple entries for the same program
        var executableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // First build a map of executable names from existing apps
        foreach (var app in apps)
        {
            string exeName = Path.GetFileName(app.ExecutablePath);
            if (!string.IsNullOrEmpty(exeName) && !executableNameMap.ContainsKey(exeName))
            {
                executableNameMap[exeName] = app.ExecutablePath;
            }
        }
        
        foreach (var exePath in Directory.GetFiles(directory, "*.exe"))
        {
            if (processedExecutables.Contains(exePath))
                continue;
                
            processedExecutables.Add(exePath);
            
            try
            {
                var fileInfo = new FileInfo(exePath);
                
                if (ShouldSkipExecutable(fileInfo))
                    continue;

                string exeName = Path.GetFileName(exePath);
                string appName = Path.GetFileNameWithoutExtension(exePath);
                
                // Skip if we already have an application with this exact executable name
                if (executableNameMap.ContainsKey(exeName))
                {
                    Debug.WriteLine($"Skipping duplicate executable: {exePath} (already have {exeName})");
                    continue;
                }
                // Add to our tracking maps
                executableNameMap[exeName] = exePath;
                
                // Add to apps if not already present 
                if (!apps.Any(a => string.Equals(a.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase)))
                {
                    apps.Add(new ApplicationInfo(_settings)
                    {
                        Name = appName,
                        Path = Path.GetDirectoryName(exePath) ?? string.Empty,
                        ExecutablePath = exePath
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing executable {exePath}: {ex.Message}");
            }
        }
    }    private bool ShouldSkipDirectory(string directory)
    {
        string dirLower = directory.ToLowerInvariant();
        string dirName = Path.GetFileName(directory).ToLowerInvariant();
        string parentDirName = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty).ToLowerInvariant();
        
        // Skip our own program's folders
        if (dirLower.Contains("savevault") || 
            dirLower.Contains("save vault") ||
            dirLower.Contains(@"etka\desktop\projeler\save vault"))
        {
            return true;
        }
        
        // Never skip game-related directories
        if (dirLower.Contains("game") ||
            dirLower.Contains("steam") ||
            dirLower.Contains("gog") ||
            dirLower.Contains("epic") ||
            dirLower.Contains("origin") ||
            dirLower.Contains("ubisoft") ||
            dirLower.Contains("bethesda") ||
            dirLower.Contains("rockstar") ||
            dirLower.EndsWith("bin") ||
            dirLower.EndsWith("binaries") ||
            dirLower.EndsWith("program files") ||
            dirLower.EndsWith("program files (x86)"))
        {
            return false;
        }
        
        // Skip browser-related directories with exact name matching
        if (dirName == "chrome" || 
            dirName == "firefox" ||
            dirName == "edge" ||
            dirName == "opera" ||
            dirName == "brave" ||
            dirName == "mozilla" ||
            dirName == "internet explorer")
        {
            return true;
        }

        // Skip specific utility directories, but be more conservative
        if (dirName == "helper" ||
            dirName == "utility" ||
            dirName == "assistant" ||
            dirName == "plugin" ||
            dirName == "extension")
        {
            return true;
        }
        
        // Never skip game-related directories unless they're installation-related
        if ((dirLower.Contains("game") ||
            dirLower.Contains("steam") ||
            dirLower.Contains("gog") ||
            dirLower.Contains("epic") ||
            dirLower.Contains("origin") ||
            dirLower.Contains("downloads"))
            && !(dirName == "installer" || dirName == "installers" || dirName == "install" || dirName == "setup"))
        {
            // Allow "installed games" type directories regardless of location
            if (dirName == "installed games" || 
                dirName == "games installed" ||
                dirName == "installed" ||
                dirName == "installs" && !dirName.Contains("installer"))
            {
                return false; // Don't skip installed game folders
            }
            return false;
        }
        
        // Skip system directories
        if (dirLower.Contains("system32") ||
            dirLower.Contains("syswow64") ||
            dirLower.Contains("$recycle.bin") ||
            dirLower.Contains("system volume information") ||
            dirLower.Contains("windows") ||
            dirLower.Contains("drivers") ||
            dirLower.Contains("winsxs") ||
            dirLower.Contains("driverstore"))
        {
            return true;
        }
        
        // Skip install-related directories - these are specifically folders that we should ignore
        if (dirName == "installer" || 
            dirName == "installers" ||
            dirName == "install" ||
            dirName == "setup" ||
            dirName == "redist" || 
            dirName == "redistributable" ||
            dirName == "redistributables" ||
            dirName == "uninstall" ||
            dirName == "uninstaller" ||
            dirName == "vcredist" ||
            dirName == "packages" ||
            dirName == "_install" ||
            dirName == "_installer" ||
            dirName == "_setup")
        {
            return true;
        }
        
        // Skip if the parent directory is an installer directory
        if (parentDirName == "installer" || 
            parentDirName == "installers" || 
            parentDirName == "install" || 
            parentDirName == "setup" ||
            parentDirName == "redistributable")
        {
            return true;
        }
        
        // Skip other common non-game directories
        if (dirName == "temp" ||
            dirName == "tmp" ||
            dirName == "cache" ||
            dirName == "updates" ||
            dirName == "update" ||
            dirName == "logs" ||
            dirName == "log")
        {
            return true;
        }
        
        return false;
    }    private string DetectSavePath(ApplicationInfo app)
    {
        // Use the new SaveLocationDetector utility class to detect save paths
        var result = Utilities.SaveLocationDetector.DetectSavePath(app);
        if (!string.IsNullOrEmpty(result.GameName))
        {
            // Update KnownGameId from the detection result
            app.KnownGameId = result.KnownGameId;
            
            // Only update if not already customized by the user
            if (!_appData.CustomNames.ContainsKey(app.ExecutablePath) && !_settings.CustomNames.ContainsKey(app.ExecutablePath))
            {
                app.Name = result.GameName;
                // Save the detected name to AppData.CustomNames to persist between application launches
                _appData.CustomNames[app.ExecutablePath] = result.GameName;
                _appData.Save();
            }
        }
        
        return result.SavePath;
    }
    [RelayCommand]
    public void ResetCache()
    {
        // Clear all saved data in settings
        _settings.LastUsedTimes.Clear();
        _settings.LastBackupTimes.Clear();
        _settings.CustomNames.Clear();
        _settings.CustomSavePaths.Clear();
        _settings.HiddenApps.Clear();
        _settings.KnownApplicationPaths.Clear();
        _settings.BackupHistory.Clear(); // Clear backup history
        _settings.AppSettings.Clear(); // Clear app-specific settings
        _settings.ForceSave();
        
        // Clear all saved data in AppData
        _appData.LastBackupTimes.Clear();
        _appData.CustomNames.Clear();
        _appData.CustomSavePaths.Clear();
        _appData.HiddenApps.Clear();
        _appData.KnownApplicationPaths.Clear();
        _appData.Save();
        
        // Clear all saved data in AppData
        _appData.LastBackupTimes.Clear();
        _appData.CustomNames.Clear();
        _appData.CustomSavePaths.Clear();
        _appData.HiddenApps.Clear();
        _appData.KnownApplicationPaths.Clear();
        _appData.Save();
        
        // Update UI status
        StatusMessage = "Completely resetting program cache and rescanning...";
        IsLoading = true;
        
        // Clear current cached apps
        AllInstalledApps.Clear();
        InstalledApps.Clear();
        HiddenGames.Clear();
        RunningApps.Clear();

        Task.Run(async () =>
        {
            await LoadInstalledAppsAsync();
        });
    }

    private bool _isEditingName;
    public bool IsEditingName
    {
        get => _isEditingName;
        set => this.RaiseAndSetIfChanged(ref _isEditingName, value);
    }

    private string _editableName = string.Empty;
    public string EditableName
    {
        get => _editableName;
        set => this.RaiseAndSetIfChanged(ref _editableName, value);
    }

    private bool _isEditingSavePath;
    public bool IsEditingSavePath
    {
        get => _isEditingSavePath;
        set => this.RaiseAndSetIfChanged(ref _isEditingSavePath, value);
    }

    private string _editableSavePath = string.Empty;
    public string EditableSavePath
    {
        get => _editableSavePath;
        set => this.RaiseAndSetIfChanged(ref _editableSavePath, value);
    }
    
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    [RelayCommand]
    public void EditApp()
    {
        if (SelectedApp == null)
            return;

        // Set both edit fields
        EditableName = SelectedApp.Name;
        EditableSavePath = SelectedApp.SavePath;
        
        // Enter edit mode
        IsEditing = true;
        IsEditingName = true;
        IsEditingSavePath = true;
    }

    // Keep the original methods for backward compatibility
    [RelayCommand]
    public void EditAppName()
    {
        if (SelectedApp == null)
            return;

        EditableName = SelectedApp.Name;
        IsEditingName = true;
    }
    
    [RelayCommand]
    public void EditSavePath()
    {
        if (SelectedApp == null)
            return;

        EditableSavePath = SelectedApp.SavePath;
        IsEditingSavePath = true;
    }    public void SaveAppName()
    {
        if (SelectedApp == null || !IsEditingName || string.IsNullOrWhiteSpace(EditableName))
        {
            CancelAll(); // Close all edit fields
            return;
        }

        string oldName = SelectedApp.Name;
        string newName = EditableName.Trim();
        string executablePath = SelectedApp.ExecutablePath;

        // Update the name in the selected app
        SelectedApp.Name = newName;
        
        // Update the name in all applications list to reflect in UI
        var appInList = AllInstalledApps.FirstOrDefault(a => 
            string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        
        if (appInList != null)
        {
            appInList.Name = newName;
        }
        
        // Handle existing backup paths if there are any
        UpdateBackupFoldersAfterNameChange(executablePath, oldName, newName);
        
        // Save custom name to AppData
        _appData.CustomNames[executablePath] = newName;
        _appData.Save();

        // Close all edit fields
        CancelAll();
        StatusMessage = $"Renamed '{oldName}' to '{newName}'";
        
        // Store the currently selected app to restore it later
        var currentApp = SelectedApp;
        
        // Safely refresh the list while maintaining selection
        ApplicationInfo[] tempArray = InstalledApps.ToArray();
        InstalledApps.Clear();
        
        // Apply sort to determine the new order
        IEnumerable<ApplicationInfo> sortedApps = SelectedSortOption switch
        {
            "A-Z" => tempArray.OrderBy(x => x.Name),
            "Z-A" => tempArray.OrderByDescending(x => x.Name),
            "Last Used" => tempArray.OrderByDescending(x => x.LastUsed),
            _ => tempArray.OrderBy(x => x.Name)
        };
        
        // Add the sorted apps back to the collection
        foreach (var app in sortedApps)
        {
            InstalledApps.Add(app);
        }
        
        // Restore the selection to the same app
        SelectedApp = InstalledApps.FirstOrDefault(a => 
            string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveSavePath()
    {
        if (SelectedApp == null || !IsEditingSavePath || string.IsNullOrWhiteSpace(EditableSavePath))
        {
            CancelAll(); // Close all edit fields
            return;
        }

        string oldPath = SelectedApp.SavePath;
        string newPath = EditableSavePath.Trim();
        string executablePath = SelectedApp.ExecutablePath;

        // Ensure the save path exists
        if (!Directory.Exists(newPath))
        {
            StatusMessage = "Selected save path does not exist!";
            return;
        }

        // Update the save path in the selected app
        SelectedApp.SavePath = newPath;
        
        // Update the save path in all applications list
        var appInList = AllInstalledApps.FirstOrDefault(a => 
            string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        
        if (appInList != null)
        {
            appInList.SavePath = newPath;
        }
        
        // Save custom save path to AppData
        _appData.CustomSavePaths[executablePath] = newPath;
        _appData.Save();

        // Close all edit fields
        CancelAll();
        StatusMessage = $"Updated save path for '{SelectedApp.Name}'";
    }

    public void CancelAppNameEdit()
    {
        IsEditingName = false;
    }

    public void CancelSavePathEdit()
    {
        IsEditingSavePath = false;
    }

    [RelayCommand]
    public void SaveAppNameCommand()
    {
        SaveAppName();
    }
    
    [RelayCommand]
    public void SaveSavePathCommand()
    {
        SaveSavePath();
    }
    
    [RelayCommand]
    private async Task BrowseForSavePath()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Get the current top-level window
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = topLevel?.MainWindow;
            
            if (mainWindow != null)
            {
                // Use the StorageProvider API instead of the deprecated OpenFolderDialog
                var folderPath = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Save Location",
                    AllowMultiple = false
                });

                if (folderPath.Count > 0)
                {
                    // Get the folder path from the first selected item
                    EditableSavePath = folderPath[0].Path.LocalPath;
                }
            }

        }
        catch (Exception ex)
        {
            StatusMessage = $"Error selecting folder: {ex.Message}";
            Debug.WriteLine($"Error selecting folder: {ex.Message}");
        }
    }
      public void SaveAll()
    {
        if (SelectedApp == null || !IsEditing)
            return;
            
        // Save both name and path
        string executablePath = SelectedApp.ExecutablePath;
        
        if (IsEditingName && !string.IsNullOrWhiteSpace(EditableName))
        {
            string oldName = SelectedApp.Name;
            string newName = EditableName.Trim();
            
            // Update the name in the selected app
            SelectedApp.Name = newName;
            
            // Update the name in all applications list to reflect in UI
            var appInList = AllInstalledApps.FirstOrDefault(a => 
                string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
            
            if (appInList != null)
            {
                appInList.Name = newName;
            }
            
            // Handle existing backup paths if there are any
            UpdateBackupFoldersAfterNameChange(executablePath, oldName, newName);
            
            // Save custom name to AppData
            _appData.CustomNames[executablePath] = newName;
            StatusMessage = $"Renamed to '{newName}'";
        }
        
        if (IsEditingSavePath && !string.IsNullOrWhiteSpace(EditableSavePath))
        {
            string newPath = EditableSavePath.Trim();
            
            // Update the save path in the selected app
            SelectedApp.SavePath = newPath;
            
            // Update the save path in all applications list
            var appInList = AllInstalledApps.FirstOrDefault(a => 
                string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
            
            if (appInList != null)
            {
                appInList.SavePath = newPath;
            }
            
            // Save custom save path to AppData
            _appData.CustomSavePaths[executablePath] = newPath;
            StatusMessage = $"Updated details for '{SelectedApp.Name}'";
        }
        
        // Save changes to AppData
        _appData.Save();
        
        // Exit editing mode
        IsEditing = false;
        IsEditingName = false;
        IsEditingSavePath = false;
        
        // Reapply sort if needed
        ApplicationInfo[] tempArray = InstalledApps.ToArray();
        InstalledApps.Clear();
        
        // Apply sort to determine the new order
        IEnumerable<ApplicationInfo> sortedApps = SelectedSortOption switch
        {
            "A-Z" => tempArray.OrderBy(x => x.Name),
            "Z-A" => tempArray.OrderByDescending(x => x.Name),
            "Last Used" => tempArray.OrderByDescending(x => x.LastUsed),
            _ => tempArray.OrderBy(x => x.Name)
        };
        
        // Add the sorted apps back to the collection
        foreach (var app in sortedApps)
        {
            InstalledApps.Add(app);
        }
        
        // Restore the selection to the same app
        SelectedApp = InstalledApps.FirstOrDefault(a => 
            string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }
    
    public void CancelAll()
    {
        IsEditing = false;
        IsEditingName = false;
        IsEditingSavePath = false;
    }
    
    [RelayCommand]
    public void SaveAllCommand()
    {
        SaveAll();
    }
    
    [RelayCommand]
    public void CancelAllCommand()
    {
        CancelAll();
    }    
    [RelayCommand]
    public void ToggleAppVisibility()
    {
        if (SelectedApp == null)
            return;

        // Store the selected app before modifying collections
        var appToToggle = SelectedApp;

        // Toggle hidden state
        appToToggle.IsHidden = !appToToggle.IsHidden;
          
        // Update the hidden apps collection in AppData
        if (appToToggle.IsHidden)
        {
            _appData.HiddenApps.Add(appToToggle.ExecutablePath);
            StatusMessage = $"'{appToToggle.Name}' is now marked as hidden";
            
            // Add to hidden games list (for the hidden section)
            if (!HiddenGames.Contains(appToToggle))
            {
                HiddenGames.Add(appToToggle);
            }
            
            // Keep in the main list but update UI
            // Note: We no longer remove from InstalledApps
        }        
        else
       
        {
            _appData.HiddenApps.Remove(appToToggle.ExecutablePath);
            StatusMessage = $"'{appToToggle.Name}' is no longer hidden";
            
            // Remove from hidden games list
            HiddenGames.Remove(appToToggle);
            
            // Make sure it's in the main list
            if (!InstalledApps.Contains(appToToggle))
            {
                InstalledApps.Add(appToToggle);
                // Reapply sort to maintain the sorting order
                ApplySort();
            }
        }
        
        _appData.Save();
    }

    private void UpdateRunningApplications()
    {
        try
        {
            var processes = Process.GetProcesses();
            var currentTime = DateTime.Now;
            
            // Get running app paths based on process names
            var runningExecutables = processes

                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)) // Only processes with window titles
               
                .Select(p => 
                {
                    try 
                    {
                        return p.MainModule?.FileName;
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(path => !string.IsNullOrEmpty(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Track if any running states actually changed
            bool runningStateChanged = false;
            
            // Update IsRunning flag and LastUsed time for all apps
            foreach (var app in AllInstalledApps)
            {
                bool wasRunning = app.IsRunning;
                bool isNowRunning = runningExecutables.Contains(app.ExecutablePath);
                
                if (wasRunning != isNowRunning)
                {
                    app.IsRunning = isNowRunning;
                    runningStateChanged = true;
                }
                
                // Update LastUsed time for running apps
                if (isNowRunning && (!wasRunning || (currentTime - app.LastUsed).TotalMinutes > 30))
                {
                    app.LastUsed = currentTime;
                    _settings.LastUsedTimes[app.ExecutablePath] = currentTime;
                }
            }

            // Only update UI if running states actually changed
            if (runningStateChanged)
            {
                // Update the RunningApps collection on UI thread
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    RunningApps.Clear();
                    foreach (var app in AllInstalledApps
                        .Where(a => a.IsRunning)
                        .Where(a => !a.ExecutablePath.Contains("SaveVault", StringComparison.OrdinalIgnoreCase) && 
                                  !a.ExecutablePath.Contains("Save Vault", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.Name))
                    {
                        RunningApps.Add(app);
                    }

                    // Only reapply sorting if we're sorting by last used
                    if (SelectedSortOption == "Last Used")
                    {
                        ApplySort();
                    }
                });
            }

            // Save settings after updating last used times
            _settings.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating running applications: {ex.Message}");
        }
    }    private void SaveNewAppsToSettings()
    {
        try
        {
            // Add all executable paths to known applications in AppData
            foreach (var app in AllInstalledApps)
            {
                _appData.KnownApplicationPaths.Add(app.ExecutablePath);
            }
            
            // Save AppData to persist them
            _appData.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving new apps to app list cache: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RestoreBackup(SaveBackupInfo backup)
    {
        if (SelectedApp == null || backup == null)
            return;

        try
        {
            if (!Directory.Exists(backup.BackupPath))
            {
                StatusMessage = "Backup folder not found!";
                return;
            }

            if (!Directory.Exists(SelectedApp.SavePath))
            {
                StatusMessage = "Save folder not found!";
                return;
            }

            // Create a backup of current save first
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string currentBackupFolder = Path.Combine(_backupRootFolder, SanitizePathName(SelectedApp.Name), timestamp);
            Directory.CreateDirectory(currentBackupFolder);

            int filesCopied = 0;
            CopyDirectory(SelectedApp.SavePath, currentBackupFolder, ref filesCopied);

            if (filesCopied > 0)
            {
                var currentBackupInfo = new SaveBackupInfo
                {
                    BackupPath = currentBackupFolder,
                    Timestamp = DateTime.Now,
                    Description = "Automatic backup before restore",
                    IsAutoBackup = false
                };
                SelectedApp.BackupHistory.Add(currentBackupInfo);
            }

            // Now restore the selected backup
            DirectoryInfo saveDirInfo = new DirectoryInfo(SelectedApp.SavePath);
            
            // Delete all files in save directory
            foreach (FileInfo file in saveDirInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting file {file.Name}: {ex.Message}");
                }
            }
            
            // Delete all subdirectories
            foreach (DirectoryInfo dir in saveDirInfo.GetDirectories())
            {
               
                try
                {
                    dir.Delete(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting directory {dir.Name}: {ex.Message}");
                }
            }

            // Copy backup files to save directory
            filesCopied = 0;
            CopyDirectory(backup.BackupPath, SelectedApp.SavePath, ref filesCopied);

            StatusMessage = $"Restored save from {backup.Description}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error restoring backup: {ex.Message}";
            Debug.WriteLine($"Error restoring backup: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteBackup(SaveBackupInfo backup)
    {
        if (SelectedApp == null || backup == null)
            return;

        try
        {
            if (Directory.Exists(backup.BackupPath))
            {
                Directory.Delete(backup.BackupPath, true);
            }

            // Remove from UI collection
            SelectedApp.BackupHistory.Remove(backup);
            
            // Remove from settings
            if (_settings.BackupHistory.TryGetValue(SelectedApp.ExecutablePath, out var backupsInSettings))
            {
                // Find the corresponding backup in settings based on path and remove it
                backupsInSettings.RemoveAll(b => b.BackupPath == backup.BackupPath);
                _settings.Save(); // Persist the deletion
            }

            StatusMessage = "Backup deleted successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting backup: {ex.Message}";
            Debug.WriteLine($"Error deleting backup: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenBackupFolder(SaveBackupInfo backup)
    {
        if (backup == null || string.IsNullOrEmpty(backup.BackupPath) || !Directory.Exists(backup.BackupPath))
        {
            StatusMessage = "Backup folder not found!";
            return;
        }

        try
        {
            // Using Process.Start to open the folder in explorer
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = backup.BackupPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
            StatusMessage = $"Opened backup folder from {backup.Description}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open backup folder: {ex.Message}";
            Debug.WriteLine($"Error opening backup folder: {ex.Message}");
        }
    }

    public void UpdateFromSettings()
    {
        // Stop the timer before potentially restarting it
        _backupTimer.Stop(); 
        
        // Start or stop backup timer based on global setting
        if (_settings.GlobalAutoSaveEnabled)
        {
            // Set timer interval to 1 minute for checking
            _backupTimer.Interval = 60000; // Check every minute for precise timing
            _backupTimer.Start();
            Debug.WriteLine($"Auto-save timer restarted. Global interval: {_settings.AutoSaveInterval} minutes");
            StatusMessage = $"Global auto-save enabled. Using {_settings.AutoSaveInterval} minute interval.";
            
            // Create initial backup for any running apps that need it
            var currentTime = DateTime.Now;
            var runningApps = AllInstalledApps
                .Where(app => app.IsRunning && 
                          !string.IsNullOrEmpty(app.SavePath) && 
                          app.SavePath != "Unknown" && 
                          Directory.Exists(app.SavePath))
                .ToList();
                
            foreach (var app in runningApps)
            {
                if (app.LastBackupTime == DateTime.MinValue)
                {
                    CreateBackup(app);
                }
            }
        }
        else
        {
            Debug.WriteLine("Auto-save timer stopped - auto-save disabled");
            StatusMessage = "Global auto-save disabled. No automatic backups will occur.";
        }
    }
    
    [RelayCommand]
    private void ToggleCustomSettings()
    {
        if (SelectedApp == null)
            return;

        SelectedApp.HasCustomSettings = !SelectedApp.HasCustomSettings;
        
        if (SelectedApp.HasCustomSettings)
        {            // Initialize custom settings with current global settings
            SelectedApp.CustomSettings = new AppSpecificSettings
            {
                HasCustomSettings = true,
                AutoSaveInterval = _settings.AutoSaveInterval,
                AutoSaveEnabled = _settings.GlobalAutoSaveEnabled,
                StartSaveEnabled = _settings.StartSaveEnabled,
                MaxAutoSaves = _settings.MaxAutoSaves,
                MaxStartSaves = _settings.MaxStartSaves,
                ChangeDetectionEnabled = _settings.ChangeDetectionEnabled // Initialize with global change detection setting
            };
            
            // Save to settings
            _settings.AppSettings[SelectedApp.ExecutablePath] = SelectedApp.CustomSettings;
            StatusMessage = $"Enabled custom settings for {SelectedApp.Name}";
        }
        else
        {
            // Remove custom settings
            _settings.AppSettings.Remove(SelectedApp.ExecutablePath);
            // Reset custom settings to default values instead of null
            SelectedApp.CustomSettings = new AppSpecificSettings();
            StatusMessage = $"Disabled custom settings for {SelectedApp.Name}";
        }
        
        _settings.Save();
    }

    private void LoadAppSettings(ApplicationInfo app)
    {
        if (_settings.AppSettings.TryGetValue(app.ExecutablePath, out var customSettings))
        {
            app.HasCustomSettings = true;
            app.CustomSettings = customSettings;
        }
    }
    
    private void OnUIRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Notify UI about property changes for time-based elements
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Update next save text
            UpdateNextSaveText();
              // Force UI refresh for all backup histories
            if (SelectedApp != null)
            {
                // Trigger UI updates for all backup history items
                foreach (var backup in SelectedApp.BackupHistory)
                {
                    // This will make the DateTimeToTimeAgoConverter re-convert the timestamp
                    backup.RefreshTimeDisplay();
                }
                
                // Also refresh LastBackupTime and LastUsed displays
                SelectedApp.RefreshTimeDisplays();
                
                // Ensure the next save text is updated properly for the selected app
                this.RaisePropertyChanged(nameof(NextSaveText));
            }
            
            // Refresh time displays for all visible apps
            foreach (var app in InstalledApps)
            {
                app.RefreshTimeDisplays();
            }
            
            // Also refresh hidden games if expanded
            if (IsHiddenGamesExpanded)
            {
                foreach (var app in HiddenGames)
                {
                    app.RefreshTimeDisplays();
                }
            }
        });
    }

    [RelayCommand]
    private void SaveNow()
    {
        if (SelectedApp != null && !string.IsNullOrEmpty(SelectedApp.SavePath) && 
            SelectedApp.SavePath != "Unknown" && Directory.Exists(SelectedApp.SavePath))
        {
            // Generate backup folder path (app-specific)
            string appBackupFolder = Path.Combine(_backupRootFolder, SanitizePathName(SelectedApp.Name));
            if (!Directory.Exists(appBackupFolder))
            {
                Directory.CreateDirectory(appBackupFolder);
            }
            
            // Create timestamp folder for this specific backup
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupFolder = Path.Combine(appBackupFolder, timestamp);
            Directory.CreateDirectory(backupFolder);
            
            // Count actual files copied
            int filesCopied = 0;
            
            // Copy all files from the save directory to the backup folder
            CopyDirectory(SelectedApp.SavePath, backupFolder, ref filesCopied);
            
            // Only create a backup entry if files were actually copied
            if (filesCopied > 0)
            {
                // Create backup info with "Forced Save" type
                var backupInfoVM = new ViewModels.SaveBackupInfo
                {
                    BackupPath = backupFolder,
                    Timestamp = DateTime.Now,
                    Description = $"Forced save ({timestamp})",
                    IsAutoBackup = false // Not auto backup
                };
                
                // Create backup info for settings
                var backupInfoModel = new Models.SaveBackupInfo
                {
                    BackupPath = backupInfoVM.BackupPath,
                    Timestamp = backupInfoVM.Timestamp,
                    Description = backupInfoVM.Description,
                    IsAutoBackup = backupInfoVM.IsAutoBackup
                };

                // Update app's backup history and last backup time
                SelectedApp.LastBackupTime = DateTime.Now;
                SelectedApp.BackupHistory.Insert(0, backupInfoVM);
                
                // Update the last backup time in settings
                _settings.LastBackupTimes[SelectedApp.ExecutablePath] = SelectedApp.LastBackupTime;
                
                // Save to settings
                if (!_settings.BackupHistory.ContainsKey(SelectedApp.ExecutablePath))
                {
                    _settings.BackupHistory[SelectedApp.ExecutablePath] = new List<Models.SaveBackupInfo>();
                }
                _settings.BackupHistory[SelectedApp.ExecutablePath].Add(backupInfoModel);
                _settings.Save();
                
                StatusMessage = $"Created manual save for '{SelectedApp.Name}'";
            }
            else
            {
                StatusMessage = "No files were copied. Save might be empty.";
            }
        }
        else
        {
            StatusMessage = "Save path is invalid or doesn't exist";
        }
    }

    private void UpdateNextSaveText()
    {
        // Default to "Save Now" if there's no proper selection or if app isn't running
        if (SelectedApp == null || !SelectedApp.IsRunning)
        {
            NextSaveText = "Save Now";
            return;
        }

        bool isAutoSaveEnabled = SelectedApp.HasCustomSettings
            ? SelectedApp.CustomSettings.AutoSaveEnabled
            : _settings.GlobalAutoSaveEnabled;

        // If auto-save isn't enabled, just show "Save Now"
        if (!isAutoSaveEnabled)
        {
            NextSaveText = "Save Now";
            return;
        }

        int effectiveInterval = SelectedApp.HasCustomSettings
            ? SelectedApp.CustomSettings.AutoSaveInterval
            : _settings.AutoSaveInterval;

        if (effectiveInterval < 1) effectiveInterval = 1;

        DateTime lastBackup = SelectedApp.LastBackupTime;
        if (lastBackup == DateTime.MinValue && _settings.LastBackupTimes.TryGetValue(SelectedApp.ExecutablePath, out DateTime settingsLastBackup))
        {
            lastBackup = settingsLastBackup;
        }

        if (lastBackup != DateTime.MinValue)
        {
            var nextBackupTime = lastBackup.AddMinutes(effectiveInterval);
            var timeUntilNext = nextBackupTime - DateTime.Now;

            if (timeUntilNext.TotalSeconds <= 0)
            {
                NextSaveText = "Save Now";
            }
            else if (timeUntilNext.TotalMinutes < 1)
            {
                NextSaveText = $"Will save in {(int)timeUntilNext.TotalSeconds}s";
            }
            else
            {
                NextSaveText = $"Will save in {(int)timeUntilNext.TotalMinutes}m";
            }
        }
        else
        {
            NextSaveText = "Save Now";
        }
    }

    private void StartBackgroundAppRefresh()
    {
        // Only run this if we have apps already loaded
        if (AllInstalledApps.Count == 0)
            return;
            
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000); // Give the UI time to settle after initial load
                
                Debug.WriteLine("Starting background app refresh...");
                var newApps = new ObservableCollection<ApplicationInfo>();
                
                if (OperatingSystem.IsWindows())
                {                    // Create a hash set of existing paths for quick lookup
                    var existingPaths = new HashSet<string>(
                        AllInstalledApps.Select(a => a.ExecutablePath),
                        StringComparer.OrdinalIgnoreCase
                    );
                    
                    // Create a hash set of executable filenames to avoid duplicates by executable name
                    var existingExeNames = new HashSet<string>(
                        AllInstalledApps.Select(a => Path.GetFileName(a.ExecutablePath)),
                        StringComparer.OrdinalIgnoreCase
                    );
                    
                    // Scan for new applications without affecting existing ones
                    SearchForExecutables(newApps);
                    
                    // Only process apps that aren't already in our list by path or executable name
                    var actuallyNewApps = newApps.Where(newApp => 
                        !existingPaths.Contains(newApp.ExecutablePath) && 
                        !existingExeNames.Contains(Path.GetFileName(newApp.ExecutablePath))).ToList();
                        
                    if (actuallyNewApps.Count > 0)
                    {
                        Debug.WriteLine($"Found {actuallyNewApps.Count} new applications in background scan");
                        
                        // Process new apps on UI thread
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            foreach (var newApp in actuallyNewApps)
                            {
                                // Configure new app settings
                                if (_appData.CustomNames != null && _appData.CustomNames.TryGetValue(newApp.ExecutablePath, out string? customName))
                                {
                                    if (!string.IsNullOrWhiteSpace(customName))
                                    {
                                        newApp.Name = customName;
                                    }
                                }
                                
                                if (_appData.CustomSavePaths != null && _appData.CustomSavePaths.TryGetValue(newApp.ExecutablePath, out string? customSavePath))
                                {
                                    if (!string.IsNullOrWhiteSpace(customSavePath))
                                    {
                                        newApp.SavePath = customSavePath;
                                    }
                                }
                                else
                                {
                                    newApp.SavePath = DetectSavePath(newApp);
                                }
                                
                                // Apply hidden state if available
                                if (_appData.HiddenApps != null)
                                {
                                    newApp.IsHidden = _appData.HiddenApps.Contains(newApp.ExecutablePath);
                                }
                                
                                LoadAppSettings(newApp);
                                
                                // Add to AllInstalledApps
                                AllInstalledApps.Add(newApp);
                                
                                // Add to the appropriate UI collection
                                if (newApp.IsHidden)
                                {
                                    HiddenGames.Add(newApp);
                                }
                                else
                                {
                                    InstalledApps.Add(newApp);
                                }
                                  // Save this app's path in known applications
                                if (!_appData.KnownApplicationPaths.Contains(newApp.ExecutablePath))
                                {
                                    _appData.KnownApplicationPaths.Add(newApp.ExecutablePath);
                                }
                            }
                              // Apply sort to maintain order
                            ApplySort();
                            
                            // Save AppData to persist the new apps and their custom settings
                            _appData.Save();
                            
                            // Calculate statistics after new apps are added
                            int totalPrograms = AllInstalledApps.Count;
                            int withSaveLocations = AllInstalledApps.Count(app => !string.IsNullOrEmpty(app.SavePath) && app.SavePath != "Unknown");
                            
                            // Update status with newly found apps and overall statistics
                            string statusMessage = $"Found {actuallyNewApps.Count} new applications. Total: {totalPrograms} programs, {withSaveLocations} with save location";
                            StatusMessage = statusMessage;
                            Debug.WriteLine(statusMessage);
                        });
                    }
                    else
                    {
                        Debug.WriteLine("No new applications found in background scan");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in background app refresh: {ex.Message}");
            }
        });
    }    
    // Login popup methods
    [RelayCommand]
    private void ShowLoginPopup()
    {
        // If in offline mode, toggle to online mode
        if (_settings.OfflineMode)
        {
            ToggleOfflineMode();
            return;
        }
        
        // Otherwise show the login popup
        IsLoginPopupOpen = true;
    }
      [RelayCommand]
    private void ToggleOfflineMode()
    {
        var wasOffline = _settings.OfflineMode;
        
        // Log the toggle action
        var logger = LoggingService.Instance;
        if (logger != null)
        {
            logger.Info($"User toggling offline mode from {(wasOffline ? "offline" : "online")} to {(!wasOffline ? "offline" : "online")}");
        }
        
        // Toggle the offline mode setting
        _settings.OfflineMode = !_settings.OfflineMode;
        
        // This is an explicit user choice, so save it immediately to make sure it persists
        _settings.ForceSave();
        
        // Store that user has manually set online/offline preference
        Environment.SetEnvironmentVariable("SAVEVAULT_USER_PREFERENCE_SET", "true");
        
        // Log after the change
        if (logger != null)
        {
            logger.Info($"Offline mode successfully changed to: {(_settings.OfflineMode ? "offline" : "online")}");
        }
        
        // Update UI properties
        this.RaisePropertyChanged(nameof(IsOfflineMode));
        this.RaisePropertyChanged(nameof(IsLoggedIn));
        this.RaisePropertyChanged(nameof(LoginStatusText));
          // Show appropriate status message
        if (_settings.OfflineMode)
        {
            StatusMessage = "Switched to offline mode. Online features disabled.";
            
            // If we're viewing notifications, go back to home
            if (IsNotificationsVisible)
            {
                IsNotificationsVisible = false;
                SelectedApp = null;
                StatusMessage = "Switched to offline mode. Notifications are disabled.";
            }
        }
        else
        {
            StatusMessage = "Switched to online mode. Online features enabled.";
        }
    }
      [RelayCommand]
    private void ShowHome()
    {
        // Clear the selected app to show the empty screen
        SelectedApp = null;
        
        // Hide notifications panel if it's visible
        if (IsNotificationsVisible)
        {
            IsNotificationsVisible = false;
        }

        // Hide Save Carrier panel if it's visible
        if (IsSaveCarrierVisible)
        {
            IsSaveCarrierVisible = false;
        }
        
        StatusMessage = "Home";
    }
      
    [RelayCommand]
    private void CloseLoginPopup()
    {
        IsLoginPopupOpen = false;
        // Clear the fields when closing
        Password = string.Empty;
        Email = string.Empty;
    }
      [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "Please enter both username/email and password";
            return;
        }
        
        try 
        {
            IsLoading = true;
            StatusMessage = "Logging in...";
            
            // Create authentication service instance
            var authService = new Services.AuthenticationService();
            
            // Attempt login with server - Username field can contain either username or email
            var (success, message, token, username) = await authService.LoginAsync(Username, Password);
            
            if (success && !string.IsNullOrEmpty(token))
            {
                // Log the automatic offline mode disable during login
                var logger = LoggingService.Instance;
                if (logger != null)
                {
                    logger.Info("User successfully logged in - automatically disabling offline mode");
                }
                
                // Ensure offline mode is disabled
                _settings.OfflineMode = false;
                
                // Store token and username in settings
                _settings.LoggedInUser = username;
                _settings.AuthToken = token;
                _settings.Save();
                
                // Log the completion of login
                if (logger != null)
                {
                    logger.Info($"Login completed for user '{username}' - offline mode disabled, settings auto-saved");
                }
                
                // Close the popup
                IsLoginPopupOpen = false;
                
                // Show success message
                StatusMessage = $"Logged in as {username}";
                
                // Clear password but keep username
                Password = string.Empty;
                
                // Notify UI about login status change
                this.RaisePropertyChanged(nameof(IsOfflineMode));
                this.RaisePropertyChanged(nameof(IsLoggedIn));
                this.RaisePropertyChanged(nameof(LoginStatusText));
                this.RaisePropertyChanged(nameof(LoggedInInitial));
            }
            else 
            {
                StatusMessage = message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Login error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
      [RelayCommand]
    private async Task Register()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(Email))
        {
            StatusMessage = "Please enter username, email and password";
            return;
        }
        
        try 
        {
            IsLoading = true;
            StatusMessage = "Creating account...";
            
            // Create authentication service instance
            var authService = new Services.AuthenticationService();
            
            // Attempt registration with server
            var (success, message, token, username) = await authService.RegisterAsync(Username, Email, Password);
            
            if (success && !string.IsNullOrWhiteSpace(token))
            {
                // Log the automatic offline mode disable during registration
                var logger = LoggingService.Instance;
                if (logger != null)
                {
                    logger.Info("User successfully registered - automatically disabling offline mode");
                }
                
                // Ensure offline mode is disabled
                _settings.OfflineMode = false;
                
                // Store token and username in settings
                _settings.LoggedInUser = username;
                _settings.AuthToken = token;
                _settings.Save();
                
                // Log the completion of registration
                if (logger != null)
                {
                    logger.Info($"Registration completed for user '{username}' - offline mode disabled, settings auto-saved");
                }
                
                // Close the popup
                IsLoginPopupOpen = false;
                
                // Show success message
                StatusMessage = $"Registered and logged in as {username}";
                
                // Clear password but keep username and email
                Password = string.Empty;
                
                // Notify UI about login status change
                this.RaisePropertyChanged(nameof(IsOfflineMode));
                this.RaisePropertyChanged(nameof(IsLoggedIn));
                this.RaisePropertyChanged(nameof(LoginStatusText));
                this.RaisePropertyChanged(nameof(LoggedInInitial));
            }
            else 
            {
                StatusMessage = message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Registration error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
      [RelayCommand]
    private async Task ForgotPassword()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email))
        {
            StatusMessage = "Please enter both username and email";
            return;
        }
        
        try 
        {
            IsLoading = true;
            StatusMessage = "Sending password reset request...";
            
            // Create authentication service instance
            var authService = new Services.AuthenticationService();
            
            // Attempt password reset with server
            var (success, message) = await authService.ForgotPasswordAsync(Username, Email);
            
            if (success)
            {
                StatusMessage = "Password reset instructions sent to your email";
            }
            else 
            {
                StatusMessage = message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Password reset error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }    [RelayCommand]
    private void ShowProfile()
    {
        if (!IsLoggedIn)
        {
            // If not logged in, show login popup directly
            ShowLoginPopup();
            return;
        }
        
        // Toggle the profile panel visibility
        IsProfilePanelOpen = true;
        
        // Close other panels if they're open
        IsAddAppPanelOpen = false;
        IsLoginPopupOpen = false;
        
        // Show profile information
        StatusMessage = $"Viewing profile for {_settings.LoggedInUser}";
    }
      [RelayCommand]
    private void Logout()
    {
        if (!IsLoggedIn)
            return;
            
        // Close the profile panel first
        IsProfilePanelOpen = false;
            
        // Clear authentication data
        _settings.LoggedInUser = null;
        _settings.AuthToken = null;
        
        // Log the automatic offline mode switch during logout
        var logger = LoggingService.Instance;
        if (logger != null)
        {
            logger.Info("User logging out - automatically enabling offline mode");
        }
        
        // Set offline mode to true when logging out
        _settings.OfflineMode = true;
        _settings.Save();
        
        // Log the completion of logout
        if (logger != null)
        {
            logger.Info("Logout completed - user switched to offline mode, settings auto-saved");
        }
        
        // Update UI
        StatusMessage = "Successfully logged out and switched to offline mode";
        this.RaisePropertyChanged(nameof(IsOfflineMode));
        this.RaisePropertyChanged(nameof(IsLoggedIn));
        this.RaisePropertyChanged(nameof(LoginStatusText));
        this.RaisePropertyChanged(nameof(LoggedInInitial));
    }
    
    // System tray commands
    public ICommand ExitApplicationCommand { get; private set; }    // Panel visibility properties
    private bool _isAddAppPanelOpen;
    public bool IsAddAppPanelOpen
    {
        get => _isAddAppPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isAddAppPanelOpen, value);
    }
      private bool _isProfilePanelOpen;
    public bool IsProfilePanelOpen
    {
        get => _isProfilePanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isProfilePanelOpen, value);
    }
    
    [RelayCommand]
    private void CloseProfilePanel()
    {
        IsProfilePanelOpen = false;
    }
      private string _newAppName = string.Empty;
    public string NewAppName
    {
        get => _newAppName;
        set => this.RaiseAndSetIfChanged(ref _newAppName, value);
    }

    private string _newAppLocationFolder = string.Empty;
    public string NewAppLocationFolder
    {
        get => _newAppLocationFolder;
        set => this.RaiseAndSetIfChanged(ref _newAppLocationFolder, value);
    }
    
    private string _newAppExecutablePath = string.Empty;
    public string NewAppExecutablePath
    {
        get => _newAppExecutablePath;
        set => this.RaiseAndSetIfChanged(ref _newAppExecutablePath, value);
    }
    
    private string _newAppSavePath = string.Empty;
    public string NewAppSavePath
    {
        get => _newAppSavePath;
        set => this.RaiseAndSetIfChanged(ref _newAppSavePath, value);
    }
    
    // Add Application Panel Commands    [RelayCommand]
    private void ShowAddAppPanel()
    {
        // Reset the form fields
        NewAppName = string.Empty;
        NewAppLocationFolder = string.Empty;
        NewAppExecutablePath = string.Empty;
        NewAppSavePath = string.Empty;
        
        // Show the panel
        IsAddAppPanelOpen = true;
    }
      [RelayCommand]    private void CloseAddAppPanel()
    {
        IsAddAppPanelOpen = false;
    }

    [RelayCommand]
    private async Task BrowseForLocationFolder()
    {
        try
        {
            // Get the current top-level window
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = topLevel?.MainWindow;
            
            if (mainWindow != null)
            {
                // Use the StorageProvider API
                var folderResult = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Application Location Folder",
                    AllowMultiple = false
                });

                if (folderResult.Count > 0)
                {
                    NewAppLocationFolder = folderResult[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error selecting folder: {ex.Message}";
            Debug.WriteLine($"Error selecting folder: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private async Task BrowseForExecutable()
    {
        try
        {
            // Get the current top-level window
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = topLevel?.MainWindow;
            
            if (mainWindow != null)
            {
                // Use the StorageProvider API
                var fileResult = await mainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Application Executable",
                    AllowMultiple = false,
                    FileTypeFilter = new[] 
                    { 
                        new Avalonia.Platform.Storage.FilePickerFileType("Executable Files")
                        {
                            Patterns = new[] { "*.exe" }
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (fileResult.Count > 0)
                {
                    NewAppExecutablePath = fileResult[0].Path.LocalPath;
                    
                    // If location folder is empty, set it to the directory containing the executable
                    if (string.IsNullOrEmpty(NewAppLocationFolder))
                    {
                        NewAppLocationFolder = Path.GetDirectoryName(NewAppExecutablePath) ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error selecting file: {ex.Message}";
            Debug.WriteLine($"Error selecting file: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private async Task BrowseForSaveLocation()
    {
        try
        {
            // Get the current top-level window
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = topLevel?.MainWindow;
            
            if (mainWindow != null)
            {
                // Use the StorageProvider API
                var folderResult = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Save Location Folder",
                    AllowMultiple = false
                });

                if (folderResult.Count > 0)
                {
                    NewAppSavePath = folderResult[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error selecting folder: {ex.Message}";
            Debug.WriteLine($"Error selecting folder: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private void AddNewApplication()
    {
        if (string.IsNullOrWhiteSpace(NewAppExecutablePath))
        {
            StatusMessage = "Executable path is required";
            return;
        }
        
        // Check if the executable exists
        if (!File.Exists(NewAppExecutablePath))
        {
            StatusMessage = "Executable file does not exist";
            return;
        }
          // Check if app is already in the list - by path
        if (AllInstalledApps.Any(app => string.Equals(app.ExecutablePath, NewAppExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "This application is already in your list";
            CloseAddAppPanel();
            return;
        }
        
        // Also check by executable name to avoid duplicates
        string newExeName = Path.GetFileName(NewAppExecutablePath);
        if (AllInstalledApps.Any(app => string.Equals(Path.GetFileName(app.ExecutablePath), newExeName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"An application with executable name '{newExeName}' is already in your list";
            CloseAddAppPanel();
            return;
        }
        
        try
        {            // Create a new ApplicationInfo object
            var defaultName = Path.GetFileNameWithoutExtension(NewAppExecutablePath);
            var app = new ApplicationInfo(_settings)
            {
                Name = !string.IsNullOrWhiteSpace(NewAppName) ? NewAppName.Trim() : defaultName,
                ExecutablePath = NewAppExecutablePath,
                Path = !string.IsNullOrEmpty(NewAppLocationFolder) ? NewAppLocationFolder : Path.GetDirectoryName(NewAppExecutablePath) ?? string.Empty
            };
            
            // Set the save path if provided, otherwise detect it
            if (!string.IsNullOrEmpty(NewAppSavePath) && Directory.Exists(NewAppSavePath))
            {
                app.SavePath = NewAppSavePath;
                
                // Save custom save path to AppData
                _appData.CustomSavePaths[NewAppExecutablePath] = NewAppSavePath;
            }
            else
            {
                app.SavePath = DetectSavePath(app);
            }
            
            // Add to collections
            AllInstalledApps.Add(app);
            InstalledApps.Add(app);
              
            // Add to known applications in AppData
            _appData.KnownApplicationPaths.Add(app.ExecutablePath);
            _appData.Save();
            
            // Apply sort to maintain order
            ApplySort();
            
            // Update status
            StatusMessage = $"Added '{app.Name}' to your applications";
            
            // Close the panel
            CloseAddAppPanel();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding application: {ex.Message}";
            Debug.WriteLine($"Error adding application: {ex.Message}");
        }
    }
    
    public IRelayCommand ShowAddAppPanelCommand => ShowAddAppPanelCommandImpl ??= new RelayCommand(ShowAddAppPanel);
    private IRelayCommand? ShowAddAppPanelCommandImpl;
      public IRelayCommand OpenLogViewerCommand => OpenLogViewerCommandImpl ??= new RelayCommand(OpenLogViewer);
    private IRelayCommand? OpenLogViewerCommandImpl;
    
    public IRelayCommand OpenSaveCarrierCommand => OpenSaveCarrierCommandImpl ??= new RelayCommand(OpenSaveCarrier);
    private IRelayCommand? OpenSaveCarrierCommandImpl;

    public IRelayCommand ShowSaveCarrierCommand => ShowSaveCarrierCommandImpl ??= new RelayCommand(ShowSaveCarrier);
    private IRelayCommand? ShowSaveCarrierCommandImpl;
    
    public IRelayCommand ShowNotificationsCommand => ShowNotificationsCommandImpl ??= new RelayCommand(ShowNotifications);
    private IRelayCommand? ShowNotificationsCommandImpl;
    
    public IRelayCommand MarkAllNotificationsAsReadCommand => MarkAllNotificationsAsReadCommandImpl ??= new RelayCommand(MarkAllNotificationsAsRead);
    private IRelayCommand? MarkAllNotificationsAsReadCommandImpl;
    
    public IRelayCommand CheckForNotificationsCommand => CheckForNotificationsCommandImpl ??= new RelayCommand(CheckForNotifications);
    private IRelayCommand? CheckForNotificationsCommandImpl;
    
    public IRelayCommand<int> MarkAsReadCommand => MarkAsReadCommandImpl ??= new RelayCommand<int>(MarkAsRead);
    private IRelayCommand<int>? MarkAsReadCommandImpl;
    
    private void OpenLogViewer()
    {
        var logViewerWindow = new Views.LogViewerWindow();
        if (_mainWindow != null)
        {
            logViewerWindow.ShowDialog(_mainWindow);
        }
        else
        {
            logViewerWindow.Show();
        }
    }
      private void ShowNotifications()
    {
        // Don't show notifications panel in offline mode
        if (_settings.OfflineMode)
        {
            StatusMessage = "Notifications are disabled in offline mode";
            return;
        }
        
        // Show notifications panel
        IsNotificationsVisible = true;
        
        // Hide the "Select an application" message
        SelectedApp = null;
        
        // Set status message
        StatusMessage = "Notifications";
    }

    private void ShowSaveCarrier()
    {
        // Process all applications to ensure save paths are detected
        var allAppsWithSavePaths = new List<ApplicationInfo>();
        
        foreach (var app in AllInstalledApps)
        {
            // Try to detect save path if it's missing or unknown
            if (string.IsNullOrEmpty(app.SavePath) || app.SavePath == "Unknown")
            {
                string detectedPath = DetectSavePath(app);
                if (!string.IsNullOrEmpty(detectedPath) && detectedPath != "Unknown")
                {
                    app.SavePath = detectedPath;
                }
            }
            
            // Add all apps (they'll be filtered in SaveCarrierViewModel)
            allAppsWithSavePaths.Add(app);
        }

        // Initialize the Save Carrier view model
        SaveCarrierViewModel = new SaveCarrierViewModel(_settings, allAppsWithSavePaths);
        
        // Show Save Carrier panel
        IsSaveCarrierVisible = true;
        
        // Hide the "Select an application" message and notifications
        SelectedApp = null;
        IsNotificationsVisible = false;
        
        // Set status message
        StatusMessage = "Save Carrier";
    }
    
    private void MarkAllNotificationsAsRead()
    {
        // Mark all notifications as read
        NotificationService.Instance.MarkAllAsRead();
    }
    
    private void MarkAsRead(int notificationId)
    {
        // Mark a specific notification as read
        NotificationService.Instance.MarkAsRead(notificationId);
    }
    
    private void CheckForNotifications()
    {
        // Check for new notifications
        _ = NotificationService.Instance.CheckForNotifications();
    }
    
    private void OpenSaveCarrier()
    {
        // Process all applications to ensure save paths are detected
        var allAppsWithSavePaths = new List<ApplicationInfo>();
        
        foreach (var app in AllInstalledApps)
        {
            // Try to detect save path if it's missing or unknown
            if (string.IsNullOrEmpty(app.SavePath) || app.SavePath == "Unknown")
            {
                string detectedPath = DetectSavePath(app);
                if (!string.IsNullOrEmpty(detectedPath) && detectedPath != "Unknown")
                {
                    app.SavePath = detectedPath;
                }
            }
            
            // Add all apps (they'll be filtered in SaveCarrierViewModel)
            allAppsWithSavePaths.Add(app);
        }
        
        var saveCarrierWindow = new Views.SaveCarrierWindow(_settings, allAppsWithSavePaths);
        if (_mainWindow != null)
        {
            saveCarrierWindow.ShowDialog(_mainWindow);
        }
        else
        {
            saveCarrierWindow.Show();
        }
    }
    
    // Update properties
    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
    }
    
    private string _updateStatus = "No updates checked";
    public string UpdateStatus
    {
        get => _updateStatus;
        set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
    }
    
    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }
    
    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => this.RaiseAndSetIfChanged(ref _isDownloadingUpdate, value);
    }
    
    private bool _isUpdateNotificationVisible;
    public bool IsUpdateNotificationVisible
    {
        get => _isUpdateNotificationVisible;
        set => this.RaiseAndSetIfChanged(ref _isUpdateNotificationVisible, value);
    }
    
    private string _updateVersion = string.Empty;
    public string UpdateVersion
    {
        get => _updateVersion;
        set => this.RaiseAndSetIfChanged(ref _updateVersion, value);
    }
    
    // Notification properties
    private bool _hasUnreadNotifications;
    public bool HasUnreadNotifications
    {
        get => _hasUnreadNotifications;
        set => this.RaiseAndSetIfChanged(ref _hasUnreadNotifications, value);
    }
    
    private ObservableCollection<Notification> _notifications = new();
    public ObservableCollection<Notification> Notifications
    {
        get => _notifications;
        set => this.RaiseAndSetIfChanged(ref _notifications, value);
    }
      private Notification? _selectedNotification;
    public Notification? SelectedNotification
    {
        get => _selectedNotification;
        set 
        {
            this.RaiseAndSetIfChanged(ref _selectedNotification, value);
            
            // If a notification is selected and it's not read, mark it as read
            if (value != null && !value.IsRead)
            {
                // Mark the notification as read using the service
                NotificationService.Instance.MarkAsRead(value.Id);
            }
        }
    }
    
    private bool _isNotificationsVisible;
    public bool IsNotificationsVisible
    {
        get => _isNotificationsVisible;
        set => this.RaiseAndSetIfChanged(ref _isNotificationsVisible, value);
    }

    private bool _isSaveCarrierVisible;
    public bool IsSaveCarrierVisible
    {
        get => _isSaveCarrierVisible;
        set => this.RaiseAndSetIfChanged(ref _isSaveCarrierVisible, value);
    }

    private SaveCarrierViewModel? _saveCarrierViewModel;
    public SaveCarrierViewModel? SaveCarrierViewModel
    {
        get => _saveCarrierViewModel;
        set => this.RaiseAndSetIfChanged(ref _saveCarrierViewModel, value);
    }
    
    // Update commands
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        await UpdateService.Instance.CheckForUpdates();
    }
    
    [RelayCommand]
    private async Task InstallUpdate()
    {
        IsDownloadingUpdate = true;
        await UpdateService.Instance.DownloadAndInstallUpdate();
        IsDownloadingUpdate = false;
    }
    
    [RelayCommand]
    private void DismissUpdateNotification()
    {
        IsUpdateNotificationVisible = false;
    }
    
    // Add this to initialize update checking when the app starts
    public async Task InitializeUpdateCheck()
    {
        // Check for updates if needed based on settings
        await UpdateService.Instance.CheckForUpdatesIfNeeded();
    }
    
    [RelayCommand]
    private void OnNextSaveHover()
    {
        IsHoveringNextSave = true;
    }
    
    [RelayCommand]
    public void ToggleHiddenGamesVisibility()
    {
        IsHiddenGamesExpanded = !IsHiddenGamesExpanded;
    }
    
    // Properties for Steam launch options
    public bool HasSteamLaunchOption
    {
        get
        {
            if (SelectedApp == null)
                return false;
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            return knownGame != null && !string.IsNullOrEmpty(knownGame.LaunchFromSteam);
        }
    }
    
    public bool HasAlternExec1
    {
        get
        {
            if (SelectedApp == null)
                return false;
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            return knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec1);
        }
    }
    
    public bool HasAlternExec2
    {
        get
        {
            if (SelectedApp == null)
                return false;
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            return knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec2);
        }
    }
    
    public bool HasStorePage
    {
        get
        {
            if (SelectedApp == null)
                return false;
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            return knownGame != null && !string.IsNullOrEmpty(knownGame.Store);
        }
    }
    
    public bool HasUninstallOption
    {
        get
        {
            if (SelectedApp == null)
                return false;
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            return knownGame != null && !string.IsNullOrEmpty(knownGame.Uninstall);
        }
    }
    
    [RelayCommand]
    private void LaunchFromSteam()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Find the matching known game
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.LaunchFromSteam))
            {
                // Launch the game via Steam URL protocol
                ProcessStartInfo startInfo = new ProcessStartInfo(knownGame.LaunchFromSteam)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                
                // Update LastUsed time and persist it
                SelectedApp.LastUsed = DateTime.Now;
                _settings.LastUsedTimes[SelectedApp.ExecutablePath] = SelectedApp.LastUsed;
                _settings.ForceSave();
                
                // If sorted by last used, refresh the sort
                if (SelectedSortOption == "Last Used")
                {
                    ApplySort();
                }
                
                // Immediately check running applications after launching
                UpdateRunningApplications();
                
                StatusMessage = $"Launched {SelectedApp.Name} via Steam";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching application from Steam: {ex.Message}");
            StatusMessage = $"Error launching from Steam: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void LaunchAlternExec1()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Find the matching known game
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec1))
            {
                string alternativePath = Path.Combine(SelectedApp.Path, knownGame.AlternExec1);
                
                if (File.Exists(alternativePath))
                {
                    // Launch the alternative executable
                    ProcessStartInfo startInfo = new ProcessStartInfo(alternativePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(alternativePath)
                    };
                    Process.Start(startInfo);
                    
                    // Update LastUsed time and persist it
                    SelectedApp.LastUsed = DateTime.Now;
                    _settings.LastUsedTimes[SelectedApp.ExecutablePath] = SelectedApp.LastUsed;
                    _settings.ForceSave();
                    
                    // If sorted by last used, refresh the sort
                    if (SelectedSortOption == "Last Used")
                    {
                        ApplySort();
                    }
                    
                    // Immediately check running applications after launching
                    UpdateRunningApplications();
                    
                    StatusMessage = $"Launched {SelectedApp.Name} (alternate executable)";
                }
                else
                {
                    StatusMessage = $"Alternative executable not found: {alternativePath}";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching alternative executable 1: {ex.Message}");
            StatusMessage = $"Error launching alternative executable: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void LaunchAlternExec2()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Find the matching known game
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec2))
            {
                string alternativePath = Path.Combine(SelectedApp.Path, knownGame.AlternExec2);
                
                if (File.Exists(alternativePath))
                {
                    // Launch the alternative executable
                    ProcessStartInfo startInfo = new ProcessStartInfo(alternativePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(alternativePath)
                    };
                    Process.Start(startInfo);
                    
                    // Update LastUsed time and persist it
                    SelectedApp.LastUsed = DateTime.Now;
                    _settings.LastUsedTimes[SelectedApp.ExecutablePath] = SelectedApp.LastUsed;
                    _settings.ForceSave();
                    
                    // If sorted by last used, refresh the sort
                    if (SelectedSortOption == "Last Used")
                    {
                        ApplySort();
                    }
                    
                    // Immediately check running applications after launching
                    UpdateRunningApplications();
                    
                    StatusMessage = $"Launched {SelectedApp.Name} (alternate executable 2)";
                }
                else
                {
                    StatusMessage = $"Alternative executable 2 not found: {alternativePath}";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching alternative executable 2: {ex.Message}");
            StatusMessage = $"Error launching alternative executable 2: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void OpenStorePage()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Find the matching known game
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.Store))
            {
                // Open the store page
                ProcessStartInfo startInfo = new ProcessStartInfo(knownGame.Store)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                
                StatusMessage = $"Opened store page for {SelectedApp.Name}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening store page: {ex.Message}");
            StatusMessage = $"Error opening store page: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void UninstallApp()
    {
        if (SelectedApp == null)
            return;
            
        try
        {
            // Find the matching known game
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.Uninstall))
            {
                // Launch the uninstall process
                ProcessStartInfo startInfo = new ProcessStartInfo(knownGame.Uninstall)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                
                StatusMessage = $"Started uninstall process for {SelectedApp.Name}";
            }
            else
            {
                // Try generic Windows uninstall approach
                ProcessStartInfo startInfo = new ProcessStartInfo("control.exe", "appwiz.cpl")
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                
                StatusMessage = $"Opened Programs and Features for {SelectedApp.Name}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error uninstalling application: {ex.Message}");
            StatusMessage = $"Error starting uninstall: {ex.Message}";
        }
    }
    
    // Properties for alternative executable menu text
    public string AlternExec1MenuText
    {
        get
        {
            if (SelectedApp == null)
                return "Launch Alternative";
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec1))
            {
                string fileName = Path.GetFileNameWithoutExtension(knownGame.AlternExec1);
                return $"Launch {fileName}";
            }
            
            return "Launch Alternative";
        }
    }
    
    public string AlternExec2MenuText
    {
        get
        {
            if (SelectedApp == null)
                return "Launch Alternative 2";
                
            var knownGame = SaveVaultApp.Utilities.KnownGames.GamesList.FirstOrDefault(g =>
                string.Equals(g.Executable, System.IO.Path.GetFileName(SelectedApp.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(g.Name, SelectedApp.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.Name)) ||
                (!string.IsNullOrEmpty(g.GameFolder) && (SelectedApp.Path.Contains(g.GameFolder, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(SelectedApp.Path).Equals(g.GameFolder, StringComparison.OrdinalIgnoreCase))));
                    
            if (knownGame != null && !string.IsNullOrEmpty(knownGame.AlternExec2))
            {
                string fileName = Path.GetFileNameWithoutExtension(knownGame.AlternExec2);
                return $"Launch {fileName}";
            }
            
            return "Launch Alternative 2";
        }
    }
    
    // Ensures that backup paths for an app match its new name by updating the backup folder names
    private void EnsureBackupPathsMatchAppName(ApplicationInfo app, string originalName)
    {
        try
        {
            if (string.IsNullOrEmpty(originalName) || originalName == app.Name)
                return;
                
            // Original name sanitized
            string sanitizedOldName = SanitizePathName(originalName);
            
            // New name sanitized
            string sanitizedNewName = SanitizePathName(app.Name);
            
            if (sanitizedOldName == sanitizedNewName)
                return; // No change needed
                
            // Folder locations
            string oldBackupFolder = Path.Combine(_backupRootFolder, sanitizedOldName);
            string newBackupFolder = Path.Combine(_backupRootFolder, sanitizedNewName);
            
            // Check if the old folder exists
            if (!Directory.Exists(oldBackupFolder))
                return;
                
            // Create the new directory if needed
            if (!Directory.Exists(newBackupFolder))
            {
                Directory.CreateDirectory(newBackupFolder);
            }              // Move all backup folders from old location to new location
            foreach (var backupInfo in app.BackupHistory.ToList()) // ToList to avoid collection modified during iteration issues
            {
                if (backupInfo.BackupPath.StartsWith(oldBackupFolder))
                {
                    // Get the relative path (timestamp subfolder)
                    string timestamp = Path.GetFileName(backupInfo.BackupPath);
                    
                    // Create new path
                    string newPath = Path.Combine(newBackupFolder, timestamp);
                    
                    // If backup folder with same timestamp doesn't exist at new location, move it
                    if (!Directory.Exists(newPath) && Directory.Exists(backupInfo.BackupPath))
                    {
                        try
                        {                            // Copy the directory to the new location
                            int filesCopied = 0;
                            CopyDirectory(backupInfo.BackupPath, newPath, ref filesCopied);
                            
                            // Update path in the backup info
                            backupInfo.BackupPath = newPath;
                            
                            // Update in settings as well
                            if (_settings.BackupHistory.TryGetValue(app.ExecutablePath, out var backupList))
                            {
                                foreach (var backup in backupList)
                                {
                                    if (backup.BackupPath.StartsWith(oldBackupFolder))
                                    {
                                        backup.BackupPath = backup.BackupPath.Replace(oldBackupFolder, newBackupFolder);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error moving backup from {backupInfo.BackupPath} to {newPath}: {ex.Message}");
                        }
                    }
                }
            }
            
            // Try to remove old folder if empty
            try
            {
                if (Directory.Exists(oldBackupFolder) && !Directory.EnumerateFileSystemEntries(oldBackupFolder).Any())
                {
                    Directory.Delete(oldBackupFolder);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting old backup folder {oldBackupFolder}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating backup paths for {app.Name}: {ex.Message}");
        }
    }
    
    // Updates backup folder paths when an app is renamed
    private void UpdateBackupFoldersAfterNameChange(string executablePath, string oldName, string newName)
    {
        // Get the app being renamed
        var app = AllInstalledApps.FirstOrDefault(a => a.ExecutablePath == executablePath);
        if (app == null)
            return;
            
        EnsureBackupPathsMatchAppName(app, oldName);
        
        // Save settings to persist the changes to backup paths
        _settings.Save();
    }
      // Toggle sidebar visibility command
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
        LoggingService.Instance.Info($"Sidebar visibility toggled to {IsSidebarVisible}");
    }
    
    // Cleanup method to dispose of resources
    public void Cleanup()
    {
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource?.Dispose();
        _scanCancellationTokenSource = null;
    }
}
 
public class ApplicationInfo : ReactiveObject
{
    // Add reference to settings
    private readonly Settings? _settings;
    
    // Constructor to initialize settings
    public ApplicationInfo(Settings settings)
    {
        _settings = settings;
    }

    private string _name = string.Empty;
    public string Name 
    { 
        get => _name; 
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }
    
    private string _path = string.Empty;
    public string Path 
    { 
        get => _path; 
        set => this.RaiseAndSetIfChanged(ref _path, value);
    }
    
    private string _executablePath = string.Empty;
    public string ExecutablePath 
    { 
        get => _executablePath; 
        set => this.RaiseAndSetIfChanged(ref _executablePath, value);
    }
    
    private DateTime _lastUsed = DateTime.MinValue;
    public DateTime LastUsed 
    { 
        get => _lastUsed; 
        set => this.RaiseAndSetIfChanged(ref _lastUsed, value);
    }
    
    private string _savePath = string.Empty;
    public string SavePath 
    { 
        get => _savePath; 
        set => this.RaiseAndSetIfChanged(ref _savePath, value);
    }
    
    private string _knownGameId = string.Empty;
    public string KnownGameId
    {
        get => _knownGameId;
        set => this.RaiseAndSetIfChanged(ref _knownGameId, value);
    }
    
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }
    
    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set => this.RaiseAndSetIfChanged(ref _isHidden, value);
    }
    
    private bool _hasCustomSettings;
    public bool HasCustomSettings
    {
        get => _hasCustomSettings;
        set => this.RaiseAndSetIfChanged(ref _hasCustomSettings, value);
    }

    private AppSpecificSettings _customSettings = new();
    public AppSpecificSettings CustomSettings
    {
        get => _customSettings;
        set
        {
            this.RaiseAndSetIfChanged(ref _customSettings, value);
            // Save settings when custom settings are updated
            if (_settings != null && HasCustomSettings)
            {
                _settings.AppSettings[ExecutablePath] = value;
                _settings.Save();
            }
        }
    }
    
    // New properties for save backup functionality
    private DateTime _lastBackupTime = DateTime.MinValue;
    public DateTime LastBackupTime
    {
        get => _lastBackupTime;
        set => this.RaiseAndSetIfChanged(ref _lastBackupTime, value);
    }
    
    private ObservableCollection<SaveBackupInfo> _backupHistory = new();
    public ObservableCollection<SaveBackupInfo> BackupHistory
    {
        get => _backupHistory;
        set => this.RaiseAndSetIfChanged(ref _backupHistory, value);
    }
      private bool _autoBackupEnabled = true;
    public bool AutoBackupEnabled
    {
        get => _hasCustomSettings ? _customSettings.AutoSaveEnabled : _autoBackupEnabled;
        set
        {
            if (_hasCustomSettings)
            {
                _customSettings.AutoSaveEnabled = value;
            }
            else
            {
                this.RaiseAndSetIfChanged(ref _autoBackupEnabled, value);
            }
        }
    }
    
    private bool _changeDetectionEnabled = true;
    public bool ChangeDetectionEnabled
    {
        get => _hasCustomSettings ? _customSettings.ChangeDetectionEnabled : _changeDetectionEnabled;
        set
        {
            if (_hasCustomSettings)
            {
                _customSettings.ChangeDetectionEnabled = value;
            }
            else
            {
                this.RaiseAndSetIfChanged(ref _changeDetectionEnabled, value);
            }
        }
    }

    // Method to refresh time displays in UI
    public void RefreshTimeDisplays()
    {
        // Trigger property change notifications for time properties
        this.RaisePropertyChanged(nameof(LastUsed));
        this.RaisePropertyChanged(nameof(LastBackupTime));
    }
}

// Save backup info class for tracking backups
public class SaveBackupInfo : ReactiveObject
{
    private string _backupPath = string.Empty;
    public string BackupPath
    {
        get => _backupPath;
        set => this.RaiseAndSetIfChanged(ref _backupPath, value);
    }
    
    private DateTime _timestamp = DateTime.Now;
    public DateTime Timestamp
    {
        get => _timestamp;
        set => this.RaiseAndSetIfChanged(ref _timestamp, value);
    }
    
    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
    
    private bool _isAutoBackup = true;
    public bool IsAutoBackup
    {
        get => _isAutoBackup;
        set => this.RaiseAndSetIfChanged(ref _isAutoBackup, value);
    }

    // Color code property for different save types
    public string ColorCode
    {
        get
        {
            // Check if this is a start save by looking at the description
            if (Description.StartsWith("Start Save"))
                return "#dbaf1f"; // The requested color code for Start Save
            
            if (IsAutoBackup)
                return "#4287f5"; // A blue color for auto saves
                
            if (Description.StartsWith("Forced save"))
                return "#42f57b"; // A green color for manual saves
                
            // Default color
            return "#808080";
        }
    }

    // Method to refresh time display
    public void RefreshTimeDisplay()
    {
        // Trigger property change notification for timestamp
        this.RaisePropertyChanged(nameof(Timestamp));
    }
}

