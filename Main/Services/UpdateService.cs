// filepath: c:\Users\Etka\Desktop\Projeler\Save Vault\Main\Services\UpdateService.cs
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using SaveVaultApp.Models;

namespace SaveVaultApp.Services
{    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public bool ForceUpdate { get; set; } = false;
        public string ReleaseDate { get; set; } = string.Empty;
    }

    public class UpdateService
    {
        private static UpdateService? _instance;
        public static UpdateService Instance => _instance ??= new UpdateService();        private readonly Settings _settings;
        private readonly HttpClient _httpClient;
        private readonly string _platform;
        private readonly string _updateUrl;

        // Current application version from assembly
        public Version CurrentVersion { get; private set; }
        
        // Latest version info from server
        public UpdateInfo? LatestVersion { get; private set; }
        
        // Update status
        public bool UpdateAvailable { get; private set; }
        public bool IsChecking { get; private set; }
        public bool IsDownloading { get; private set; }
        public string StatusMessage { get; private set; } = "No updates checked";
        public double DownloadProgress { get; private set; }
        
        // Event handlers for UI updates
        public event EventHandler? UpdateCheckCompleted;
        public event EventHandler<double>? DownloadProgressChanged;
        public event EventHandler<string>? UpdateStatusChanged;
        public event EventHandler<bool>? UpdateAvailabilityChanged;        private UpdateService()
        {
            _settings = Settings.Load();
            _httpClient = new HttpClient();
            
            // Get current version
            var assembly = Assembly.GetExecutingAssembly();
            var versionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            CurrentVersion = versionAttribute != null ? 
                new Version(versionAttribute.Version) : 
                new Version(1, 0, 0);
                
            // Determine platform
            _platform = GetCurrentPlatform();
            _updateUrl = $"https://vault.etka.co.uk/download/{_platform}/version.json";
            
            LoggingService.Instance.Info($"Current version: {CurrentVersion} on platform: {_platform}");
        }
        
        private string GetCurrentPlatform()
        {
            if (OperatingSystem.IsWindows())
                return "windows";
            else if (OperatingSystem.IsLinux())
                return "linux";
            else if (OperatingSystem.IsMacOS())
                return "macos";
            else
                return "windows"; // Default to Windows if unknown platform
        }

        /// <summary>
        /// Checks for updates if the update check interval has been reached
        /// </summary>
        public async Task CheckForUpdatesIfNeeded()
        {
            if (!_settings.AutoCheckUpdates || _settings.OfflineMode)
            {
                LoggingService.Instance.Info("Update checks are disabled (auto-update disabled or offline mode)");
                return;
            }

            var hoursSinceLastCheck = (DateTime.Now - _settings.LastUpdateCheck).TotalHours;
            if (hoursSinceLastCheck >= _settings.UpdateCheckInterval)
            {
                await CheckForUpdates();
            }
        }

        /// <summary>
        /// Checks for updates by downloading version.json from the server
        /// </summary>
        public async Task<bool> CheckForUpdates()
        {
            try
            {
                if (IsChecking) return false;
                
                // Don't check for updates in offline mode
                if (_settings.OfflineMode)
                {
                    UpdateStatus("Update checks are disabled in offline mode");
                    return false;
                }
                
                IsChecking = true;
                UpdateStatus("Checking for updates...");
                  LoggingService.Instance.Info($"Checking for updates at {_updateUrl}");
                
                var response = await _httpClient.GetStringAsync(_updateUrl);
                
                try {
                    // Use case-insensitive property name matching to improve robustness
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    LatestVersion = JsonSerializer.Deserialize<UpdateInfo>(response, options);
                    
                    if (LatestVersion == null)
                    {
                        LoggingService.Instance.Warning("Failed to parse update information");
                        UpdateStatus("Update check failed: Invalid data");
                        return false;
                    }
                }
                catch (JsonException ex)
                {
                    LoggingService.Instance.Error($"Error deserializing version info: {ex.Message}");
                    UpdateStatus("Update check failed: Invalid format");
                    return false;
                }

                // Update last check time
                _settings.LastUpdateCheck = DateTime.Now;
                _settings.Save();
                
                Version serverVersion = new Version(LatestVersion.Version);
                UpdateAvailable = serverVersion > CurrentVersion;
                
                if (UpdateAvailable)
                {
                    UpdateStatus($"Update available: {LatestVersion.Version}");
                    LoggingService.Instance.Info($"Update available: Current={CurrentVersion}, Latest={serverVersion}");
                }
                else
                {
                    UpdateStatus($"You have the latest version ({CurrentVersion})");
                    LoggingService.Instance.Info("No updates available");
                }
                
                // Notify listeners
                OnUpdateAvailabilityChanged(UpdateAvailable);
                OnUpdateCheckCompleted();
                
                return UpdateAvailable;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error checking for updates: {ex.Message}");
                UpdateStatus("Update check failed");
                return false;
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Downloads and installs the latest update
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdate()
        {
            if (LatestVersion == null || !UpdateAvailable || IsDownloading)
            {
                return false;
            }
            
            IsDownloading = true;
            try
            {                string downloadUrl = LatestVersion.DownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    // Use platform-specific default URL
                    string platform = GetCurrentPlatform();
                    string fileName = platform switch
                    {
                        "windows" => "Save%20Vault.exe",
                        "linux" => "save-vault",
                        "macos" => "SaveVault",
                        _ => "Save%20Vault.exe"
                    };
                    downloadUrl = $"https://vault.etka.co.uk/download/{platform}/{fileName}";
                    LoggingService.Instance.Warning($"Using default download URL for {platform}: {downloadUrl}");
                }
                
                UpdateStatus($"Downloading update v{LatestVersion.Version}...");
                LoggingService.Instance.Info($"Downloading update from: {downloadUrl}");
                
                // Create temp directory for download
                string tempDir = Path.Combine(Path.GetTempPath(), "SaveVaultUpdate");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }
                
                // Download the update
                string updateFilePath = Path.Combine(tempDir, "SaveVault_Update.exe");
                
                // Delete existing file if it exists
                if (File.Exists(updateFilePath))
                {
                    File.Delete(updateFilePath);
                }
                
                // Download with progress
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(updateFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        var bytesRead = 0;
                        var totalBytesRead = 0L;
                        
                        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                            totalBytesRead += bytesRead;
                            
                            if (totalBytes > 0)
                            {
                                double progressPercentage = (double)totalBytesRead / totalBytes * 100;
                                DownloadProgress = progressPercentage;
                                OnDownloadProgressChanged(progressPercentage);
                            }
                        }
                    }
                }
                
                LoggingService.Instance.Info("Update downloaded successfully");
                UpdateStatus("Update downloaded. Installing...");
                
                // Run the updater 
                // The updater should handle:
                // 1. Checking if the app is running and asking to close it
                // 2. Backing up the current version
                // 3. Replacing the current version with the new one
                // 4. Restarting the application
                
                // For now, we'll just start the update executable and let the current app close
                try
                {
                    var currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    if (string.IsNullOrEmpty(currentExePath))
                    {
                        LoggingService.Instance.Error("Could not determine current executable path");
                        UpdateStatus("Update installation failed");
                        return false;
                    }
                      // Create platform-specific update script
                    string scriptPath;
                    string scriptContent;
                    ProcessStartInfo startInfo;
                    
                    if (OperatingSystem.IsWindows())
                    {
                        // Windows - use batch file
                        scriptPath = Path.Combine(tempDir, "update.bat");
                        scriptContent = $@"@echo off
echo Updating Save Vault...
timeout /t 2 /nobreak > nul
taskkill /f /im ""Save Vault.exe"" > nul 2>&1
timeout /t 1 /nobreak > nul
copy /Y ""{updateFilePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{scriptPath}""
del ""{updateFilePath}""
exit";

                        File.WriteAllText(scriptPath, scriptContent);
                        
                        // Run the batch file
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/C start /min \"\" \"{scriptPath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        };
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        // Linux - use shell script
                        scriptPath = Path.Combine(tempDir, "update.sh");
                        scriptContent = $@"#!/bin/bash
echo ""Updating Save Vault...""
sleep 2
pkill -f ""SaveVault""
sleep 1
cp -f ""{updateFilePath}"" ""{currentExePath}""
chmod +x ""{currentExePath}""
""{currentExePath}"" &
rm ""{scriptPath}""
rm ""{updateFilePath}""
exit";

                        File.WriteAllText(scriptPath, scriptContent);
                        
                        // Make script executable
                        var chmodProcess = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(chmodProcess)?.WaitForExit();
                        
                        // Run the shell script
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        };
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        // macOS - use shell script (similar to Linux)
                        scriptPath = Path.Combine(tempDir, "update.sh");
                        scriptContent = $@"#!/bin/bash
echo ""Updating Save Vault...""
sleep 2
pkill -f ""SaveVault""
sleep 1
cp -f ""{updateFilePath}"" ""{currentExePath}""
chmod +x ""{currentExePath}""
open ""{currentExePath}""
rm ""{scriptPath}""
rm ""{updateFilePath}""
exit";

                        File.WriteAllText(scriptPath, scriptContent);
                        
                        // Make script executable
                        var chmodProcess = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(chmodProcess)?.WaitForExit();
                        
                        // Run the shell script
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        };
                    }
                    else
                    {
                        // Fallback - use Windows script
                        LoggingService.Instance.Warning("Unknown platform, using Windows update script");
                        scriptPath = Path.Combine(tempDir, "update.bat");
                        scriptContent = $@"@echo off
echo Updating Save Vault...
timeout /t 2 /nobreak > nul
taskkill /f /im ""Save Vault.exe"" > nul 2>&1
timeout /t 1 /nobreak > nul
copy /Y ""{updateFilePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{scriptPath}""
del ""{updateFilePath}""
exit";

                        File.WriteAllText(scriptPath, scriptContent);
                        
                        // Run the batch file
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/C start /min \"\" \"{scriptPath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        };
                    }
                    
                    Process.Start(startInfo);
                    
                    UpdateStatus("Update will be installed when app restarts");
                    LoggingService.Instance.Info("Update installation initiated");
                    return true;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Error starting updater: {ex.Message}");
                    UpdateStatus("Error installing update");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error downloading update: {ex.Message}");
                UpdateStatus($"Update download failed: {ex.Message}");
                return false;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void UpdateStatus(string message)
        {
            StatusMessage = message;
            OnUpdateStatusChanged(message);
        }

        private void OnUpdateCheckCompleted()
        {
            UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void OnDownloadProgressChanged(double progress)
        {
            DownloadProgressChanged?.Invoke(this, progress);
        }

        private void OnUpdateStatusChanged(string status)
        {
            UpdateStatusChanged?.Invoke(this, status);
        }

        private void OnUpdateAvailabilityChanged(bool available)
        {
            UpdateAvailabilityChanged?.Invoke(this, available);
        }
    }
}
