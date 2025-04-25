using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using SaveVaultApp.Services;
using ReactiveUI;

namespace SaveVaultApp.ViewModels
{
    public class LogViewerViewModel : ViewModelBase
    {
        private readonly LoggingService _loggingService;
        private ObservableCollection<LogEntry> _filteredLogs;
        private string _searchText = string.Empty;
        private int _selectedLogLevel;
        
        public ObservableCollection<LogEntry> FilteredLogs => _filteredLogs;
        
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                RefreshFilteredLogs();
            }
        }
        
        public int SelectedLogLevel
        {
            get => _selectedLogLevel;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
                RefreshFilteredLogs();
            }
        }
        
        public string StatusText => $"{FilteredLogs.Count} log entries shown (total: {_loggingService.Logs.Count})";
        
        public LogViewerViewModel()
        {
            _loggingService = LoggingService.Instance;
            _filteredLogs = new ObservableCollection<LogEntry>();
            
            // Subscribe to new log entries
            _loggingService.NewLogEntry += (s, e) => RefreshFilteredLogs();
            
            // Initial load
            RefreshFilteredLogs();
        }
        
        public void ClearLogs()
        {
            _loggingService.Clear();
            RefreshFilteredLogs();
        }
        
        public void RefreshFilteredLogs()
        {
            string searchText = SearchText?.ToLowerInvariant() ?? string.Empty;
            
            var filteredList = _loggingService.Logs.Where(log =>
            {
                // Apply log level filter
                if (SelectedLogLevel > 0)
                {
                    LogLevel filterLevel = (LogLevel)(SelectedLogLevel - 1);
                    if (log.Level != filterLevel)
                        return false;
                }
                
                // Apply search filter
                if (!string.IsNullOrEmpty(searchText))
                {
                    return log.FormattedMessage.ToLowerInvariant().Contains(searchText);
                }
                
                return true;
            }).ToList();
            
            _filteredLogs.Clear();
            foreach (var log in filteredList)
            {
                _filteredLogs.Add(log);
            }
            
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }
}
