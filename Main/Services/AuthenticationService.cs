using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveVaultApp.Services
{
    public class AuthenticationService
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl = "https://vault.etka.co.uk/api"; // Your API URL
        private readonly LoggingService _logger = LoggingService.Instance;
        
        public AuthenticationService()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _logger.Info("AuthenticationService initialized");
        }

        /// <summary>
        /// Login with username/email and password
        /// </summary>
        public async Task<(bool success, string message, string? token, string? username)> LoginAsync(string usernameOrEmail, string password)
        {
            try
            {
                var payload = new
                {
                    usernameOrEmail,
                    password
                };
                
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                
                _logger.LogHttpRequest("POST", $"{_baseUrl}/login");
                var response = await _client.PostAsync($"{_baseUrl}/login", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("POST", $"{_baseUrl}/login", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, "Server returned an invalid response. The API may be unavailable.", null, null);
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        var data = result.Data;
                        _logger.Info($"Login successful for user: {data.GetProperty("username").GetString()}");
                        return (true, result.Message, data.GetProperty("token").GetString(), data.GetProperty("username").GetString());
                    }
                    else
                    {
                        _logger.Warning($"Login failed: {result?.Message ?? "Unknown error"}");
                        return (false, result?.Message ?? "Failed to login", null, null);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during login: {ex.Message}");
                    return (false, $"Invalid server response format: {ex.Message}", null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Login request");
                return (false, $"Error: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        public async Task<(bool success, string message, string? token, string? username)> RegisterAsync(string username, string email, string password)
        {
            try
            {
                var payload = new
                {
                    username,
                    email,
                    password
                };
                
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                
                _logger.LogHttpRequest("POST", $"{_baseUrl}/register");
                var response = await _client.PostAsync($"{_baseUrl}/register", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("POST", $"{_baseUrl}/register", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, "Server returned an invalid response. The API may be unavailable.", null, null);
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        var data = result.Data;
                        _logger.Info($"Registration successful for user: {username}");
                        return (true, result.Message, data.GetProperty("token").GetString(), data.GetProperty("username").GetString());
                    }
                    else
                    {
                        _logger.Warning($"Registration failed: {result?.Message ?? "Unknown error"}");
                        return (false, result?.Message ?? "Failed to register", null, null);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during registration: {ex.Message}");
                    return (false, $"Invalid server response format: {ex.Message}", null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Registration request");
                return (false, $"Error: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Validate an existing token
        /// </summary>
        public async Task<(bool success, string? username)> ValidateTokenAsync(string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/validate");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                _logger.LogHttpRequest("GET", $"{_baseUrl}/validate");
                var response = await _client.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("GET", $"{_baseUrl}/validate", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, null); // Silent failure for token validation
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        string username = result.Data.GetProperty("username").GetString() ?? "";
                        _logger.Info($"Token validation successful for user: {username}");
                        return (true, username);
                    }
                    else
                    {
                        _logger.Warning("Token validation failed");
                        return (false, null);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during token validation: {ex.Message}");
                    return (false, null); // Silent failure for token validation
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Token validation request");
                return (false, null);
            }
        }
        
        /// <summary>
        /// Sync user data with server
        /// </summary>
        public async Task<(bool success, string message)> SyncDataAsync(string token, object data)
        {
            try
            {
                var payload = new
                {
                    data
                };
                
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/sync");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = content;
                
                _logger.LogHttpRequest("POST", $"{_baseUrl}/sync");
                var response = await _client.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("POST", $"{_baseUrl}/sync", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, "Server returned an invalid response. The API may be unavailable.");
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        _logger.Info("Data sync successful");
                        return (true, result.Message);
                    }
                    else
                    {
                        _logger.Warning($"Data sync failed: {result?.Message ?? "Unknown error"}");
                        return (false, result?.Message ?? "Failed to sync data");
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during data sync: {ex.Message}");
                    return (false, $"Invalid server response format: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Data sync request");
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get user data from server
        /// </summary>
        public async Task<(bool success, string message, object? data)> GetUserDataAsync(string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/sync");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                _logger.LogHttpRequest("GET", $"{_baseUrl}/sync");
                var response = await _client.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("GET", $"{_baseUrl}/sync", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, "Server returned an invalid response. The API may be unavailable.", null);
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        var data = result.Data.GetProperty("data");
                        _logger.Info("User data fetch successful");
                        return (true, result.Message, data);
                    }
                    else
                    {
                        _logger.Warning($"User data fetch failed: {result?.Message ?? "Unknown error"}");
                        return (false, result?.Message ?? "Failed to get data", null);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during data fetch: {ex.Message}");
                    return (false, $"Invalid server response format: {ex.Message}", null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "User data fetch request");
                return (false, $"Error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Request a password reset for a user
        /// </summary>
        public async Task<(bool success, string message)> ForgotPasswordAsync(string username, string email)
        {
            try
            {
                var payload = new
                {
                    username,
                    email
                };
                
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                
                _logger.LogHttpRequest("POST", $"{_baseUrl}/forgot-password");
                var response = await _client.PostAsync($"{_baseUrl}/forgot-password", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogHttpResponse("POST", $"{_baseUrl}/forgot-password", (int)response.StatusCode, responseContent);
                
                // Check if response starts with '<' which indicates HTML instead of JSON
                if (responseContent.StartsWith("<"))
                {
                    _logger.Error($"HTML response received instead of JSON: {responseContent.Substring(0, Math.Min(responseContent.Length, 100))}");
                    return (false, "Server returned an invalid response. The API may be unavailable.");
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<ApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                    if (response.IsSuccessStatusCode && result?.Success == true)
                    {
                        _logger.Info($"Password reset request successful for user: {username}");
                        return (true, result.Message);
                    }
                    else
                    {
                        _logger.Warning($"Password reset request failed: {result?.Message ?? "Unknown error"}");
                        return (false, result?.Message ?? "Failed to process password reset request");
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error($"JSON parsing error during password reset request: {ex.Message}");
                    return (false, $"Invalid server response format: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Password reset request");
                return (false, $"Error: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Model for API responses
    /// </summary>
    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
    }
}
