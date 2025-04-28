using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SaveVaultApp.Utilities
{
    [SupportedOSPlatform("windows")]
    public static class RegistryScanner
    {
        /// <summary>
        /// Scans the Windows Registry for installed applications and returns their executable paths
        /// </summary>
        /// <returns>A list of executable paths found in the registry</returns>
        public static List<string> ScanRegistryForApplications()
        {
            var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // Scan machine-wide installations
                ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false, executablePaths);
                ScanRegistryKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", false, executablePaths);
                
                // Scan user-specific installations
                ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true, executablePaths);
                ScanRegistryKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", true, executablePaths);
                
                // Scan Steam games specifically
                ScanSteamRegistry(executablePaths);
                
                // Scan Epic Games Store games
                ScanEpicGamesRegistry(executablePaths);
                
                // Scan GOG Galaxy games
                ScanGogRegistry(executablePaths);
                
                // Scan EA games (Origin/EA App)
                ScanEaRegistry(executablePaths);
                
                // Scan Microsoft Store apps (where accessible)
                ScanMicrosoftStoreApps(executablePaths);
                
                // Scan Ubisoft Connect games
                ScanUbisoftRegistry(executablePaths);
                
                // Scan Bethesda Launcher games (legacy but still used)
                ScanBethesdaRegistry(executablePaths);
                
                // Scan Rockstar Games Launcher games
                ScanRockstarRegistry(executablePaths);
                
                // Scan Battle.net games
                ScanBattleNetRegistry(executablePaths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning registry: {ex.Message}");
            }
            
            return new List<string>(executablePaths);
        }
        
        private static void ScanRegistryKey(string keyPath, bool isUserSpecific, HashSet<string> executablePaths)
        {
            try
            {
                RegistryKey? baseKey = isUserSpecific ? Registry.CurrentUser : Registry.LocalMachine;
                using RegistryKey? key = baseKey.OpenSubKey(keyPath);
                
                if (key == null)
                    return;
                
                foreach (string subkeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? subkey = key.OpenSubKey(subkeyName);
                        if (subkey == null)
                            continue;
                        
                        // Get install location and executable path
                        string? displayName = subkey.GetValue("DisplayName") as string;
                        string? installLocation = subkey.GetValue("InstallLocation") as string;
                        string? uninstallString = subkey.GetValue("UninstallString") as string;
                        string? displayIcon = subkey.GetValue("DisplayIcon") as string;
                        
                        if (string.IsNullOrEmpty(installLocation) && !string.IsNullOrEmpty(uninstallString))
                        {
                            // Try to extract install location from uninstall string
                            installLocation = Path.GetDirectoryName(uninstallString.Replace("\"", ""));
                        }
                        
                        // Skip if we couldn't determine an install location
                        if (string.IsNullOrEmpty(installLocation))
                            continue;
                        
                        // Try to find executables using DisplayIcon or by scanning the directory
                        if (!string.IsNullOrEmpty(displayIcon))
                        {
                            string iconPath = displayIcon.Replace("\"", "").Split(',')[0];
                            if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
                            {
                                executablePaths.Add(iconPath);
                            }
                        }
                        
                        // Scan install location directory for executables
                        if (Directory.Exists(installLocation))
                        {
                            foreach (string exePath in Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories))
                            {
                                if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                                {
                                    executablePaths.Add(exePath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing registry subkey {subkeyName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening registry key {keyPath}: {ex.Message}");
            }
        }
        
        private static void ScanSteamRegistry(HashSet<string> executablePaths)
        {
            try
            {
                // Try to locate Steam installation directory from registry
                string? steamInstallPath = null;
                
                using (RegistryKey? steamKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    steamInstallPath = steamKey?.GetValue("InstallPath") as string;
                }
                
                if (steamInstallPath == null)
                {
                    using (RegistryKey? steamKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                    {
                        steamInstallPath = steamKey?.GetValue("InstallPath") as string;
                    }
                }
                
                if (string.IsNullOrEmpty(steamInstallPath) || !Directory.Exists(steamInstallPath))
                    return;
                
                // Steam libraries file contains paths to all Steam library folders
                string libraryFoldersPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFoldersPath))
                {
                    // Parse the VDF file to extract library paths
                    string[] lines = File.ReadAllLines(libraryFoldersPath);
                    var libraryPaths = new List<string> { Path.Combine(steamInstallPath, "steamapps") };
                    
                    foreach (string line in lines)
                    {
                        if (line.Contains("\"path\""))
                        {
                            string path = line.Split('"')[3].Replace(@"\\", @"\");
                            if (Directory.Exists(path))
                            {
                                libraryPaths.Add(Path.Combine(path, "steamapps"));
                            }
                        }
                    }
                    
                    // Scan each library for game installations
                    foreach (string libraryPath in libraryPaths)
                    {
                        if (!Directory.Exists(libraryPath))
                            continue;
                            
                        // Look for manifest files that indicate installed games
                        foreach (string manifestFile in Directory.GetFiles(libraryPath, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string[] manifestContent = File.ReadAllLines(manifestFile);
                                string? installDir = null;
                                
                                // Extract the installdir from the manifest
                                foreach (string line in manifestContent)
                                {
                                    if (line.Contains("\"installdir\""))
                                    {
                                        installDir = line.Split('"')[3];
                                        break;
                                    }
                                }
                                
                                if (string.IsNullOrEmpty(installDir))
                                    continue;
                                
                                // Construct the full path to the game directory
                                string gameDir = Path.Combine(libraryPath, "common", installDir);
                                if (!Directory.Exists(gameDir))
                                    continue;
                                
                                // Scan for executables in the game directory
                                foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                                {
                                    if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                                    {
                                        executablePaths.Add(exePath);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing Steam manifest {manifestFile}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning Steam registry: {ex.Message}");
            }
        }
        
        private static void ScanEpicGamesRegistry(HashSet<string> executablePaths)
        {
            try
            {
                string epicManifestDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");
                    
                if (!Directory.Exists(epicManifestDir))
                    return;
                
                foreach (string manifestFile in Directory.GetFiles(epicManifestDir, "*.item"))
                {
                    try
                    {
                        string manifestContent = File.ReadAllText(manifestFile);
                        // Very simplistic parsing - in a real implementation you'd want to use JSON parsing
                        if (manifestContent.Contains("\"InstallLocation\""))
                        {
                            int startIndex = manifestContent.IndexOf("\"InstallLocation\"");
                            int valueStart = manifestContent.IndexOf(':', startIndex) + 1;
                            int valueEnd = manifestContent.IndexOf(',', valueStart);
                            
                            if (valueStart > 0 && valueEnd > valueStart)
                            {
                                string installLocation = manifestContent.Substring(valueStart, valueEnd - valueStart)
                                    .Trim(' ', '"', '\r', '\n');
                                
                                if (Directory.Exists(installLocation))
                                {
                                    foreach (string exePath in Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories))
                                    {
                                        if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                                        {
                                            executablePaths.Add(exePath);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing Epic manifest {manifestFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning Epic Games registry: {ex.Message}");
            }
        }
        
        private static void ScanGogRegistry(HashSet<string> executablePaths)
        {
            // Common GOG installation directories
            string[] possibleGogPaths = new[]
            {
                @"C:\Program Files\GOG Galaxy\Games",
                @"C:\Program Files (x86)\GOG Galaxy\Games",
                @"C:\GOG Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games")
            };
            
            foreach (string gogPath in possibleGogPaths)
            {
                if (!Directory.Exists(gogPath))
                    continue;
                    
                // Each subdirectory is a game
                foreach (string gameDir in Directory.GetDirectories(gogPath))
                {
                    try
                    {
                        // Scan for executables
                        foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                        {
                            if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                            {
                                executablePaths.Add(exePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning GOG game directory {gameDir}: {ex.Message}");
                    }
                }
            }
        }
        
        private static void ScanEaRegistry(HashSet<string> executablePaths)
        {
            // Common EA/Origin installation directories
            string[] possibleEaPaths = new[]
            {
                @"C:\Program Files\EA Games",
                @"C:\Program Files (x86)\EA Games",
                @"C:\Program Files\Origin Games",
                @"C:\Program Files (x86)\Origin Games",
                @"C:\Program Files\Electronic Arts",
                @"C:\Program Files (x86)\Electronic Arts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games")
            };
            
            foreach (string eaPath in possibleEaPaths)
            {
                if (!Directory.Exists(eaPath))
                    continue;
                    
                // Each subdirectory is a game
                foreach (string gameDir in Directory.GetDirectories(eaPath))
                {
                    try
                    {
                        // Scan for executables
                        foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                        {
                            if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                            {
                                executablePaths.Add(exePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning EA/Origin game directory {gameDir}: {ex.Message}");
                    }
                }
            }
        }
        
        private static void ScanMicrosoftStoreApps(HashSet<string> executablePaths)
        {
            // Microsoft Store apps are harder to locate as they're in protected directories
            // This is a simplified version that checks common locations
            string[] possibleMsStorePaths = new[]
            {
                @"C:\Program Files\WindowsApps",
                @"C:\Program Files\ModifiableWindowsApps"
            };
            
            foreach (string msStorePath in possibleMsStorePaths)
            {
                if (!Directory.Exists(msStorePath))
                    continue;
                    
                try
                {
                    // This might fail due to permissions
                    foreach (string gameDir in Directory.GetDirectories(msStorePath))
                    {
                        try
                        {
                            // Scan for executables
                            foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                            {
                                if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                                {
                                    executablePaths.Add(exePath);
                                }
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // This is expected for many MS Store apps
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error scanning MS Store app directory {gameDir}: {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // This is expected
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error scanning Microsoft Store apps: {ex.Message}");
                }
            }
        }
        
        private static void ScanUbisoftRegistry(HashSet<string> executablePaths)
        {
            // Common Ubisoft Connect installation directories
            string[] possibleUbiPaths = new[]
            {
                @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\games",
                @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft", "Ubisoft Game Launcher", "games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "games")
            };
            
            foreach (string ubiPath in possibleUbiPaths)
            {
                if (!Directory.Exists(ubiPath))
                    continue;
                    
                // Each subdirectory is a game
                foreach (string gameDir in Directory.GetDirectories(ubiPath))
                {
                    try
                    {
                        // Scan for executables
                        foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                        {
                            if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                            {
                                executablePaths.Add(exePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning Ubisoft game directory {gameDir}: {ex.Message}");
                    }
                }
            }
        }
        
        private static void ScanBethesdaRegistry(HashSet<string> executablePaths)
        {
            // Common Bethesda Launcher installation directories
            string[] possibleBethesdaPaths = new[]
            {
                @"C:\Program Files\Bethesda.net Launcher\games",
                @"C:\Program Files (x86)\Bethesda.net Launcher\games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bethesda.net Launcher", "games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Bethesda.net Launcher", "games")
            };
            
            foreach (string bethesdaPath in possibleBethesdaPaths)
            {
                if (!Directory.Exists(bethesdaPath))
                    continue;
                    
                // Each subdirectory is a game
                foreach (string gameDir in Directory.GetDirectories(bethesdaPath))
                {
                    try
                    {
                        // Scan for executables
                        foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                        {
                            if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                            {
                                executablePaths.Add(exePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning Bethesda game directory {gameDir}: {ex.Message}");
                    }
                }
            }
        }
        
        private static void ScanRockstarRegistry(HashSet<string> executablePaths)
        {
            // Common Rockstar Games Launcher installation directories
            string[] possibleRockstarPaths = new[]
            {
                @"C:\Program Files\Rockstar Games",
                @"C:\Program Files (x86)\Rockstar Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rockstar Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rockstar Games")
            };
            
            foreach (string rockstarPath in possibleRockstarPaths)
            {
                if (!Directory.Exists(rockstarPath))
                    continue;
                    
                // Each subdirectory is a game or the launcher
                foreach (string gameDir in Directory.GetDirectories(rockstarPath))
                {
                    try
                    {
                        // Scan for executables
                        foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                        {
                            if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                            {
                                executablePaths.Add(exePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning Rockstar game directory {gameDir}: {ex.Message}");
                    }
                }
            }
        }
        
        private static void ScanBattleNetRegistry(HashSet<string> executablePaths)
        {
            // Common Battle.net installation directories
            string[] possibleBnetPaths = new[]
            {
                @"C:\Program Files\Battle.net",
                @"C:\Program Files (x86)\Battle.net",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net")
            };
            
            // Blizzard often stores games in separate locations
            string[] possibleBlizzardPaths = new[]
            {
                @"C:\Program Files\Blizzard Entertainment",
                @"C:\Program Files (x86)\Blizzard Entertainment",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blizzard Entertainment"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Blizzard Entertainment")
            };
            
            // Combine paths to scan
            var pathsToScan = new List<string>();
            pathsToScan.AddRange(possibleBnetPaths);
            pathsToScan.AddRange(possibleBlizzardPaths);
            
            foreach (string bnetPath in pathsToScan)
            {
                if (!Directory.Exists(bnetPath))
                    continue;
                
                try
                {
                    // Scan for executables
                    foreach (string exePath in Directory.GetFiles(bnetPath, "*.exe", SearchOption.AllDirectories))
                    {
                        if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                        {
                            executablePaths.Add(exePath);
                        }
                    }
                    
                    // Each subdirectory might be a game
                    foreach (string gameDir in Directory.GetDirectories(bnetPath))
                    {
                        try
                        {
                            foreach (string exePath in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                            {
                                if (File.Exists(exePath) && !IsSystemOrUtilityExecutable(exePath))
                                {
                                    executablePaths.Add(exePath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error scanning Battle.net game directory {gameDir}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error scanning Battle.net path {bnetPath}: {ex.Message}");
                }
            }
        }
        
        private static bool IsSystemOrUtilityExecutable(string exePath)
        {
            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            
            // Skip common system or utility executables
            return fileName.Contains("unins") ||
                   fileName.Contains("install") ||
                   fileName.Contains("setup") ||
                   fileName.Contains("update") ||
                   fileName.Contains("patch") ||
                   fileName.Contains("launcher") && new FileInfo(exePath).Length < 1024 * 1024 ||
                   fileName.Contains("helper") ||
                   fileName.Contains("redist") ||
                   fileName.Contains("vcredist") ||
                   fileName.Contains("dotnet") ||
                   fileName.Contains("uninstall") ||
                   fileName.Contains("repair") ||
                   fileName.EndsWith("utility.exe") ||
                   fileName.EndsWith("helper.exe");
        }
    }
}
