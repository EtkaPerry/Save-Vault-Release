using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SaveVaultApp.Views
{
    public partial class PolicyViewer : Window
    {
        private string _policyUrl = string.Empty;
        private float _textSizeFactor = 1.0f;
        private const float TEXT_SIZE_STEP = 0.1f;
        private const float MIN_TEXT_SIZE = 0.7f;
        private const float MAX_TEXT_SIZE = 1.8f;
        
        public PolicyViewer()
        {
            InitializeComponent();
            
            // Set up the close button
            var closeButton = this.FindControl<Button>("CloseButton");
            if (closeButton != null)
            {
                closeButton.Click += (s, e) => Close();
            }
            
            // Set up the external link button
            var externalLinkButton = this.FindControl<Button>("ExternalLinkButton");
            if (externalLinkButton != null)
            {
                externalLinkButton.Click += ExternalLinkButton_Click;
            }
            
            // Set up text size adjustment buttons
            var increaseTextSizeButton = this.FindControl<Button>("IncreaseTextSizeButton");
            if (increaseTextSizeButton != null)
            {
                increaseTextSizeButton.Click += (s, e) => AdjustTextSize(true);
            }
            
            var decreaseTextSizeButton = this.FindControl<Button>("DecreaseTextSizeButton");
            if (decreaseTextSizeButton != null)
            {
                decreaseTextSizeButton.Click += (s, e) => AdjustTextSize(false);
            }
            
            var resetTextSizeButton = this.FindControl<Button>("ResetTextSizeButton");
            if (resetTextSizeButton != null)
            {
                resetTextSizeButton.Click += (s, e) => ResetTextSize();
            }
            
            // Set up window resize event
            this.PropertyChanged += (s, e) => 
            {
                if (e.Property.Name == "Bounds")
                {
                    UpdateTextWrapping();
                }
            };
        }
        
        private void AdjustTextSize(bool increase)
        {
            if (increase)
            {
                _textSizeFactor = Math.Min(MAX_TEXT_SIZE, _textSizeFactor + TEXT_SIZE_STEP);
            }
            else
            {
                _textSizeFactor = Math.Max(MIN_TEXT_SIZE, _textSizeFactor - TEXT_SIZE_STEP);
            }
            
            ApplyTextSizeFactor();
        }
        
        private void ResetTextSize()
        {
            _textSizeFactor = 1.0f;
            ApplyTextSizeFactor();
        }
        
        private void ApplyTextSizeFactor()
        {
            var contentPanel = this.FindControl<StackPanel>("ContentPanel");
            if (contentPanel != null)
            {
                foreach (var child in contentPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        // Apply size factor based on the class of the text block
                        if (textBlock.Classes.Contains("h1"))
                        {
                            textBlock.FontSize = 24 * _textSizeFactor;
                        }
                        else if (textBlock.Classes.Contains("h2"))
                        {
                            textBlock.FontSize = 20 * _textSizeFactor;
                        }
                        else if (textBlock.Classes.Contains("h3"))
                        {
                            textBlock.FontSize = 18 * _textSizeFactor;
                        }
                        else if (textBlock.Classes.Contains("list-item"))
                        {
                            textBlock.FontSize = 14 * _textSizeFactor;
                        }
                        else
                        {
                            // Default text size
                            textBlock.FontSize = 14 * _textSizeFactor;
                        }
                    }
                }
            }
        }
        
        private void UpdateTextWrapping()
        {
            var contentPanel = this.FindControl<StackPanel>("ContentPanel");
            if (contentPanel != null)
            {
                // Update the width to ensure proper text wrapping
                contentPanel.MaxWidth = Math.Max(400, this.Width - 60); // Adjust for margins
                
                foreach (var child in contentPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        // Ensure text wrapping is enabled
                        textBlock.TextWrapping = TextWrapping.Wrap;
                    }
                }
            }
        }
        
        private void ExternalLinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_policyUrl))
            {
                OpenUrl(_policyUrl);
            }
        }
        
        private void OpenUrl(string url)
        {
            try
            {
                // Open the URL in the default browser
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Services.LoggingService.Instance.Error($"Failed to open URL: {ex.Message}");
            }
        }
        
        public static async Task<PolicyViewer> ShowPolicy(Window owner, string policyName, string policyPath)
        {
            var viewer = new PolicyViewer();
            
            // Set the title and URL
            viewer.Title = policyName;
            var titleBlock = viewer.FindControl<TextBlock>("TitleBlock");
            if (titleBlock != null)
            {
                titleBlock.Text = policyName;
            }
            
            // Set the policy URL
            string urlSuffix = policyName.ToLower().Replace(" ", "-");
            viewer._policyUrl = $"https://vault.etka.co.uk/{urlSuffix}";
            
            try
            {
                // Load the policy text
                string policyText = await File.ReadAllTextAsync(policyPath);
                
                // Get file last modified date
                var fileInfo = new FileInfo(policyPath);
                var lastUpdatedBlock = viewer.FindControl<TextBlock>("LastUpdatedBlock");
                if (lastUpdatedBlock != null)
                {
                    lastUpdatedBlock.Text = $"Last updated: {fileInfo.LastWriteTime:MMMM d, yyyy}";
                }
                
                // Parse markdown and create formatted content
                var contentPanel = viewer.FindControl<StackPanel>("ContentPanel");
                if (contentPanel != null)
                {
                    ParseMarkdownContent(policyText, contentPanel);
                }
                
                // Initial update of text wrapping based on current window size
                viewer.UpdateTextWrapping();
            }
            catch (Exception ex)
            {
                // Handle error loading policy
                var contentPanel = viewer.FindControl<StackPanel>("ContentPanel");
                if (contentPanel != null)
                {
                    contentPanel.Children.Add(new TextBlock 
                    { 
                        Text = $"Error loading {policyName}: {ex.Message}\n\n" +
                               $"Please visit our website to view this policy: {viewer._policyUrl}"
                    });
                }
                
                Services.LoggingService.Instance.Error($"Failed to load policy: {ex.Message}");
            }
            
            // Show the dialog as modal to the owner
            await viewer.ShowDialog(owner);
            
            return viewer;
        }
        
        private static void ParseMarkdownContent(string markdown, StackPanel contentPanel)
        {
            // Split the content by lines
            string[] lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Add spacing between sections
                    contentPanel.Children.Add(new TextBlock { Height = 10 });
                    continue;
                }
                
                // Check for headers
                if (line.StartsWith("# "))
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = line.Substring(2),
                        Classes = { "h1" },
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else if (line.StartsWith("## "))
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = line.Substring(3),
                        Classes = { "h2" },
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else if (line.StartsWith("### "))
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = line.Substring(4),
                        Classes = { "h3" },
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                // Check for list items
                else if (line.StartsWith("- "))
                {
                    var text = line.Substring(2);
                    // Check for bold text within list items
                    text = HandleBoldText(text);
                    
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = "• " + text,
                        Classes = { "list-item" },
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                // Regular paragraph
                else
                {
                    // Check for bold text in paragraphs
                    var text = HandleBoldText(line);
                    
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }
        }
        
        private static string HandleBoldText(string text)
        {
            // Simple replacement for bold text (not a complete Markdown parser)
            // Replace patterns like **text** with the text
            return Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
        }
    }
}