using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using Avalonia.Markup.Xaml;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Views;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using Avalonia.Styling;

namespace SaveVaultApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Add test logs
            var logger = LoggingService.Instance;
            logger.Debug("Application startup - Debug test message");
            logger.Info("Application startup - Info test message");
            logger.Warning("Application startup - Warning test message");
            logger.Error("Application startup - Error test message");
            logger.Critical("Application startup - Critical test message");
            logger.Info("Open log viewer with Shift+é key combination");
            
            // Log environment details
            try
            {
                logger.Info($"Current Working Directory: {Directory.GetCurrentDirectory()}");
                logger.Info($"User Name: {Environment.UserName}");
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                logger.Info($"Environment.SpecialFolder.ApplicationData: {appDataPath}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error logging environment details: {ex.Message}");
            }            // Debug environment paths before loading settings
            Settings.DebugEnvironmentPaths();
                          // Ensure SaveVault directory exists
            string appDataRoamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string saveVaultDirectory = Path.Combine(appDataRoamingPath, "SaveVault");

            if (!Directory.Exists(saveVaultDirectory))
            {
                logger.Debug($"SaveVault directory does not exist. Creating: {saveVaultDirectory}");
                try {
                    Directory.CreateDirectory(saveVaultDirectory);
                    logger.Debug($"SaveVault directory created: {Directory.Exists(saveVaultDirectory)}");
                    
                    // Test write permissions
                    string testFile = Path.Combine(saveVaultDirectory, "directory_test.txt");
                    File.WriteAllText(testFile, "Testing directory permissions");
                    logger.Debug($"Successfully wrote test file: {testFile}");
                    File.Delete(testFile);
                    logger.Debug("Successfully deleted test file");
                }
                catch (Exception ex) {
                    logger.Error($"Failed to create or test SaveVault directory: {ex.Message}");
                    
                    // Try local app data as fallback
                    try {
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string fallbackDir = Path.Combine(localAppData, "SaveVault");
                        logger.Warning($"Attempting to use fallback location: {fallbackDir}");
                        Directory.CreateDirectory(fallbackDir);
                        
                        // Set a static field to use this path instead
                        Settings.UseAlternativeSettingsPath(Path.Combine(fallbackDir, "settings.json"));
                    }
                    catch (Exception fallbackEx) {
                        logger.Critical($"Failed to create fallback directory: {fallbackEx.Message}");
                    }
                }
            }              // Load and ensure settings are saved properly
            logger.Info("Loading settings");
            var settings = Settings.Load();
              // Force save if the settings file doesn't exist
            string settingsFilePath = Settings.GetSettingsPath();
            
            // Log what properties are set in the loaded settings
            logger.Info("Logging active settings properties");
            settings.LogActiveSettings();
            
            if (!File.Exists(settingsFilePath))
            {
                logger.Info("Settings file doesn't exist. Forcing save to create it.");
                settings.ForceSave();
            }
            else
            {
                logger.Info($"Settings file already exists at: {settingsFilePath}");
                
                // Check file size and force a save if it's suspiciously small
                var fileInfo = new FileInfo(settingsFilePath);
                if (fileInfo.Length < 50) // Arbitrary small size to detect minimal JSON
                {
                    logger.Warning($"Settings file exists but is suspiciously small ({fileInfo.Length} bytes). Forcing save.");
                    settings.ForceSave();
                }
            }// Check if the settings file was actually created after trying to save
            // Use our public method to get the settings path
            string settingsPath = Settings.GetSettingsPath();
                
            logger.Info($"Checking for settings file at: {settingsPath}");
            if (File.Exists(settingsPath))
            {
                logger.Info($"✅ Settings file verified at: {settingsPath}");
                
                try
                {
                    // Try to check if file is readable
                    string contents = File.ReadAllText(settingsPath);
                    logger.Info($"Settings file is readable, length: {contents.Length}");
                    
                    // Try to parse it as JSON to ensure it's valid
                    var jsonDocument = System.Text.Json.JsonDocument.Parse(contents);
                    logger.Info($"Settings file is valid JSON with {jsonDocument.RootElement.EnumerateObject().Count()} properties");
                }
                catch (Exception ex)
                {
                    logger.Error($"Settings file exists but can't be read or parsed: {ex.Message}");
                }
            }
            else
            {
                logger.Error($"❌ Settings file doesn't exist at: {settingsPath}");
                
                // Check directory permissions
                try
                {
                    var dirPath = Path.GetDirectoryName(settingsPath);
                    if (dirPath != null)
                    {
                        var dirInfo = new DirectoryInfo(dirPath);
                        logger.Info($"Directory exists: {dirInfo.Exists}, Created: {dirInfo.CreationTime}");
                        
                        if (dirInfo.Exists)
                        {
                            // Try to create a test file in the directory
                            string testFile = Path.Combine(dirPath, $"permission_test_{DateTime.Now.Ticks}.txt");
                            File.WriteAllText(testFile, "Testing write permissions");
                            logger.Info($"✅ Successfully created test file: {testFile}");
                            File.Delete(testFile);
                            logger.Info("✅ Successfully deleted test file");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Permission test failed: {ex.Message}");
                }
            }
            if (settings.Theme != null)
            {
                // Apply the saved theme
                RequestedThemeVariant = settings.Theme switch
                {
                    "Light" => ThemeVariant.Light,
                    "Dark" => ThemeVariant.Dark,
                    _ => ThemeVariant.Default // System theme
                };
            }
            
            // Create the ViewModel and MainWindow
            var viewModel = new MainWindowViewModel();
            viewModel.IsSearchEnabled = false; // Disable searching until terms are accepted
            
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            
            // Store the window reference for later showing/hiding
            viewModel._mainWindow = mainWindow;
            
            // Check for updates if enabled
            _ = viewModel.InitializeUpdateCheck();
            
            // Check for notifications
            _ = Services.NotificationService.Instance.CheckForNotificationsIfNeeded();
            
            // Show Terms and Conditions if not accepted yet
            if (!settings.TermsAccepted)
            {
                logger.Info("Terms not yet accepted, showing Terms and Conditions window");
                var termsWindow = new TermsWindow();
                termsWindow.Show();
                  // Only proceed with showing the main window after terms are accepted
                termsWindow.Closed += (sender, args) =>
                {
                    // Check both the window property and the settings to be sure
                    var currentSettings = Settings.Instance;
                    bool termsWereAccepted = termsWindow.TermsAccepted || currentSettings.TermsAccepted;
                    
                    if (termsWereAccepted)
                    {
                        // Terms were accepted, show the main window
                        logger.Info("Terms accepted, showing main window");
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                        
                        // Now that terms are accepted and main window is shown, start searching for programs
                        logger.Info("Starting program search after terms accepted");
                        viewModel.IsSearchEnabled = true;
                        viewModel.InitializeApplicationSearch();
                    }
                    else
                    {
                        // Terms were not accepted, exit the application
                        logger.Info("Terms not accepted, exiting application");
                        desktop.Shutdown();
                    }
                };
            }            else
            {
                // Terms already accepted, show the main window directly
                logger.Info("Terms already accepted, showing main window");
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                
                // Start searching for programs
                logger.Info("Starting program search (terms previously accepted)");
                viewModel.IsSearchEnabled = true;
                viewModel.InitializeApplicationSearch();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}