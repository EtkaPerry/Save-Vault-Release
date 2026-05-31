using Avalonia.Media.Imaging;
using SaveVaultApp.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SaveVaultApp.Utilities
{
    public static class ExtensionIconLoader
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public static Bitmap? LoadIconForExtension(Extension extension)
        {
            if (string.IsNullOrEmpty(extension.IconUrl))
            {
                return TryLoadLocalIcon(extension);
            }

            var iconPath = extension.IconUrl;

            try
            {
                // Check if it's a local file path
                if (File.Exists(iconPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(iconPath);
                        return new Bitmap(stream);
                    }
                    catch (Exception ex)
                    {
                        Services.LoggingService.Instance.Error($"Failed to load local icon for extension {extension.Id}: {ex.Message}");
                    }
                }
                else if (iconPath.StartsWith("http"))
                {
                    // For remote URLs, try to download async (but for now just log it)
                    Services.LoggingService.Instance.Info($"Remote icon URL for extension {extension.Id}: {iconPath}");
                    
                    // Try to load from local cache or downloaded files
                    return TryLoadLocalIcon(extension);
                }
                else
                {
                    Services.LoggingService.Instance.Warning($"Icon file not found for extension {extension.Id} at {iconPath}");
                    return TryLoadLocalIcon(extension);
                }
            }
            catch (Exception ex)
            {
                Services.LoggingService.Instance.Error($"Exception loading icon for extension {extension.Id}: {ex.Message}");
            }

            return TryLoadLocalIcon(extension);
        }
        
        /// <summary>
        /// Try to load icon from local extension directory
        /// </summary>
        private static Bitmap? TryLoadLocalIcon(Extension extension)
        {
            try
            {
                var configManager = Services.ExtensionConfigManager.Instance;
                var userExtensionsPath = configManager.GetUserExtensionsPath();
                var extensionDir = Path.Combine(userExtensionsPath, extension.Id);
                  // Try different icon file names
                var iconFiles = new[] { "logo.png", "icon.png", "preview.png", "icon.jpg", "icon.jpeg" };
                
                foreach (var iconFile in iconFiles)
                {
                    var iconPath = Path.Combine(extensionDir, iconFile);
                    if (File.Exists(iconPath))
                    {
                        try
                        {
                            using var stream = File.OpenRead(iconPath);
                            Services.LoggingService.Instance.Info($"Loaded local icon for extension {extension.Id}: {iconFile}");
                            return new Bitmap(stream);
                        }
                        catch (Exception ex)
                        {
                            Services.LoggingService.Instance.Warning($"Failed to load icon file {iconFile} for extension {extension.Id}: {ex.Message}");
                        }
                    }
                }
                
                Services.LoggingService.Instance.Info($"No local icon found for extension {extension.Id} in {extensionDir}");
            }
            catch (Exception ex)
            {
                Services.LoggingService.Instance.Error($"Error trying to load local icon for extension {extension.Id}: {ex.Message}");
            }
            
            return null;
        }
    }
}
