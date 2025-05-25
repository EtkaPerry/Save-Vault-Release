using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Models;
using SaveVaultApp.Utilities;
using System.IO;
using System;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml; // Add this using statement for AvaloniaXamlLoader

namespace SaveVaultApp.Views;

public partial class OptionsWindow : Window
{    private Panel? _generalPanel;
    private Panel? _appearancePanel;
    private Panel? _storagePanel;
    private Panel? _updatesPanel;
    private Panel? _legalPanel;
    private Panel? _creditPanel;
    
    // Reference to the main view model for app refresh
    private MainWindowViewModel? _mainViewModel;
    
    public OptionsWindow()
    {
        InitializeComponent();

        // Set window startup location to center relative to owner
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        
        // Restore window size and position from settings
        var settings = Settings.Instance;
        if (settings != null)
        {
            if (!double.IsNaN(settings.OptionsWindowPositionX) && !double.IsNaN(settings.OptionsWindowPositionY))
            {
                // Ensure the position is on a visible screen
                var screens = Screens.All;
                var proposedPosition = new PixelPoint((int)settings.OptionsWindowPositionX, (int)settings.OptionsWindowPositionY);
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

            // Set window size if saved values are valid
            if (settings.OptionsWindowWidth > 0 && settings.OptionsWindowHeight > 0)
            {
                Width = settings.OptionsWindowWidth;
                Height = settings.OptionsWindowHeight;
            }

            if (settings.IsOptionsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        // Subscribe to window state changes
        PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(WindowState) && settings != null)
            {
                settings.IsOptionsMaximized = WindowState == WindowState.Maximized;
                settings.Save();
            }
        };

        // Set up window dragging
        var dragRegion = this.FindControl<Control>("DragRegion");
        if (dragRegion != null)
        {
            dragRegion.PointerPressed += DragRegion_PointerPressed;
        }
        
        // Set up close button
        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += CloseButton_Click;
        }          // Get panel references
        _generalPanel = this.FindControl<Panel>("GeneralPanel");
        _appearancePanel = this.FindControl<Panel>("AppearancePanel");
        _storagePanel = this.FindControl<Panel>("StoragePanel");
        _updatesPanel = this.FindControl<Panel>("UpdatesPanel");
        _legalPanel = this.FindControl<Panel>("LegalPanel");
        _creditPanel = this.FindControl<Panel>("CreditPanel");
        
        // Set up options list selection handling
        var optionsList = this.FindControl<ListBox>("OptionsList");
        if (optionsList != null)
        {
            optionsList.SelectionChanged += OptionsList_SelectionChanged;
            optionsList.SelectedIndex = 0; // Select General by default
        }
        
        // Set up reset cache button
        var resetCacheButton = this.FindControl<Button>("ResetCacheButton");
        if (resetCacheButton != null)
        {
            resetCacheButton.Click += ResetCacheButton_Click;
        }
          // Set up reset options button
        var resetOptionsButton = this.FindControl<Button>("ResetOptionsButton");
        if (resetOptionsButton != null)
        {
            resetOptionsButton.Click += ResetOptionsButton_Click;
        }
        
        // Set up legal document buttons
        var termsButton = this.FindControl<Button>("TermsOfServiceButton");
        var securityButton = this.FindControl<Button>("SecurityPolicyButton");
        var privacyButton = this.FindControl<Button>("PrivacyPolicyButton");
        
        if (termsButton != null)
        {
            termsButton.Click += (s, e) => LoadLegalDocument("TermsOfService");
        }
        
        if (securityButton != null)
        {
            securityButton.Click += (s, e) => LoadLegalDocument("SecurityPolicy");
        }
        
        if (privacyButton != null)
        {
            privacyButton.Click += (s, e) => LoadLegalDocument("PrivacyPolicy");
        }
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Set the main view model reference and initialize settings
    public void SetMainViewModel(MainWindowViewModel viewModel)
    {
        _mainViewModel = viewModel;
        
        // Create and set the options view model
        DataContext = new OptionsViewModel(viewModel.Settings, () =>
        {
            // This will be called when settings are saved
            viewModel.UpdateFromSettings();
        });
    }
    
    private void OptionsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            if (listBox.SelectedItem is ListBoxItem item)
            {                // Hide all panels first
                if (_generalPanel != null) _generalPanel.IsVisible = false;
                if (_appearancePanel != null) _appearancePanel.IsVisible = false;
                if (_storagePanel != null) _storagePanel.IsVisible = false;
                if (_updatesPanel != null) _updatesPanel.IsVisible = false;
                if (_legalPanel != null) _legalPanel.IsVisible = false;
                if (_creditPanel != null) _creditPanel.IsVisible = false;
                
                // Show the selected panel
                switch (item.Content as string)
                {
                    case "General":
                        if (_generalPanel != null) _generalPanel.IsVisible = true;
                        break;
                    case "Appearance":
                        if (_appearancePanel != null) _appearancePanel.IsVisible = true;
                        break;                    case "Storage":
                        if (_storagePanel != null) 
                        {
                            _storagePanel.IsVisible = true;
                            // Automatically calculate storage usage when panel is selected
                            if (DataContext is OptionsViewModel viewModel)
                            {
                                _ = viewModel.CalculateStorageUsageCommand.ExecuteAsync(null);
                            }
                        }
                        break;
                    case "Updates":
                        if (_updatesPanel != null) 
                        {
                            _updatesPanel.IsVisible = true;
                            // Refresh the update status when panel is selected
                            if (DataContext is OptionsViewModel viewModel)
                            {
                                ((ReactiveUI.IReactiveObject)viewModel).RaisePropertyChanged(
                                    new System.ComponentModel.PropertyChangedEventArgs(nameof(OptionsViewModel.LastUpdateCheck))
                                );
                            }                        }
                        break;                    case "Legal":
                        if (_legalPanel != null) 
                        {
                            _legalPanel.IsVisible = true;
                            // Load Terms of Service by default when Legal panel is opened
                            LoadLegalDocument("TermsOfService");
                            // Update the legal acceptance date to current date when the user views the legal documents
                            if (DataContext is OptionsViewModel viewModel)
                            {
                                viewModel.UpdateLegalAcceptanceDate();
                            }
                        }
                        break;
                    case "Credit":
                        if (_creditPanel != null) _creditPanel.IsVisible = true;
                        break;
                }
            }
        }
    }
    
    private void DragRegion_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
    
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveWindowPosition();
        this.Close();
    }
    
    // Save window position and size before closing
    private void SaveWindowPosition()
    {
        var settings = Settings.Instance;
        if (settings != null && WindowState != WindowState.Minimized)
        {
            // Only save position if window is not maximized
            if (WindowState == WindowState.Normal)
            {
                settings.OptionsWindowPositionX = Position.X;
                settings.OptionsWindowPositionY = Position.Y;
                settings.OptionsWindowWidth = Width;
                settings.OptionsWindowHeight = Height;
            }
            
            // Always save maximized state
            settings.IsOptionsMaximized = WindowState == WindowState.Maximized;
            settings.Save();
        }
    }

    private async void ResetCacheButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainViewModel != null)
        {            var messageBox = new Window
            {
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Confirm Reset Program Cache",
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Children =
                    {                        new TextBlock
                        {
                            Text = "This will completely reset the program cache including all detected games, save paths, backup history and search data. The system will re-scan all drives for applications. This might take a while. Continue?",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 0, 0, 20)
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Yes",
                                    Width = 60,
                                    Margin = new Avalonia.Thickness(0, 0, 10, 0)
                                },
                                new Button
                                {
                                    Content = "No",
                                    Width = 60
                                }
                            }
                        }
                    }
                }
            };
            
            var yesButton = (Button)((StackPanel)((StackPanel)messageBox.Content).Children[1]).Children[0];
            var noButton = (Button)((StackPanel)((StackPanel)messageBox.Content).Children[1]).Children[1];

            yesButton.Click += (s, e) => messageBox.Close(true);
            noButton.Click += (s, e) => messageBox.Close(false);

            var result = await messageBox.ShowDialog<bool>(this);

            if (result)
            {
                _mainViewModel.ResetCache();
                this.Close();
            }
        }
    }
    
    private async void ResetOptionsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainViewModel != null)
        {
            var messageBox = new Window
            {
                Width = 350,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Confirm Reset All Settings",
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "This will reset ALL settings to default values and restart the application. All custom paths, names, and preferences will be lost. Are you sure?",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 0, 0, 20)
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Yes",
                                    Width = 60,
                                    Margin = new Avalonia.Thickness(0, 0, 10, 0)
                                },
                                new Button
                                {
                                    Content = "No",
                                    Width = 60
                                }
                            }
                        }
                    }
                }
            };
            
            var yesButton = (Button)((StackPanel)((StackPanel)messageBox.Content).Children[1]).Children[0];
            var noButton = (Button)((StackPanel)((StackPanel)messageBox.Content).Children[1]).Children[1];

            yesButton.Click += (s, e) => messageBox.Close(true);
            noButton.Click += (s, e) => messageBox.Close(false);

            var result = await messageBox.ShowDialog<bool>(this);

            if (result)
            {
                // Reset all settings and close the app to restart fresh
                if (_mainViewModel.Settings != null)
                {                    try
                    {                        // Create a completely new settings object with default values
                        // This will automatically update the static instance
                        var freshSettings = new SaveVaultApp.Models.Settings();
                        
                        // Save the fresh settings to override the existing file
                        freshSettings.Save();
                        
                        // Let the user know we're closing
                        _mainViewModel.StatusMessage = "Settings reset. Closing application...";
                        
                        // Set a flag to indicate a controlled exit
                        _mainViewModel.IsExiting = true;
                        
                        // Close this window and request app to exit
                        this.Close();
                        
                        // Close the main window which should exit the app
                        if (_mainViewModel._mainWindow != null)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                                _mainViewModel._mainWindow.Close();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        // Show error message
                        _mainViewModel.StatusMessage = $"Error resetting settings: {ex.Message}";
                    }
                }
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        
        var settings = Settings.Instance;
        if (settings != null && WindowState != WindowState.Maximized)
        {
            settings.OptionsWindowWidth = Width;
            settings.OptionsWindowHeight = Height;
            settings.OptionsWindowPositionX = Position.X;
            settings.OptionsWindowPositionY = Position.Y;            settings.ForceSave(); // Use ForceSave for reliability
        }
    }    private void LoadLegalDocument(string documentType)
    {
        if (DataContext is OptionsViewModel viewModel)
        {
            viewModel.LoadLegalDocument(documentType);
            
            // Update button styles
            var termsButton = this.FindControl<Button>("TermsOfServiceButton");
            var securityButton = this.FindControl<Button>("SecurityPolicyButton");
            var privacyButton = this.FindControl<Button>("PrivacyPolicyButton");
            
            // Try to get the dynamic resources safely
            Avalonia.Media.IBrush? normalBackground = null;
            Avalonia.Media.IBrush? hoverBackground = null;
            
            try
            {
                if (this.TryFindResource("ListItemBackground", out var normalBg) && normalBg is Avalonia.Media.IBrush)
                    normalBackground = (Avalonia.Media.IBrush)normalBg;
                
                if (this.TryFindResource("ListItemBackgroundHover", out var hoverBg) && hoverBg is Avalonia.Media.IBrush)
                    hoverBackground = (Avalonia.Media.IBrush)hoverBg;
            }
            catch
            {
                // Fallback to default colors if dynamic resources fail
                normalBackground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#303030"));
                hoverBackground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#404040"));
            }
            
            // Use fallback colors if resources not found
            normalBackground ??= new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#303030"));
            hoverBackground ??= new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#404040"));
            
            // Reset all button backgrounds and update text colors
            if (termsButton != null)
            {
                termsButton.Background = normalBackground;
                termsButton.Foreground = this.FindResource("TextColor") as Avalonia.Media.IBrush ?? 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            }
            
            if (securityButton != null)
            {
                securityButton.Background = normalBackground;
                securityButton.Foreground = this.FindResource("TextColor") as Avalonia.Media.IBrush ?? 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            }
            
            if (privacyButton != null)
            {
                privacyButton.Background = normalBackground;
                privacyButton.Foreground = this.FindResource("TextColor") as Avalonia.Media.IBrush ?? 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            }
            
            // Highlight the selected button
            Button? selectedButton = documentType switch
            {
                "TermsOfService" => termsButton,
                "SecurityPolicy" => securityButton,
                "PrivacyPolicy" => privacyButton,
                _ => null
            };
            
            if (selectedButton != null)
            {
                selectedButton.Background = hoverBackground;
                selectedButton.Foreground = this.FindResource("TextColor") as Avalonia.Media.IBrush ?? 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            }
        }
    }
}