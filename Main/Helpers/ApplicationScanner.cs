using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using SaveVaultApp.Services;
using SaveVaultApp.Utilities;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Models;

namespace SaveVaultApp.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class ApplicationScanner
    {
        /// <summary>
        /// Enhanced method to find installed applications by combining filesystem and registry scanning
        /// </summary>
        public static void FindInstalledApplications(ObservableCollection<ApplicationInfo> installedApps, Settings settings)
        {
            var processedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // First look for known games from KnownGames.json
            CheckKnownGamesFromConfig(installedApps, settings, processedExecutables);
            
            var searchPaths = new List<string>();
            
            LoggingService.Instance.Info("Starting enhanced application discovery scan...");
            
            // Then scan the Windows Registry for installed applications
            try
            {
                LoggingService.Instance.Info("Scanning Windows Registry for installed applications...");
                var registryExecutables = RegistryScanner.ScanRegistryForApplications();
                
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
                                    installedApps.Add(new ApplicationInfo(settings)
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
                
                LoggingService.Instance.Info($"Registry scan found {registryExecutables.Count} total executables");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during registry scan: {ex.Message}");
                LoggingService.Instance.Error($"Error during registry scan: {ex.Message}");
            }
            
            // Build a list of search paths - start with checking all drives
            try
            {
                // Check each drive for common game installation paths
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType == DriveType.Network || drive.DriveType == DriveType.NoRootDirectory)
                            continue;

                        string driveLetter = drive.Name;
                        
                        // Common installation directories on this drive
                        var commonPaths = new[]
                        {
                            "Program Files",
                            "Program Files (x86)",
                            "Games",
                            "SteamLibrary",
                            "Steam",
                            "SteamApps",
                            "Epic Games",
                            "GoG Games",
                            "Origin Games",
                            "Ubisoft Games",
                            "Xbox Games"
                        };
                        
                        foreach (var path in commonPaths)
                        {
                            string fullPath = Path.Combine(driveLetter, path);
                            if (Directory.Exists(fullPath))
                                searchPaths.Add(fullPath);
                        }
                        
                        // Check for the Users folder on this drive
                        string usersFolder = Path.Combine(driveLetter, "Users");
                        if (Directory.Exists(usersFolder))
                        {
                            foreach (var userDir in Directory.GetDirectories(usersFolder))
                            {
                                try
                                {
                                    // Add common user game folders
                                    var userGameFolders = new[]
                                    {
                                        Path.Combine(userDir, "Documents", "My Games"),
                                        Path.Combine(userDir, "Saved Games"),
                                        Path.Combine(userDir, "Games"),
                                        Path.Combine(userDir, "Downloads"),
                                        Path.Combine(userDir, "Desktop")
                                    };
                                    
                                    foreach (var gameFolder in userGameFolders)
                                    {
                                        if (Directory.Exists(gameFolder))
                                            searchPaths.Add(gameFolder);
                                    }
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    // Skip if we don't have access to this user's directory
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error accessing user folder {userDir}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error accessing drive {drive.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enumerating drives: {ex.Message}");
                LoggingService.Instance.Error($"Error enumerating drives: {ex.Message}");
            }
            
            // Add standard Windows shell folders
            var systemPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "My Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
            };
            searchPaths.AddRange(systemPaths.Where(Directory.Exists));
            
            // Add common game launcher paths
            var launcherPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rockstar Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rockstar Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bethesda.net Launcher"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Bethesda.net Launcher"),
                @"C:\SteamLibrary",
                @"D:\SteamLibrary",
                @"E:\SteamLibrary",
                @"F:\SteamLibrary",
                @"C:\Games",
                @"D:\Games",
                @"E:\Games",
                @"F:\Games"
            };
            searchPaths.AddRange(launcherPaths.Where(Directory.Exists));
            
            LoggingService.Instance.Info($"Searching {searchPaths.Count} paths for applications...");
                
            // Process each search path
            foreach (var searchPath in searchPaths.Distinct())
            {
                if (!Directory.Exists(searchPath))
                    continue;
                
                try
                {
                    SearchDirectoryForExecutables(searchPath, installedApps, processedExecutables, settings);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error searching path {searchPath}: {ex.Message}");
                }
            }
            
            LoggingService.Instance.Info($"Enhanced scan completed. Found {processedExecutables.Count} unique executables.");
        }
        
        /// <summary>
        /// Checks for presence of known games from KnownGames.json by verifying their installation directories
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void CheckKnownGamesFromConfig(ObservableCollection<ApplicationInfo> installedApps, Settings settings, HashSet<string> processedExecutables)
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
                
                foreach (var drive in availableDrives)
                {
                    string possibleGamePath = Path.Combine(drive, game.GameLocation.TrimStart('\\'));
                    string possibleExePath = Path.Combine(possibleGamePath, game.Executable);
                    
                    // Check if the executable exists
                    if (File.Exists(possibleExePath))
                    {
                        processedExecutables.Add(possibleExePath);
                        
                        // Create the ApplicationInfo and directly set the save path
                        var app = new ApplicationInfo(settings)
                        {
                            Name = game.Name,
                            Path = possibleGamePath,
                            ExecutablePath = possibleExePath
                        };
                        
                        // Set the save path from known game configuration
                        if (game.SavePath != null && !string.IsNullOrEmpty(game.SavePath.Path))
                        {
                            string savePath = SaveLocationDetector.ExpandEnvironmentVariables(game.SavePath.Path);
                            app.SavePath = Directory.Exists(savePath) ? savePath : "Unknown";
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
        
        private static void SearchDirectoryForExecutables(string directory, ObservableCollection<ApplicationInfo> apps, 
            HashSet<string> processedExecutables, Settings settings, int maxDepth = 6, int currentDepth = 0)
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
                    ProcessExecutablesInDirectory(binFolder, apps, processedExecutables, settings);
                }

                // Then process current directory
                ProcessExecutablesInDirectory(directory, apps, processedExecutables, settings);
                
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
                        
                        SearchDirectoryForExecutables(subDir, apps, processedExecutables, settings, maxDepth, currentDepth + 1);
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
        }
        
        private static void ProcessExecutablesInDirectory(string directory, ObservableCollection<ApplicationInfo> apps, 
            HashSet<string> processedExecutables, Settings settings)
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
                        apps.Add(new ApplicationInfo(settings)
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
        }
        
        private static bool ShouldSkipDirectory(string directory)
        {
            string dirLower = directory.ToLowerInvariant();
            string dirName = Path.GetFileName(directory).ToLowerInvariant();
            string parentDirName = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty).ToLowerInvariant();
            
            // Skip our own program's folders
            if (dirLower.Contains("savevault") || 
                dirLower.Contains("save vault"))
            {
                return true;
            }
            
            // Skip browser-related directories but be more specific to avoid skipping game directories
            // that might contain these words (like FireChrome game)
            if ((dirName == "chrome" || 
                dirName == "firefox" ||
                dirName == "edge" ||
                dirName == "opera" ||
                dirName == "brave" ||
                dirName == "mozilla") &&
                !dirLower.Contains("game") &&
                !dirLower.Contains("steam") &&
                !dirLower.Contains("epic") &&
                !dirLower.Contains("gog"))
            {
                return true;
            }
            
            // Skip system directories
            if (dirLower.Contains("system32") ||
                dirLower.Contains("syswow64") ||
                dirLower.Contains("$recycle.bin") ||
                dirLower.Contains("system volume information") ||
                (dirLower.Contains("windows") && !dirLower.Contains("game")) ||
                (dirLower.Contains("drivers") && !dirLower.Contains("game")) ||
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
            
            // Skip other common non-game directories
            if (dirName == "temp" ||
                dirName == "tmp" ||
                dirName == "cache" ||
                (dirName == "updates" && !dirLower.Contains("game")) ||
                (dirName == "update" && !dirLower.Contains("game")) ||
                dirName == "logs" ||
                dirName == "log")
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
                dirLower.Contains("rockstar") ||
                dirLower.Contains("bethesda"))
            {
                return false;
            }
            
            return false;
        }
        
        private static bool ShouldSkipExecutable(FileInfo fileInfo)
        {
            string fileName = fileInfo.Name.ToLowerInvariant();
            string dirName = Path.GetFileName(fileInfo.DirectoryName ?? string.Empty).ToLowerInvariant();
            string fullPath = fileInfo.FullName.ToLowerInvariant();
            
            // Skip very small files unless they're in a games directory
            bool isInGamesDir = IsLikelyGamePath(fileInfo.DirectoryName ?? string.Empty);
            if (fileInfo.Length < 50 * 1024 && !isInGamesDir) // 50 KB minimum unless in games directory
                return true;
                
            // Skip browser-related executables and their components
            if ((fileName.Contains("chrome") ||
                fileName.Contains("firefox") ||
                fileName.Contains("edge") ||
                fileName.Contains("opera") ||
                fileName.Contains("brave") ||
                fileName.Contains("mozilla") ||
                fileName.Contains("iexplore") ||
                fileName.Contains("msedge") ||
                fileName.Contains("browser")) &&
                !IsLikelyGameExecutable(fileName, dirName))
            {
                return true;
            }
            
            // Skip common launcher components (but not main launchers)
            if (fileName.EndsWith("crashreport.exe") ||
                fileName.EndsWith("updater.exe") ||
                fileName.EndsWith("helper.exe") ||
                fileName.EndsWith("gpu.exe") ||
                fileName.EndsWith("broker.exe") ||
                fileName.EndsWith("crashpad.exe") ||
                fileName.EndsWith("notification-helper.exe") ||
                fileName.EndsWith("plugin-container.exe") ||
                fileName.EndsWith("service.exe") ||
                fileName.Contains("unins") ||
                fileName.Contains("setup") && fileInfo.Length < 5 * 1024 * 1024)
            {
                return true;
            }
            
            // Allow executables that are likely games
            if (IsLikelyGameExecutable(fileName, dirName))
                return false;
                
            return false;
        }
        
        private static bool IsLikelyGamePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
                
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
        }
        
        private static bool IsLikelyGameExecutable(string fileName, string dirName)
        {
            fileName = fileName.ToLowerInvariant();
            dirName = dirName?.ToLowerInvariant() ?? string.Empty;

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
    }
}
