using Avalonia.Controls;
using SaveVaultApp.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveVaultApp.Services;

/// <summary>
/// Service that provides UI integration points for extensions
/// </summary>
public class ExtensionUIService
{
    private static readonly Lazy<ExtensionUIService> _instance = new(() => new ExtensionUIService());
    public static ExtensionUIService Instance => _instance.Value;

    private readonly Dictionary<string, List<ExtensionMenuItem>> _extensionMenuItems = new();
    private readonly Dictionary<string, List<ExtensionButton>> _extensionButtons = new();
    private readonly Dictionary<string, List<ExtensionWindow>> _extensionWindows = new();
    
    private MainWindow? _mainWindow;

    private ExtensionUIService()
    {
    }

    /// <summary>
    /// Initialize with the main window reference
    /// </summary>
    public void Initialize(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        LoggingService.Instance.Info("ExtensionUIService initialized with main window reference");
    }

    /// <summary>
    /// Add a menu item to the specified menu
    /// </summary>
    public bool AddMenuItem(string extensionId, string menuName, string itemText, string? tooltip = null)
    {
        try
        {
            if (_mainWindow == null)
            {
                LoggingService.Instance.Error($"Cannot add menu item '{itemText}' for extension '{extensionId}': MainWindow not initialized");
                LoggingService.Instance.Error("Extensions should be loaded after the MainWindow is created and ExtensionUIService is initialized");
                return false;
            }

            var menuItem = new ExtensionMenuItem
            {
                ExtensionId = extensionId,
                MenuName = menuName,
                Text = itemText,
                Tooltip = tooltip
            };

            // Add to our tracking
            if (!_extensionMenuItems.ContainsKey(extensionId))
                _extensionMenuItems[extensionId] = new List<ExtensionMenuItem>();
            
            _extensionMenuItems[extensionId].Add(menuItem);

            // Add to actual UI
            AddMenuItemToUI(menuItem);
            
            LoggingService.Instance.Info($"Added menu item '{itemText}' to '{menuName}' for extension '{extensionId}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add menu item: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add a button to the toolbar or status bar
    /// </summary>
    public bool AddButton(string extensionId, string location, string buttonText, string callbackFunction, string? tooltip = null)
    {
        try
        {
            var button = new ExtensionButton
            {
                ExtensionId = extensionId,
                Location = location,
                Text = buttonText,
                CallbackFunction = callbackFunction,
                Tooltip = tooltip
            };

            if (!_extensionButtons.ContainsKey(extensionId))
                _extensionButtons[extensionId] = new List<ExtensionButton>();
            
            _extensionButtons[extensionId].Add(button);

            // Add to actual UI
            AddButtonToUI(button);
            
            LoggingService.Instance.Info($"Added button '{buttonText}' to '{location}' for extension '{extensionId}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add button: {ex.Message}");
            return false;
        }
    }    /// <summary>
    /// Create a new window for the extension
    /// </summary>
    public bool CreateWindow(string extensionId, string windowTitle, int width = 400, int height = 300)
    {
        try
        {
            // Create the actual Avalonia window
            var windowElement = new Window
            {
                Title = windowTitle,
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.Transparent,
                CanResize = true
            };            // Set the background to use the current theme
            try
            {
                if (Avalonia.Application.Current?.Resources.TryGetResource("PanelBackground", 
                    Avalonia.Styling.ThemeVariant.Default, out var backgroundBrush) == true)
                {
                    windowElement.Background = backgroundBrush as Avalonia.Media.IBrush;
                }
            }
            catch
            {
                // Fallback to a default background if theme resource isn't available
                windowElement.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(45, 45, 48));
            }

            // Create content for the window
            var content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15
            };

            windowElement.Content = content;

            var extensionWindow = new ExtensionWindow
            {
                ExtensionId = extensionId,
                Title = windowTitle,
                Width = width,
                Height = height,
                WindowElement = windowElement,
                ContentPanel = content
            };

            if (!_extensionWindows.ContainsKey(extensionId))
                _extensionWindows[extensionId] = new List<ExtensionWindow>();
            
            _extensionWindows[extensionId].Add(extensionWindow);

            // Show the window
            if (_mainWindow != null)
            {
                windowElement.Show(_mainWindow);
            }
            else
            {
                windowElement.Show();
            }
            
            // Notify UITranslationService about the new window so it can apply translations  
            try
            {
                UITranslationService.Instance.TrackNewWindow(windowElement);
                LoggingService.Instance.Info($"Registered new extension window with UITranslationService");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning($"Failed to register window with UITranslationService: {ex.Message}");
            }
            
            LoggingService.Instance.Info($"Created and showed window '{windowTitle}' for extension '{extensionId}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to create window: {ex.Message}");
            return false;
        }
    }

    public bool AddLabel(string extensionId, string windowTitle, string text)
    {
        var window = _extensionWindows.GetValueOrDefault(extensionId)?.FirstOrDefault(w => w.Title == windowTitle);
        if (window?.ContentPanel == null) return false;

        var label = new TextBlock
        {
            Text = text,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 5)
        };
        
        // Set text color from theme
        try
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("TextColor", 
                Avalonia.Styling.ThemeVariant.Default, out var textBrush) == true)
            {
                label.Foreground = textBrush as Avalonia.Media.IBrush;
            }
        }
        catch
        {
            label.Foreground = Avalonia.Media.Brushes.White;
        }
        
        window.ContentPanel.Children.Add(label);
        return true;
    }

    public bool AddWindowButton(string extensionId, string windowTitle, string text, string callbackFunction)
    {
        var window = _extensionWindows.GetValueOrDefault(extensionId)?.FirstOrDefault(w => w.Title == windowTitle);
        if (window?.ContentPanel == null) return false;

        var button = new Button
        {
            Content = text,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Margin = new Avalonia.Thickness(0, 5)
        };
        
        button.Click += (s, e) => 
        {
            LuaEngine.Instance.TriggerExtensionCallback(extensionId, callbackFunction);
        };
        
        window.ContentPanel.Children.Add(button);
        return true;
    }

    public bool AddTextBox(string extensionId, string windowTitle, string name, string placeholder = "")
    {
        var window = _extensionWindows.GetValueOrDefault(extensionId)?.FirstOrDefault(w => w.Title == windowTitle);
        if (window?.ContentPanel == null) return false;

        var textBox = new TextBox
        {
            Watermark = placeholder,
            Margin = new Avalonia.Thickness(0, 5)
        };
        
        window.Controls[name] = textBox;
        window.ContentPanel.Children.Add(textBox);
        return true;
    }

    public string GetControlValue(string extensionId, string windowTitle, string controlName)
    {
        var window = _extensionWindows.GetValueOrDefault(extensionId)?.FirstOrDefault(w => w.Title == windowTitle);
        if (window?.Controls.TryGetValue(controlName, out var control) == true)
        {
            if (control is TextBox textBox)
            {
                return textBox.Text ?? "";
            }
        }
        return "";
    }

    /// <summary>
    /// Remove all UI elements for an extension
    /// </summary>
    public void RemoveExtensionUI(string extensionId)
    {
        try
        {
            // Remove menu items
            if (_extensionMenuItems.ContainsKey(extensionId))
            {
                foreach (var menuItem in _extensionMenuItems[extensionId])
                {
                    RemoveMenuItemFromUI(menuItem);
                }
                _extensionMenuItems.Remove(extensionId);
            }

            // Remove buttons
            if (_extensionButtons.ContainsKey(extensionId))
            {
                foreach (var button in _extensionButtons[extensionId])
                {
                    RemoveButtonFromUI(button);
                }
                _extensionButtons.Remove(extensionId);
            }

            // Close and remove windows
            if (_extensionWindows.ContainsKey(extensionId))
            {
                foreach (var window in _extensionWindows[extensionId])
                {
                    window.Close();
                }
                _extensionWindows.Remove(extensionId);
            }

            LoggingService.Instance.Info($"Removed all UI elements for extension '{extensionId}'");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to remove extension UI: {ex.Message}");
        }
    }

    private void AddMenuItemToUI(ExtensionMenuItem menuItem)
    {
        if (_mainWindow == null) return;

        try
        {
            // Find the target menu
            var menu = FindMenuByName(menuItem.MenuName);
            if (menu != null)
            {
                var newMenuItem = new MenuItem
                {
                    Header = menuItem.Text
                };

                if (!string.IsNullOrEmpty(menuItem.Tooltip))
                {
                    ToolTip.SetTip(newMenuItem, menuItem.Tooltip);
                }

                // Store extension ID for later removal
                newMenuItem.Tag = menuItem.ExtensionId;

                // Add click handler that triggers Lua callback
                newMenuItem.Click += (s, e) => 
                {
                    LuaEngine.Instance.TriggerExtensionCallback(menuItem.ExtensionId, "onMenuItemClick", menuItem.Text);
                };

                menu.Items.Add(newMenuItem);
                menuItem.UIElement = newMenuItem;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add menu item to UI: {ex.Message}");
        }
    }

    private void RemoveMenuItemFromUI(ExtensionMenuItem menuItem)
    {
        if (menuItem.UIElement != null && _mainWindow != null)
        {
            try
            {
                var menu = FindMenuByName(menuItem.MenuName);
                if (menu != null && menu.Items.Contains(menuItem.UIElement))
                {
                    menu.Items.Remove(menuItem.UIElement);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Failed to remove menu item from UI: {ex.Message}");
            }
        }
    }

    private void AddButtonToUI(ExtensionButton button)
    {
        if (_mainWindow == null)
        {
            LoggingService.Instance.Warning($"Cannot add button '{button.Text}': MainWindow not initialized");
            return;
        }

        try
        {
            var host = _mainWindow.FindControl<StackPanel>("ExtensionButtonHost");
            if (host == null)
            {
                LoggingService.Instance.Warning("ExtensionButtonHost panel not found; cannot add extension button");
                return;
            }

            var uiButton = new Button
            {
                Content = button.Text,
                Tag = button.ExtensionId,
                Margin = new Avalonia.Thickness(4, 0, 0, 0),
                Padding = new Avalonia.Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(button.Tooltip))
                ToolTip.SetTip(uiButton, button.Tooltip);

            // Capture locals so the click handler doesn't depend on mutable state.
            var extensionId = button.ExtensionId;
            var callback = button.CallbackFunction;
            var text = button.Text;
            uiButton.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(callback))
                    LuaEngine.Instance.TriggerExtensionCallback(extensionId, callback, text);
            };

            host.Children.Add(uiButton);
            button.UIElement = uiButton;
            LoggingService.Instance.Info($"Added toolbar button '{button.Text}' for extension '{button.ExtensionId}'");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add button to UI: {ex.Message}");
        }
    }

    private void RemoveButtonFromUI(ExtensionButton button)
    {
        if (button.UIElement == null || _mainWindow == null)
            return;

        try
        {
            var host = _mainWindow.FindControl<StackPanel>("ExtensionButtonHost");
            if (host != null && host.Children.Contains(button.UIElement))
                host.Children.Remove(button.UIElement);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to remove button from UI: {ex.Message}");
        }
    }

    private MenuItem? FindMenuByName(string menuName)
    {
        if (_mainWindow == null) 
        {
            LoggingService.Instance.Warning($"Cannot find menu '{menuName}': MainWindow is null");
            return null;
        }

        try
        {
            LoggingService.Instance.Info($"Searching for menu '{menuName}'");
            
            // Find the main menu
            var menu = _mainWindow.FindControl<Menu>("MainMenu");
            LoggingService.Instance.Info($"Found MainMenu control: {menu != null}");
            
            if (menu == null)
            {
                LoggingService.Instance.Warning("MainMenu not found, trying to find menu in title bar");
                // Try to find menu in title bar
                var titleBar = _mainWindow.FindControl<Grid>("TitleBar");
                LoggingService.Instance.Info($"Found TitleBar grid: {titleBar != null}");
                
                if (titleBar != null)
                {
                    menu = titleBar.FindControl<Menu>("");
                    LoggingService.Instance.Info($"Found empty name menu in TitleBar: {menu != null}");
                    
                    if (menu == null)
                    {
                        // Look for the first menu in title bar
                        menu = titleBar.Children.OfType<Menu>().FirstOrDefault();
                        LoggingService.Instance.Info($"Found first menu in TitleBar: {menu != null}");
                    }
                }
            }

            if (menu != null)
            {
                LoggingService.Instance.Info($"Found menu control, searching {menu.Items.Count} menu items for '{menuName}'");
                
                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    var headerText = item.Header?.ToString();
                    LoggingService.Instance.Info($"Checking menu item header: '{headerText}' against '{menuName}'");
                    
                    if (headerText?.Equals(menuName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        LoggingService.Instance.Info($"Found matching menu item: '{headerText}'");
                        return item;
                    }
                }
                
                LoggingService.Instance.Warning($"Menu '{menuName}' not found among available menu items");
            }
            else
            {
                LoggingService.Instance.Error("Could not find any menu control in MainWindow");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error finding menu '{menuName}': {ex.Message}");
        }

        LoggingService.Instance.Warning($"Menu '{menuName}' not found");
        return null;
    }
}

public class ExtensionMenuItem
{
    public string ExtensionId { get; set; } = "";
    public string MenuName { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Tooltip { get; set; }
    public MenuItem? UIElement { get; set; }
}

public class ExtensionButton
{
    public string ExtensionId { get; set; } = "";
    public string Location { get; set; } = "";
    public string Text { get; set; } = "";
    public string CallbackFunction { get; set; } = "";
    public string? Tooltip { get; set; }
    public Button? UIElement { get; set; }
}

public class ExtensionWindow
{
    public string ExtensionId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public Window? WindowElement { get; set; }
    public StackPanel? ContentPanel { get; set; }
    public Dictionary<string, Control> Controls { get; set; } = new();

    public void Close()
    {
        WindowElement?.Close();
    }
}