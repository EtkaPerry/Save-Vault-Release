using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace SaveVaultApp.Services;

/// <summary>
/// Service to apply translation text replacements to the main UI
/// </summary>
public class UITranslationService
{
    private static readonly Lazy<UITranslationService> _instance = new(() => new UITranslationService());
    public static UITranslationService Instance => _instance.Value;

    private readonly Dictionary<string, Dictionary<string, string>> _languageReplacements = new();
    private readonly ConcurrentDictionary<Window, bool> _trackedWindows = new();
    private Application? _application;
    private string _currentLanguage = "en-US";
    private bool _pendingTranslationApplication = false;

    private UITranslationService()
    {
        // Subscribe to language changes
        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    public void Initialize(Application application)
    {
        _application = application;
        LoggingService.Instance.Info("UITranslationService initialized");
        
        // Subscribe to window opened events if available
        if (_application.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Hook into window creation by monitoring the Windows collection
            StartWindowTracking(desktop);
            
            // Subscribe to new windows being opened
            if (desktop.MainWindow != null)
            {
                TrackWindow(desktop.MainWindow);
                desktop.MainWindow.Opened += (s, e) => 
                {
                    LoggingService.Instance.Info("Main window opened, applying pending translations");
                    if (_pendingTranslationApplication)
                    {
                        ApplyTranslations();
                    }
                };
            }
        }
        
        // If we have pending translations to apply, try to apply them now
        if (_pendingTranslationApplication)
        {
            LoggingService.Instance.Info("Applying pending translations after initialization");
            ApplyTranslations();
        }
    }    /// <summary>
    /// Start monitoring for new windows being created
    /// </summary>
    private void StartWindowTracking(Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Check periodically for new windows
        _ = Task.Run(async () =>
        {
            while (_application != null)
            {
                try
                {
                    await Task.Delay(200); // Check every 200ms for more responsive tracking
                    
                    // Get current windows
                    var currentWindows = desktop.Windows.ToList();
                    
                    // Track any new windows
                    foreach (var window in currentWindows)
                    {
                        if (!_trackedWindows.ContainsKey(window))
                        {
                            LoggingService.Instance.Info($"New window detected: {window.GetType().Name}");
                            TrackWindow(window);
                            
                            // Apply current language translations to the new window
                            ApplyTranslationsToWindow(window);
                        }
                    }
                    
                    // Remove closed windows from tracking
                    var closedWindows = _trackedWindows.Keys.Where(w => !currentWindows.Contains(w)).ToList();
                    foreach (var closedWindow in closedWindows)
                    {
                        _trackedWindows.TryRemove(closedWindow, out _);
                        LoggingService.Instance.Debug($"Removed closed window from tracking: {closedWindow.GetType().Name}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Error in window tracking: {ex.Message}");
                }
            }
        });
        
        // Also subscribe to application events to catch window creations more reliably
        desktop.ShutdownRequested += (s, e) => 
        {
            LoggingService.Instance.Info("Application shutdown requested, stopping window tracking");
            _application = null; // This will stop the tracking loop
        };
    }/// <summary>
    /// Track a window for translation updates
    /// </summary>
    private void TrackWindow(Window window)
    {
        if (_trackedWindows.TryAdd(window, true))
        {
            LoggingService.Instance.Debug($"Now tracking window for translations: {window.GetType().Name}");
            
            // Subscribe to window events for better translation timing
            window.Opened += (s, e) =>
            {
                LoggingService.Instance.Debug($"Window opened: {window.GetType().Name}, applying translations");
                ApplyTranslationsToWindow(window);
            };
            
            // Also try to apply immediately if window is already loaded
            if (window.IsLoaded)
            {
                ApplyTranslationsToWindow(window);
            }
        }
    }

    /// <summary>
    /// Public method to track a new window (called by other services when creating windows)
    /// </summary>
    public void TrackNewWindow(Window window)
    {
        LoggingService.Instance.Info($"Manually tracking new window: {window.GetType().Name}");
        TrackWindow(window);
        
        // Apply current language translations immediately
        ApplyTranslationsToWindow(window);
    }    private void OnLanguageChanged(object? sender, string languageCode)
    {
        _currentLanguage = languageCode;
        LoggingService.Instance.Info($"UITranslationService: Language changed to {languageCode}");
        
        // Force a comprehensive re-scan of all windows when language changes
        ForceRescanAndApplyTranslations();
    }

    /// <summary>
    /// Apply translations with retry mechanism if no windows are available
    /// </summary>
    private async void ApplyTranslationsWithRetry()
    {
        bool applied = ApplyTranslations();
        
        if (!applied)
        {
            LoggingService.Instance.Info("No windows available for translation, will retry when windows become available");
            _pendingTranslationApplication = true;
            
            // Try again after a short delay to allow windows to be created
            await Task.Delay(100);
            applied = ApplyTranslations();
            
            if (!applied)
            {
                // Try once more after a longer delay
                await Task.Delay(500);
                applied = ApplyTranslations();
                
                if (!applied)
                {
                    LoggingService.Instance.Warning("Still no windows available after retries, translations will be applied when windows become available");
                }
            }
        }
    }    /// <summary>
    /// Register a text replacement for a specific language
    /// This can be called by extensions via the Lua API
    /// </summary>
    public void RegisterTextReplacement(string languageCode, string originalText, string translatedText)
    {
        try
        {
            if (!_languageReplacements.ContainsKey(languageCode))
            {
                _languageReplacements[languageCode] = new Dictionary<string, string>();
            }

            _languageReplacements[languageCode][originalText] = translatedText;
            LoggingService.Instance.Info($"Registered text replacement for {languageCode}: '{originalText}' -> '{translatedText}'");

            // Apply immediately if this is the current language
            if (_currentLanguage == languageCode)
            {
                LoggingService.Instance.Info($"Applying translation immediately since {languageCode} is the current language");
                // Use the comprehensive rescan method to ensure all windows get the new translation
                ForceRescanAndApplyTranslations();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error registering text replacement: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear UI text replacements for a specific language
    /// </summary>
    public void ClearLanguageReplacements(string languageCode)
    {
        if (_languageReplacements.ContainsKey(languageCode))
        {
            _languageReplacements.Remove(languageCode);
            LoggingService.Instance.Info($"Cleared UI text replacements for language: {languageCode}");
        }
    }

    /// <summary>
    /// Clear all UI text replacements (typically called when extension is unloaded)
    /// </summary>
    public void ClearAllReplacements()
    {
        _languageReplacements.Clear();
        LoggingService.Instance.Info("Cleared all UI text replacements");
        RevertToDefaults();
    }    /// <summary>
    /// Force re-application of current language translations
    /// </summary>
    public void ForceApplyTranslations()
    {
        LoggingService.Instance.Info($"Force applying translations for current language: {_currentLanguage}");
        
        // Try multiple times with increasing delays to catch when windows become available
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                bool applied = ApplyTranslations();
                if (applied)
                {
                    LoggingService.Instance.Info($"Successfully applied translations on attempt {i + 1}");
                    break;
                }
                
                // Wait progressively longer between attempts
                int delay = (i + 1) * 200; // 200ms, 400ms, 600ms, etc.
                LoggingService.Instance.Info($"Translation attempt {i + 1} failed, retrying in {delay}ms...");
                await Task.Delay(delay);
            }
        });
    }

    /// <summary>
    /// Force rescan for all windows and apply translations (more comprehensive than regular ForceApplyTranslations)
    /// </summary>
    public void ForceRescanAndApplyTranslations()
    {
        LoggingService.Instance.Info($"Force rescanning all windows and applying translations for current language: {_currentLanguage}");
        
        // Try to find ALL windows, including ones not tracked by the normal mechanism
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    // Clear our tracked windows and start fresh
                    _trackedWindows.Clear();
                    
                    if (_application?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        // Track ALL currently existing windows
                        var allWindows = desktop.Windows.ToList();
                        LoggingService.Instance.Info($"Found {allWindows.Count} windows during rescan");
                        
                        foreach (var window in allWindows)
                        {
                            TrackWindow(window);
                            ApplyTranslationsToWindow(window);
                        }
                        
                        if (allWindows.Count > 0)
                        {
                            LoggingService.Instance.Info($"Successfully rescanned and applied translations to {allWindows.Count} windows");
                            break;
                        }
                    }
                    
                    // Wait before next attempt
                    await Task.Delay((attempt + 1) * 300);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Error during window rescan attempt {attempt + 1}: {ex.Message}");
                    await Task.Delay((attempt + 1) * 300);
                }
            }        });
    }

    private bool ApplyTranslations()
    {
        if (_application == null)
        {
            LoggingService.Instance.Warning("UITranslationService: Application is null, cannot apply translations");
            return false;
        }

        if (!_languageReplacements.ContainsKey(_currentLanguage))
        {
            LoggingService.Instance.Info($"UITranslationService: No translations available for language {_currentLanguage}");
            return false;
        }

        try
        {
            var replacements = _languageReplacements[_currentLanguage];
            LoggingService.Instance.Info($"Applying {replacements.Count} text replacements for language: {_currentLanguage}");

            // Find all windows and apply translations
            var windows = _application.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                         ? desktop.Windows 
                         : Enumerable.Empty<Window>();

            int windowCount = 0;
            foreach (var window in windows)
            {
                windowCount++;
                ApplyTranslationsToWindow(window);
            }
            
            LoggingService.Instance.Info($"Applied translations to {windowCount} windows");
            
            if (windowCount > 0)
            {
                _pendingTranslationApplication = false;
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Error applying translations: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Apply translations to a specific window
    /// </summary>
    private void ApplyTranslationsToWindow(Window window)
    {
        if (!_languageReplacements.ContainsKey(_currentLanguage))
        {
            return;
        }

        try
        {
            var replacements = _languageReplacements[_currentLanguage];
            LoggingService.Instance.Debug($"Applying translations to window: {window.GetType().Name}");
            ApplyTranslationsToControl(window, replacements);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Error applying translations to window {window.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyTranslationsToControl(Control control, Dictionary<string, string> replacements)
    {
        try
        {
            // Handle TextBlock controls
            if (control is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
            {
                if (replacements.TryGetValue(textBlock.Text, out var replacement))
                {
                    var originalText = textBlock.Text;
                    textBlock.Text = replacement;
                    LoggingService.Instance.Debug($"Translated TextBlock: '{originalText}' -> '{replacement}'");
                }
            }

            // Handle controls with Content property (like Button, MenuItem, etc.)
            if (control is ContentControl contentControl && contentControl.Content is string contentText)
            {
                if (replacements.TryGetValue(contentText, out var replacement))
                {
                    contentControl.Content = replacement;
                    LoggingService.Instance.Debug($"Translated ContentControl: '{contentText}' -> '{replacement}'");
                }
            }

            // Handle HeaderedContentControl (like MenuItem with Header)
            if (control is HeaderedContentControl headeredControl && headeredControl.Header is string headerText)
            {
                if (replacements.TryGetValue(headerText, out var replacement))
                {
                    headeredControl.Header = replacement;
                    LoggingService.Instance.Debug($"Translated HeaderedContentControl: '{headerText}' -> '{replacement}'");
                }
            }

            // Handle Window titles
            if (control is Window window && !string.IsNullOrEmpty(window.Title))
            {
                if (replacements.TryGetValue(window.Title, out var replacement))
                {
                    var originalTitle = window.Title;
                    window.Title = replacement;
                    LoggingService.Instance.Debug($"Translated Window title: '{originalTitle}' -> '{replacement}'");
                }
            }

            // Handle ListBoxItem content (for categories, options, etc.)
            if (control is ListBoxItem listBoxItem && listBoxItem.Content is string itemText)
            {
                if (replacements.TryGetValue(itemText, out var replacement))
                {
                    listBoxItem.Content = replacement;
                    LoggingService.Instance.Debug($"Translated ListBoxItem: '{itemText}' -> '{replacement}'");
                }
            }

            // Handle ComboBoxItem content
            if (control is ComboBoxItem comboBoxItem && comboBoxItem.Content is string comboText)
            {
                if (replacements.TryGetValue(comboText, out var replacement))
                {
                    comboBoxItem.Content = replacement;
                    LoggingService.Instance.Debug($"Translated ComboBoxItem: '{comboText}' -> '{replacement}'");
                }
            }            // Handle CheckBox content
            if (control is CheckBox checkBox && checkBox.Content is string checkBoxText)
            {
                if (replacements.TryGetValue(checkBoxText, out var replacement))
                {
                    checkBox.Content = replacement;
                    LoggingService.Instance.Debug($"Translated CheckBox: '{checkBoxText}' -> '{replacement}'");
                }
            }

            // Handle RadioButton content
            if (control is RadioButton radioButton && radioButton.Content is string radioText)
            {
                if (replacements.TryGetValue(radioText, out var replacement))
                {
                    radioButton.Content = replacement;
                    LoggingService.Instance.Debug($"Translated RadioButton: '{radioText}' -> '{replacement}'");
                }
            }

            // Handle Label content
            if (control is Label label && label.Content is string labelText)
            {
                if (replacements.TryGetValue(labelText, out var replacement))
                {
                    label.Content = replacement;
                    LoggingService.Instance.Debug($"Translated Label: '{labelText}' -> '{replacement}'");
                }
            }

            // Handle ToolTip text
            if (ToolTip.GetTip(control) is string toolTipText)
            {
                if (replacements.TryGetValue(toolTipText, out var replacement))
                {
                    ToolTip.SetTip(control, replacement);
                    LoggingService.Instance.Debug($"Translated ToolTip: '{toolTipText}' -> '{replacement}'");
                }
            }

            // Handle TextBox watermark/placeholder
            if (control is TextBox textBox && !string.IsNullOrEmpty(textBox.Watermark))
            {
                if (replacements.TryGetValue(textBox.Watermark, out var replacement))
                {
                    var originalWatermark = textBox.Watermark;
                    textBox.Watermark = replacement;
                    LoggingService.Instance.Debug($"Translated TextBox watermark: '{originalWatermark}' -> '{replacement}'");
                }
            }

            // Recursively apply to child controls
            foreach (var child in control.GetLogicalChildren().OfType<Control>())
            {
                ApplyTranslationsToControl(child, replacements);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning($"Error applying translation to control {control.GetType().Name}: {ex.Message}");
        }
    }

    private void RevertToDefaults()
    {
        // This would restore original text values
        // For now, we'll just log that a revert is needed
        LoggingService.Instance.Info("Reverting UI text to defaults - this feature needs implementation");
    }
}
