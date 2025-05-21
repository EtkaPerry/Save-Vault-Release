using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using SaveVaultApp.Models;
using SaveVaultApp.ViewModels;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Utility for packing and unpacking game saves for transfer between devices
    /// </summary>
    public static class SaveCarrier
    {
        // Compression level options
        public enum CompressionLevel
        {
            None,           // No compression, just file copying
            Standard,       // Normal compression (good balance)
            Maximum         // Maximum compression (slower but smaller)
        }

        // Metadata class to store information about packed saves
        public class SaveCarrierMetadata
        {
            public string CarrierVersion { get; set; } = "1.0";
            public DateTime CreationDate { get; set; } = DateTime.Now;
            public List<SaveCarrierGameInfo> Games { get; set; } = new List<SaveCarrierGameInfo>();
        }

        // Information about each game in the carrier package
        public class SaveCarrierGameInfo
        {
            public string Name { get; set; } = string.Empty;
            public string ExecutablePath { get; set; } = string.Empty;
            public string SavePath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public bool IsKnownGame { get; set; } = false;
            public string KnownGameId { get; set; } = string.Empty;
        }

        /// <summary>
        /// Pack selected game saves into a single file for easy transfer
        /// </summary>
        /// <param name="games">List of games to include</param>
        /// <param name="outputPath">Path where to save the carrier file</param>
        /// <param name="compressionLevel">Level of compression to apply</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> PackSaves(List<ApplicationInfo> games, string outputPath, CompressionLevel compressionLevel)
        {
            try
            {
                Debug.WriteLine($"Starting to pack {games.Count} games to {outputPath} with compression level {compressionLevel}");
                
                // Create temp directory for staging files
                string tempDir = Path.Combine(Path.GetTempPath(), "SaveVault_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                
                // Create metadata
                var metadata = new SaveCarrierMetadata();
                int gameIndex = 0;
                
                // Process each game
                foreach (var game in games)
                {
                    if (string.IsNullOrEmpty(game.SavePath) || game.SavePath == "Unknown" || !Directory.Exists(game.SavePath))
                    {
                        Debug.WriteLine($"Skipping game {game.Name} - invalid save path: {game.SavePath}");
                        continue;
                    }
                    
                    try
                    {
                        // Create a folder for this game's saves
                        string gameDir = Path.Combine(tempDir, $"game_{gameIndex}");
                        Directory.CreateDirectory(gameDir);
                        
                        // Copy all save files
                        CopyDirectory(game.SavePath, gameDir);
                        
                        // Add to metadata
                        metadata.Games.Add(new SaveCarrierGameInfo
                        {
                            Name = game.Name,
                            ExecutablePath = game.ExecutablePath,
                            SavePath = game.SavePath,
                            RelativePath = $"game_{gameIndex}",
                            IsKnownGame = !string.IsNullOrEmpty(game.KnownGameId),
                            KnownGameId = game.KnownGameId ?? string.Empty
                        });
                        
                        gameIndex++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing game {game.Name}: {ex.Message}");
                    }
                }
                
                // Save metadata to the temp directory
                string metadataPath = Path.Combine(tempDir, "metadata.json");
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
                
                // Create the carrier file (zip)
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                
                // Apply appropriate compression based on the selected level
                switch (compressionLevel)
                {
                    case CompressionLevel.None:
                        ZipFile.CreateFromDirectory(tempDir, outputPath, System.IO.Compression.CompressionLevel.NoCompression, false);
                        break;
                    case CompressionLevel.Standard:
                        ZipFile.CreateFromDirectory(tempDir, outputPath, System.IO.Compression.CompressionLevel.Fastest, false);
                        break;
                    case CompressionLevel.Maximum:
                        ZipFile.CreateFromDirectory(tempDir, outputPath, System.IO.Compression.CompressionLevel.Optimal, false);
                        break;
                }
                
                // Clean up temp directory
                Directory.Delete(tempDir, true);
                
                Debug.WriteLine($"Successfully packed {metadata.Games.Count} games to {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error packing saves: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get metadata from a SaveCarrier package without extracting saves
        /// </summary>
        /// <param name="packagePath">Path to the package file</param>
        /// <returns>Package metadata if successful, null otherwise</returns>
        public static async Task<SaveCarrierMetadata?> GetPackageMetadata(string packagePath)
        {
            try
            {
                Debug.WriteLine($"Reading metadata from package: {packagePath}");
                
                // Create temp directory just for metadata extraction
                string tempDir = Path.Combine(Path.GetTempPath(), "SaveVault_MetadataRead_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    // Extract just the metadata file from the zip
                    using (var archive = ZipFile.OpenRead(packagePath))
                    {
                        var metadataEntry = archive.GetEntry("metadata.json");
                        if (metadataEntry == null)
                        {
                            Debug.WriteLine("Metadata file not found in package");
                            return null;
                        }
                        
                        string metadataPath = Path.Combine(tempDir, "metadata.json");
                        metadataEntry.ExtractToFile(metadataPath);
                        
                        // Read and parse metadata
                        var metadata = JsonSerializer.Deserialize<SaveCarrierMetadata>(
                            await File.ReadAllTextAsync(metadataPath));
                            
                        return metadata;
                    }
                }
                finally
                {
                    // Clean up temp directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error cleaning up temp directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading package metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Unpack saves from a carrier file and restore them to appropriate locations
        /// </summary>
        /// <param name="carrierFilePath">Path to the carrier file</param>
        /// <param name="settings">Application settings</param>
        /// <returns>Number of games successfully restored</returns>
        public static async Task<int> UnpackSaves(string carrierFilePath, Settings settings)
        {
            try
            {
                Debug.WriteLine($"Starting to unpack saves from {carrierFilePath}");
                
                // Create temp directory for extraction
                string tempDir = Path.Combine(Path.GetTempPath(), "SaveVault_Extract_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                
                // Extract the carrier file
                ZipFile.ExtractToDirectory(carrierFilePath, tempDir);
                
                // Load metadata
                string metadataPath = Path.Combine(tempDir, "metadata.json");
                if (!File.Exists(metadataPath))
                {
                    throw new FileNotFoundException("Metadata file not found in carrier package");
                }
                
                var metadata = JsonSerializer.Deserialize<SaveCarrierMetadata>(
                    await File.ReadAllTextAsync(metadataPath));
                
                if (metadata == null)
                {
                    throw new InvalidDataException("Invalid carrier metadata");
                }
                
                int restoredCount = 0;
                
                // Restore each game
                foreach (var game in metadata.Games)
                {
                    try
                    {
                        string sourcePath = Path.Combine(tempDir, game.RelativePath);
                        string targetPath = game.SavePath;
                        
                        // For known games, resolve the path on this system
                        if (game.IsKnownGame && !string.IsNullOrEmpty(game.KnownGameId))
                        {
                            var knownGame = KnownGames.GamesList.FirstOrDefault(g => g.Name == game.KnownGameId);
                            if (knownGame != null)
                            {
                                targetPath = ExpandEnvironmentVariables(knownGame.SavePath);
                            }
                        }
                        
                        // Check if target directory exists, create if not
                        if (!Directory.Exists(targetPath))
                        {
                            Directory.CreateDirectory(targetPath);
                        }
                        
                        // Backup existing saves before restoring
                        if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length > 0)
                        {
                            string backupDir = Path.Combine(
                                settings.BackupStorageLocation,
                                "CarrierRestore_Backup",
                                game.Name,
                                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                            
                            Directory.CreateDirectory(backupDir);
                            CopyDirectory(targetPath, backupDir);
                            
                            // Clear target directory
                            foreach (var file in Directory.GetFiles(targetPath))
                            {
                                File.Delete(file);
                            }
                            
                            foreach (var dir in Directory.GetDirectories(targetPath))
                            {
                                Directory.Delete(dir, true);
                            }
                        }
                        
                        // Copy from carrier to target
                        CopyDirectory(sourcePath, targetPath);
                        restoredCount++;
                        
                        Debug.WriteLine($"Restored game {game.Name} to {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error restoring game {game.Name}: {ex.Message}");
                    }
                }
                
                // Clean up temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning up temp directory: {ex.Message}");
                }
                
                return restoredCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error unpacking saves: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Copy directory and its contents
        /// </summary>
        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            // Create the target directory if it doesn't exist
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            // Copy all files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string targetPath = Path.Combine(targetDir, fileName);
                File.Copy(file, targetPath, true);
            }
            
            // Copy all subdirectories
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string targetPath = Path.Combine(targetDir, dirName);
                CopyDirectory(dir, targetPath);
            }
        }
        
        /// <summary>
        /// Expand environment variables in a path
        /// </summary>
        private static string ExpandEnvironmentVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
                
            try
            {
                // Handle common environment variables
                return Environment.ExpandEnvironmentVariables(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error expanding environment variables: {ex.Message}");
                return path;
            }
        }
    }
}