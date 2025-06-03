using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace SaveVaultApp.Services;

public class SingleInstanceService
{
    private static SingleInstanceService? _instance;
    public static SingleInstanceService Instance => _instance ??= new SingleInstanceService();
    
    private static EventWaitHandle? _shutdownEvent;
    private static CancellationTokenSource? _cancellationTokenSource;
    private Window? _pendingDialog;

    private SingleInstanceService() 
    {
        // Initialize shutdown event monitoring for the first instance
        InitializeShutdownMonitoring();
    }    private void InitializeShutdownMonitoring()
    {
        var logger = LoggingService.Instance;
        
        // Only monitor shutdown signals if this is the first instance (not second instance)
        bool isSecondInstance = Environment.GetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE") == "true";
        if (isSecondInstance)
        {
            logger?.Info("Second instance - not monitoring shutdown signals");
            return;
        }
        
        // Clean up existing monitoring if it exists
        try
        {
            _cancellationTokenSource?.Cancel();
            _shutdownEvent?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            logger?.Debug($"Error cleaning up existing shutdown monitoring: {ex.Message}");
        }
        
        try
        {
            // Create or open the shutdown event
            const string eventName = "SaveVaultApp_Shutdown_Event";
            _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            _cancellationTokenSource = new CancellationTokenSource();
            
            logger?.Info("First instance - starting shutdown signal monitoring");
            
            // Monitor for shutdown signals in background task
            Task.Run(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // Wait for shutdown signal with timeout
                        bool signaled = _shutdownEvent.WaitOne(1000); // Check every second
                        if (signaled)
                        {
                            logger?.Info("Shutdown signal received - initiating graceful shutdown");
                            await InitiateGracefulShutdown();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Error($"Error in shutdown monitoring: {ex.Message}");
                }
            }, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            logger?.Warning($"Could not initialize shutdown monitoring: {ex.Message}");
        }
    }
    
