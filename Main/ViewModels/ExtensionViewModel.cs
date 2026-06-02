using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using SaveVaultApp.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace SaveVaultApp.ViewModels;

public partial class ExtensionViewModel : ViewModelBase
{
    private ObservableCollection<Extension> _extensions = new();
    public ObservableCollection<Extension> Extensions
    {
        get => _extensions;
        set => this.RaiseAndSetIfChanged(ref _extensions, value);
    }

    private ObservableCollection<Extension> _filteredExtensions = new();
    public ObservableCollection<Extension> FilteredExtensions
    {
        get => _filteredExtensions;
        set => this.RaiseAndSetIfChanged(ref _filteredExtensions, value);
    }

    private Extension? _selectedExtension;
    public Extension? SelectedExtension
    {
        get => _selectedExtension;
        set => this.RaiseAndSetIfChanged(ref _selectedExtension, value);
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            FilterExtensions();
        }
    }

    private ExtensionCategory _selectedCategory = ExtensionCategory.All;
    public ExtensionCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCategory, value);
            FilterExtensions();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private bool _isLoadingExternal;
    public bool IsLoadingExternal
    {
        get => _isLoadingExternal;
        set => this.RaiseAndSetIfChanged(ref _isLoadingExternal, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);    }
    
    private bool _showOnlyOfficial = false;
    public bool ShowOnlyOfficial
    {
        get => _showOnlyOfficial;
        set
        {
            this.RaiseAndSetIfChanged(ref _showOnlyOfficial, value);
            FilterExtensions();
        }
    }

    private bool _extensionsModified = false;
    public bool ExtensionsModified
    {
        get => _extensionsModified;
        set => this.RaiseAndSetIfChanged(ref _extensionsModified, value);
    }
      // New properties for tab control
    private string _currentTab = "download";
    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentTab, value);
            FilterExtensions();
            UpdateTabStyles();
        }
    }
    
    private IBrush _downloadTabBackground = new SolidColorBrush(Color.Parse("#FF9800"));
    public IBrush DownloadTabBackground
    {
        get => _downloadTabBackground;
        set => this.RaiseAndSetIfChanged(ref _downloadTabBackground, value);
    }
    
    private IBrush _downloadTabForeground = new SolidColorBrush(Colors.White);
    public IBrush DownloadTabForeground
    {
        get => _downloadTabForeground;
        set => this.RaiseAndSetIfChanged(ref _downloadTabForeground, value);
    }
    
    private IBrush _installedTabBackground = new SolidColorBrush(Colors.Transparent);
    public IBrush InstalledTabBackground
    {
        get => _installedTabBackground;
        set => this.RaiseAndSetIfChanged(ref _installedTabBackground, value);
    }
    
    private IBrush _installedTabForeground = new SolidColorBrush(Colors.White);
    public IBrush InstalledTabForeground
    {
        get => _installedTabForeground;
        set => this.RaiseAndSetIfChanged(ref _installedTabForeground, value);
    }
    
    public bool IsDownloadTab => CurrentTab == "download";
    public bool IsInstalledTab => CurrentTab == "installed";

    // Explicit order (Official listed first among real categories) rather than enum order.
    public List<ExtensionCategory> Categories { get; } = new()
    {
        ExtensionCategory.All,
        ExtensionCategory.Official,
        ExtensionCategory.Fixes,
        ExtensionCategory.Localization,
        ExtensionCategory.Theming,
        ExtensionCategory.Other
    };

    public string CategoryDisplayName =>
        CurrentTab == "download" 
            ? (SelectedCategory switch
            {
                ExtensionCategory.All => "Download Extensions",
                ExtensionCategory.Official => "Official Extensions",
                ExtensionCategory.Fixes => "Bug Fixes",
                ExtensionCategory.Localization => "Localization",
                ExtensionCategory.Theming => "Themes",
                ExtensionCategory.Other => "Other",
                _ => "Download Extensions"
            })
            : "My Extensions";public ExtensionViewModel()
    {
        // Subscribe to extension service events
        ExtensionService.Instance.ExtensionInstalled += OnExtensionInstalled;
        ExtensionService.Instance.ExtensionUninstalled += OnExtensionUninstalled;
        ExtensionService.Instance.ExtensionEnabled += OnExtensionEnabled;
        ExtensionService.Instance.ExtensionDisabled += OnExtensionDisabled;        // Initialize tab styles
        UpdateTabStyles();

        LoadExtensionsCommand.Execute(null);
    }
      [RelayCommand]
    private void SelectTab(string tabName)
    {
        CurrentTab = tabName;
        // This will trigger FilterExtensions through property changed
    }
    
    private void UpdateTabStyles()
    {
        // Set active tab styles
        if (CurrentTab == "download")
        {
            DownloadTabBackground = new SolidColorBrush(Color.Parse("#FF9800"));
            DownloadTabForeground = new SolidColorBrush(Colors.White);
            InstalledTabBackground = new SolidColorBrush(Colors.Transparent);
            InstalledTabForeground = new SolidColorBrush(Colors.White);
        }
        else
        {
            DownloadTabBackground = new SolidColorBrush(Colors.Transparent);
            DownloadTabForeground = new SolidColorBrush(Colors.White);
            InstalledTabBackground = new SolidColorBrush(Color.Parse("#FF9800"));
            InstalledTabForeground = new SolidColorBrush(Colors.White);
        }
        
        // Update property bindings
        this.RaisePropertyChanged(nameof(IsDownloadTab));
        this.RaisePropertyChanged(nameof(IsInstalledTab));
    }    [RelayCommand]
    private void LoadExtensions()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading local extensions...";

            // Phase 1: Load local extensions immediately (built-in + installed)
            var localExtensions = ExtensionService.Instance.GetLocalExtensions();
            
            // Update UI with local extensions first
            Extensions.Clear();
            foreach (var extension in localExtensions.OrderBy(e => e.Name))
            {
                Extensions.Add(extension);
            }            FilterExtensions();
            StatusMessage = $"Loaded {FilteredExtensions.Count} local extensions. Loading external extensions in background...";
            
            // Notify count changes
            this.RaisePropertyChanged(nameof(DownloadableExtensionsCount));
            this.RaisePropertyChanged(nameof(InstalledExtensionsCount));

            // Phase 1 is complete - hide loading indicator for local extensions
            IsLoading = false;            // Phase 2: Load external extensions in background
            _ = Task.Run(async () => await LoadExternalExtensions());
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load local extensions: {ex.Message}");
            StatusMessage = $"Error loading local extensions: {ex.Message}";
            IsLoading = false;
        }
    }    /// <summary>
    /// Load external extensions from server/GitHub in background
    /// </summary>
    private async Task LoadExternalExtensions()
    {
        // Set loading state for external extensions
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoadingExternal = true;
        });
        
        try
        {
            LoggingService.Instance.Info("Loading external extensions in background...");
            
            // Get remote extensions (without built-in ones to avoid duplication)
            var remoteExtensions = await ExtensionService.Instance.GetRemoteExtensionsAsync();
            var installedExtensions = ExtensionService.Instance.GetInstalledExtensions();

            // Update UI on the main thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // Create a set of existing extension IDs for efficient lookup
                    var existingIds = new HashSet<string>(Extensions.Select(e => e.Id));
                    var newExtensionsAdded = 0;

                    // Add remote extensions that aren't already shown
                    foreach (var remoteExt in remoteExtensions.Where(e => e.IsValid))
                    {
                        if (!existingIds.Contains(remoteExt.Id))
                        {
                            Extensions.Add(remoteExt);
                            newExtensionsAdded++;
                        }
                        else
                        {
                            // Update existing extension with remote data
                            var existing = Extensions.FirstOrDefault(e => e.Id == remoteExt.Id);
                            if (existing != null)
                            {
                                // Update properties that might be different in remote version
                                if (!string.IsNullOrEmpty(remoteExt.Description))
                                    existing.Description = remoteExt.Description;
                                if (!string.IsNullOrEmpty(remoteExt.Version))
                                    existing.Version = remoteExt.Version;
                                if (!string.IsNullOrEmpty(remoteExt.DownloadUrl))
                                    existing.DownloadUrl = remoteExt.DownloadUrl;
                            }
                        }
                    }

                    // Add any installed extensions that might not be in remote catalog
                    foreach (var installed in installedExtensions.Where(e => e.IsValid))
                    {
                        if (!existingIds.Contains(installed.Id))
                        {
                            Extensions.Add(installed);
                            newExtensionsAdded++;
                        }
                        else
                        {
                            // Update existing extension with installed data
                            var existing = Extensions.FirstOrDefault(e => e.Id == installed.Id);
                            if (existing != null)
                            {
                                existing.IsInstalled = installed.IsInstalled;
                                existing.IsEnabled = installed.IsEnabled;
                                existing.ScriptPath = installed.ScriptPath;
                            }
                        }
                    }

                    // Re-sort extensions by name after adding new ones
                    var sortedExtensions = Extensions.OrderBy(e => e.Name).ToList();
                    Extensions.Clear();
                    foreach (var ext in sortedExtensions)
                    {
                        Extensions.Add(ext);
                    }

                    FilterExtensions();
                    StatusMessage = $"Loaded {FilteredExtensions.Count} extensions ({newExtensionsAdded} external extensions added)";
                    
                    // Notify count changes
                    this.RaisePropertyChanged(nameof(DownloadableExtensionsCount));
                    this.RaisePropertyChanged(nameof(InstalledExtensionsCount));                }
                catch (Exception uiEx)
                {
                    LoggingService.Instance.Error($"Failed to update UI with external extensions: {uiEx.Message}");
                    StatusMessage = "Loaded local extensions. Failed to update with external extensions.";
                }
                finally
                {
                    IsLoadingExternal = false;
                }
            });
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to load external extensions: {ex.Message}");
              // Update status on UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Loaded local extensions. Failed to load external extensions: {ex.Message}";
                IsLoadingExternal = false;
            });
        }
    }
    
    [RelayCommand]
    private async Task InstallExtensionAsync(Extension extension)
    {
        try
        {
            StatusMessage = $"Installing {extension.Name}...";
            var success = await ExtensionService.Instance.InstallExtensionAsync(extension);
              if (success)
            {
                StatusMessage = $"{extension.Name} installed successfully";
                // Enable the extension by default
                ExtensionService.Instance.SetExtensionEnabled(extension, true);
                // Mark that extensions have been modified
                ExtensionsModified = true;
                
                // Notify count changes
                this.RaisePropertyChanged(nameof(DownloadableExtensionsCount));
                this.RaisePropertyChanged(nameof(InstalledExtensionsCount));
            }
            else
            {
                StatusMessage = $"Failed to install {extension.Name}";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to install extension: {ex.Message}");
            StatusMessage = $"Error installing {extension.Name}: {ex.Message}";        }
    }
    
    [RelayCommand]
    private void UninstallExtension(Extension extension)
    {
        try
        {
            StatusMessage = $"Uninstalling {extension.Name}...";
            var success = ExtensionService.Instance.UninstallExtension(extension);
              if (success)
            {
                StatusMessage = $"{extension.Name} uninstalled successfully";
                // Mark that extensions have been modified
                ExtensionsModified = true;
                
                // Notify count changes
                this.RaisePropertyChanged(nameof(DownloadableExtensionsCount));
                this.RaisePropertyChanged(nameof(InstalledExtensionsCount));
            }
            else
            {
                StatusMessage = $"Failed to uninstall {extension.Name}";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to uninstall extension: {ex.Message}");
            StatusMessage = $"Error uninstalling {extension.Name}: {ex.Message}";        }
    }
    
    [RelayCommand]
    private void ToggleExtension(Extension extension)
    {
        try
        {
            var newState = !extension.IsEnabled;
            LoggingService.Instance.Info($"ToggleExtension called for '{extension.Name}': {extension.IsEnabled} -> {newState}");
            StatusMessage = $"{(newState ? "Enabling" : "Disabling")} {extension.Name}...";
            
            var success = ExtensionService.Instance.SetExtensionEnabled(extension, newState);
              LoggingService.Instance.Info($"SetExtensionEnabled returned: {success}. Extension.IsEnabled is now: {extension.IsEnabled}");
              if (success)
            {
                // Force UI update if needed
                if (extension.IsEnabled != newState)
                {
                    LoggingService.Instance.Warning($"Extension state mismatch! Forcing update to: {newState}");
                    extension.IsEnabled = newState;
                }                
                StatusMessage = $"{extension.Name} {(extension.IsEnabled ? "enabled" : "disabled")} successfully";
                
                // Mark that extensions have been modified
                ExtensionsModified = true;
            }
            else
            {
                StatusMessage = $"Failed to {(newState ? "enable" : "disable")} {extension.Name}";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to toggle extension: {ex.Message}");
            StatusMessage = $"Error toggling {extension.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ImportExtension()
    {
        try
        {
            // This would be implemented with file picker in the view
            StatusMessage = "Select an extension file to import...";
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import extension: {ex.Message}");
            StatusMessage = $"Error importing extension: {ex.Message}";
        }
    }    [RelayCommand]
    private void RefreshExtensions()
    {
        LoadExtensionsCommand.Execute(null);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }    private void FilterExtensions()
    {
        try
        {
            // First filter out any invalid or empty extensions
            var filtered = Extensions.Where(e => e.IsValid);

            // Filter by tab
            if (CurrentTab == "installed")
            {
                // In "My Extensions" tab, only show installed extensions
                filtered = filtered.Where(e => e.IsInstalled);
            }            else
            {
                // In "Download Extensions" tab, apply additional filters
                // Filter by category
                if (SelectedCategory != ExtensionCategory.All)
                {
                    filtered = filtered.Where(e => e.Category == SelectedCategory);
                }
                else
                {
                    // When "All" is selected, show all extensions including installed ones
                    // No category filtering needed
                }
            }// Filter by search text (applied to both tabs)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                filtered = filtered.Where(e => 
                    e.Name.ToLowerInvariant().Contains(searchLower) ||
                    e.Description.ToLowerInvariant().Contains(searchLower) ||
                    e.Author.ToLowerInvariant().Contains(searchLower) ||
                    e.Tags.Any(tag => tag.ToLowerInvariant().Contains(searchLower)));
            }            // Filter by official status (applied to both tabs)
            if (ShowOnlyOfficial)
            {
                filtered = filtered.Where(e => e.IsOfficial);
            }

            FilteredExtensions.Clear();
            foreach (var extension in filtered.OrderBy(e => e.Name))
            {
                FilteredExtensions.Add(extension);
            }

            // Update category display name
            this.RaisePropertyChanged(nameof(CategoryDisplayName));
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to filter extensions: {ex.Message}");
        }
    }public async Task ImportExtensionFile(string filePath)
    {
        try
        {
            StatusMessage = $"Importing extension from {Path.GetFileName(filePath)}...";
            var success = await ExtensionService.Instance.ImportExtensionAsync(filePath);
              if (success)
            {
                StatusMessage = "Extension imported successfully";
                LoadExtensionsCommand.Execute(null); // Refresh the list
                // Mark that extensions have been modified
                ExtensionsModified = true;
            }
            else
            {
                StatusMessage = "Failed to import extension";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import extension file: {ex.Message}");
            StatusMessage = $"Error importing extension: {ex.Message}";
        }
    }

    private void OnExtensionInstalled(object? sender, Extension extension)
    {
        // Update the extension in the list
        var existing = Extensions.FirstOrDefault(e => e.Id == extension.Id);
        if (existing != null)
        {
            existing.IsInstalled = true;
        }
        FilterExtensions();
    }

    private void OnExtensionUninstalled(object? sender, Extension extension)
    {
        var existing = Extensions.FirstOrDefault(e => e.Id == extension.Id);
        if (existing != null)
        {
            existing.IsInstalled = false;
            existing.IsEnabled = false;
        }
        FilterExtensions();
    }

    private void OnExtensionEnabled(object? sender, Extension extension)
    {
        var existing = Extensions.FirstOrDefault(e => e.Id == extension.Id);
        if (existing != null)
        {
            existing.IsEnabled = true;
        }
        FilterExtensions();
    }

    private void OnExtensionDisabled(object? sender, Extension extension)
    {
        var existing = Extensions.FirstOrDefault(e => e.Id == extension.Id);
        if (existing != null)
        {
            existing.IsEnabled = false;
        }        FilterExtensions();
    }

    public void ResetModifiedFlag()
    {        ExtensionsModified = false;
    }

    public int DownloadableExtensionsCount => Extensions.Count(e => !e.IsInstalled);
    
    public int InstalledExtensionsCount => Extensions.Count(e => e.IsInstalled);
}