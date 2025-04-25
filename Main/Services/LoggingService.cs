using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Threading;

namespace SaveVaultApp.Services
{
    public class LogEntry : INotifyPropertyChanged
    {
        private string _message;
        private LogLevel _level;
        private DateTime _timestamp;

        public string Message 
        { 
            get => _message; 
            set { _message = value; OnPropertyChanged(); } 
        }
        
        public LogLevel Level 
        { 
            get => _level; 
            set { _level = value; OnPropertyChanged(); } 
        }
        
        public DateTime Timestamp 
        { 
            get => _timestamp; 
            set { _timestamp = value; OnPropertyChanged(); } 
        }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

        public string FormattedMessage => $"[{FormattedTime}] [{Level}] {Message}";

        public LogEntry(string message, LogLevel level)
        {
            _message = message;
            _level = level;
            _timestamp = DateTime.Now;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public class LoggingService : INotifyPropertyChanged
    {
        private static LoggingService? _instance;
        public static LoggingService Instance => _instance ??= new LoggingService();

        private readonly object _lockObject = new object();
        
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();
        
        private int _maxLogEntries = 1000;
        public int MaxLogEntries
        {
            get => _maxLogEntries;
            set
            {
                if (_maxLogEntries != value)
                {
                    _maxLogEntries = value;
                    OnPropertyChanged();
                    TrimLogs();
                }
            }
        }

        private LoggingService()
        {
            // Private constructor for singleton
        }

        public void Debug(string message)
        {
            Log(message, LogLevel.Debug);
        }

        public void Info(string message)
        {
            Log(message, LogLevel.Info);
        }

        public void Warning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        public void Error(string message)
        {
            Log(message, LogLevel.Error);
        }

        public void Critical(string message)
        {
            Log(message, LogLevel.Critical);
        }

        public void LogHttpRequest(string method, string url)
        {
            Info($"HTTP {method} request to {url}");
        }

        public void LogHttpResponse(string method, string url, int statusCode, string content)
        {
            // Truncate content if it's too long
            string truncatedContent = content;
            if (content.Length > 500)
            {
                truncatedContent = content.Substring(0, 500) + "... (truncated)";
            }
            
            Info($"HTTP {method} response from {url} (Status: {statusCode}):\n{truncatedContent}");
        }

        public void LogException(Exception ex, string context = "")
        {
            StringBuilder sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(context))
                sb.AppendLine($"Context: {context}");
                
            sb.AppendLine($"Exception: {ex.GetType().Name}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"Type: {ex.InnerException.GetType().Name}");
                sb.AppendLine($"Message: {ex.InnerException.Message}");
                sb.AppendLine($"StackTrace: {ex.InnerException.StackTrace}");
            }
            
            Error(sb.ToString());
        }

        private void Log(string message, LogLevel level)
        {
            lock (_lockObject)
            {
                var logEntry = new LogEntry(message, level);
                
                // Use Avalonia's Dispatcher for UI thread safety
                Dispatcher.UIThread.Post(() =>
                {
                    Logs.Add(logEntry);
                    TrimLogs();
                    OnNewLogEntry(logEntry);
                });
                
                // Also output to debug console
                System.Diagnostics.Debug.WriteLine(logEntry.FormattedMessage);
            }
        }

        private void TrimLogs()
        {
            while (Logs.Count > MaxLogEntries)
            {
                Logs.RemoveAt(0);
            }
        }

        public event EventHandler<LogEntry>? NewLogEntry;

        protected virtual void OnNewLogEntry(LogEntry logEntry)
        {
            NewLogEntry?.Invoke(this, logEntry);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Clear()
        {
            lock (_lockObject)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Logs.Clear();
                });
            }
        }
    }
}
