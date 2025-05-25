using Avalonia.Controls;
using Avalonia.Interactivity;
using SaveVaultApp.Models;
using System;
using System.Diagnostics;
using Avalonia.Platform;
using System.Reactive.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace SaveVaultApp.Views;

public partial class TermsWindow : Window, INotifyPropertyChanged
{
    private bool _termsAccepted = false;
    public bool TermsAccepted => _termsAccepted;
    
    private bool _privacyPolicyChecked = false;
    private bool _termsOfServiceChecked = false;
    private bool _securityPolicyChecked = false;

    // Website URLs for policies
    private const string PRIVACY_POLICY_URL = "https://vault.etka.co.uk/privacy-policy";
    private const string TERMS_OF_SERVICE_URL = "https://vault.etka.co.uk/terms-of-service";
    private const string SECURITY_POLICY_URL = "https://vault.etka.co.uk/security-policy";
    
    // Local policy file paths
    private readonly string PRIVACY_POLICY_PATH;
    private readonly string TERMS_OF_SERVICE_PATH;
    private readonly string SECURITY_POLICY_PATH;
    
    public TermsWindow()
    {
        InitializeComponent();
        
        // Initialize policy file paths
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        PRIVACY_POLICY_PATH = Path.Combine(basePath, "Assets", "PrivacyPolicy.txt");
        TERMS_OF_SERVICE_PATH = Path.Combine(basePath, "Assets", "TermsOfService.txt");
        SECURITY_POLICY_PATH = Path.Combine(basePath, "Assets", "SecurityPolicy.txt");
        
        // Set up button click events
        var acceptButton = this.FindControl<Button>("AcceptButton");
        if (acceptButton != null)
        {
            acceptButton.Click += AcceptButton_Click;
            acceptButton.IsEnabled = false; // Disabled until all checkboxes are checked
        }
        
        // Set up decline button
        var declineButton = this.FindControl<Button>("DeclineButton");
        if (declineButton != null)
        {
            declineButton.Click += DeclineButton_Click;
        }
        
        // Set up policy button click events
        SetupPolicyButton("PrivacyPolicyButton", "Privacy Policy", PRIVACY_POLICY_PATH, PRIVACY_POLICY_URL);
        SetupPolicyButton("TermsOfServiceButton", "Terms of Service", TERMS_OF_SERVICE_PATH, TERMS_OF_SERVICE_URL);
        SetupPolicyButton("SecurityPolicyButton", "Security Policy", SECURITY_POLICY_PATH, SECURITY_POLICY_URL);
        
        // Set up checkbox changed events
        SetupCheckbox("PrivacyPolicyCheckbox", value => _privacyPolicyChecked = value);
        SetupCheckbox("TermsOfServiceCheckbox", value => _termsOfServiceChecked = value);
        SetupCheckbox("SecurityPolicyCheckbox", value => _securityPolicyChecked = value);
    }

    private void SetupPolicyButton(string name, string policyName, string policyPath, string backupUrl)
    {
        var button = this.FindControl<Button>(name);
        if (button != null)
        {
            button.Click += async (s, e) => 
            {
                try
                {
                    // First try to show the local policy text
                    if (File.Exists(policyPath))
                    {
                        await PolicyViewer.ShowPolicy(this, policyName, policyPath);
                    }
                    else
                    {
                        // If local file doesn't exist, fall back to the website
                        Services.LoggingService.Instance.Warning($"Local policy file not found: {policyPath}, opening URL instead");
                        OpenUrl(backupUrl);
                    }
                }
                catch (Exception ex)
                {
                    Services.LoggingService.Instance.Error($"Error showing policy: {ex.Message}");
                    OpenUrl(backupUrl);
                }
            };
        }
    }
    
    private void SetupCheckbox(string name, Action<bool> valueChanged)
    {
        var checkbox = this.FindControl<CheckBox>(name);
        if (checkbox != null)
        {
            checkbox.IsCheckedChanged += (s, e) => 
            {
                if (s is CheckBox cb && cb.IsChecked.HasValue)
                {
                    valueChanged(cb.IsChecked.Value);
                    UpdateAcceptButtonState();
                }
            };
        }
    }
    
    private void UpdateAcceptButtonState()
    {
        var acceptButton = this.FindControl<Button>("AcceptButton");
        if (acceptButton != null)
        {
            acceptButton.IsEnabled = _privacyPolicyChecked && _termsOfServiceChecked && _securityPolicyChecked;
        }
    }    private void AcceptButton_Click(object? sender, RoutedEventArgs e)
    {
        // Verify all policies have been accepted
        if (!(_privacyPolicyChecked && _termsOfServiceChecked && _securityPolicyChecked))
        {
            return;
        }
        
        try
        {
            // Update the settings
            var settings = Settings.Instance;
            settings.TermsAccepted = true;
            settings.ForceSave();
            
            // Set the property
            _termsAccepted = true;
            
            // Log the acceptance
            Services.LoggingService.Instance.Info("User accepted terms and conditions");
            
            // Close the window
            Close();
        }
        catch (Exception ex)
        {
            Services.LoggingService.Instance.Error($"Error saving terms acceptance: {ex.Message}");
            // Still set the property to true and close
            _termsAccepted = true;
            Close();
        }
    }
    
    private void DeclineButton_Click(object? sender, RoutedEventArgs e)
    {
        // Set terms as not accepted
        _termsAccepted = false;
        
        // Close the window
        Close();
    }
    
    private void OpenUrl(string url)
    {
        try
        {
            // Log which URL is being opened
            Services.LoggingService.Instance.Info($"Opening URL: {url}");
            
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Log error
            Services.LoggingService.Instance.Error($"Failed to open URL: {ex.Message}");
            
            // Show error message to user with the URL
            var messageBox = new Window
            {
                Title = "Error Opening URL",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock
                {
                    Text = $"Could not open the URL in your browser. Please copy and paste this URL manually:\n\n{url}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(20)
                }
            };
            
            messageBox.ShowDialog(this);
        }
    }
    
    // INotifyPropertyChanged implementation
    public new event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}