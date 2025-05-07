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
                Directory.CreateDirectory(saveVaultDirectory);
                logger.Debug($"SaveVault directory created: {Directory.Exists(saveVaultDirectory)}");
            }
              // Load and ensure settings are saved properly
            logger.Info("Loading settings");
            var settings = Settings.Load();
            logger.Info("Force saving settings to ensure file creation");
            settings.ForceSave();
              // Check if the settings file was actually created after trying to save
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SaveVault",
                "settings.json");
                
            if (File.Exists(settingsPath))
            {
                logger.Info($"✅ Settings file verified at: {settingsPath}");
                
                try
                {
                    // Try to check if file is readable
                    string contents = File.ReadAllText(settingsPath);
                    logger.Info($"Settings file is readable, length: {contents.Length}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Settings file exists but can't be read: {ex.Message}");
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
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            
            // Store the window reference for later showing/hiding
            viewModel._mainWindow = mainWindow;
            
            // Check for updates if enabled
            _ = viewModel.InitializeUpdateCheck();
            
            // Set the MainWindow
            desktop.MainWindow = mainWindow;
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