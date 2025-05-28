using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SaveVaultApp.Models;

namespace SaveVaultApp.Services
{
    // Custom JSON converter for handling different date formats from the server
    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                return DateTime.Now;

            // Try parsing with multiple formats
            if (DateTime.TryParse(dateString, out DateTime result))
                return result;

            // If standard parsing fails, try custom formats
            string[] formats = { 
                "yyyy-MM-dd HH:mm:ss", 
                "yyyy-MM-ddTHH:mm:ss", 
                "yyyy-MM-dd", 
                "MM/dd/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss"
            };

            if (DateTime.TryParseExact(dateString, formats, null, System.Globalization.DateTimeStyles.None, out result))
                return result;

            // If all parsing attempts fail, log and return current date
            LoggingService.Instance.Warning($"Failed to parse date: {dateString}. Using current date instead.");
            return DateTime.Now;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }

    public class NotificationService
    {
        private static NotificationService? _instance;
        public static NotificationService Instance => _instance ??= new NotificationService();
        
        private readonly Settings _settings;
        private readonly HttpClient _httpClient;
        private readonly string _notificationsUrl = "https://vault.etka.co.uk/notifications_api.php";
        private readonly string _storageFolder;
        private readonly string _notificationsFile = "notifications.json";
        
        // Properties
        public bool HasUnreadNotifications { get; private set; }
        public bool IsChecking { get; private set; }
        public string StatusMessage { get; private set; } = "No notifications checked";
        
        // Events
        public event EventHandler<bool>? UnreadNotificationsChanged;
        public event EventHandler<List<Notification>>? NotificationsUpdated;
        public event EventHandler<string>? StatusChanged;        private NotificationService()
        {
            _settings = Settings.Load();
            _httpClient = new HttpClient();
            
            // Initialize storage folder
            _storageFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SaveVault"
            );
            
            // Ensure the storage directory exists
            Directory.CreateDirectory(_storageFolder);
              // Load notifications from local storage
            LoadNotificationsFromLocalStorage();
            
            // Check if there are any unread notifications in the settings
            HasUnreadNotifications = _settings.Notifications.Any(n => !n.IsRead);
            
            // Ensure notifications are displayed in the UI
            if (_settings.Notifications.Any())
            {
                OnNotificationsUpdated(_settings.Notifications);
            }
            
            LoggingService.Instance.Info($"NotificationService initialized. Unread notifications: {HasUnreadNotifications}");
            
            // Schedule initial notification check
            Task.Run(async () => 
            {
                // Wait a short time to allow application to fully start
                await Task.Delay(5000);
                await CheckForNotifications();
            });
        }
        
        /// <summary>
        /// Checks for notifications if the check interval has been reached
        /// </summary>
        public async Task CheckForNotificationsIfNeeded(int intervalHours = 1)
        {
            var hoursSinceLastCheck = (DateTime.Now - _settings.LastNotificationCheck).TotalHours;
            if (hoursSinceLastCheck >= intervalHours)
            {
                await CheckForNotifications();
            }
        }
          /// <summary>
        /// Checks for new notifications from the server
        /// </summary>
        public async Task<bool> CheckForNotifications()
        {
            try
            {
                IsChecking = true;
                UpdateStatus("Checking for notifications...");
                
                // Check if in offline mode or not authenticated
                if (_settings.OfflineMode)
                {
                    UpdateStatus("Offline mode active. Notifications disabled.");
                    IsChecking = false;
                    return false;
                }
                
                // Check if user is authenticated
                if (string.IsNullOrEmpty(_settings.AuthToken))
                {
                    UpdateStatus("Not logged in. No notifications to fetch.");
                    IsChecking = false;
                    return false;
                }
                
                // Set authentication header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.AuthToken}");
                
                // Fetch notifications from server
                var response = await _httpClient.GetAsync(_notificationsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    UpdateStatus($"Error fetching notifications: {response.StatusCode}");
                    IsChecking = false;
                    return false;
                }                
                var content = await response.Content.ReadAsStringAsync();
                
                // Configure JSON options with custom DateTime converter
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };
                options.Converters.Add(new DateTimeConverter());
                
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<Notification>>>(
                    content,
                    options
                );
                if (apiResponse == null || !apiResponse.Success || apiResponse.Data == null)
                {
                    UpdateStatus($"Failed to retrieve notifications: {apiResponse?.Message ?? "Unknown error"}");
                    IsChecking = false;
                    return false;
                }
                  // Process new notifications
                var serverNotifications = apiResponse.Data
                    .Where(n => !n.Message.Contains("Welcome to Save Vault notifications"))
                    .ToList();
                var newNotificationsAdded = false;
                
                // Get existing notification IDs for comparison
                var existingIds = _settings.Notifications.Select(n => n.Id).ToHashSet();
                
                foreach (var notification in serverNotifications)
                {
                    // Sanitize notification content for security
                    SanitizeNotification(notification);
                    
                    // Check message length - truncate if too long
                    if (notification.Message.Length > 128)
                    {
                        notification.Message = notification.Message.Substring(0, 125) + "...";
                    }
                    
                    // Only add new notifications that don't already exist
                    if (!existingIds.Contains(notification.Id))
                    {                        _settings.Notifications.Insert(0, notification);
                        newNotificationsAdded = true;
                    }
                }
                
                // Update unread status
                bool hadUnread = HasUnreadNotifications;
                HasUnreadNotifications = _settings.Notifications.Any(n => !n.IsRead);
                
                // Notify if unread status changed
                if (HasUnreadNotifications != hadUnread)
                {
                    OnUnreadNotificationsChanged(HasUnreadNotifications);
                }
                  // Update last check time and save settings
                _settings.LastNotificationCheck = DateTime.Now;
                _settings.Save();
                
                // Save notifications to local storage
                SaveNotificationsToLocalStorage();
                
                // Notify about updated notifications
                if (newNotificationsAdded)
                {
                    OnNotificationsUpdated(_settings.Notifications);
                    UpdateStatus($"Found {serverNotifications.Count} notifications.");
                }
                else
                {
                    UpdateStatus("No new notifications.");
                }
                
                IsChecking = false;
                return newNotificationsAdded;
            }            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error checking for notifications: {ex.Message}");
                UpdateStatus($"Error: {ex.Message}");
                
                // Try to load from local storage as fallback
                LoadNotificationsFromLocalStorage();
                
                // Add a local notification about the error
                AddLocalNotification(
                    $"Failed to check for new notifications: {ex.Message}",
                    "warning"
                );
                
                IsChecking = false;
                return false;
            }
        }        /// <summary>
        /// Gets all notifications sorted by date (newest first)
        /// </summary>
        public List<Notification> GetNotifications()
        {
            // Ensure we have the latest notifications from local storage
            if (_settings.Notifications.Count == 0)
            {
                LoadNotificationsFromLocalStorage();
            }
            
            return _settings.Notifications
                .OrderByDescending(n => n.Date)
                .ToList();
        }
        
        /// <summary>
        /// Gets unread notifications
        /// </summary>
        public List<Notification> GetUnreadNotifications()
        {
            return _settings.Notifications.Where(n => !n.IsRead)
                .OrderByDescending(n => n.Date)
                .ToList();
        }
          /// <summary>
        /// Marks a notification as read
        /// </summary>
        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = _settings.Notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null && !notification.IsRead)
            {                // Update locally first
                notification.IsRead = true;
                _settings.Save();
                
                // Save to local storage
                SaveNotificationsToLocalStorage();
                
                // Update HasUnreadNotifications
                bool hadUnread = HasUnreadNotifications;
                HasUnreadNotifications = _settings.Notifications.Any(n => !n.IsRead);
                
                // Notify if unread status changed
                if (HasUnreadNotifications != hadUnread)
                {
                    OnUnreadNotificationsChanged(HasUnreadNotifications);
                }
                
                // Notify of updated notifications
                OnNotificationsUpdated(_settings.Notifications);
                
                // If authenticated, update on server
                if (!string.IsNullOrEmpty(_settings.AuthToken))
                {
                    try
                    {
                        // Set authentication header
                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.AuthToken}");
                        
                        // Create request payload
                        var payload = new 
                        {
                            notificationId = notificationId,
                            isRead = true
                        };
                        
                        // Send PUT request to update notification status
                        var content = new StringContent(
                            JsonSerializer.Serialize(payload),
                            System.Text.Encoding.UTF8,
                            "application/json"
                        );
                        
                        var response = await _httpClient.PutAsync($"{_notificationsUrl}/{notificationId}/read", content);
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            LoggingService.Instance.Warning($"Failed to mark notification {notificationId} as read on server: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Error marking notification as read on server: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Marks a notification as read (synchronous version for backward compatibility)
        /// </summary>
        public void MarkAsRead(int notificationId)
        {
            var notification = _settings.Notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null && !notification.IsRead)
            {                notification.IsRead = true;
                _settings.Save();
                
                // Save to local storage
                SaveNotificationsToLocalStorage();
                
                // Update HasUnreadNotifications
                bool hadUnread = HasUnreadNotifications;
                HasUnreadNotifications = _settings.Notifications.Any(n => !n.IsRead);
                
                // Notify if unread status changed
                if (HasUnreadNotifications != hadUnread)
                {
                    OnUnreadNotificationsChanged(HasUnreadNotifications);
                }
                
                // Notify of updated notifications
                OnNotificationsUpdated(_settings.Notifications);
                
                // Fire and forget the server update
                _ = Task.Run(() => MarkAsReadAsync(notificationId));
            }
        }
          /// <summary>
        /// Marks all notifications as read
        /// </summary>
        public async Task MarkAllAsReadAsync()
        {
            bool hadUnread = HasUnreadNotifications;
            bool anyUnread = _settings.Notifications.Any(n => !n.IsRead);
            
            if (!anyUnread)
            {
                return; // No unread notifications to mark
            }
              // Mark all as read locally
            foreach (var notification in _settings.Notifications)
            {
                notification.IsRead = true;
            }
            
            _settings.Save();
            
            // Save to local storage
            SaveNotificationsToLocalStorage();
            
            // Update HasUnreadNotifications
            HasUnreadNotifications = false;
            
            // Notify if unread status changed
            if (hadUnread)
            {
                OnUnreadNotificationsChanged(false);
            }
            
            // Notify of updated notifications
            OnNotificationsUpdated(_settings.Notifications);
            
            // If authenticated, update on server
            if (!string.IsNullOrEmpty(_settings.AuthToken))
            {
                try
                {
                    // Set authentication header
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.AuthToken}");
                    
                    // Send PUT request to mark all notifications as read
                    var response = await _httpClient.PutAsync($"{_notificationsUrl}/read-all", null);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        LoggingService.Instance.Warning($"Failed to mark all notifications as read on server: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Error marking all notifications as read on server: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Marks all notifications as read (synchronous version for backward compatibility)
        /// </summary>
        public void MarkAllAsRead()
        {
            bool hadUnread = HasUnreadNotifications;
              foreach (var notification in _settings.Notifications)
            {
                notification.IsRead = true;
            }
            
            _settings.Save();
            
            // Save to local storage
            SaveNotificationsToLocalStorage();
            
            // Update HasUnreadNotifications
            HasUnreadNotifications = false;
            
            // Notify if unread status changed
            if (hadUnread)
            {
                OnUnreadNotificationsChanged(false);
            }
            
            // Notify of updated notifications
            OnNotificationsUpdated(_settings.Notifications);
            
            // Fire and forget the server update
            _ = Task.Run(() => MarkAllAsReadAsync());
        }
        
        /// <summary>
        /// Path to the local notifications file
        /// </summary>
        private string NotificationsFilePath => Path.Combine(_storageFolder, _notificationsFile);        /// <summary>
        /// Loads notifications from local storage and updates the settings
        /// </summary>
        private void LoadNotificationsFromLocalStorage()
        {
            try
            {
                // Check if notifications file exists
                if (File.Exists(NotificationsFilePath))
                {
                    // Read all text from the file
                    string json = File.ReadAllText(NotificationsFilePath);
                    
                    // If the file is empty, return
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return;
                    }
                    
                    // Configure JSON options with custom DateTime converter
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    options.Converters.Add(new DateTimeConverter());
                    
                    // Deserialize notifications
                    var notifications = JsonSerializer.Deserialize<List<Notification>>(json, options);
                    
                    // Filter out the welcome notification
                    notifications = notifications?
                        .Where(n => !n.Message.Contains("Welcome to Save Vault notifications"))
                        .ToList() ?? new List<Notification>();
                    
                    // Update settings
                    if (notifications != null && notifications.Any())
                    {
                        _settings.Notifications = notifications;
                        _settings.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error loading notifications from local storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves notifications to local storage
        /// </summary>
        private void SaveNotificationsToLocalStorage()
        {
            try
            {
                // Configure JSON options with custom DateTime converter
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                options.Converters.Add(new DateTimeConverter());
                
                // Serialize notifications
                var json = JsonSerializer.Serialize(_settings.Notifications, options);
                
                // Write to file
                File.WriteAllText(NotificationsFilePath, json);
                
                LoggingService.Instance.Info($"Saved {_settings.Notifications.Count} notifications to local storage");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error saving notifications to local storage: {ex.Message}");
            }
        }
        
        private void UpdateStatus(string message)
        {
            StatusMessage = message;
            OnStatusChanged(message);
        }
        
        private void OnUnreadNotificationsChanged(bool hasUnread)
        {
            UnreadNotificationsChanged?.Invoke(this, hasUnread);
        }
        
        private void OnNotificationsUpdated(List<Notification> notifications)
        {
            NotificationsUpdated?.Invoke(this, notifications);
        }
        
        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }
        
        /// <summary>
        /// Sanitizes notification content to prevent security issues
        /// </summary>
        private void SanitizeNotification(Notification notification)
        {
            try
            {
                // Safety checks for null or empty strings
                if (string.IsNullOrEmpty(notification.Message))
                {
                    notification.Message = "Empty notification";
                    return;
                }
                
                // Regex patterns for potentially malicious content
                string[] patterns = {
                    @"<script[^>]*>.*?</script>",
                    @"javascript:",
                    @"on\w+\s*=",
                    @"eval\s*\(",
                    @"document\.",
                    @"window\.",
                    @"alert\s*\("
                };
                
                // Check and sanitize the message
                string sanitizedMessage = notification.Message;
                foreach (var pattern in patterns)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(sanitizedMessage, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        // Log potential security issue
                        LoggingService.Instance.Warning($"Potentially malicious content detected in notification: {notification.Id}");
                        
                        // Replace the pattern with empty string
                        sanitizedMessage = System.Text.RegularExpressions.Regex.Replace(
                            sanitizedMessage, 
                            pattern, 
                            string.Empty, 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );
                    }
                }
                
                // Basic HTML encoding for extra security
                sanitizedMessage = System.Net.WebUtility.HtmlEncode(sanitizedMessage);
                
                // Update the notification
                notification.Message = sanitizedMessage;
                
                // Check and sanitize link if present
                if (!string.IsNullOrEmpty(notification.Link))
                {
                    // Only allow http/https URLs
                    if (!notification.Link.StartsWith("http://") && !notification.Link.StartsWith("https://"))
                    {
                        // Invalid link format, clear it
                        notification.Link = string.Empty;
                    }
                    else
                    {
                        // Additional check for javascript: in URLs
                        if (notification.Link.Contains("javascript:"))
                        {
                            notification.Link = string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error and provide safe fallback
                LoggingService.Instance.Error($"Error sanitizing notification: {ex.Message}");
                notification.Message = "Notification content could not be displayed safely";
                notification.Link = string.Empty;
            }
        }
        
        /// <summary>
        /// Adds a local notification when offline or for important local events
        /// </summary>
        public void AddLocalNotification(string message, string type = "info", string link = "")
        {
            try
            {
                // Create a new notification with a negative ID (to avoid conflicts with server IDs)
                var notification = new Notification
                {
                    Id = -(_settings.Notifications.Count + 1), // Negative ID to distinguish from server notifications
                    Message = message.Length > 128 ? message.Substring(0, 125) + "..." : message, // Limit message length
                    Date = DateTime.Now,
                    IsRead = false,
                    Type = type,
                    Link = link
                };
                
                // Apply security measures
                SanitizeNotification(notification);
                
                // Add to the beginning of the list
                _settings.Notifications.Insert(0, notification);
                _settings.Save();
                
                // Save to local storage
                SaveNotificationsToLocalStorage();
                
                // Update unread status
                bool hadUnread = HasUnreadNotifications;
                HasUnreadNotifications = true;
                
                // Notify if unread status changed
                if (!hadUnread)
                {
                    OnUnreadNotificationsChanged(true);
                }
                
                // Notify of updated notifications
                OnNotificationsUpdated(_settings.Notifications);
                
                LoggingService.Instance.Info($"Added local notification: {message}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Error adding local notification: {ex.Message}");
            }
        }
    }
      // Helper class for API responses
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }
}