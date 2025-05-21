using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using SaveVaultApp.Models;
using SaveVaultApp.ViewModels;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Command-line utility program for SaveCarrier functionality
    /// </summary>
    public static class SaveCarrierProgram
    {
        /// <summary>
        /// Runs the SaveCarrier command-line utility
        /// </summary>
        /// <param name="args">Command-line arguments</param>
        /// <returns>Exit code (0 for success, non-zero for error)</returns>
        public static async Task<int> Run(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    ShowHelp();
                    return 1;
                }

                string command = args[0].ToLower();
                
                switch (command)
                {
                    case "export":
                        if (args.Length < 4)
                        {
                            Console.WriteLine("Error: Export command requires game path, output path, and compression level.");
                            ShowHelp();
                            return 1;
                        }
                        return await ExportSaves(args[1], args[2], ParseCompressionLevel(args[3]));
                        
                    case "import":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: Import command requires package path.");
                            ShowHelp();
                            return 1;
                        }
                        return await ImportSaves(args[1]);
                        
                    case "list":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: List command requires package path.");
                            ShowHelp();
                            return 1;
                        }
                        return await ListPackageContents(args[1]);
                        
                    case "help":
                    default:
                        ShowHelp();
                        return command == "help" ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// Exports game saves to a SaveCarrier package
        /// </summary>
        private static async Task<int> ExportSaves(string gameSavePath, string outputPath, SaveCarrier.CompressionLevel compressionLevel)
        {
            Console.WriteLine($"Exporting saves from {gameSavePath} to {outputPath} with {compressionLevel} compression...");
            
            if (!Directory.Exists(gameSavePath))
            {
                Console.WriteLine($"Error: Game save directory does not exist: {gameSavePath}");
                return 1;
            }
            
            try
            {
                // Create a minimal application info for the game
                var gameInfo = new ApplicationInfo(new Settings())
                {
                    Name = Path.GetFileName(gameSavePath),
                    SavePath = gameSavePath,
                    ExecutablePath = "Unknown", // CLI mode doesn't require executable path
                    KnownGameId = "Unknown" // CLI mode doesn't need known game ID
                };
                
                bool success = await SaveCarrier.PackSaves(new List<ApplicationInfo> { gameInfo }, outputPath, compressionLevel);
                
                if (success)
                {
                    Console.WriteLine("Export completed successfully!");
                    return 0;
                }
                else
                {
                    Console.WriteLine("Export failed.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export error: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// Imports game saves from a SaveCarrier package
        /// </summary>
        private static async Task<int> ImportSaves(string packagePath)
        {
            Console.WriteLine($"Importing saves from package: {packagePath}");
            
            if (!File.Exists(packagePath))
            {
                Console.WriteLine($"Error: Package file does not exist: {packagePath}");
                return 1;
            }
            
            try
            {
                // Create minimal settings for the import
                var settings = new Settings
                {
                    BackupStorageLocation = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SaveVault", "Backups")
                };
                
                int restoredCount = await SaveCarrier.UnpackSaves(packagePath, settings);
                
                if (restoredCount > 0)
                {
                    Console.WriteLine($"Import completed successfully! {restoredCount} games restored.");
                    return 0;
                }
                else
                {
                    Console.WriteLine("No games were imported.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import error: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// Lists the contents of a SaveCarrier package
        /// </summary>
        private static async Task<int> ListPackageContents(string packagePath)
        {
            Console.WriteLine($"Listing contents of package: {packagePath}");
            
            if (!File.Exists(packagePath))
            {
                Console.WriteLine($"Error: Package file does not exist: {packagePath}");
                return 1;
            }
            
            try
            {
                var metadata = await SaveCarrier.GetPackageMetadata(packagePath);
                
                if (metadata == null)
                {
                    Console.WriteLine("Error: Failed to read package metadata.");
                    return 1;
                }
                
                Console.WriteLine($"Package version: {metadata.CarrierVersion}");
                Console.WriteLine($"Created: {metadata.CreationDate}");
                Console.WriteLine($"Games: {metadata.Games.Count}\n");
                
                foreach (var game in metadata.Games)
                {
                    Console.WriteLine($"Game: {game.Name}");
                    Console.WriteLine($"  Save Path: {game.SavePath}");
                    Console.WriteLine($"  Is Known Game: {game.IsKnownGame}");
                    if (game.IsKnownGame)
                    {
                        Console.WriteLine($"  Known Game ID: {game.KnownGameId}");
                    }
                    Console.WriteLine();
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing package contents: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// Shows help text for the command-line utility
        /// </summary>
        private static void ShowHelp()
        {
            Console.WriteLine("SaveCarrier Command-Line Utility");
            Console.WriteLine("-------------------------------\n");
            Console.WriteLine("Usage:");
            Console.WriteLine("  savecarrier export <game-save-path> <output-path> <compression>");
            Console.WriteLine("  savecarrier import <package-path>");
            Console.WriteLine("  savecarrier list <package-path>");
            Console.WriteLine("  savecarrier help\n");
            Console.WriteLine("Commands:");
            Console.WriteLine("  export     Export game saves to a portable package");
            Console.WriteLine("  import     Import game saves from a package");
            Console.WriteLine("  list       List contents of a package");
            Console.WriteLine("  help       Display this help information\n");
            Console.WriteLine("Compression Levels:");
            Console.WriteLine("  none       No compression (fastest, largest file size)");
            Console.WriteLine("  standard   Standard compression (balanced)");
            Console.WriteLine("  maximum    Maximum compression (smallest size, slowest)\n");
            Console.WriteLine("Example:");
            Console.WriteLine("  savecarrier export \"C:\\Games\\MySave\" \"C:\\Backup\\MySave.svp\" standard");
        }
        
        /// <summary>
        /// Parses compression level from string
        /// </summary>
        private static SaveCarrier.CompressionLevel ParseCompressionLevel(string level)
        {
            return level.ToLower() switch
            {
                "none" => SaveCarrier.CompressionLevel.None,
                "standard" => SaveCarrier.CompressionLevel.Standard,
                "maximum" => SaveCarrier.CompressionLevel.Maximum,
                _ => SaveCarrier.CompressionLevel.Standard // Default to standard compression
            };
        }
    }
}