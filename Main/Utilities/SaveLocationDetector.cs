using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SaveVaultApp.Models;
using SaveVaultApp.ViewModels;

namespace SaveVaultApp.Utilities
{    public class SaveLocationDetector
    {
        // Use the KnownGames class to get the list of known games
        private static readonly List<KnownGameInfo> KnownGames = Utilities.KnownGames.GamesList;

        public static DetectionResult DetectSavePath(ApplicationInfo app)
        {
            Debug.WriteLine($"DetectSavePath called for: {app.Name}, Executable: {Path.GetFileName(app.ExecutablePath)}");
            
            // First try to identify based on known games
            var knownGameResult = DetectKnownGame(app);
            if (knownGameResult != null && !string.IsNullOrEmpty(knownGameResult.SavePath) && knownGameResult.SavePath != "Unknown")
            {
                Debug.WriteLine($"Detected known game: {knownGameResult.GameName}, Save path: {knownGameResult.SavePath}");
                return knownGameResult;
            }
            
            // Then try Steam-specific detection
            string steamPath = DetectSteamSavePath(app);
            if (!string.IsNullOrEmpty(steamPath) && steamPath != "Unknown")
            {
                // For Steam games, use the app's name as the identifier
                return new DetectionResult(steamPath, app.Name, Path.GetFileNameWithoutExtension(app.ExecutablePath));
            }
            
            // Fall back to general detection if specific detections fail
            string generalPath = DetectSavePathGeneral(app);
            return new DetectionResult(generalPath, "", "");
        }

        /// <summary>
        /// Checks if the application matches any known game profiles
        /// </summary>
        public static DetectionResult? DetectKnownGame(ApplicationInfo app)
        {
            try
            {
                string executableName = Path.GetFileName(app.ExecutablePath).ToLowerInvariant();
                string folderName = Path.GetFileName(Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty);
                
                // Look for games by executable name or folder name
                var matchedGame = KnownGames.FirstOrDefault(game => 
                    (executableName.Equals(game.Executable.ToLowerInvariant()) && 
                     (string.IsNullOrEmpty(game.GameFolder) || 
                      folderName.Contains(game.GameFolder, StringComparison.OrdinalIgnoreCase))) || 
                    (!string.IsNullOrEmpty(folderName) && 
                     !string.IsNullOrEmpty(game.GameFolder) && 
                     folderName.Equals(game.GameFolder, StringComparison.OrdinalIgnoreCase)));

                if (matchedGame != null)
                {
                    // Expand the save path with environment variables
                    string expandedSavePath = ExpandEnvironmentVariables(matchedGame.SavePath);
                    
                    // Verify the save path exists, if not, return Unknown
                    if (!string.IsNullOrEmpty(expandedSavePath) && !Directory.Exists(expandedSavePath))
                    {
                        Debug.WriteLine($"Save path for {matchedGame.Name} doesn't exist: {expandedSavePath}");
                        expandedSavePath = "Unknown";
                    }
                    
                    return new DetectionResult(expandedSavePath, matchedGame.Name, matchedGame.Name);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error detecting known game: {ex.Message}");
            }
            
            return null;
        }

        // Public method to search for known games in common installation directories
        public static List<ApplicationInfo> ScanForKnownGames(Settings settings, HashSet<string> processedExecutables)
        {
            var foundGames = new List<ApplicationInfo>();
            
            try
            {
                // Common game installation directories to scan
                var pathsToScan = GetCommonGamePaths();
                
                foreach (var basePath in pathsToScan)
                {
                    if (!Directory.Exists(basePath))
                        continue;
                        
                    // First scan immediate children directories for game folders
                    foreach (var gameFolder in Directory.GetDirectories(basePath))
                    {
                        string folderName = Path.GetFileName(gameFolder);
                        
                        // Check if this folder matches any known game folder names
                        foreach (var knownGame in KnownGames)
                        {
                            if (folderName.Equals(knownGame.GameFolder, StringComparison.OrdinalIgnoreCase) || 
                                folderName.Contains(knownGame.GameFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                // Find the executable in this folder
                                string executablePath = string.Empty;
                                
                                try
                                {
                                    // First check direct in the folder
                                    string directExePath = Path.Combine(gameFolder, knownGame.Executable);
                                    if (File.Exists(directExePath))
                                    {
                                        executablePath = directExePath;
                                    }
                                    else
                                    {
                                        // Search recursively up to 3 levels deep
                                        var exeFiles = Directory.GetFiles(gameFolder, knownGame.Executable, SearchOption.AllDirectories);
                                        if (exeFiles.Length > 0)
                                        {
                                            executablePath = exeFiles[0]; // Take the first match
                                        }
                                        else
                                        {
                                            // If the specific exe wasn't found, look for any exe that might match
                                            exeFiles = Directory.GetFiles(gameFolder, "*.exe", SearchOption.AllDirectories);
                                            var gameExe = exeFiles.FirstOrDefault(exe => 
                                                !IsSystemOrUtilityExecutable(exe) && 
                                                IsLikelyMainExecutable(exe, knownGame.Name));
                                                
                                            if (gameExe != null)
                                            {
                                                executablePath = gameExe;
                                            }
                                        }
                                    }
                                    
                                    // If we found an executable and haven't processed it yet
                                    if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath) && 
                                        !processedExecutables.Contains(executablePath))
                                    {
                                        processedExecutables.Add(executablePath);
                                        
                                        // Create application info object
                                        var gameInfo = new ApplicationInfo(settings)
                                        {
                                            Name = knownGame.Name,
                                            Path = gameFolder,
                                            ExecutablePath = executablePath,
                                            SavePath = ExpandEnvironmentVariables(knownGame.SavePath)
                                        };
                                          foundGames.Add(gameInfo);
                                        // Debug log for known games only - this won't clutter the main logs
                                        Debug.WriteLine($"Found known game: {knownGame.Name} at {executablePath}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error processing game folder {gameFolder}: {ex.Message}");
                                }
                                
                                // Once we've found a match for this folder, stop checking other known games
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning for known games: {ex.Message}");
            }
            
            return foundGames;
        }
        
        // Helper method to check if a file is a system or utility executable (not a game)
        private static bool IsSystemOrUtilityExecutable(string exePath)
        {
            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            
            // Skip common system or utility executables
            return fileName.Contains("unins") ||
                   fileName.Contains("installer") ||
                   fileName.Contains("setup") ||
                   fileName.Contains("update") ||
                   fileName.Contains("patch") ||
                   (fileName.Contains("launcher") && new FileInfo(exePath).Length < 1024 * 1024) ||
                   fileName.Contains("helper") ||
                   fileName.Contains("redist") ||
                   fileName.Contains("vcredist") ||
                   fileName.Contains("dotnet") ||
                   fileName.Contains("uninstall") ||
                   fileName.Contains("repair") ||
                   fileName.EndsWith("utility.exe") ||
                   fileName.EndsWith("helper.exe");
        }
        
        // Helper method to identify if an executable is likely the main game executable
        private static bool IsLikelyMainExecutable(string exePath, string gameName)
        {
            string fileName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            string gameNameLower = gameName.ToLowerInvariant();
            
            // If the exe name contains the game name (or simplified version), it's likely the main exe
            bool containsGameName = fileName.Contains(gameNameLower) || 
                                   gameNameLower.Contains(fileName);
                                   
            // Common patterns for main game executables
            bool commonPattern = fileName == "game" || 
                                fileName == "main" || 
                                fileName.Contains("start") || 
                                fileName.Contains("play") ||
                                fileName.EndsWith("game");
                                
            return containsGameName || commonPattern;
        }
        
        // Get common paths where games might be installed
        private static List<string> GetCommonGamePaths()
        {
            var paths = new List<string>();
            
            // Add standard installation paths
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)));
            
            // Common game launcher paths
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "Games"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin Games"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games"));
            
            // Check all drive letters for common game folders
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    paths.Add(Path.Combine(drive.Name, "Games"));
                    paths.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common"));
                    paths.Add(Path.Combine(drive.Name, "Epic Games"));
                    paths.Add(Path.Combine(drive.Name, "GOG Games"));
                }
            }
            
