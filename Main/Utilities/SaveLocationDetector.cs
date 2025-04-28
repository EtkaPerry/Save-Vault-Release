using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveVaultApp.Models;
using SaveVaultApp.ViewModels;

namespace SaveVaultApp.Utilities
{      public class SaveLocationDetector
    {
        private static List<GameInfo> _knownGames = new List<GameInfo>();
        private static string _knownGamesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "KnownGames.json");
            
        /// <summary>
        /// Checks if the application with the given executable path is a known game
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>True if the executable is recognized as a known game</returns>
        public static bool IsKnownGame(string executablePath)
        {
            try
            {
                if (string.IsNullOrEmpty(executablePath))
                    return false;
                    
                string executableName = Path.GetFileName(executablePath)?.ToLowerInvariant() ?? string.Empty;
                string directoryPath = Path.GetDirectoryName(executablePath)?.ToLowerInvariant() ?? string.Empty;
                
                // Check if the executable matches any known game
                return _knownGames.Any(game => 
                    // Check by executable name
                    (!string.IsNullOrEmpty(game.Executable) &&
                     (executableName.Equals(game.Executable.ToLowerInvariant()) || 
                      executableName.Contains(game.Executable.ToLowerInvariant()) ||
                      // Also check some alternate executables like launchers
                      (executableName.Contains("launcher") && directoryPath.Contains(game.Name.ToLowerInvariant())))) ||
                    // Also check by game name in path
                    (!string.IsNullOrEmpty(game.Name) && 
                     directoryPath.Contains(game.Name.ToLowerInvariant())));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking if executable is known game: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Gets the count of known games loaded from the configuration
        /// </summary>
        /// <returns>The number of known game definitions</returns>
        public static int GetKnownGamesCount()
        {
            return _knownGames.Count;
        }
        
        /// <summary>
        /// Gets the list of all known games from the configuration
        /// </summary>
        /// <returns>A list of GameInfo objects representing known games</returns>
        public static List<GameInfo> GetKnownGames()
        {
            // Ensure games are loaded
            if (_knownGames.Count == 0)
            {
                LoadKnownGames();
            }
            
            return _knownGames;
        }
        
        static SaveLocationDetector()
        {
            try
            {
                LoadKnownGames();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading known games: {ex.Message}");
                _knownGames = new List<GameInfo>();
            }
        }
    
        /// <summary>
        /// Reloads the known games list from disk.
        /// This is useful when the file has been updated or when resetting the program cache.
        /// </summary>
        public static void ReloadKnownGames()
        {
            try
            {
                LoadKnownGames();
                Debug.WriteLine("Successfully reloaded known games list");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reloading known games: {ex.Message}");
                _knownGames = new List<GameInfo>();
            }
        }
          private static void LoadKnownGames()
        {
            if (!File.Exists(_knownGamesPath))
            {
                Debug.WriteLine($"KnownGames.json file not found at: {_knownGamesPath}");
                
                // Try to find the file in alternative locations
                var possiblePaths = new List<string>
                {
                    // Path 1: Relative to base directory
                    Path.Combine(
                        Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) ?? string.Empty, 
                        "Data", 
                        "KnownGames.json"),
                    
                    // Path 2: Relative to current directory
                    Path.Combine(
                        Directory.GetCurrentDirectory(), 
                        "Data", 
                        "KnownGames.json"),
                    
                    // Path 3: Direct project path (for development)
                    @"c:\Users\Etka\Desktop\Projeler\Save Vault\Main\Data\KnownGames.json",
                    
                    // Path 4: One level up from base directory
                    Path.Combine(
                        Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? string.Empty,
                        "Data",
                        "KnownGames.json"),
                        
                    // Path 5: Executable's directory
                    Path.Combine(
                        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty) ?? string.Empty,
                        "Data",
                        "KnownGames.json")
                };
                
                // Log all paths we're checking
                foreach (var path in possiblePaths)
                {
                    Debug.WriteLine($"Checking for KnownGames.json at: {path}, Exists: {File.Exists(path)}");
                }
                
                // Find the first path that exists
                var existingPath = possiblePaths.FirstOrDefault(File.Exists);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    Debug.WriteLine($"Found KnownGames.json at location: {existingPath}");
                    _knownGamesPath = existingPath;
                }
                else
                {
                    Debug.WriteLine("Could not find KnownGames.json in any location");
                    _knownGames = new List<GameInfo>();
                    return;
                }
            }
              try
            {
                string rawJson = File.ReadAllText(_knownGamesPath);
                
                // Remove comments (lines starting with //)
                var jsonLines = rawJson.Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("//"))
                    .ToArray();
                string json = string.Join('\n', jsonLines);
                
