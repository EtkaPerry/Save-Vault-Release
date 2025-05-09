using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SaveVaultApp.Models;
using Avalonia.Controls.ApplicationLifetimes;
using System.Windows.Input;
using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;
using Avalonia.Media;
using SaveVaultApp.Services;
using Avalonia.Markup.Xaml;
using System.Threading;

namespace SaveVaultApp.Views;

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private TrayIcon? _trayIcon;
    private LogViewerWindow? _logViewer;
    
    public MainWindow()
    {
        InitializeComponent();
        
        _settings = Settings.Load();
        
        // Set window startup location
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        // Initialize logging system
        LoggingService.Instance.Info("Application started");
        
        // Register for key events
        KeyDown += OnKeyDown;
        
        // Restore window position and size
        if (!double.IsNaN(_settings.WindowPositionX) && !double.IsNaN(_settings.WindowPositionY))
        {
            // Ensure the position is on a visible screen
            var screens = Screens.All;
            var proposedPosition = new PixelPoint((int)_settings.WindowPositionX, (int)_settings.WindowPositionY);
            bool isOnScreen = false;

            foreach (var screen in screens)
            {
                if (screen.Bounds.Contains(proposedPosition))
                {
                    isOnScreen = true;
                    break;
                }
            }

            if (isOnScreen)
            {
                Position = proposedPosition;
            }
        }
        
        if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }
        
        if (_settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        
        // Attach event handlers to window control buttons
        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        var maximizeButton = this.FindControl<Button>("MaximizeButton");
        var closeButton = this.FindControl<Button>("CloseButton");
        var dragRegion = this.FindControl<Control>("DragRegion");
        var optionsMenuItem = this.FindControl<MenuItem>("OptionsMenuItem");
        var logoSettingsMenuItem = this.FindControl<MenuItem>("LogoSettingsMenuItem");
        var logoButton = this.FindControl<Button>("LogoButton");
        var homeSection = this.FindControl<Grid>("HomeSection");
        
        // Set up Logo button flyout width adjustment
        if (logoButton != null)
        {
            logoButton.AttachedToVisualTree += (s, e) => 
            {
                if (logoButton.Flyout is MenuFlyout menuFlyout && homeSection != null)
                {
                    // Listen for the flyout's opening event
                    menuFlyout.Opening += (sender, args) =>
                    {
                        // Set the width of all menu items to match the home section width
                        double homeSectionWidth = homeSection.Bounds.Width;
                        foreach (var item in menuFlyout.Items)
                        {
                            if (item is MenuItem menuItem)
                            {
                                menuItem.MinWidth = homeSectionWidth;
                            }
                        }
                    };
                }
            };
        }
        
        if (minimizeButton != null)
            minimizeButton.Click += MinimizeButton_Click;
        
        if (maximizeButton != null)
            maximizeButton.Click += MaximizeButton_Click;
        
        if (closeButton != null)
            closeButton.Click += CloseButton_Click;
            
        // Set up window dragging
        if (dragRegion != null)
        {
            dragRegion.PointerPressed += DragRegion_PointerPressed;
        }
        
        // Set up options menu item click
        if (optionsMenuItem != null)
        {
            optionsMenuItem.Click += OptionsMenuItem_Click;
        }
        
        // Set up logo settings menu item click
        if (logoSettingsMenuItem != null)
        {
            logoSettingsMenuItem.Click += OptionsMenuItem_Click;
        }

        // Subscribe to window state changes
        PropertyChanged += MainWindow_PropertyChanged;
        
        // Setup closing event to minimize to tray instead of closing
        Closing += MainWindow_Closing;
        
        // Initialize system tray icon
        InitializeSystemTray();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void InitializeSystemTray()
    {
        try
        {
            // Create the system tray icon
            _trayIcon = new TrayIcon();
            
            // Use the application's window icon (which is already in the window)
            _trayIcon.Icon = this.Icon;
            
            // Set tooltip text
            _trayIcon.ToolTipText = "Save Vault";
            
            // Set up the context menu
            NativeMenu menu = new NativeMenu();
            
            // Add "Open" menu item
            NativeMenuItem openItem = new NativeMenuItem("Open Save Vault");
            openItem.Click += (s, e) => ShowWindow();
            menu.Add(openItem);
            
            // Add separator
            menu.Add(new NativeMenuItemSeparator());
            
            // Add "Exit" menu item
            NativeMenuItem exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (s, e) => 
            {
                // Mark as exiting so the window actually closes
                var vm = DataContext as ViewModels.MainWindowViewModel;
                if (vm != null)
                {
                    vm.IsExiting = true;
                }
                
                // Dispose the tray icon
                if (_trayIcon != null)
                {
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                
                // Close the window and application
                Close();
                
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            };
            menu.Add(exitItem);
            
            // Assign menu to tray icon
            _trayIcon.Menu = menu;
            
            // Handle tray icon click to show window
            _trayIcon.Clicked += (s, e) => ShowWindow();
            
            // Make the tray icon visible
            _trayIcon.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize system tray: {ex.Message}");
        }
    }
    
    private void ShowWindow()
    {
        // Show the window
        Show();
        WindowState = WindowState.Normal;
        Activate();
        
        // Ensure it's brought to front
        Topmost = true;
        Topmost = false;
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // If this is a normal window close initiated by the user (not an application shutdown), 
        // hide the window instead of closing it
        var vm = DataContext as SaveVaultApp.ViewModels.MainWindowViewModel;
        if (vm != null && !vm.IsExiting)
        {
            e.Cancel = true; // Cancel the close
            Hide(); // Hide the window instead
            // Save settings when hiding to tray as an extra checkpoint
            _settings.ForceSave();
        }
        else
        {
            // Clean up tray icon when actually exiting
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            // Save settings on real exit
            _settings.ForceSave();
        }
    }    // Timer to debounce window resize events
    private System.Threading.Timer? _resizeDebounceTimer;
    private bool _isSavingWindowSize = false;
    
    private void MainWindow_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_isSavingWindowSize) return; // Prevent recursive saves
        
        if (e.Property.Name == nameof(WindowState))
        {
            _settings.IsMaximized = WindowState == WindowState.Maximized;
            
            if (!_settings.IsMaximized && Width > 100 && Height > 100)
            {
                // When exiting maximized state, immediately capture the restored size
                _settings.WindowPositionX = Position.X;
                _settings.WindowPositionY = Position.Y;
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                System.Diagnostics.Debug.WriteLine($"Exiting maximized: saving size {Width}x{Height}");
            }
            
            SaveSettingsWithDebounce();
        }
        else if ((e.Property.Name == nameof(Position) || e.Property.Name == nameof(Width) || e.Property.Name == nameof(Height))
                && WindowState != WindowState.Maximized && Width > 100 && Height > 100)
        {
            SaveSettingsWithDebounce();
        }
    }
      private void SaveSettingsWithDebounce()
    {
        // Cancel existing timer
        _resizeDebounceTimer?.Dispose();
        
        // Store current values immediately to avoid race conditions
        double currentWidth = Width;
        double currentHeight = Height;
        double currentPosX = Position.X;
        double currentPosY = Position.Y;
        
        // Create new timer with 300ms delay (reduced from 500ms)
        _resizeDebounceTimer = new System.Threading.Timer(_ => 
        {
            // Invoke on UI thread
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
            {
                if (WindowState != WindowState.Maximized && Width > 100 && Height > 100)
                {
                    try
                    {
                        _isSavingWindowSize = true;
                        
                        // Log before saving
                        System.Diagnostics.Debug.WriteLine($"Saving window size: {currentWidth}x{currentHeight} at position {currentPosX},{currentPosY}");
                        LoggingService.Instance.Debug($"Saving window size: {currentWidth}x{currentHeight}");
                        
                        // Update settings with captured values
                        _settings.WindowPositionX = currentPosX;
                        _settings.WindowPositionY = currentPosY;
                        _settings.WindowWidth = currentWidth;
                        _settings.WindowHeight = currentHeight;
                        
                        // Save settings with force to ensure it works
                        _settings.ForceSave();
                        
                        // Log after saving
                        LoggingService.Instance.Debug($"Window size saved successfully: {_settings.WindowWidth}x{_settings.WindowHeight}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Failed to save window size: {ex.Message}");
                    }
                    finally
                    {
                        _isSavingWindowSize = false;
                    }
                }
            });
        }, null, 300, System.Threading.Timeout.Infinite);
    }
    
    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }
    
    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        this.WindowState = this.WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }
    
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        // Minimize to tray instead of closing
        Hide();
        // Save settings when minimizing to tray as an extra checkpoint
        _settings.ForceSave();
    }
    
    private void DragRegion_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
    
    private void OptionsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var optionsWindow = new OptionsWindow();
        
        // Get the view model from DataContext and pass it to OptionsWindow
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            optionsWindow.SetMainViewModel(viewModel);
        }
        
        optionsWindow.ShowDialog(this);
    }

    private void OnNameEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            if (e.Key == Key.Enter)
            {
                viewModel.SaveAppName();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.CancelAppNameEdit();
                e.Handled = true;
            }
        }
    }
    
    private void OnSavePathEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            if (e.Key == Key.Enter)
            {
                viewModel.SaveSavePath();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.CancelSavePathEdit();
                e.Handled = true;
            }
        }
    }

    private void NextSaveButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.IsHoveringNextSave = true;
        }
    }

    private void NextSaveButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.IsHoveringNextSave = false;
        }
    }    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowLogViewer();
        }

        // Shift+` (backtick/grave accent key under Esc) to toggle log viewer
        if (e.Key == Key.OemTilde && e.KeyModifiers == KeyModifiers.Shift)
        {
            ToggleLogViewer();
            e.Handled = true;
        }
    }
    
    private void ToggleLogViewer()
    {
        if (_logViewer == null || !_logViewer.IsVisible)
        {
            ShowLogViewer();
        }
        else
        {
            _logViewer.Hide();
        }
    }
    
    private void ShowLogViewer()
    {
        if (_logViewer == null)
        {
            _logViewer = new LogViewerWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 900,
                Height = 500
            };
            
            // When closed, set to null so we recreate it next time
            _logViewer.Closed += (s, e) => _logViewer = null;
        }
        
        if (!_logViewer.IsVisible)
        {
            // Show as a dialog but don't block
            _logViewer.Show(this);
        }
        
        // Bring to front
        _logViewer.Activate();
    }
}