using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using SaveVaultApp.Services;

namespace SaveVaultApp.Utilities
{
    public static class DialogHelper
    {
        /// <summary>
        /// Shows a confirmation dialog with Yes/No buttons
        /// </summary>
        /// <param name="owner">The parent window</param>
        /// <param name="title">Dialog title</param>
        /// <param name="message">Dialog message</param>
        /// <param name="yesButtonText">Text for the Yes button (default: "Yes")</param>
        /// <param name="noButtonText">Text for the No button (default: "No")</param>
        /// <returns>True if Yes was clicked, False if No was clicked</returns>
        public static async Task<bool> ShowConfirmationAsync(Window? owner, string title, string message, 
            string yesButtonText = "Yes", string noButtonText = "No")
        {
            try
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 20,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = message,
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 14,
                                Foreground = new SolidColorBrush(Colors.White),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextAlignment = TextAlignment.Center
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Spacing = 10,
                                Children =
                                {
                                    new Button
                                    {
                                        Content = yesButtonText,
                                        Width = 80,
                                        Height = 32,
                                        Name = "YesButton",
                                        Background = new SolidColorBrush(Color.Parse("#FF9D45")),
                                        Foreground = new SolidColorBrush(Colors.White)
                                    },
                                    new Button
                                    {
                                        Content = noButtonText,
                                        Width = 80,
                                        Height = 32,
                                        Name = "NoButton",
                                        Background = new SolidColorBrush(Color.Parse("#4B4B4B")),
                                        Foreground = new SolidColorBrush(Colors.White)
                                    }
                                }
                            }
                        }
                    }
                };

                bool result = false;
                
                // Get buttons from the dialog
                var mainPanel = (StackPanel)dialog.Content;
                var buttonPanel = (StackPanel)mainPanel.Children[1];
                var yesButton = (Button)buttonPanel.Children[0];
                var noButton = (Button)buttonPanel.Children[1];

                // Set up event handlers
                yesButton.Click += (s, e) =>
                {
                    result = true;
                    dialog.Close();
                };

                noButton.Click += (s, e) =>
                {
                    result = false;
                    dialog.Close();
                };

                // Handle window closing (default to No)
                dialog.Closing += (s, e) =>
                {
                    if (!result)
                    {
                        result = false;
                    }
                };

                // Show dialog
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

                return result;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error showing confirmation dialog: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shows an information dialog with an OK button
        /// </summary>
        /// <param name="owner">The parent window</param>
        /// <param name="title">Dialog title</param>
        /// <param name="message">Dialog message</param>
        /// <param name="okButtonText">Text for the OK button (default: "OK")</param>
        public static async Task ShowInfoAsync(Window? owner, string title, string message, string okButtonText = "OK")
        {
            try
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 20,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = message,
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 14,
                                Foreground = new SolidColorBrush(Colors.White),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextAlignment = TextAlignment.Center
                            },
                            new Button
                            {
                                Content = okButtonText,
                                Width = 80,
                                Height = 32,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Background = new SolidColorBrush(Color.Parse("#FF9D45")),
                                Foreground = new SolidColorBrush(Colors.White)
                            }
                        }
                    }
                };

                // Get button from the dialog
                var mainPanel = (StackPanel)dialog.Content;
                var okButton = (Button)mainPanel.Children[1];

                // Set up event handler
                okButton.Click += (s, e) => dialog.Close();

                // Show dialog
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
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error showing info dialog: {ex.Message}");
            }
        }
    }
}
