using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using SaveVaultApp.Utilities;
using SaveVaultApp.ViewModels;

namespace SaveVaultApp.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class KnownGamesScanner
    {
        /// <summary>
        /// Checks for presence of known games from KnownGames.json by verifying their installation directories
        /// </summary>
        public static void CheckKnownGamesFromConfig(
            ObservableCollection<ApplicationInfo> installedApps, 
            Settings settings, 
            HashSet<string> processedExecutables)
        {
            LoggingService.Instance.Info("Searching for known games from configuration...");
            
            // Get all available drive letters
            var availableDrives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.Network && d.DriveType != DriveType.NoRootDirectory)
                .Select(d => d.Name)
                .ToList();
            
            // Get the known games from SaveLocationDetector
            var knownGames = SaveLocationDetector.GetKnownGames();
            int foundKnownGames = 0;
            
            if (knownGames.Count == 0)
            {
                LoggingService.Instance.Info("No known games found in configuration.");
                return;
            }
            
            LoggingService.Instance.Info($"Checking for {knownGames.Count} known games from configuration...");
            
            foreach (var game in knownGames)
            {
                if (string.IsNullOrEmpty(game.GameLocation) || string.IsNullOrEmpty(game.Executable))
                    continue;
                
                // Check each drive
                foreach (var drive in availableDrives)
                {
                    // Construct the potential installation path
                    string possibleGamePath = Path.Combine(drive, game.GameLocation.TrimStart('\\'));
                    string possibleExePath = Path.Combine(possibleGamePath, game.Executable);
                    
                    // Check if the executable exists
                    if (File.Exists(possibleExePath))
                    {
                        // Add to the processed executables to avoid re-detection in the general scan
                        processedExecutables.Add(possibleExePath);
                        
                        // Create the ApplicationInfo object with proper game information
                        var app = new ApplicationInfo(settings)
                        {
                            Name = game.Name,
                            Path = possibleGamePath,
                            ExecutablePath = possibleExePath
                        };
                        
                        // Set the save path directly from the known game configuration
                        if (game.SavePath != null && !string.IsNullOrEmpty(game.SavePath.Path))
                        {
                            string savePath = SaveLocationDetector.ExpandEnvironmentVariables(game.SavePath.Path);
                            app.SavePath = Directory.Exists(savePath) ? savePath : "Unknown";
                            
                            Debug.WriteLine($"Setting save path for {game.Name}: {savePath} (exists: {Directory.Exists(savePath)})");
                        }
                        
                        // Add to the collection if not already present
                        if (!installedApps.Any(a => string.Equals(a.ExecutablePath, possibleExePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            installedApps.Add(app);
                            foundKnownGames++;
                            
                            Debug.WriteLine($"Found known game: {game.Name} at {possibleExePath}");
                            
                            // Skip checking other drives once found
                            break;
                        }
                    }
                }
            }
            
            LoggingService.Instance.Info($"Found {foundKnownGames} known games from configuration.");
        }
    }
}
