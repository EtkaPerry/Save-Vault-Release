using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
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
            logger.Info("Open log viewer with Shift+E key combination");
            
            // Load application settings and apply theme
            var settings = Settings.Load();
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