            return paths.Where(Directory.Exists).ToList();
        }

        // Existing methods remain unchanged
        private static string DetectSteamSavePath(ApplicationInfo app)
        {
            try
            {
                string steamUserDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Steam",
                    "userdata"
                );

                if (!Directory.Exists(steamUserDataPath))
                {
                    return "Unknown";
                }

                // Get the game's folder name from its exe path
                string exeDir = Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty;
                if (exeDir.Contains("steamapps\\common\\"))
                {
                    string gameFolderName = exeDir.Split(new[] { "steamapps\\common\\" }, StringSplitOptions.None).Last();
                    if (!string.IsNullOrEmpty(gameFolderName))
                    {
                        // Steam uses numeric app IDs for save folders, so we need to check each user's folder
                        foreach (string userFolder in Directory.GetDirectories(steamUserDataPath))
                        {
                            foreach (string appFolder in Directory.GetDirectories(userFolder))
                            {
                                string remoteFolder = Path.Combine(appFolder, "remote");
                                if (Directory.Exists(remoteFolder) && Directory.EnumerateFileSystemEntries(remoteFolder).Any())
                                {
                                    return remoteFolder;
                                }
                            }
                        }
                    }
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error detecting Steam save path: {ex.Message}");
                return "Unknown";
            }
        }        public static string ExpandEnvironmentVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            try 
            {
                // First, normalize slashes - Windows environment paths use backslashes
                string normalizedPath = path.Replace("/", "\\");
                
                // Check if this is a Steam userdata path with wildcards
                if (normalizedPath.Contains("userdata") && normalizedPath.Contains("*"))
                {
                    return ExpandSteamUserDataPath(normalizedPath);
                }
                
                // Explicitly handle common environment variables
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string expandedPath = normalizedPath.Replace("%USERPROFILE%", userProfile);
                
                // Then use the system method to expand any other variables
                expandedPath = Environment.ExpandEnvironmentVariables(expandedPath);
                
                Debug.WriteLine($"Expanded path: '{path}' → '{expandedPath}'");
                
                // Double-check that USERPROFILE was correctly replaced
                if (expandedPath.Contains("%USERPROFILE%"))
                {
                    // If still contains the variable, replace it again
                    expandedPath = expandedPath.Replace("%USERPROFILE%", userProfile);
                    Debug.WriteLine($"Re-expanded USERPROFILE: '{expandedPath}'");
                }
                
                return expandedPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error expanding environment variables in path '{path}': {ex.Message}");
                return path;
            }
        }

        private static string DetectSavePathGeneral(ApplicationInfo app)
        {
            // Extract useful information for path detection
            string appName = app.Name;
            string appNameLower = appName.ToLowerInvariant();
            string execDir = Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty;
            
            // Generic approach for all games - no special cases
            // Try without spaces or special characters
            string simplifiedName = new string(appName.Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c)).ToArray());
            
            // Common save path locations - check with various name formats
            var possiblePaths = new List<string>
            {
                // Common game launcher paths for non-Steam games
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EpicGamesLauncher", "Saved", "savegames", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Electronic Arts", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ubisoft", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", appName),
                
                // Standard locations with exact app name
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "My Games", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", appName),
                
                // With simplified name (no spaces or punctuation)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), simplifiedName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), simplifiedName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), simplifiedName),
                
                // Check for save folders in installation directory
                Path.Combine(execDir, "Saves"),
                Path.Combine(execDir, "SavedGames"),
                Path.Combine(execDir, "SaveGames"),
                Path.Combine(execDir, "save"),
                Path.Combine(execDir, "saves"),
                Path.Combine(execDir, "savegame"),
                Path.Combine(execDir, "savegames"),
                Path.Combine(execDir, "data", "saves"),
                
                // Additional common patterns
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveGames", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", appName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", simplifiedName),
                
                // Try parent directory of the executable
                Path.Combine(Directory.GetParent(execDir)?.FullName ?? execDir, "Saves"),
                Path.Combine(Directory.GetParent(execDir)?.FullName ?? execDir, "SavedGames")
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    Debug.WriteLine($"Found save path for {appName}: {path}");
                    return path;
                }
            }
            
            // Look for folders with "save" in the name in the installation directory
            try
            {
                var potentialSaveDirs = Directory.GetDirectories(execDir)
                    .Where(dir => Path.GetFileName(dir).ToLowerInvariant().Contains("save"))
                    .ToList();
                    
                if (potentialSaveDirs.Count > 0)
                {
                    return potentialSaveDirs.First();
                }
                
                // Also check the parent directory
                string parentDir = Directory.GetParent(execDir)?.FullName ?? string.Empty;
                if (Directory.Exists(parentDir))
                {
                    potentialSaveDirs = Directory.GetDirectories(parentDir)
                        .Where(dir => Path.GetFileName(dir).ToLowerInvariant().Contains("save"))
                        .ToList();
                        
                    if (potentialSaveDirs.Count > 0)
                    {
                        return potentialSaveDirs.First();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching for save directories: {ex.Message}");
            }

            return "Unknown"; // Default if no save path is found
        }
        
        // Helper method to expand Steam userdata paths with wildcards
        public static string ExpandSteamUserDataPath(string pathWithWildcards)
        {
            try
            {
                // Check if this is a Steam userdata path with wildcards
                if (pathWithWildcards.Contains("userdata") && pathWithWildcards.Contains("*"))
                {
                    // Find Steam installation directory
                    string steamPath = string.Empty;
                    
                    // Common Steam installation locations
                    var possibleSteamPaths = new List<string>
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
                    };
                    
                    // Check drives for Steam installations
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            possibleSteamPaths.Add(Path.Combine(drive.Name, "Steam"));
                            possibleSteamPaths.Add(Path.Combine(drive.Name, "SteamLibrary"));
                        }
                    }
                    
                    // Find the first valid Steam installation
                    foreach (var path in possibleSteamPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            steamPath = path;
                            break;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                    {
                        Debug.WriteLine("Steam directory not found");
                        return "Unknown";
                    }
                    
                    // Extract the app ID and subfolder from the path
                    // Format: userdata\*\236850\remote
                    string[] parts = pathWithWildcards.Split('\\');
                    int appIdIndex = Array.IndexOf(parts, "*") + 1;
                    
                    if (appIdIndex >= parts.Length)
                    {
                        Debug.WriteLine("Invalid Steam userdata path format");
                        return "Unknown";
                    }
                    
                    // The app ID (for EU4 it's 236850)
                    string appId = parts[appIdIndex];
                    
                    // The subdirectory (for EU4 it's "remote")
                    string subDir = (appIdIndex + 1 < parts.Length) ? parts[appIdIndex + 1] : string.Empty;
                    
                    // Look for the userdata directory
                    string userdataPath = Path.Combine(steamPath, "userdata");
                    
                    if (!Directory.Exists(userdataPath))
                    {
                        Debug.WriteLine($"Steam userdata directory not found at {userdataPath}");
                        return "Unknown";
                    }
                    
                    // Find all user folders
                    string[] userFolders = Directory.GetDirectories(userdataPath);
                    
                    foreach (var userFolder in userFolders)
                    {
                        // Check if this user has the specified app ID folder
                        string appFolder = Path.Combine(userFolder, appId);
                        
                        if (Directory.Exists(appFolder))
                        {
                            // If a subdirectory was specified, include it
                            string finalPath = !string.IsNullOrEmpty(subDir) ? 
                                Path.Combine(appFolder, subDir) : appFolder;
                            
                            if (Directory.Exists(finalPath))
                            {
                                Debug.WriteLine($"Found Steam save path: {finalPath}");
                                return finalPath;
                            }
                        }
                    }
                    
                    Debug.WriteLine($"No matching Steam userdata path found for app ID {appId}");
                    return "Unknown";
                }
                
                return pathWithWildcards;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error expanding Steam userdata path: {ex.Message}");
                return "Unknown";
            }
        }
    }
    
    // Result class to return both save path and game name
    public class DetectionResult
    {
        public string SavePath { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string KnownGameId { get; set; } = string.Empty;
        
        public DetectionResult(string savePath = "Unknown", string gameName = "", string knownGameId = "")
        {
            SavePath = savePath;
            GameName = gameName;
            KnownGameId = knownGameId;
        }
    }
    
    // Class to hold information about known games
    public class KnownGameInfo
    {
        public string Name { get; set; } = string.Empty;
        public string GameFolder { get; set; } = string.Empty;
        public string Executable { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public string AlternExec1 { get; set; } = string.Empty;
        public string AlternExec2 { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string LaunchFromSteam { get; set; } = string.Empty;
        public string Uninstall { get; set; } = string.Empty;
        public string Store { get; set; } = string.Empty;
    }
}