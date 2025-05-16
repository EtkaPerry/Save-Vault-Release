using System;
using ReactiveUI;
using CommunityToolkit.Mvvm.Input;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using Avalonia.Styling;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace SaveVaultApp.ViewModels;

public class ProgramStorageInfo : ReactiveObject
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private long _storageSize;
    public long StorageSize 
    { 
        get => _storageSize;
        set => this.RaiseAndSetIfChanged(ref _storageSize, value);
    }

    public string FormattedSize => FormatFileSize(StorageSize);

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        // Format with comma as thousands separator for values >= 1000
        if (len < 10)
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        else if (len < 100)
            return string.Format("{0:0.#} {1}", len, sizes[order]);
        else
            return string.Format("{0:0,0} {1}", Math.Round(len), sizes[order]);
    }
}

public partial class OptionsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly Action _onSettingsChanged;

    private int _autoSaveInterval;
    public int AutoSaveInterval
    {
        get => _autoSaveInterval;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoSaveInterval, value);
            _settings.AutoSaveInterval = value;
            SaveChanges();
        }
    }

    private bool _globalAutoSaveEnabled;
    public bool GlobalAutoSaveEnabled
    {
        get => _globalAutoSaveEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _globalAutoSaveEnabled, value);
            _settings.GlobalAutoSaveEnabled = value;
            SaveChanges();
        }
    }

    private bool _startSaveEnabled;
    public bool StartSaveEnabled
    {
        get => _startSaveEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _startSaveEnabled, value);
            _settings.StartSaveEnabled = value;
            SaveChanges();
        }
    }

    private bool _changeDetectionEnabled;
    public bool ChangeDetectionEnabled
    {
        get => _changeDetectionEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _changeDetectionEnabled, value);
            _settings.ChangeDetectionEnabled = value;
            SaveChanges();
        }
    }

    private int _maxAutoSaves;
    public int MaxAutoSaves
    {
        get => _maxAutoSaves;
        set
        {
            this.RaiseAndSetIfChanged(ref _maxAutoSaves, value);
            _settings.MaxAutoSaves = value;
            SaveChanges();
        }
    }
    
    private int _maxStartSaves;
    public int MaxStartSaves
    {
        get => _maxStartSaves;
        set
        {
            this.RaiseAndSetIfChanged(ref _maxStartSaves, value);
            _settings.MaxStartSaves = value;
            SaveChanges();
        }
    }
    
    // Theme properties
    private string _selectedTheme;
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedTheme, value);
                _settings.Theme = value;
                ApplyTheme(value);
                SaveChanges();
            }
        }
    }
    
    // List of available themes for the ComboBox
    public List<string> AvailableThemes { get; } = new List<string> { "System", "Light", "Dark" };

    private string _backupStorageLocation;
    public string BackupStorageLocation
    {
        get => _backupStorageLocation;
        set
        {
            this.RaiseAndSetIfChanged(ref _backupStorageLocation, value);
            _settings.BackupStorageLocation = value;
            SaveChanges();
        }
    }

    // Storage usage tracking
    private ObservableCollection<ProgramStorageInfo> _programStorageInfos = new ObservableCollection<ProgramStorageInfo>();
    public ObservableCollection<ProgramStorageInfo> ProgramStorageInfos
    {
        get => _programStorageInfos;
        set => this.RaiseAndSetIfChanged(ref _programStorageInfos, value);
    }

    // Indicates whether storage info is currently being calculated
    private bool _isCalculatingStorage;
    public bool IsCalculatingStorage
    {
        get => _isCalculatingStorage;
        set => this.RaiseAndSetIfChanged(ref _isCalculatingStorage, value);
    }    public OptionsViewModel(Settings settings, Action onSettingsChanged)
    {
        // Force Settings.Instance to be initialized and ensure we're using the singleton
        _settings = Settings.Instance ?? settings;
        if (_settings != Settings.Instance)
        {
            Debug.WriteLine("WARNING: OptionsViewModel not using Settings.Instance!");
            // Try to update the instance
            if (Settings.Instance == null)
            {
                Settings.Load();
            }
        }
        
        _onSettingsChanged = onSettingsChanged;

        // Load current settings from the instance
        _autoSaveInterval = _settings.AutoSaveInterval;
        _globalAutoSaveEnabled = _settings.GlobalAutoSaveEnabled;
        _startSaveEnabled = _settings.StartSaveEnabled;
        _changeDetectionEnabled = _settings.ChangeDetectionEnabled;
        _maxAutoSaves = _settings.MaxAutoSaves;
        _maxStartSaves = _settings.MaxStartSaves;
        _selectedTheme = _settings.Theme ?? "System";
        _backupStorageLocation = _settings.BackupStorageLocation;
        
        // Load update settings
        _autoCheckUpdates = _settings.AutoCheckUpdates;
        _updateCheckInterval = _settings.UpdateCheckInterval;

        Debug.WriteLine($"OptionsViewModel initialized with settings. AutoSaveInterval={_autoSaveInterval}, GlobalAutoSaveEnabled={_globalAutoSaveEnabled}");
        
        // Set up update service events
        var updateService = UpdateService.Instance;
        updateService.UpdateStatusChanged += (s, status) => UpdateStatus = status;
        updateService.UpdateAvailabilityChanged += (s, available) => UpdateAvailable = available;
        
        // Update status from service
        UpdateAvailable = updateService.UpdateAvailable;
        UpdateStatus = updateService.StatusMessage;
        
        // Force a save of current settings to ensure they're persisted
        SaveChanges();
        
        // Apply the current theme
        ApplyTheme(_selectedTheme);    }      // Helper method to save settings and notify about changes
    private void SaveChanges()
    {
        // Log current settings before saving
        Debug.WriteLine($"SaveChanges - Current settings: AutoSaveInterval={_settings.AutoSaveInterval}, GlobalAutoSaveEnabled={_settings.GlobalAutoSaveEnabled}");

        // Force an immediate save of settings to disk
        _settings.ForceSave();
        Debug.WriteLine("Settings saved with ForceSave()");

        // Update the main view model with the new settings
        _onSettingsChanged?.Invoke();
        Debug.WriteLine("MainViewModel updated via callback");
    }
    
    private void ApplyTheme(string themeName)
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;

        ThemeVariant theme = themeName switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default // System
        };
        
        app.RequestedThemeVariant = theme;
    }

    // Keep this for backward compatibility or direct saving if needed
    [RelayCommand]
    private void SaveSettings()
    {
        SaveChanges();
    }

    [RelayCommand]
    public async Task BrowseForBackupLocation()
    {
        try
        {
            // Get the current top-level window
            var topLevel = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = topLevel?.MainWindow;
            
            if (mainWindow != null)
            {
                // Use the StorageProvider API to select a folder
                var folderPath = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Backup Storage Location",
                    AllowMultiple = false
                });

                if (folderPath.Count > 0)
                {
                    // Get the folder path from the first selected item
                    BackupStorageLocation = folderPath[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            // Handle any errors that might occur
            Console.WriteLine($"Error selecting folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task CalculateStorageUsage()
    {
        if (IsCalculatingStorage)
            return;

        IsCalculatingStorage = true;
        ProgramStorageInfos.Clear();

        try
        {
            if (string.IsNullOrEmpty(_backupStorageLocation) || !Directory.Exists(_backupStorageLocation))
            {
                IsCalculatingStorage = false;
                return;
            }

            await Task.Run(() =>
            {
                var programFolders = Directory.GetDirectories(_backupStorageLocation);
                var storageInfos = new List<ProgramStorageInfo>();

                foreach (var folder in programFolders)
                {
                    var programName = Path.GetFileName(folder);
                    var size = CalculateDirectorySize(folder);
                    
                    if (size > 0)
                    {
                        storageInfos.Add(new ProgramStorageInfo
                        {
                            Name = programName,
                            StorageSize = size
                        });
                    }
                }

                // Sort by size in descending order
                var sortedInfos = storageInfos.OrderByDescending(x => x.StorageSize).ToList();
                
                // Update UI collection on main thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgramStorageInfos.Clear();
                    foreach (var info in sortedInfos)
                    {
                        ProgramStorageInfos.Add(info);
                    }
                    IsCalculatingStorage = false;
                });
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calculating storage usage: {ex.Message}");
            IsCalculatingStorage = false;
        }
    }

    private long CalculateDirectorySize(string folderPath)
    {
        try
        {
            long size = 0;
            
            // Add the size of all files
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                size += fileInfo.Length;
            }
            
            return size;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calculating size for {folderPath}: {ex.Message}");
            return 0;
        }
    }
    
    // Update-related properties
    private bool _autoCheckUpdates;    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoCheckUpdates, value);
            _settings.AutoCheckUpdates = value;
            
            // Log the toggle state change
            var logger = SaveVaultApp.Services.LoggingService.Instance;
            logger.Debug($"AutoCheckUpdates changed to: {value}");
            
            // Use ForceSave to ensure it really saves
            _settings.ForceSave();
            
            // Then continue with normal SaveChanges
            SaveChanges();
        }
    }

    private int _updateCheckInterval;
    public int UpdateCheckInterval
    {
        get => _updateCheckInterval;
        set
        {
            this.RaiseAndSetIfChanged(ref _updateCheckInterval, value);
            _settings.UpdateCheckInterval = value;
            SaveChanges();
        }
    }
    
    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
    }
    
    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => this.RaiseAndSetIfChanged(ref _isDownloadingUpdate, value);
    }    private string _updateStatus = "No updates checked";
    public string UpdateStatus
    {
        get => _updateStatus;
        set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
    }
      public string LastUpdateCheck => _settings.LastUpdateCheck == DateTime.MinValue ? 
        "Never" : _settings.LastUpdateCheck.ToString("g");
    
    // Expose the actual DateTime object for the converter
    public DateTime LastUpdateCheckDateTime => _settings.LastUpdateCheck;
        
    public string CurrentVersion => UpdateService.Instance.CurrentVersion.ToString();

    public string? ReleaseNotes => UpdateService.Instance.LatestVersion?.ReleaseNotes;
    
    public string? ReleaseDate => UpdateService.Instance.LatestVersion?.ReleaseDate;
    
    public string? LatestVersion => UpdateService.Instance.LatestVersion?.Version;

    // Update commands
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        await UpdateService.Instance.CheckForUpdates();
        
        // Raise property changed for all update-related properties
        this.RaisePropertyChanged(nameof(LastUpdateCheck));
        this.RaisePropertyChanged(nameof(LatestVersion));
        this.RaisePropertyChanged(nameof(ReleaseNotes));
        this.RaisePropertyChanged(nameof(ReleaseDate));
        this.RaisePropertyChanged(nameof(UpdateAvailable));
    }
    
    [RelayCommand]
    private async Task InstallUpdate()
    {
        IsDownloadingUpdate = true;
        await UpdateService.Instance.DownloadAndInstallUpdate();
        IsDownloadingUpdate = false;
    }
}