                Debug.WriteLine($"Parsing JSON from file: {_knownGamesPath} (Length: {json.Length})");
                
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                _knownGames = JsonSerializer.Deserialize<List<GameInfo>>(json, options) ?? new List<GameInfo>();
                
                Debug.WriteLine($"Successfully loaded {_knownGames.Count} known game definitions");
                
                // Log the loaded game executables for debugging
                foreach (var game in _knownGames)
                {
                    Debug.WriteLine($"Loaded game: {game.Name}, Executable: {game.Executable}, SavePath: {game.SavePath?.Path}");
                    
                    // Validate save path structure
                    if (game.SavePath == null || string.IsNullOrEmpty(game.SavePath.Path))
                    {
                        Debug.WriteLine($"WARNING: Game {game.Name} has no save path defined");
                    }
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Error parsing KnownGames.json: {ex.Message}");
                Debug.WriteLine($"JSON error at line {ex.LineNumber}, path: {ex.Path}");
                _knownGames = new List<GameInfo>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error loading KnownGames.json: {ex.Message}");
                _knownGames = new List<GameInfo>();
            }
        }
          public static DetectionResult DetectSavePath(ApplicationInfo app)
        {
            Debug.WriteLine($"DetectSavePath called for: {app.Name}, Executable: {Path.GetFileName(app.ExecutablePath)}");
            
            // First check if we know this game
            DetectionResult result = DetectFromKnownGames(app);
            Debug.WriteLine($"DetectFromKnownGames returned: {result.SavePath}");
            Debug.WriteLine($"Path exists check: {(!string.IsNullOrEmpty(result.SavePath) && result.SavePath != "Unknown" ? Directory.Exists(result.SavePath).ToString() : "N/A")}");
            
            if (!string.IsNullOrEmpty(result.SavePath) && result.SavePath != "Unknown" && Directory.Exists(result.SavePath))
            {
                Debug.WriteLine($"Found save path from known games list for {app.Name}: {result.SavePath}");
                return result;
            }
            else
            {
                Debug.WriteLine($"Path from KnownGames not found or not valid. Will try general detection.");
            }
            
            // If not found in known games, use the general detection approach
            string generalPath = DetectSavePathGeneral(app);
            return new DetectionResult(generalPath, ""); // No game name for general detection
        }
          public static string ExpandEnvironmentVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            try 
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(path);
                Debug.WriteLine($"Expanded path: '{path}' → '{expandedPath}'");
                
                // Handle special cases
                if (expandedPath.Contains("%USERPROFILE%"))
                {
                    // If %USERPROFILE% wasn't expanded, do it manually
                    expandedPath = expandedPath.Replace(
                        "%USERPROFILE%", 
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    Debug.WriteLine($"Manually expanded USERPROFILE: '{expandedPath}'");
                }
                
                return expandedPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error expanding environment variables in path '{path}': {ex.Message}");
                return path;
            }
        }
          private static DetectionResult DetectFromKnownGames(ApplicationInfo app)
        {
            try
            {
                // Normalize executable name and extract useful info
                string executableName = Path.GetFileName(app.ExecutablePath)?.ToLowerInvariant() ?? string.Empty;
                string appName = app.Name.ToLowerInvariant();
                string appPath = app.Path.ToLowerInvariant();

                // First, check for exact executable matches to avoid duplicates
                var exactMatches = _knownGames
                    .Where(game => !string.IsNullOrEmpty(game.Executable) && 
                                  executableName.Equals(game.Executable.ToLowerInvariant()))
                    .ToList();

                // If we found exact matches for this executable, only consider those
                // This prevents multiple games with similar executables from all matching
                if (exactMatches.Count > 0)
                {
                    Debug.WriteLine($"Found {exactMatches.Count} exact executable matches for {executableName}");
                    
                    // Check if any of them have a valid save path
                    foreach (var match in exactMatches)
                    {
                        if (match.SavePath != null && !string.IsNullOrEmpty(match.SavePath.Path))
                        {
                            string expandedPath = ExpandEnvironmentVariables(match.SavePath.Path);
                            if (Directory.Exists(expandedPath))
                            {
                                Debug.WriteLine($"Using exact match for {executableName}: {match.Name} with save path: {expandedPath}");
                                return new DetectionResult(expandedPath, match.Name);
                            }
                        }
                    }
                    
                    // If we couldn't find a valid save path, return the first match anyway
                    var firstMatch = exactMatches.First();
                    string path = "Unknown";
                    
                    if (firstMatch.SavePath != null && !string.IsNullOrEmpty(firstMatch.SavePath.Path))
                    {
                        path = ExpandEnvironmentVariables(firstMatch.SavePath.Path);
                    }
                    
                    Debug.WriteLine($"Using first exact match for {executableName}: {firstMatch.Name}");
                    return new DetectionResult(path, firstMatch.Name);
                }
                  
                // Special handling for Cyberpunk 2077
                if (executableName.Equals("redprelauncher.exe", StringComparison.OrdinalIgnoreCase) || 
                    appName.Contains("cyberpunk") || appPath.Contains("cyberpunk"))
                {
                    Debug.WriteLine($"[CYBERPUNK DEBUG] Special handling for Cyberpunk - Executable: {executableName}, Name: {appName}");
                    Debug.WriteLine($"[CYBERPUNK DEBUG] App path: {appPath}");
                    
                    // Create Cyberpunk save path
                    string cyberpunkSavePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Saved Games", "CD Projekt Red", "Cyberpunk 2077");
                        
                    Debug.WriteLine($"[CYBERPUNK DEBUG] Constructed Cyberpunk path: {cyberpunkSavePath}");
                    Debug.WriteLine($"[CYBERPUNK DEBUG] UserProfile resolves to: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
                    Debug.WriteLine($"[CYBERPUNK DEBUG] Path exists check: {Directory.Exists(cyberpunkSavePath)}");
                    
                    // Do a direct file check to confirm files exist
                    try {
                        if (Directory.Exists(cyberpunkSavePath)) {
                            string[] files = Directory.GetFiles(cyberpunkSavePath);
                            Debug.WriteLine($"[CYBERPUNK DEBUG] Found {files.Length} files in save directory");
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"[CYBERPUNK DEBUG] Error checking files: {ex.Message}");
                    }
                    
                    if (Directory.Exists(cyberpunkSavePath))
                    {
                        Debug.WriteLine($"[CYBERPUNK DEBUG] Returning Cyberpunk special case path: {cyberpunkSavePath}");
                        return new DetectionResult(cyberpunkSavePath, "Cyberpunk 2077");
                    }
                    else
                    {
                        Debug.WriteLine("[CYBERPUNK DEBUG] Cyberpunk special case path doesn't exist, continuing with regular detection");
                    }
                }
                
                // Special handling for Skyrim - ENHANCED DEBUGGING
                if (executableName.Equals("skyrimse.exe", StringComparison.OrdinalIgnoreCase) || 
                    appName.Contains("skyrim") || appPath.Contains("skyrim"))
                {
                    Debug.WriteLine($"[SKYRIM DEBUG] Special handling for Skyrim - Executable: {executableName}, Name: {appName}");
                    Debug.WriteLine($"[SKYRIM DEBUG] App path: {appPath}");
                    
                    // Create Skyrim save path
                    string skyrimSavePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "My Games", "Skyrim Special Edition", "Saves");
                    
                    Debug.WriteLine($"[SKYRIM DEBUG] Constructed Skyrim path: {skyrimSavePath}");
                    Debug.WriteLine($"[SKYRIM DEBUG] MyDocuments resolves to: {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}");
                    Debug.WriteLine($"[SKYRIM DEBUG] Path exists check: {Directory.Exists(skyrimSavePath)}");
                    
                    // Do a direct file check to confirm files exist
                    try {
                        string[] files = Directory.GetFiles(skyrimSavePath);
                        Debug.WriteLine($"[SKYRIM DEBUG] Found {files.Length} files in save directory");
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"[SKYRIM DEBUG] Error checking files: {ex.Message}");
                    }
                    
                    if (Directory.Exists(skyrimSavePath))
                    {                        Debug.WriteLine($"[SKYRIM DEBUG] Returning Skyrim special case path: {skyrimSavePath}");
                        return new DetectionResult(skyrimSavePath, "Skyrim Special Edition");
                    }
                    else
                    {
                        Debug.WriteLine("[SKYRIM DEBUG] Skyrim special case path doesn't exist, continuing with regular detection");
                    }
                }
                
                // Calculate a "best match" for each game
                var matches = new List<(GameInfo Game, int Score, string Path)>();
                
                // For same executable name, use a dictionary to keep only the highest score match
                var executableMatches = new Dictionary<string, (GameInfo Game, int Score, string Path)>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var game in _knownGames)
                {
                    int score = 0;
                    string gameSavePath = string.Empty;
                    
                    // Exact executable match is highest priority
                    bool exactExecMatch = !string.IsNullOrEmpty(game.Executable) && 
                                         executableName.Equals(game.Executable.ToLowerInvariant());
                    if (exactExecMatch)
                    {
                        score += 100;
                    }
                      // Partial executable match (contains)
                    if (!exactExecMatch && !string.IsNullOrEmpty(game.Executable) && 
                        executableName.Contains(game.Executable.ToLowerInvariant()))
                    {
                        score += 50;
                    }
                      // Launcher executable match - like REDprelauncher.exe for Cyberpunk
                    bool isLauncher = executableName.Contains("launcher") || 
                                      executableName.Contains("prelauncher") || 
                                      executableName.StartsWith("red") || 
                                      executableName.EndsWith("launcher");
                                      
                    if (!exactExecMatch && isLauncher)
                    {
                        // Special handling for known launchers
                        if (executableName.Equals("redprelauncher.exe") && 
                            game.Name.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase))
                        {
                            score += 200; // Direct match for Cyberpunk launcher
                            Debug.WriteLine($"Direct match for Cyberpunk launcher: {executableName}");
                        }
                        // Special handling for Skyrim launcher
                        else if (executableName.Contains("skyrim") && executableName.Contains("launcher") && 
                                game.Name.Contains("Skyrim", StringComparison.OrdinalIgnoreCase))
                        {
                            score += 200; // Direct match for Skyrim launcher
                            Debug.WriteLine($"Direct match for Skyrim launcher: {executableName}");
                        }
                        // General launcher detection
                        else if (!string.IsNullOrEmpty(game.Name) && 
                                (appPath.Contains(game.Name.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) ||
                                 appPath.Replace(" ", "").Contains(game.Name.ToLowerInvariant().Replace(" ", ""), 
                                                                StringComparison.OrdinalIgnoreCase)))
                        {
                            score += 90; // High score for launchers with matching game name in path
                            Debug.WriteLine($"Detected launcher for {game.Name}: {executableName} in path {appPath}");
                        }
                    }
                    
                    // Name matches - exact match gets more points
                    if (!string.IsNullOrEmpty(game.Name))
                    {
                        string gameName = game.Name.ToLowerInvariant();
                        if (appName.Equals(gameName))
                        {
                            score += 80;
                        }
                        else if (appName.Contains(gameName) || gameName.Contains(appName))
                        {
                            score += 40;
                        }
                    }
                    
                    // Location matches
                    if (!string.IsNullOrEmpty(game.GameLocation) && 
                        appPath.Contains(game.GameLocation.ToLowerInvariant()))
                    {
                        score += 60;
                    }
                      // Add special handling for Cyberpunk by looking for its path
                    if (executableName.Equals("redprelauncher.exe", StringComparison.OrdinalIgnoreCase) &&
                        game.Name.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[CYBERPUNK] Special handling for Cyberpunk - Executable: {executableName}, Name: {appName}");
                        Debug.WriteLine($"[CYBERPUNK] App path: {appPath}");
                        score += 50; // Increase score for Cyberpunk launcher
                    }
                    
                    // If we have a reasonable match and a save path
                    // Lower threshold to 30 to catch more matches, especially for launchers
                    if (score >= 30 && game.SavePath != null && !string.IsNullOrEmpty(game.SavePath.Path))
                    {
                        // Replace environment variables (now supports all, not just %USERPROFILE%)
                        gameSavePath = game.SavePath.Path;
                        string expandedPath = ExpandEnvironmentVariables(gameSavePath);
                        
                        Debug.WriteLine($"Checking save path for {game.Name} - Score: {score}");
                        Debug.WriteLine($"  Original path: {gameSavePath}");
                        Debug.WriteLine($"  Expanded path: {expandedPath}");
                        Debug.WriteLine($"  Path exists check: {Directory.Exists(expandedPath)}");
                        
                        // Add debug logging for Skyrim specifically
                        if (game.Name.Contains("Skyrim", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[SKYRIM MATCH] Skyrim save path check - Name: {game.Name}, Score: {score}");
                            Debug.WriteLine($"[SKYRIM MATCH]   Original path: {gameSavePath}");
                            Debug.WriteLine($"[SKYRIM MATCH]   Expanded path: {expandedPath}");
                            Debug.WriteLine($"[SKYRIM MATCH]   Path exists check: {Directory.Exists(expandedPath)}");
                            
                            // Try to list files in the directory
                            try {
                                if (Directory.Exists(expandedPath)) {
                                    string[] files = Directory.GetFiles(expandedPath);
                                    Debug.WriteLine($"[SKYRIM MATCH]   Found {files.Length} files in save directory");
                                }
                            }
                            catch (Exception ex) {
                                Debug.WriteLine($"[SKYRIM MATCH]   Error checking files: {ex.Message}");
                            }
                            
                            // Special case for Skyrim, score and path check
                            if (score >= 40)
                            {
                                Debug.WriteLine($"[SKYRIM MATCH]   Skyrim matched with score {score}");
                                matches.Add((game, score, expandedPath));
                                continue; // Skip the regular path check
                            }
                        }
                          // Validate the path exists
                        if (Directory.Exists(expandedPath))
                        {
                            Debug.WriteLine($"  Path exists, adding match for {game.Name} with score {score}");
                            matches.Add((game, score, expandedPath));
                        }
                        else
                        {
                            Debug.WriteLine($"  WARNING: Save path doesn't exist: {expandedPath}");
                            
                            // For high-confidence matches (like exact executables or launchers), 
                            // check if parent directory exists and try to create the save directory
                            if (score >= 80)
                            {
                                try
                                {                                    string? parentDir = Path.GetDirectoryName(expandedPath);
                                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                                    {
                                        Debug.WriteLine($"  Parent directory exists, adding match anyway: {parentDir}");
                                        matches.Add((game, score, expandedPath)); // Add anyway if parent exists
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"  Error checking parent directory: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                
                // Sort by score and return the best match                if (matches.Count > 0)
                {
                    // Log all matches for debugging
                    Debug.WriteLine($"Found {matches.Count} potential game matches for {app.Name} - {app.ExecutablePath}:");
                    foreach (var match in matches.OrderByDescending(m => m.Score))
                    {
                        Debug.WriteLine($"  - {match.Game.Name} (Score: {match.Score}, Path: {match.Path})");
                    }
                    
                    var bestMatch = matches.OrderByDescending(m => m.Score).First();
                    Debug.WriteLine($"Found match for {app.Name}: {bestMatch.Game.Name} (Score: {bestMatch.Score})");
                    return new DetectionResult(bestMatch.Path, bestMatch.Game.Name);
                }
            }            catch (Exception ex)
            {
                Debug.WriteLine($"Error detecting save path from known games: {ex.Message}");
            }
            
            return new DetectionResult("Unknown", "");
        }
        
        private static string DetectSavePathGeneral(ApplicationInfo app)
        {
            // Extract useful information for path detection
            string appName = app.Name;
            string appNameLower = appName.ToLowerInvariant();
            string execDir = Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty;
            
            // Game-specific save path detection
            if (appNameLower.Contains("steam") || execDir.Contains("steam", StringComparison.OrdinalIgnoreCase))
            {
                // For Steam games, try to find the Steam userdata folder
                string? steamPath = Path.GetDirectoryName(execDir);
                if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(Path.Combine(steamPath, "userdata")))
                {
                    return Path.Combine(steamPath, "userdata");
                }
            }
            
            // Special cases for common games
            if (appNameLower.Contains("minecraft"))
            {
                string minecraftPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
                if (Directory.Exists(minecraftPath))
                {
                    return minecraftPath;
                }
            }
            
            // Special case for Skyrim in general detection
            if (appNameLower.Contains("skyrim"))
            {
                Debug.WriteLine("[SKYRIM GENERAL] Skyrim detected in general path detection");
                string skyrimPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "My Games", "Skyrim Special Edition", "Saves");
                    
                Debug.WriteLine($"[SKYRIM GENERAL] Path: {skyrimPath}, Exists: {Directory.Exists(skyrimPath)}");
                
                if (Directory.Exists(skyrimPath))
                {
                    return skyrimPath;
                }
            }
            
            // Check for Epic Games
            if (appNameLower.Contains("epic games") || execDir.Contains("epic games", StringComparison.OrdinalIgnoreCase))
            {
                // Epic Games save paths often in LocalAppData/[GameName]
                string epicSavesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
                if (Directory.Exists(epicSavesPath))
                {
                    return epicSavesPath;
                }
            }
            
            // EA Games often save in Documents/Electronic Arts/[GameName]
            string eaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Electronic Arts", appName);
            if (Directory.Exists(eaPath))
            {
                return eaPath;
            }
            
            // Ubisoft games
            string ubisoftPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ubisoft", appName);
            if (Directory.Exists(ubisoftPath))
            {
                return ubisoftPath;
            }
            
            // Check for common patterns with different forms of the app name
            // Try without spaces or special characters
            string simplifiedName = new string(appName.Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c)).ToArray());
            
            // Common save path locations - check with various name formats
            var possiblePaths = new List<string>
            {
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
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", appName), // For Linux compatibility
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
    }    // Model classes to match the JSON structure
    public class GameInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Launcher { get; set; } = string.Empty;
        public string GameLocation { get; set; } = string.Empty;
        public string Executable { get; set; } = string.Empty;
        public SavePathInfo? SavePath { get; set; }
    }

    public class SavePathInfo
    {
        public string Path { get; set; } = string.Empty;
    }
    
    // Result class to return both save path and game name
    public class DetectionResult
    {
        public string SavePath { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        
        public DetectionResult(string savePath = "Unknown", string gameName = "")
        {
            SavePath = savePath;
            GameName = gameName;
        }
    }
}
