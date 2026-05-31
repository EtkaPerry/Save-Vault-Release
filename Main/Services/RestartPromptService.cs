using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace SaveVaultApp.Services;

public class RestartPromptService
{
    private static RestartPromptService? _instance;
    public static RestartPromptService Instance => _instance ??= new RestartPromptService();

    private RestartPromptService() { }

    public async Task<bool> ShowExtensionRestartPromptAsync(Window? owner = null)
    {
        var logger = LoggingService.Instance;
        logger?.Info("ShowExtensionRestartPromptAsync called");        try
        {
            bool result = false;
            
            // Ensure we're on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                logger?.Info("Creating extension restart dialog on UI thread");
                
                // Wait a bit to ensure UI is ready
                await Task.Delay(100);
                
                // Create a dialog window
                var dialog = new Window
                {
                    Width = 450,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Title = "Extensions Modified - Restart Required",
                    CanResize = false,
                    Topmost = true,
                    ShowInTaskbar = true
                };                // Create buttons
                var restartButton = new Button
                {
                    Content = "Restart",
                    Width = 120,
                    Margin = new Avalonia.Thickness(0, 0, 10, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                var notNowButton = new Button
                {
                    Content = "Not Now",
                    Width = 100,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                // Create logo image
                Control? logoImage = null;
                try
                {
                    logger?.Info("Attempting to load logo for restart dialog...");
                    
                    // Try multiple possible paths using the avares:// scheme for embedded resources
                    string[] possiblePaths = 
                    {
                        "avares://Save Vault/Assets/Logo.png",
                        "avares://Save Vault/Assets/logo.png", 
                        "avares://Save Vault/Assets/logo.ico",
                        "avares://SaveVaultApp/Assets/Logo.png",
                        "avares://SaveVaultApp/Assets/logo.png", 
                        "avares://SaveVaultApp/Assets/logo.ico"
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            logger?.Info($"Trying to load logo from: {path}");
                            var logoUri = new Uri(path);
                            var logoAsset = AssetLoader.Open(logoUri);
                            var logoBitmap = new Bitmap(logoAsset);
                            logoImage = new Image
                            {
                                Source = logoBitmap,
                                Width = 48,
                                Height = 48,
                                Margin = new Avalonia.Thickness(0, 0, 0, 15),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                            };
                            logger?.Info($"Successfully loaded logo from: {path}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger?.Warning($"Failed to load logo from {path}: {ex.Message}");
                        }
                    }
                    
                    if (logoImage == null)
                    {
                        logger?.Warning("Could not load logo from any path, creating placeholder");
                        // Create a placeholder with text if logo fails to load
                        logoImage = new TextBlock
                        {
                            Text = "🔒",
                            FontSize = 32,
                            FontWeight = Avalonia.Media.FontWeight.Bold,
                            Margin = new Avalonia.Thickness(0, 0, 0, 15),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            TextAlignment = Avalonia.Media.TextAlignment.Center
                        } as Control;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Could not load logo for restart dialog: {ex.Message}");
                    // Create a placeholder with text if logo fails to load
                    logoImage = new TextBlock
                    {
                        Text = "🔒",
                        FontSize = 32,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Margin = new Avalonia.Thickness(0, 0, 0, 15),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    } as Control;
                }

                // Create content
                var contentChildren = new Avalonia.Collections.AvaloniaList<Avalonia.Controls.Control>();
                
                // Add logo if loaded successfully
                if (logoImage != null)
                {
                    contentChildren.Add(logoImage);
                }

                // Add title
                contentChildren.Add(new TextBlock
                {
                    Text = "Extensions Modified",
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 0, 0, 10),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center
                });

                // Add message
                contentChildren.Add(new TextBlock
                {
                    Text = "For extensions to take effect, you need to restart the application.",
                    FontSize = 14,
                    Margin = new Avalonia.Thickness(20, 0, 20, 20),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

                // Add sub-message
                contentChildren.Add(new TextBlock
                {
                    Text = "Would you like to restart now?",
                    FontSize = 13,
                    Margin = new Avalonia.Thickness(20, 0, 20, 25),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    Opacity = 0.8
                });                // Create button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 10, 0, 0)
                };
                buttonPanel.Children.Add(restartButton);
                buttonPanel.Children.Add(notNowButton);
                contentChildren.Add(buttonPanel);                // Create main content panel
                var content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                // Add all content children to the panel
                foreach (var child in contentChildren)
                {
                    content.Children.Add(child);
                }

                dialog.Content = content;                // Handle button clicks using TaskCompletionSource for cleaner async handling
                var tcs = new TaskCompletionSource<bool>();
                
                restartButton.Click += (s, e) =>
                {
                    logger?.Info("User clicked Restart button");
                    tcs.TrySetResult(true);
                    dialog.Close();
                };

                notNowButton.Click += (s, e) =>
                {
                    logger?.Info("User clicked Not Now button");
                    tcs.TrySetResult(false);
                    dialog.Close();
                };

                // Handle dialog close with X button (default to not now)
                dialog.Closing += (s, e) =>
                {
                    logger?.Info("Dialog closing via X button or other means");
                    tcs.TrySetResult(false);
                };

                // Show dialog
                logger?.Info("Showing extension restart dialog");
                
                if (owner != null)
                {
                    // Show as modal dialog
                    _ = dialog.ShowDialog(owner);
                }
                else
                {
                    // Show as regular window
                    dialog.Show();
                }

                // Wait for user decision
                result = await tcs.Task;
            });

            logger?.Info($"Extension restart dialog result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            logger?.Warning($"Could not show extension restart dialog: {ex.Message}. Defaulting to not restart.");
            return false;
        }
    }

    public async Task<bool> RestartApplicationAsync()
    {
        var logger = LoggingService.Instance;
        logger?.Info("RestartApplicationAsync called");

        try
        {
            // Get current executable path
            var currentProcess = Process.GetCurrentProcess();
            var executablePath = currentProcess.MainModule?.FileName;
            
            if (string.IsNullOrEmpty(executablePath))
            {
                logger?.Error("Could not determine executable path for restart");
                return false;
            }

            logger?.Info($"Restarting application: {executablePath}");

            // Set environment variable to identify this as a restart (not second instance)
            Environment.SetEnvironmentVariable("SAVEVAULT_RESTART", "true");

            // Start new process
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            logger?.Info("Starting new application instance...");
            Process.Start(startInfo);

            // Wait a moment for the new process to start
            await Task.Delay(1000);

            // Shutdown current application
            logger?.Info("Shutting down current application instance");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            logger?.Error($"Failed to restart application: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ShowLanguageChangeRestartPromptAsync(Window? owner = null)
    {
        var logger = LoggingService.Instance;
        logger?.Info("ShowLanguageChangeRestartPromptAsync called");

        try
        {
            bool result = false;
            
            // Ensure we're on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                logger?.Info("Creating language change restart dialog on UI thread");
                
                // Wait a bit to ensure UI is ready
                await Task.Delay(100);
                
                // Create a dialog window
                var dialog = new Window
                {
                    Width = 450,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Title = "Language Changed - Restart Required",
                    CanResize = false,
                    Topmost = true,
                    ShowInTaskbar = true
                };

                // Create buttons
                var restartButton = new Button
                {
                    Content = "Restart",
                    Width = 120,
                    Margin = new Avalonia.Thickness(0, 0, 10, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                var notNowButton = new Button
                {
                    Content = "Not Now",
                    Width = 100,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                // Create logo image
                Control? logoImage = null;
                try
                {
                    logger?.Info("Attempting to load logo for language restart dialog...");
                    
                    // Try multiple possible paths using the avares:// scheme for embedded resources
                    string[] possiblePaths = 
                    {
                        "avares://Save Vault/Assets/Logo.png",
                        "avares://Save Vault/Assets/logo.png", 
                        "avares://Save Vault/Assets/logo.ico",
                        "avares://SaveVaultApp/Assets/Logo.png",
                        "avares://SaveVaultApp/Assets/logo.png", 
                        "avares://SaveVaultApp/Assets/logo.ico"
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            logger?.Info($"Trying to load logo from: {path}");
                            var logoUri = new Uri(path);
                            var logoAsset = AssetLoader.Open(logoUri);
                            var logoBitmap = new Bitmap(logoAsset);
                            logoImage = new Image
                            {
                                Source = logoBitmap,
                                Width = 48,
                                Height = 48,
                                Margin = new Avalonia.Thickness(0, 0, 0, 15),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                            };
                            logger?.Info($"Successfully loaded logo from: {path}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger?.Warning($"Failed to load logo from {path}: {ex.Message}");
                        }
                    }
                    
                    if (logoImage == null)
                    {
                        logger?.Warning("Could not load logo from any path, creating placeholder");
                        // Create a placeholder with text if logo fails to load
                        logoImage = new TextBlock
                        {
                            Text = "🌐",
                            FontSize = 32,
                            FontWeight = Avalonia.Media.FontWeight.Bold,
                            Margin = new Avalonia.Thickness(0, 0, 0, 15),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            TextAlignment = Avalonia.Media.TextAlignment.Center
                        } as Control;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Could not load logo for language restart dialog: {ex.Message}");
                    // Create a placeholder with text if logo fails to load
                    logoImage = new TextBlock
                    {
                        Text = "🌐",
                        FontSize = 32,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Margin = new Avalonia.Thickness(0, 0, 0, 15),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    } as Control;
                }

                // Create content
                var contentChildren = new Avalonia.Collections.AvaloniaList<Avalonia.Controls.Control>();
                
                // Add logo if loaded successfully
                if (logoImage != null)
                {
                    contentChildren.Add(logoImage);
                }

                // Add title
                contentChildren.Add(new TextBlock
                {
                    Text = "Language Changed",
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 0, 0, 10),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center
                });

                // Add message
                contentChildren.Add(new TextBlock
                {
                    Text = "For the language change to take full effect, you need to restart the application.",
                    FontSize = 14,
                    Margin = new Avalonia.Thickness(20, 0, 20, 20),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

                // Add sub-message
                contentChildren.Add(new TextBlock
                {
                    Text = "Would you like to restart now?",
                    FontSize = 13,
                    Margin = new Avalonia.Thickness(20, 0, 20, 25),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    Opacity = 0.8
                });

                // Create button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 10, 0, 0)
                };
                buttonPanel.Children.Add(restartButton);
                buttonPanel.Children.Add(notNowButton);
                contentChildren.Add(buttonPanel);

                // Create main content panel
                var content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                // Add all content children to the panel
                foreach (var child in contentChildren)
                {
                    content.Children.Add(child);
                }

                dialog.Content = content;

                // Handle button clicks using TaskCompletionSource for cleaner async handling
                var tcs = new TaskCompletionSource<bool>();
                
                restartButton.Click += (s, e) =>
                {
                    logger?.Info("User clicked Restart button for language change");
                    tcs.TrySetResult(true);
                    dialog.Close();
                };

                notNowButton.Click += (s, e) =>
                {
                    logger?.Info("User clicked Not Now button for language change");
                    tcs.TrySetResult(false);
                    dialog.Close();
                };

                // Handle dialog close with X button (default to not now)
                dialog.Closing += (s, e) =>
                {
                    logger?.Info("Language restart dialog closing via X button or other means");
                    tcs.TrySetResult(false);
                };

                // Show dialog
                logger?.Info("Showing language change restart dialog");
                
                if (owner != null)
                {
                    // Show as modal dialog
                    _ = dialog.ShowDialog(owner);
                }
                else
                {
                    // Show as regular window
                    dialog.Show();
                }

                // Wait for user decision
                result = await tcs.Task;
            });

            logger?.Info($"Language change restart dialog result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            logger?.Warning($"Could not show language change restart dialog: {ex.Message}. Defaulting to not restart.");
            return false;
        }
    }

    public async Task<bool> ShowThemeRestartPromptAsync(Window? owner = null)
    {
        var logger = LoggingService.Instance;
        logger?.Info("ShowThemeRestartPromptAsync called");
        
        try
        {
            bool result = false;
            
            // Ensure we're on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                logger?.Info("Creating theme restart dialog on UI thread");
                
                // Wait a bit to ensure UI is ready
                await Task.Delay(100);
                
                // Create a dialog window
                var dialog = new Window
                {
                    Width = 450,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Title = "Theme Changed - Restart Required",
                    CanResize = false,
                    Topmost = true,
                    ShowInTaskbar = true
                };
                
                // Set background color based on current theme
                try
                {
                    if (Avalonia.Application.Current?.Resources.TryGetResource("PanelBackground", 
                        Avalonia.Styling.ThemeVariant.Default, out var backgroundBrush) == true)
                    {
                        dialog.Background = backgroundBrush as Avalonia.Media.IBrush;
                    }
                    else
                    {
                        dialog.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(45, 45, 48));
                    }
                }
                catch
                {
                    dialog.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(45, 45, 48));
                }

                // Create content
                var content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 20,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                var messageText = new TextBlock
                {
                    Text = "The application needs to restart to apply the new theme completely.\n\nDo you want to restart now?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    FontSize = 14
                };
                
                // Set text color
                try
                {
                    if (Avalonia.Application.Current?.Resources.TryGetResource("TextColor", 
                        Avalonia.Styling.ThemeVariant.Default, out var textBrush) == true)
                    {
                        messageText.Foreground = textBrush as Avalonia.Media.IBrush;
                    }
                    else
                    {
                        messageText.Foreground = Avalonia.Media.Brushes.White;
                    }
                }
                catch
                {
                    messageText.Foreground = Avalonia.Media.Brushes.White;
                }

                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Spacing = 20
                };

                // Create buttons
                var restartButton = new Button
                {
                    Content = "Restart Now",
                    Width = 120,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                
                var laterButton = new Button
                {
                    Content = "Later",
                    Width = 120,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                restartButton.Click += (s, e) =>
                {
                    result = true;
                    dialog.Close();
                };

                laterButton.Click += (s, e) =>
                {
                    result = false;
                    dialog.Close();
                };

                buttonPanel.Children.Add(restartButton);
                buttonPanel.Children.Add(laterButton);

                content.Children.Add(messageText);
                content.Children.Add(buttonPanel);

                dialog.Content = content;

                if (owner != null)
                {
                    await dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.Show();
                    
                    // Wait for dialog to close
                    while (dialog.IsVisible)
                    {
                        await Task.Delay(100);
                    }
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            logger?.Error($"Error showing theme restart prompt: {ex.Message}");
            return false;
        }
    }
}
