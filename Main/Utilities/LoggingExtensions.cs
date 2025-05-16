using System;
using System.Diagnostics;
using System.Linq;
using SaveVaultApp.Services;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Extensions for better application logging
    /// </summary>
    public static class LoggingExtensions
    {
        /// <summary>
        /// Logs information about a change detection operation
        /// </summary>
        /// <param name="appId">The application ID</param>
        /// <param name="hasChanges">Whether changes were detected</param>
        /// <param name="fileCount">Number of files checked</param>
        public static void LogChangeDetection(string appId, bool hasChanges, int fileCount)
        {
            string appName = appId?.Split('\\').LastOrDefault() ?? "Unknown";
            string status = hasChanges ? "Changes detected" : "No changes";
            
            Debug.WriteLine($"Change detection for {appName}: {status} (checked {fileCount} files)");
            
            try
            {
                var loggingService = LoggingService.Instance;
                if (loggingService != null)
                {
                    loggingService.Debug($"Change detection for {appName}: {status} (checked {fileCount} files)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging change detection: {ex.Message}");
            }
        }
    }
}
