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
using System.Text;

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
    private readonly string _initialLanguage; // Track initial language for restart prompt

    // Language change tracking
    private bool _languageChanged = false;
    public bool LanguageChanged 
    { 
        get => _languageChanged; 
        private set => this.RaiseAndSetIfChanged(ref _languageChanged, value);
    }

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
    private ObservableCollection<string> _availableThemes = new();
    public ObservableCollection<string> AvailableThemes
    {
        get => _availableThemes;
        set => this.RaiseAndSetIfChanged(ref _availableThemes, value);
    }

    // Maps a theme display-name shown in the dropdown to the theme extension that provides it
    private readonly Dictionary<string, Extension> _themeExtensions = new();    // Language properties
    private string _selectedLanguage = string.Empty;
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
                
                // Check if language actually changed from initial value
                if (value != _initialLanguage)
                {
                    LanguageChanged = true;
                }
                
                // Get language code from display name
                var languageCode = LanguageManager.Instance.GetLanguageCodeByDisplayName(value);
                if (!string.IsNullOrEmpty(languageCode))
                {
                    LanguageManager.Instance.SetCurrentLanguage(languageCode);
                    _settings.Language = languageCode;
                    SaveChanges();
                }
            }
        }
    }

    // List of available languages for the ComboBox
    private ObservableCollection<string> _availableLanguages = new();
    public ObservableCollection<string> AvailableLanguages 
    { 
        get => _availableLanguages; 
        set => this.RaiseAndSetIfChanged(ref _availableLanguages, value);
    }

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
        
        // Store initial language to track changes
        _initialLanguage = LanguageManager.Instance.GetCurrentLanguageDisplayName();

        // Load current settings from the instance
        _autoSaveInterval = _settings.AutoSaveInterval;
        _globalAutoSaveEnabled = _settings.GlobalAutoSaveEnabled;
        _startSaveEnabled = _settings.StartSaveEnabled;
        _changeDetectionEnabled = _settings.ChangeDetectionEnabled;
        _maxAutoSaves = _settings.MaxAutoSaves;        _maxStartSaves = _settings.MaxStartSaves;
        _selectedTheme = _settings.Theme ?? "System";
        _backupStorageLocation = _settings.BackupStorageLocation;        // Load update settings
        _autoCheckUpdates = _settings.AutoCheckUpdates;
        _updateCheckInterval = _settings.UpdateCheckInterval;

        // Load available themes
        LoadAvailableThemes();
          // Load available languages from LanguageManager
        LoadAvailableLanguages();
        
        // Make sure we have the correct current language selection
        var actualCurrentLanguage = LanguageManager.Instance.GetCurrentLanguageDisplayName();
        if (_selectedLanguage != actualCurrentLanguage)
        {
            _selectedLanguage = actualCurrentLanguage;
            this.RaisePropertyChanged(nameof(SelectedLanguage));
        }

        Debug.WriteLine($"OptionsViewModel initialized with settings. AutoSaveInterval={_autoSaveInterval}, GlobalAutoSaveEnabled={_globalAutoSaveEnabled}");        // Set up update service events
        var updateService = UpdateService.Instance;
        updateService.UpdateStatusChanged += (s, status) => UpdateStatus = status;
        updateService.UpdateAvailabilityChanged += (s, available) => UpdateAvailable = available;
        
        // Subscribe to language registration events
        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
        LanguageManager.Instance.LanguageRegistered += (s, languageInfo) => RefreshAvailableLanguages();
        LanguageManager.Instance.LanguageUnregistered += (s, languageInfo) => RefreshAvailableLanguages();        // Subscribe to extension events to refresh languages when extensions are enabled/disabled
        ExtensionService.Instance.ExtensionEnabled += (s, extension) => {
            RefreshAvailableLanguages();
            RefreshAvailableThemes();
        };
        ExtensionService.Instance.ExtensionDisabled += (s, extension) => {
            RefreshAvailableLanguages();
            RefreshAvailableThemes();
        };
        ExtensionService.Instance.ExtensionUninstalled += (s, extension) => {
            RefreshAvailableLanguages();
            RefreshAvailableThemes();
        };
        
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
    }    private void ApplyTheme(string themeName)
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;

        var settings = Settings.Instance;

        // Built-in themes take precedence over any extension that might share their name
        bool isBuiltIn = themeName is "System" or "Light" or "Dark";

        // Always revert any previous extension theme overrides first, so the built-in
        // ThemeDictionary (or the next theme) starts from a clean slate.
        LuaEngine.Instance.ClearThemeOverrides();

        if (!isBuiltIn && _themeExtensions.TryGetValue(themeName, out var themeExtension))
        {
            // A theme provided by an extension. Custom themes sit on the Dark base variant
            // so that built-in (Fluent) controls match their typically-dark backgrounds.
            app.RequestedThemeVariant = ThemeVariant.Dark;
            LuaEngine.Instance.ApplyThemeExtension(themeExtension);
            settings.SetExtensionSetting("app.theme.selected", themeExtension.Id);
        }
        else
        {
            app.RequestedThemeVariant = themeName switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default // System
            };

            // No extension theme active anymore
            settings.SetExtensionSetting("app.theme.selected", "");
        }
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
      // Legal document properties
    private string _legalDocumentContent = string.Empty;
    public string LegalDocumentContent
    {
        get => _legalDocumentContent;
        set => this.RaiseAndSetIfChanged(ref _legalDocumentContent, value);    }
    
    // Property for legal acceptance date display
    public string LegalAcceptanceDate => _settings.LegalAcceptanceDate.ToString("dd.MM.yyyy");
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
      // Legal document loading method
    public void LoadLegalDocument(string documentType)
    {
        try
        {
            string fileName = documentType switch
            {
                "TermsOfService" => "TermsOfService.txt",
                "SecurityPolicy" => "SecurityPolicy.txt", 
                "PrivacyPolicy" => "PrivacyPolicy.txt",
                _ => "TermsOfService.txt"
            };
            
            // Get the path to the Assets folder
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
            
            if (File.Exists(assetsPath))
            {                var content = File.ReadAllText(assetsPath);
                
                // Apply enhanced formatting for legal documents
                StringBuilder formattedContent = new StringBuilder();
                
                // Process the content line by line for better control
                var lines = content.Split('\n');
                bool isFirstH1 = true;
                
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // Handle main title (H1)
                    if (trimmedLine.StartsWith("# "))
                    {
                        string title = trimmedLine.Substring(2);
                        // Only add extra spacing after the first H1 (title)
                        if (isFirstH1)
                        {
                            formattedContent.AppendLine(title);
                            isFirstH1 = false;
                        }
                        else
                        {
                            formattedContent.AppendLine($"\n\n{title}");
                        }
                    }
                    // Handle section headers (H2)
                    else if (trimmedLine.StartsWith("## "))
                    {
                        string sectionTitle = trimmedLine.Substring(3);
                        formattedContent.AppendLine($"\n\n{sectionTitle.ToUpper()}");
                        formattedContent.AppendLine($"{new string('━', 40)}");
                    }
                    // Handle subsection headers (H3)
                    else if (trimmedLine.StartsWith("### "))
                    {
                        string subSectionTitle = trimmedLine.Substring(4);
                        formattedContent.AppendLine($"\n{subSectionTitle}");
                        formattedContent.AppendLine($"{new string('─', 25)}");
                    }
                    // Handle list items
                    else if (trimmedLine.StartsWith("- "))
                    {
                        string listItem = trimmedLine.Substring(2);
                        formattedContent.AppendLine($"• {listItem}");
                    }
                    // Process other content
                    else if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        // Clean up markdown formatting
                        string processedLine = trimmedLine
                            .Replace("_Last updated:", "Last updated:")
                            .Replace("_", "")
                            .Replace("**", "")
                            .Replace("*", "");
                        
                        formattedContent.AppendLine(processedLine);
                    }
                    else
                    {
                        // Preserve empty lines for spacing
                        formattedContent.AppendLine();
                    }
                }
                
                // Clean up multiple consecutive newlines
                string result = System.Text.RegularExpressions.Regex.Replace(
                    formattedContent.ToString(), 
                    @"\n{3,}", 
                    "\n\n"
                );
                
                LegalDocumentContent = result.Trim();
            }
            else
            {
                LegalDocumentContent = $"Could not load {documentType}.\n\nFile not found at: {assetsPath}\n\nPlease ensure the application assets are properly installed.";
            }
        }
        catch (Exception ex)
        {            LegalDocumentContent = $"Error loading {documentType}:\n\n{ex.Message}\n\nPlease contact support if this issue persists.";
        }
    }
    
    // Method to update the legal acceptance date to the current date
    public void UpdateLegalAcceptanceDate()
    {
        _settings.LegalAcceptanceDate = DateTime.Now;
        SaveChanges();
        this.RaisePropertyChanged(nameof(LegalAcceptanceDate));
    }    private void LoadAvailableThemes()
    {
        try
        {
            // Clear existing themes
            AvailableThemes.Clear();
            _themeExtensions.Clear();

            // Built-in themes
            AvailableThemes.Add("System");
            AvailableThemes.Add("Light");
            AvailableThemes.Add("Dark");

            // Themes provided by enabled theming extensions
            foreach (var ext in ExtensionService.Instance.GetInstalledExtensions())
            {
                if (ext.Category != ExtensionCategory.Theming || !ext.IsEnabled)
                    continue;

                var displayName = ext.Name;
                if (string.IsNullOrWhiteSpace(displayName) || AvailableThemes.Contains(displayName))
                    continue; // avoid clashing with the built-in entries

                AvailableThemes.Add(displayName);
                _themeExtensions[displayName] = ext;
            }

            Debug.WriteLine($"Loaded {AvailableThemes.Count} themes ({_themeExtensions.Count} from extensions)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load available themes: {ex.Message}");
        }
    }    public void RefreshAvailableThemes()
    {
        LoadAvailableThemes();
        
        // Ensure selected theme is valid (only built-in themes)
        if (!AvailableThemes.Contains(SelectedTheme))
        {
            SelectedTheme = "System";
            Debug.WriteLine("Reverted to System theme as current selection is no longer valid");
        }
    }

    private void LoadAvailableLanguages()
    {
        try
        {
            AvailableLanguages.Clear();
            
            // Get languages from LanguageManager
            var availableLanguages = LanguageManager.Instance.GetLanguageDisplayNames();
            foreach (var language in availableLanguages)
            {
                AvailableLanguages.Add(language);
                Debug.WriteLine($"Added language to options: {language}");
            }
            
            // Set current selection
            _selectedLanguage = LanguageManager.Instance.GetCurrentLanguageDisplayName();
            this.RaisePropertyChanged(nameof(SelectedLanguage));
            
            Debug.WriteLine($"Loaded {AvailableLanguages.Count} languages in options. Current: {_selectedLanguage}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load available languages: {ex.Message}");
        }
    }    public void RefreshAvailableLanguages()
    {
        var currentSelection = SelectedLanguage;
        LoadAvailableLanguages();
        
        // Check if we need to update the selection to match the actual current language
        var actualCurrentLanguage = LanguageManager.Instance.GetCurrentLanguageDisplayName();
        if (SelectedLanguage != actualCurrentLanguage)
        {
            Debug.WriteLine($"Updating language selection from '{SelectedLanguage}' to '{actualCurrentLanguage}' to match actual current language");
            _selectedLanguage = actualCurrentLanguage;
            this.RaisePropertyChanged(nameof(SelectedLanguage));
        }
        
        // Check if the previously selected language is no longer available
        if (!string.IsNullOrEmpty(currentSelection) && !AvailableLanguages.Contains(currentSelection))
        {
            // Current language is no longer available (extension disabled), revert to English
            Debug.WriteLine($"Current language '{currentSelection}' is no longer available, reverting to English");
            SelectedLanguage = "English";
        }
    }

    private void OnLanguageChanged(object? sender, string languageCode)
    {
        // Update the selected language in the UI when language is changed programmatically
        try
        {
            var displayName = LanguageManager.Instance.GetCurrentLanguageDisplayName();
            
            if (SelectedLanguage != displayName)
            {
                _selectedLanguage = displayName;
                this.RaisePropertyChanged(nameof(SelectedLanguage));
                Debug.WriteLine($"UI updated to show language: {displayName} ({languageCode})");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating language in UI: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset the language changed flag (used when user chooses not to restart)
    /// </summary>
    public void ResetLanguageChangeFlag()
    {
        LanguageChanged = false;
    }
}