    private async Task InitiateGracefulShutdown()
    {
        var logger = LoggingService.Instance;
        logger?.Info("Initiating graceful shutdown of first instance");
        
        try
        {
            // Shutdown on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    logger?.Info("Shutting down application from shutdown signal");
                    desktop.Shutdown();
                }
            });
        }
        catch (Exception ex)
        {
            logger?.Error($"Error during graceful shutdown: {ex.Message}");
            // Force exit if graceful shutdown fails
            Environment.Exit(0);
        }
    }    public async Task<bool> CheckForExistingInstanceAsync(Window? owner = null)
    {
        var logger = LoggingService.Instance;
        
        // Check if this is a second instance
        bool isSecondInstance = Environment.GetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE") == "true";
        
        logger?.Info($"CheckForExistingInstanceAsync called. IsSecondInstance: {isSecondInstance}");
        
        if (!isSecondInstance)
        {
            logger?.Info("This is the first instance, continuing normally");
            return false; // This is the first instance, continue normally
        }

        logger?.Info("Second instance detected, showing dialog");
        
        try
        {
            // Show dialog with options and keep it open during transition
            var (shouldTerminate, dialogWindow) = await ShowInstanceDialogWithRefAsync(owner);
            
            logger?.Info($"Dialog result: {shouldTerminate}");
            
            if (shouldTerminate)
            {
                // User chose to terminate existing instance
                logger?.Info("User chose to terminate existing instance");
                
                // Keep dialog open while we handle the transition
                await UpdateDialogStatus(dialogWindow, "Terminating existing instance...");
                
                bool terminationSuccessful = await RequestFirstInstanceShutdown();
                  if (terminationSuccessful)
                {
                    // Reinitialize this instance as the new first instance
                    ReinitializeAsFirstInstance();
                    logger?.Info("Successfully became the new first instance");
                    
                    // Update dialog to show we're starting new instance
                    await UpdateDialogStatus(dialogWindow, "Starting new instance...");
                    
                    // Store dialog reference for later closing
                    _pendingDialog = dialogWindow;
                    
                    return false; // Continue with this instance
                }
                else
                {
                    logger?.Warning("Failed to terminate existing instance, closing this instance");
                    await UpdateDialogStatus(dialogWindow, "Failed to terminate existing instance");
                    await Task.Delay(2000); // Show error for 2 seconds
                    dialogWindow?.Close();
                    return true; // Signal to exit
                }
            }
            else
            {
                // User chose to close this instance
                logger?.Info("User chose to close this instance");
                dialogWindow?.Close();
                return true; // Signal to exit
            }
        }
        catch (Exception ex)
        {
            // If we can't show the dialog (e.g., headless environment), default to closing this instance
            logger?.Warning($"Could not show instance dialog: {ex.Message}. Closing this instance.");
            return true; // Signal to exit
        }
    }

    private async Task<bool> ShowInstanceDialogAsync(Window? owner)
    {
        bool? result = null;
        var logger = LoggingService.Instance;
        
        logger?.Info("ShowInstanceDialogAsync called");
        
        try
        {
            // Ensure we're on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                logger?.Info("Creating dialog window on UI thread");
                
                // Wait a tiny bit more to ensure Avalonia is fully ready
                await Task.Delay(100);
                  // Create a simple dialog window
                var dialog = new Window
                {
                    Width = 420,
                    Height = 280,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Title = "Save Vault Already Running",
                    CanResize = false,
                    Topmost = true,
                    ShowInTaskbar = true
                };
                
                // Create buttons
                var terminateButton = new Button
                {
                    Content = "Terminate existing and start new",
                    Width = 200,
                    Margin = new Avalonia.Thickness(0, 0, 5, 0)
                };
                
                var closeButton = new Button
                {
                    Content = "Close this instance",
                    Width = 140
                };                // Create logo image
                Control? logoImage = null;
                try
                {
                    logger?.Info("Attempting to load logo for dialog...");                    // Try multiple possible paths using the avares:// scheme for embedded resources
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
                                Width = 64,
                                Height = 64,
                                Margin = new Avalonia.Thickness(0, 0, 0, 10),
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
                            Text = "🔒 Save Vault",
                            FontSize = 24,
                            FontWeight = Avalonia.Media.FontWeight.Bold,
                            Margin = new Avalonia.Thickness(0, 0, 0, 10),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            TextAlignment = Avalonia.Media.TextAlignment.Center
                        } as Control;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Could not load logo for dialog: {ex.Message}");
                    // Create a placeholder with text if logo fails to load
                    logoImage = new TextBlock
                    {
                        Text = "🔒 Save Vault",
                        FontSize = 24,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Margin = new Avalonia.Thickness(0, 0, 0, 10),
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
                  contentChildren.Add(new TextBlock
                {
                    Text = "Save Vault is already running. What would you like to do?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 20),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center
                });

                contentChildren.Add(new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        terminateButton,
                        closeButton
                    }
                });                var stackPanel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20)
                };

                // Add children to the stack panel
                foreach (var child in contentChildren)
                {
                    stackPanel.Children.Add(child);
                }

                dialog.Content = stackPanel;
                
                // Set up event handlers
                terminateButton.Click += (s, e) =>
                {
                    logger?.Info("Terminate button clicked");
                    result = true;
                    dialog.Close();
                };
                
                closeButton.Click += (s, e) =>
                {
                    logger?.Info("Close button clicked");
                    result = false;
                    dialog.Close();
                };

                // Handle window closing without button click (default to close this instance)
                dialog.Closing += (s, e) =>
                {
                    if (result == null)
                    {
                        logger?.Info("Dialog closed without button click, defaulting to close this instance");
                        result = false;
                    }
                };
                
                // Show dialog and wait for it to close
                logger?.Info("Showing dialog");
                dialog.Show();
                dialog.Activate(); // Try to bring to front
                logger?.Info("Dialog shown and activated");
                
                // Wait for dialog to close
                while (dialog.IsVisible && result == null)
                {
                    await Task.Delay(100);
                }
                
                logger?.Info($"Dialog finished with result: {result}");
            });
        }
        catch (Exception ex)
        {
            logger?.Error($"Error showing Avalonia dialog: {ex.Message}");
            
            // Fallback to MsBox.Avalonia message box
            try
            {
                logger?.Info("Attempting MsBox.Avalonia fallback");
                
                var box = MessageBoxManager
                    .GetMessageBoxStandard("Save Vault Already Running", 
                        "Save Vault is already running. Do you want to terminate the existing instance and start new?",
                        ButtonEnum.YesNo);
                
                var msgResult = await box.ShowAsync();
                result = msgResult == ButtonResult.Yes;
                logger?.Info($"MsBox fallback result: {result}");
            }
            catch (Exception msBoxEx)
            {
                logger?.Error($"MsBox fallback failed: {msBoxEx.Message}");
                
                // Final fallback to console input if we're in a console environment
                try
                {
                    logger?.Info("Attempting console fallback");
                    Console.WriteLine("Save Vault is already running. What would you like to do?");
                    Console.WriteLine("1. Terminate existing and start new");
                    Console.WriteLine("2. Close this instance");
                    Console.Write("Enter your choice (1 or 2): ");
                    
                    string? input = Console.ReadLine();
                    result = input?.Trim() == "1";
                    logger?.Info($"Console fallback result: {result}");
                }
                catch (Exception consoleEx)
                {
                    logger?.Error($"Console fallback also failed: {consoleEx.Message}");
                    result = false; // Default to closing this instance
                }
            }
        }
          return result ?? false;
    }

    private async Task<(bool shouldTerminate, Window? dialogWindow)> ShowInstanceDialogWithRefAsync(Window? owner)
    {
        bool? result = null;
        Window? dialogWindow = null;
        var logger = LoggingService.Instance;
        
        logger?.Info("ShowInstanceDialogWithRefAsync called");
        
        try
        {
            // Ensure we're on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                logger?.Info("Creating dialog window on UI thread");
                
                // Wait a tiny bit more to ensure Avalonia is fully ready
                await Task.Delay(100);                // Create a modern looking dialog window
                dialogWindow = new Window
                {
                    Width = 480,
                    Height = 380, // Increased height to ensure all content is visible
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Title = "Save Vault Already Running",
                    CanResize = false,
                    Topmost = true,
                    ShowInTaskbar = true,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E1E"))
                };
                
                // Create an image for the logo
                Control logoControl;
                try
                {                    // Try to load the logo from assets with improved error handling
                    string[] possiblePaths = new[]
                    {
                        "avares://Save Vault/Assets/Logo.png",
                        "avares://Save Vault/Assets/logo.png", 
                        "avares://Save Vault/Assets/logo.ico",
                        "avares://SaveVaultApp/Assets/Logo.png",
                        "avares://SaveVaultApp/Assets/logo.png", 
                        "avares://SaveVaultApp/Assets/logo.ico"
                    };
                    
                    Bitmap? bitmap = null;
                    Exception? lastException = null;
                    
                    // Try each path until one works
                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            var assets = AssetLoader.Open(new Uri(path));
                            bitmap = new Bitmap(assets);
                            logger?.Info($"Successfully loaded logo from {path}");
                            break; // Exit the loop if we successfully loaded the image
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            logger?.Warning($"Failed to load logo from {path}: {ex.Message}");
                            // Continue to try the next path
                        }
                    }
                    
                    if (bitmap != null)
                    {
                        logoControl = new Image
                        {
                            Source = bitmap,                            Width = 128,
                            Height = 128,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Margin = new Avalonia.Thickness(0, 0, 0, 15)
                        };
                    }
                    else
                    {
                        // If all paths failed, throw the last exception to trigger the fallback
                        throw lastException ?? new Exception("Failed to load logo from any path");
                    }
                }
                catch (Exception ex)
                {
                    // Log the failure
                    logger?.Error($"Failed to load logo: {ex.Message}");
                    
                    // Create a text logo as a fallback instead of emoji
                    logoControl = new TextBlock
                    {
                        Text = "SAVE VAULT", // Text logo instead of emoji
                        FontSize = 24,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        TextAlignment = Avalonia.Media.TextAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 0, 0, 15),
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF9D45")) // Orange color to match app theme
                    };
                }// Create main title text with better styling
                var titleText = new TextBlock
                {
                    Text = "Save Vault",
                    Margin = new Avalonia.Thickness(0, 0, 0, 5),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    FontSize = 22,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF9D45")) // Match the orange theme
                };
                
                // Create status text block for updates with better styling
                var statusText = new TextBlock
                {
                    Text = "The application is already running.\nPlease choose an option:",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 20),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    Name = "StatusText",
                    FontSize = 14,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF"))
                };// Create buttons with improved styling
                var terminateButton = new Button
                {
                    Content = "Restart application",
                    Width = 280,
                    Height = 40,
                    Name = "TerminateButton",
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF9D45")), // Orange color to match app theme
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")),
                    Margin = new Avalonia.Thickness(0, 0, 0, 10), // Bottom margin for space between buttons
                    Padding = new Avalonia.Thickness(10, 8, 10, 8),
                    CornerRadius = new Avalonia.CornerRadius(4)
                };
                
                var closeButton = new Button
                {
                    Content = "Close this instance",
                    Width = 280,
                    Height = 40,
                    Name = "CloseButton",
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4B4B4B")),
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")),
                    Padding = new Avalonia.Thickness(10, 8, 10, 8),
                    CornerRadius = new Avalonia.CornerRadius(4)
                };
                
                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Vertical, // Changed to vertical orientation
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Spacing = 10,
                    Name = "ButtonPanel",
                    Children =
                    {
                        terminateButton,
                        closeButton
                    }
                };
                
                // Create content with better layout                // Create main container
                var mainPanel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(25),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Spacing = 5
                };                  // Add children to main panel
                mainPanel.Children.Add(logoControl);
                mainPanel.Children.Add(titleText);
                mainPanel.Children.Add(statusText);
                mainPanel.Children.Add(buttonPanel);
                
                // Set as window content
                dialogWindow.Content = mainPanel;
                
                // Set up event handlers
                terminateButton.Click += (s, e) =>
                {
                    logger?.Info("Terminate button clicked");
                    result = true;
                    // Don't close dialog immediately - we'll handle this in the calling method
                };
                
                closeButton.Click += (s, e) =>
                {
                    logger?.Info("Close button clicked");
                    result = false;
                    dialogWindow.Close();
                };

                // Handle window closing without button click (default to close this instance)
                dialogWindow.Closing += (s, e) =>
                {
                    if (result == null)
                    {
                        logger?.Info("Dialog closed without button click, defaulting to close this instance");
                        result = false;
                    }
                };
                
                // Show dialog and wait for it to close
                logger?.Info("Showing dialog");
                dialogWindow.Show();
                dialogWindow.Activate(); // Try to bring to front
                logger?.Info("Dialog shown and activated");
                
                // Wait for user to make a choice
                while (dialogWindow.IsVisible && result == null)
                {
                    await Task.Delay(100);
                }
                
                logger?.Info($"Dialog user choice made: {result}");
            });
        }
        catch (Exception ex)
        {
            logger?.Error($"Error showing Avalonia dialog: {ex.Message}");
            
            // Fallback to simple dialog without reference
            result = await ShowInstanceDialogAsync(owner);
        }
        
        return (result ?? false, dialogWindow);
    }    private async Task UpdateDialogStatus(Window? dialogWindow, string statusMessage)
    {
        if (dialogWindow == null || !dialogWindow.IsVisible) return;
        
        var logger = LoggingService.Instance;
        logger?.Info($"Updating dialog status: {statusMessage}");
        
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (dialogWindow.Content is StackPanel mainPanel)
                {
                    // Find the status text block (should be the third TextBlock - after logo and title)
                    var textBlocks = mainPanel.Children.OfType<TextBlock>().ToList();
                    var statusText = textBlocks.Count >= 2 ? textBlocks[1] : textBlocks.FirstOrDefault();
                    
                    if (statusText != null)
                    {
                        statusText.Text = statusMessage;
                        statusText.FontSize = 12; // Smaller text for status messages
                        statusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC")); // Slightly dimmed
                    }
                    
                    // Hide buttons when showing status updates
                    var buttonPanel = mainPanel.Children.OfType<StackPanel>().FirstOrDefault(sp => sp.Name == "ButtonPanel");
                    if (buttonPanel != null)
                    {
                        buttonPanel.IsVisible = false;
                    }
                }
            });
            
            // Give user time to read the status message
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            logger?.Error($"Error updating dialog status: {ex.Message}");
        }
    }

    public void CloseTransitionDialog()
    {
        var logger = LoggingService.Instance;
        logger?.Info("CloseTransitionDialog called");
        
        try
        {
            if (_pendingDialog != null)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _pendingDialog?.Close();
                    _pendingDialog = null;
                    logger?.Info("Transition dialog closed successfully");
                });
            }
        }
        catch (Exception ex)
        {
            logger?.Error($"Error closing transition dialog: {ex.Message}");
        }
    }
      private async Task<bool> RequestFirstInstanceShutdown()
    {
        var logger = LoggingService.Instance;
        logger?.Info("Requesting first instance shutdown via signal");
        
        try
        {
            // Create or open the shutdown event
            const string eventName = "SaveVaultApp_Shutdown_Event";
            using var shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            
            // Signal the first instance to shut down
            logger?.Info("Sending shutdown signal to first instance");
            shutdownEvent.Set();
            
            // Wait for the first instance to release the mutex
            logger?.Info("Waiting for first instance to exit and release mutex");
            
            // Try to acquire the mutex to verify the first instance has exited
            const string mutexName = "SaveVaultApp_SingleInstance_Mutex";
            bool mutexAcquired = false;
            
            // Wait up to 15 seconds for the first instance to shut down (increased timeout)
            for (int i = 0; i < 150; i++) // 150 * 100ms = 15 seconds
            {
                try
                {
                    using var testMutex = new Mutex(false, mutexName);
                    if (testMutex.WaitOne(0)) // Try to acquire immediately
                    {
                        logger?.Info("Successfully acquired mutex - first instance has exited");
                        testMutex.ReleaseMutex();
                        mutexAcquired = true;
                        break;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // This actually means the first instance terminated unexpectedly 
                    // but we can still continue - the mutex is now available
                    logger?.Info("Mutex was abandoned - first instance terminated, continuing");
                    mutexAcquired = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger?.Debug($"Mutex test iteration {i}: {ex.Message}");
                }
                
                await Task.Delay(100); // Wait 100ms before trying again
                
                // Signal again every 2 seconds in case the first signal was missed
                if (i > 0 && i % 20 == 0)
                {
                    logger?.Debug("Sending additional shutdown signal");
                    shutdownEvent.Set();
                }
            }
            
            if (mutexAcquired)
            {
                logger?.Info("First instance shutdown completed successfully");
                
                // Additional wait to ensure cleanup is complete
                await Task.Delay(500);
                
                return true;
            }
            else
            {
                logger?.Warning("Timeout waiting for first instance to exit");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger?.Error($"Error requesting first instance shutdown: {ex.Message}");
            return false;
        }
    }
    
    public void ReinitializeAsFirstInstance()
    {
        var logger = LoggingService.Instance;
        logger?.Info("Reinitializing as first instance");
        
        // Clear the second instance flag
        Environment.SetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE", null);
        
        // Re-initialize shutdown monitoring
        InitializeShutdownMonitoring();
    }
    
    public static void Cleanup()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _shutdownEvent?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            var logger = LoggingService.Instance;
            logger?.Warning($"Error during SingleInstanceService cleanup: {ex.Message}");
        }
    }
}