using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Models;
using SaveVaultApp.Services;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;

namespace SaveVaultApp.Views;

public partial class ExtensionWindow : Window
{
    public ExtensionWindow()
    {
        InitializeComponent();
        DataContext = new ExtensionViewModel();
        
        // Handle window events
        Loaded += ExtensionWindow_Loaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }    private void ExtensionWindow_Loaded(object? sender, RoutedEventArgs e)
    {        if (DataContext is ExtensionViewModel viewModel)
        {
            // Reset the modified flag when window loads
            viewModel.ResetModifiedFlag();
            
            // Load extensions when window opens
            viewModel.LoadExtensionsCommand.Execute(null);
        }
    }    private bool _isClosingFromRestart = false;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        // If we're already in the process of closing from restart, don't interfere
        if (_isClosingFromRestart)
        {
            SaveWindowSettings();
            base.OnClosing(e);
            return;
        }

        // Check if extensions were modified and handle restart prompt
        if (DataContext is ExtensionViewModel viewModel && viewModel.ExtensionsModified)
        {
            // Cancel the close to handle restart prompt first
            e.Cancel = true;
            
            try
            {
                // Show restart prompt
                bool shouldRestart = await RestartPromptService.Instance.ShowExtensionRestartPromptAsync(this);
                
                if (shouldRestart)
                {
                    // User chose to restart - save settings and restart
                    SaveWindowSettings();
                    
                    // Set flag to prevent re-entry and restart the application
                    _isClosingFromRestart = true;
                    bool restartSuccessful = await RestartPromptService.Instance.RestartApplicationAsync();
                    
                    if (!restartSuccessful)
                    {
                        // If restart failed, still close the window but log the error
                        LoggingService.Instance?.Error("Failed to restart application after extension changes");
                        _isClosingFromRestart = true;
                        Close();
                    }
                    // If restart successful, the app will close automatically
                }
                else
                {
                    // User chose "Not Now" - reset the modified flag and close normally
                    viewModel.ResetModifiedFlag();
                    _isClosingFromRestart = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance?.Error($"Error handling extension restart prompt: {ex.Message}");
                // If something goes wrong, reset flag and close normally
                if (DataContext is ExtensionViewModel vm)
                {
                    vm.ResetModifiedFlag();
                }
                _isClosingFromRestart = true;
                Close();
            }
        }
        else
        {
            // No extensions modified, close normally
            SaveWindowSettings();
            base.OnClosing(e);
        }
    }

    private void SaveWindowSettings()
    {
        // Save window position and size before closing
        var settings = Settings.Instance;
        settings.OptionsWindowWidth = Width;
        settings.OptionsWindowHeight = Height;
        settings.OptionsWindowPositionX = Position.X;
        settings.OptionsWindowPositionY = Position.Y;
        settings.IsOptionsMaximized = WindowState == WindowState.Maximized;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }

    public async void ImportExtension_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Extension",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Extension Files")
                    {
                        Patterns = new[] { "*.zip", "*.lua" },
                        MimeTypes = new[] { "application/zip", "text/plain" }
                    },
                    new FilePickerFileType("Zip Archives")
                    {
                        Patterns = new[] { "*.zip" },
                        MimeTypes = new[] { "application/zip" }
                    },
                    new FilePickerFileType("Lua Scripts")
                    {
                        Patterns = new[] { "*.lua" },
                        MimeTypes = new[] { "text/plain" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is ExtensionViewModel viewModel)
            {
                var file = files[0];
                await viewModel.ImportExtensionFile(file.Path.LocalPath);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to import extension: {ex.Message}");
        }
    }
}