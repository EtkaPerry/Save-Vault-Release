using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using SaveVaultApp.Models;
using SaveVaultApp.Utilities;
using static SaveVaultApp.Utilities.SaveCarrier;

namespace SaveVaultApp.ViewModels
{
    public partial class SaveCarrierViewModel : ViewModelBase
    {
        private readonly Settings _settings;
        
        // Collection of available games with saves
        public ObservableCollection<GameSelectionItem> Games { get; } = new();
        
        // Selected compression level
        private CompressionLevel _compressionLevel = CompressionLevel.Standard;
        public CompressionLevel CompressionLevel
        {
            get => _compressionLevel;
            set => this.RaiseAndSetIfChanged(ref _compressionLevel, value);
        }
        
        // Compression level options for UI binding
        public List<string> CompressionLevels { get; } = new List<string>
        {
            "None (Fastest)",
            "Standard (Recommended)",
            "Maximum (Smallest Size)"
        };
        
        // Selected compression level index for UI binding
        private int _selectedCompressionIndex = 1; // Default to Standard
        public int SelectedCompressionIndex
        {
            get => _selectedCompressionIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCompressionIndex, value);
                CompressionLevel = (CompressionLevel)value;
            }
        }
        
        // Status message
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }
          // Processing indicator
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }
        
        // Show only known games flag
        private bool _showOnlyKnownGames = true; // Default to true
        public bool ShowOnlyKnownGames
        {
            get => _showOnlyKnownGames;
            set
            {
                this.RaiseAndSetIfChanged(ref _showOnlyKnownGames, value);
                // Refresh the game list when this property changes
                RefreshGameList();
            }
        }
        
        // Full list of applications (stored for filtering)
        private List<ApplicationInfo> _allApplications = new();
        
        // Constructor
        public SaveCarrierViewModel(Settings settings, List<ApplicationInfo> applications)
        {
            _settings = settings;
            _allApplications = applications;
            
            // Initialize game list
            PopulateGames(applications);
        }
        
        // Refresh the game list based on current filter settings
        private void RefreshGameList()
        {
            // If showing only known games, filter the applications list
            var filteredApps = _showOnlyKnownGames
                ? _allApplications.Where(app => !string.IsNullOrEmpty(app.KnownGameId)).ToList()
                : _allApplications;
                
            PopulateGames(filteredApps);
            
            // Update status message
            StatusMessage = _showOnlyKnownGames
                ? "Showing only known games from database"
                : "Showing all games with save locations";
        }
          // Populate the game list from installed applications
        private void PopulateGames(List<ApplicationInfo> applications)
        {
            Games.Clear();
            
            foreach (var app in applications)
            {
                // Only include games with save locations:
                // - Found by algorithm
                // - From KnownGames.cs
                // - User-added (in app.SavePath)
                if (!string.IsNullOrEmpty(app.SavePath) && app.SavePath != "Unknown")
                {
                    var gameItem = new GameSelectionItem
                    {
                        ApplicationInfo = app,
                        // By default, only select known games
                        IsSelected = !string.IsNullOrEmpty(app.KnownGameId)
                    };
                    
                    Games.Add(gameItem);
                    
                    // Calculate folder size asynchronously
                    Task.Run(() =>
                    {
                        try
                        {
                            if (Directory.Exists(gameItem.SavePath))
                            {
                                long size = CalculateFolderSize(gameItem.SavePath);
                                gameItem.SaveSize = GetFileSizeString(size);
                            }
                            else
                            {
                                gameItem.SaveSize = "Path not found";
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error calculating folder size: {ex.Message}");
                            gameItem.SaveSize = "Error";
                        }
                    });
                }
            }
        }
        
        // Calculate the size of a folder recursively
        private long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            
            // Add size of all files
            foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(file);
                    size += fileInfo.Length;
                }
                catch
                {
                    // Skip files that can't be accessed
                }
            }
            
            return size;
        }
        
        // Command to select all games
        [RelayCommand]
        private void SelectAll()
        {
            foreach (var game in Games)
            {
                game.IsSelected = true;
            }
            StatusMessage = "Selected all games";
        }
        
        // Command to deselect all games
        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var game in Games)
            {
                game.IsSelected = false;
            }
            StatusMessage = "Deselected all games";
        }
        
        // Command to invert the current selection
        [RelayCommand]
        private void InvertSelection()
        {
            foreach (var game in Games)
            {
                game.IsSelected = !game.IsSelected;
            }
            StatusMessage = "Inverted game selection";
        }
          // Command to select only known games
        [RelayCommand]
        private void SelectOnlyKnownGames()
        {
            foreach (var game in Games)
            {
                game.IsSelected = game.IsKnownGame;
            }
            
            // We're selecting known games but not necessarily filtering the list
            // This allows users to still see all games but only have known ones selected
            StatusMessage = "Selected only games from KnownGames database";
        }
        
        // Command to open the save folder - removed in favor of directly implementing in GameSelectionItem
        
        // Command to pack selected saves
        [RelayCommand]
        public async Task ExportSaves()
        {
            try
            {
                IsProcessing = true;
                StatusMessage = "Preparing to export saves...";
                
                // Get selected games
                var selectedGames = Games
                    .Where(g => g.IsSelected)
                    .Select(g => g.ApplicationInfo)
                    .ToList();
                
                if (!selectedGames.Any())
                {
                    StatusMessage = "No games selected for export";
                    IsProcessing = false;
                    return;
                }
                
                // Check if any selected game has no valid save path
                var invalidSavePaths = selectedGames
                    .Where(g => string.IsNullOrEmpty(g.SavePath) || g.SavePath == "Unknown")
                    .ToList();
                
                if (invalidSavePaths.Any())
                {
                    string invalidGames = string.Join(", ", invalidSavePaths.Select(g => g.Name));
                    StatusMessage = $"Cannot export games without save locations: {invalidGames}";
                    IsProcessing = false;
                    return;
                }
                
                // Get top-level window for dialog
                var topLevel = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = topLevel?.MainWindow;
                
                if (mainWindow == null)
                {
                    StatusMessage = "Error: Cannot access application window";
                    IsProcessing = false;
                    return;
                }
                
                // Use modern StorageProvider API
                var storageProvider = mainWindow.StorageProvider;
                var fileOptions = new FilePickerSaveOptions
                {
                    Title = "Export Game Saves",
                    SuggestedFileName = "SaveVault_Export.svp",
                    DefaultExtension = ".svp"
                };
                
                var result = await storageProvider.SaveFilePickerAsync(fileOptions);
                
                if (result == null)
                {
                    StatusMessage = "Export cancelled";
                    IsProcessing = false;
                    return;
                }
                
                var outputPath = result.TryGetLocalPath();
                if (string.IsNullOrEmpty(outputPath))
                {
                    StatusMessage = "Error: Could not get local file path";
                    IsProcessing = false;
                    return;
                }

                StatusMessage = $"Exporting {selectedGames.Count} games with {CompressionLevel} compression...";
                
                // Pack the saves
                bool success = await SaveCarrier.PackSaves(selectedGames, outputPath, CompressionLevel);
                
                if (success)
                {
                    // Get file size for reporting
                    var fileInfo = new FileInfo(outputPath);
                    string sizeDisplay = GetFileSizeString(fileInfo.Length);
                    
                    StatusMessage = $"Export completed successfully! File size: {sizeDisplay}";
                }
                else
                {
                    StatusMessage = "Error exporting saves";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Debug.WriteLine($"Error in ExportSaves: {ex}");
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        // Command to unpack saves
        [RelayCommand]
        public async Task ImportSaves()
        {
            try
            {
                IsProcessing = true;
                StatusMessage = "Preparing to import saves...";
                
                // Get top-level window for dialog
                var topLevel = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = topLevel?.MainWindow;
                
                if (mainWindow == null)
                {
                    StatusMessage = "Error: Cannot access application window";
                    IsProcessing = false;
                    return;
                }
                
                // Use modern StorageProvider API
                var storageProvider = mainWindow.StorageProvider;
                var fileOptions = new FilePickerOpenOptions
                {
                    Title = "Import Game Saves",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("SaveVault Packages") { Patterns = new[] { "*.svp" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                };
                
                var files = await storageProvider.OpenFilePickerAsync(fileOptions);
                
                if (files == null || files.Count == 0)
                {
                    StatusMessage = "Import cancelled";
                    IsProcessing = false;
                    return;
                }
                
                string filePath = files[0].TryGetLocalPath() ?? string.Empty;
                
                if (string.IsNullOrEmpty(filePath))
                {
                    StatusMessage = "Error: Could not get local file path";
                    IsProcessing = false;
                    return;
                }

                StatusMessage = "Importing saves...";
                
                // Unpack the saves
                int restoredCount = await SaveCarrier.UnpackSaves(filePath, _settings);
                
                if (restoredCount > 0)
                {
                    StatusMessage = $"Import completed successfully! {restoredCount} games restored.";
                }
                else
                {
                    StatusMessage = "No games were imported";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Debug.WriteLine($"Error in ImportSaves: {ex}");
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        // Helper method to format file size
        private string GetFileSizeString(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;
            
            while (number > 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:F2} {suffixes[counter]}";
        }
    }
    
    // Class for game selection items with checkbox binding support
    public class GameSelectionItem : ReactiveObject
    {
        public ApplicationInfo ApplicationInfo { get; set; } = null!;
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
        
        // Display name - just reference the underlying app's name
        public string Name => ApplicationInfo?.Name ?? "Unknown";
        
        // Display save path - just reference the underlying app's save path
        public string SavePath => ApplicationInfo?.SavePath ?? "Unknown";
        
        // Save folder size
        private string _saveSize = "Calculating...";
        public string SaveSize
        {
            get => _saveSize;
            set => this.RaiseAndSetIfChanged(ref _saveSize, value);
        }
        
        // Is this a known game from KnownGames.cs?
        public bool IsKnownGame => !string.IsNullOrEmpty(ApplicationInfo?.KnownGameId);

        // Open folder command 
        public ICommand OpenFolderCommand => _openFolderCommand ??= new RelayCommand(OpenFolder);
        private ICommand? _openFolderCommand;

        public void OpenFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(SavePath) && Directory.Exists(SavePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SavePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening folder: {ex.Message}");
            }
        }
    }
}