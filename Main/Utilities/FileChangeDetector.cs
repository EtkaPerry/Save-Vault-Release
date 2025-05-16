using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using SaveVaultApp.Models;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Provides utilities for detecting changes in files for backup purposes
    /// </summary>
    public static class FileChangeDetector
    {
        /// <summary>
        /// Computes file state information for a list of files
        /// </summary>
        /// <param name="filePaths">List of file paths to check</param>
        /// <returns>Dictionary of file paths and their state hash</returns>
        public static Dictionary<string, string> ComputeFileStates(IEnumerable<string> filePaths)
        {
            var result = new Dictionary<string, string>();
            
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        // Get both hash and file info for more accurate change detection
                        var fileInfo = new FileInfo(filePath);
                        var stateKey = $"{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
                        
                        // For small files (< 10MB), include hash for more accurate detection
                        if (fileInfo.Length < 10_000_000) // 10MB
                        {
                            var hash = ComputeFileHash(filePath);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                stateKey += $"|{hash}";
                            }
                        }
                        
                        result[filePath] = stateKey;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error computing file state for {filePath}: {ex.Message}");
                }
            }
            
            return result;
        }        /// <summary>
        /// Checks if files have changed since the last backup
        /// </summary>
        /// <param name="appId">Application identifier</param>
        /// <param name="filePaths">List of file paths to check</param>
        /// <param name="appSpecificSettings">Optional app-specific settings to check for change detection override</param>
        /// <returns>True if changes are detected, false otherwise</returns>
        public static bool HaveFilesChanged(string appId, IEnumerable<string> filePaths, AppSpecificSettings? appSpecificSettings = null)
        {
            var settings = Settings.Instance;
            
            // First check if there's an app-specific setting for change detection
            bool changeDetectionEnabled = true; // Default to true
            if (appSpecificSettings != null && appSpecificSettings.HasCustomSettings)
            {
                // Use app-specific setting if available
                changeDetectionEnabled = appSpecificSettings.ChangeDetectionEnabled;
                Debug.WriteLine($"Using app-specific change detection setting: {(changeDetectionEnabled ? "enabled" : "disabled")}");
            }
            else
            {
                // Fall back to global setting
                changeDetectionEnabled = settings.ChangeDetectionEnabled;
                Debug.WriteLine($"Using global change detection setting: {(changeDetectionEnabled ? "enabled" : "disabled")}");
            }
            
            // If change detection is disabled (globally or for this app), always return true (force backup)
            if (!changeDetectionEnabled)
            {
                Debug.WriteLine("Change detection is disabled, forcing backup");
                return true;
            }
            
            try
            {
                // Check if we have previous states for this app
                if (!settings.LastFileStates.ContainsKey(appId))
                {
                    Debug.WriteLine($"No previous file states found for {appId}, treating as changed");
                    return true;
                }
                
                // Compute current states
                var currentStates = ComputeFileStates(filePaths);
                Debug.WriteLine($"Computed states for {currentStates.Count} files for {appId}");
                
                // Check for changes by comparing with last known states
                bool hasChanges = settings.HasChanges(appId, currentStates);
                Debug.WriteLine($"Change detection result for {appId}: {(hasChanges ? "Changes detected" : "No changes")}");
                
                return hasChanges;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in change detection: {ex.Message}");
                return true; // On error, assume changes exist to be safe
            }
        }
        
        /// <summary>
        /// Updates the stored file states after a successful backup
        /// </summary>
        /// <param name="appId">Application identifier</param>
        /// <param name="filePaths">List of file paths that were backed up</param>
        public static void UpdateFileStates(string appId, IEnumerable<string> filePaths)
        {
            try
            {
                var currentStates = ComputeFileStates(filePaths);
                Settings.Instance.UpdateFileStates(appId, currentStates);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating file states: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Computes an MD5 hash for a file
        /// </summary>
        private static string ComputeFileHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    // For large files, only hash the first and last 4MB
                    if (stream.Length > 8_000_000) // 8MB
                    {
                        const int bufferSize = 4_000_000; // 4MB
                        byte[] firstBuffer = new byte[bufferSize];
                        byte[] lastBuffer = new byte[bufferSize];
                        
                        // Read first 4MB
                        stream.Position = 0;
                        int firstBytesRead = 0;
                        int bytesRead;
                        while (firstBytesRead < bufferSize && 
                               (bytesRead = stream.Read(firstBuffer, firstBytesRead, bufferSize - firstBytesRead)) > 0)
                        {
                            firstBytesRead += bytesRead;
                        }
                        
                        // Read last 4MB if file is big enough
                        int lastBytesRead = 0;
                        if (stream.Length > bufferSize)
                        {
                            stream.Position = Math.Max(0, stream.Length - bufferSize);
                            while (lastBytesRead < bufferSize && 
                                   (bytesRead = stream.Read(lastBuffer, lastBytesRead, bufferSize - lastBytesRead)) > 0)
                            {
                                lastBytesRead += bytesRead;
                            }
                        }
                        else
                        {
                            // If the file is smaller than our buffer size, just use what we already read
                            lastBytesRead = 0;
                        }
                        
                        // Process both parts for the hash
                        md5.TransformBlock(firstBuffer, 0, firstBytesRead, firstBuffer, 0);
                        md5.TransformFinalBlock(lastBuffer, 0, lastBytesRead);
                    }
                    else
                    {
                        // Hash entire file for smaller files
                        byte[] hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                    
                    byte[] resultHash = md5.Hash ?? Array.Empty<byte>();
                    return BitConverter.ToString(resultHash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error computing hash for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
