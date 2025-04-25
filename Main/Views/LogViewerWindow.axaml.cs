using System;
using System.Text;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Services;

namespace SaveVaultApp.Views
{
    public partial class LogViewerWindow : Window
    {
        private readonly LoggingService _loggingService;
        private ComboBox? _logLevelFilter;
        private TextBox? _searchFilter;
        private TextBox? _logTextBox;
        private TextBlock? _statusText;
        private Button? _clearLogsButton;
        private Button? _copyButton;
        private Button? _saveButton;
        private Button? _closeButton;
        
        private LogLevel? _selectedLogLevel;
        private string _searchText = string.Empty;

        public LogViewerWindow()
        {
            InitializeComponent();
            
            _loggingService = LoggingService.Instance;
            
            // Find all controls
            _logLevelFilter = this.FindControl<ComboBox>("LogLevelFilter");
            _searchFilter = this.FindControl<TextBox>("SearchFilter");
            _logTextBox = this.FindControl<TextBox>("LogTextBox");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _clearLogsButton = this.FindControl<Button>("ClearLogsButton");
            _copyButton = this.FindControl<Button>("CopyButton");
            _saveButton = this.FindControl<Button>("SaveButton");
            _closeButton = this.FindControl<Button>("CloseButton");
            
            // Initial selection
            if (_logLevelFilter != null)
                _logLevelFilter.SelectedIndex = 0;  // "All" by default
                
            // Event handlers
            if (_logLevelFilter != null)
                _logLevelFilter.SelectionChanged += OnFilterChanged;
                
            if (_searchFilter != null)
                _searchFilter.TextChanged += OnFilterChanged;
                
            if (_clearLogsButton != null)
                _clearLogsButton.Click += ClearLogs;
                
            if (_copyButton != null)
                _copyButton.Click += CopyLogs;
                
            if (_saveButton != null)
                _saveButton.Click += SaveLogs;
                
            if (_closeButton != null)
                _closeButton.Click += (s, e) => Close();
            
            // Subscribe to new log entries
            _loggingService.NewLogEntry += OnNewLogEntry;
            
            // Initial display of logs
            RefreshLogDisplay();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void OnNewLogEntry(object? sender, LogEntry e)
        {
            Dispatcher.UIThread.Post(() => RefreshLogDisplay());
        }
        
        private void OnFilterChanged(object? sender, EventArgs e)
        {
            if (_logLevelFilter != null)
                _selectedLogLevel = _logLevelFilter.SelectedIndex > 0 
                    ? (LogLevel)(_logLevelFilter.SelectedIndex - 1) 
                    : null;
                    
            if (_searchFilter != null)
                _searchText = _searchFilter.Text ?? string.Empty;
                
            RefreshLogDisplay();
        }
        
        private void RefreshLogDisplay()
        {
            if (_logTextBox == null || _statusText == null)
                return;
                
            var filteredLogs = _loggingService.Logs.Where(log =>
            {
                // Apply log level filter
                if (_selectedLogLevel.HasValue && log.Level != _selectedLogLevel)
                    return false;
                    
                // Apply search filter
                if (!string.IsNullOrEmpty(_searchText) && 
                    !log.FormattedMessage.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    return false;
                    
                return true;
            }).ToList();
            
            // Format logs
            var sb = new StringBuilder();
            foreach (var log in filteredLogs)
            {
                // Format based on log level
                sb.AppendLine(log.FormattedMessage);
            }
            
            // Update the text box and status
            _logTextBox.Text = sb.ToString();
            _statusText.Text = $"{filteredLogs.Count} log entries shown (total: {_loggingService.Logs.Count})";
            
            // Scroll to the bottom
            _logTextBox.CaretIndex = _logTextBox.Text.Length;
        }
        
        private void ClearLogs(object? sender, RoutedEventArgs e)
        {
            _loggingService.Clear();
            RefreshLogDisplay();
        }

        private async void CopyLogs(object? sender, RoutedEventArgs e)
        {
            if (_logTextBox == null)
                return;
                
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(_logTextBox.Text);
                    _loggingService.Info("Logs copied to clipboard");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to copy logs to clipboard: {ex.Message}");
            }
        }

        private async void SaveLogs(object? sender, RoutedEventArgs e)
        {
            if (_logTextBox == null)
                return;
                
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Log File",
                    DefaultExtension = "log",
                    SuggestedFileName = $"SaveVault_Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
                    FileTypeChoices = new[] 
                    {
                        new FilePickerFileType("Log Files") { Patterns = new[] { "*.log" } },
                        new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (saveFile != null)
                {
                    await using var stream = await saveFile.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(_logTextBox.Text);

                    _loggingService.Info($"Logs saved to {saveFile.Name}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to save logs: {ex.Message}");
            }
        }
    }
}
