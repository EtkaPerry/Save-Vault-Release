using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;

namespace SaveVaultApp.Models;

/// <summary>
/// Separate storage for application data that gets updated frequently.
/// This prevents the main settings file from being rewritten too often.
/// </summary>
public class AppData
{
    // Add static instance
    private static AppData? _instance;
    public static AppData? Instance => _instance;
    
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SaveVault",
        "appdata.json"
    );
    
    // App specific data that changes frequently
    public Dictionary<string, DateTime> LastBackupTimes { get; set; } = new();
    public Dictionary<string, string> CustomNames { get; set; } = new();
    public Dictionary<string, string> CustomSavePaths { get; set; } = new();
    public HashSet<string> HiddenApps { get; set; } = new();
    public HashSet<string> KnownApplicationPaths { get; set; } = new();
    
    // Constructor that ensures this instance is the current static instance
    public AppData()
    {
        // Set this instance as the current static instance
        // This ensures that any instance creation updates the static reference
        _instance = this;
    }

    public static AppData Load()
    {
        try
        {
            var directory = Path.GetDirectoryName(AppDataPath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(AppDataPath))
            {
                var json = File.ReadAllText(AppDataPath);
                var appData = JsonSerializer.Deserialize<AppData>(json) ?? new AppData();
                
                // Initialize collections if they're null to prevent null reference exceptions
                appData.LastBackupTimes ??= new();
                appData.CustomNames ??= new();
                appData.CustomSavePaths ??= new();
                appData.HiddenApps ??= new();
                appData.KnownApplicationPaths ??= new();

                _instance = appData;
                return appData;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading app data: {ex.Message}");
        }
        
        // Return new app data if file doesn't exist or loading fails
        var newAppData = new AppData();
        _instance = newAppData;
        return newAppData;
    }
      public void Save()
    {
        try
        {
            // Make sure this instance is set as the static instance
            if (_instance != this)
            {
                _instance = this;
            }
            
            var directory = Path.GetDirectoryName(AppDataPath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            // Make sure we have valid collections before saving
            KnownApplicationPaths ??= new HashSet<string>();
            LastBackupTimes ??= new Dictionary<string, DateTime>();
            CustomNames ??= new Dictionary<string, string>();
            CustomSavePaths ??= new Dictionary<string, string>();
            HiddenApps ??= new HashSet<string>();            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(AppDataPath, json);
            
            Debug.WriteLine($"App data saved successfully to {AppDataPath}");
            Debug.WriteLine($"  - Known Applications: {KnownApplicationPaths?.Count ?? 0}");
            Debug.WriteLine($"  - Custom Names: {CustomNames?.Count ?? 0}");
            Debug.WriteLine($"  - Custom Save Paths: {CustomSavePaths?.Count ?? 0}");
            Debug.WriteLine($"  - Last Backup Times: {LastBackupTimes?.Count ?? 0}");
            Debug.WriteLine($"  - Hidden Apps: {HiddenApps?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving app data: {ex.Message}");
        }
    }    // Migration helper method to migrate data from Settings to AppData
    public static void MigrateFromSettings(Settings settings)
    {
        try
        {
            // Ensure we have an instance
            var appData = Instance ?? new AppData();
            
            // Migrate LastBackupTimes
            if (settings.LastBackupTimes != null)
            {
                foreach (var item in settings.LastBackupTimes)
                {
                    appData.LastBackupTimes[item.Key] = item.Value;
                }
            }
            
            // Migrate CustomNames
            if (settings.CustomNames != null)
            {
                foreach (var item in settings.CustomNames)
                {
                    appData.CustomNames[item.Key] = item.Value;
                }
            }
            
            // Migrate CustomSavePaths
            if (settings.CustomSavePaths != null)
            {
                foreach (var item in settings.CustomSavePaths)
                {
                    appData.CustomSavePaths[item.Key] = item.Value;
                }
            }
            
            // Migrate HiddenApps
            if (settings.HiddenApps != null)
            {
                foreach (var item in settings.HiddenApps)
                {
                    appData.HiddenApps.Add(item);
                }
            }
            
            // Migrate KnownApplicationPaths
            if (settings.KnownApplicationPaths != null)
            {
                foreach (var path in settings.KnownApplicationPaths)
                {
                    appData.KnownApplicationPaths.Add(path);
                }
            }
            
            // Save the migrated data
            appData.Save();
            Debug.WriteLine("Successfully migrated settings to AppData");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error migrating settings to AppData: {ex.Message}");
        }
    }
}
