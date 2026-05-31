using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SaveVaultApp.Services;

/// <summary>
/// Service that provides translation capabilities for extensions
/// </summary>
public class ExtensionTranslationService
{
    private static readonly Lazy<ExtensionTranslationService> _instance = new(() => new ExtensionTranslationService());
    public static ExtensionTranslationService Instance => _instance.Value;

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _translations = new();
    private string _currentLanguage = "en-US";

    private ExtensionTranslationService()
    {
        // Set initial language from system
        try
        {
            _currentLanguage = CultureInfo.CurrentUICulture.Name;
        }
        catch
        {
            _currentLanguage = "en-US";
        }
    }

    /// <summary>
    /// Register translations for an extension
    /// </summary>
    public bool RegisterTranslations(string extensionId, string language, Dictionary<string, string> translations)
    {
        try
        {
            if (!_translations.ContainsKey(extensionId))
                _translations[extensionId] = new Dictionary<string, Dictionary<string, string>>();

            _translations[extensionId][language] = new Dictionary<string, string>(translations);
            
            LoggingService.Instance.Info($"Registered {translations.Count} translations for extension '{extensionId}' in language '{language}'");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to register translations for extension '{extensionId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add a single translation for an extension
    /// </summary>
    public bool AddTranslation(string extensionId, string language, string key, string value)
    {
        try
        {
            if (!_translations.ContainsKey(extensionId))
                _translations[extensionId] = new Dictionary<string, Dictionary<string, string>>();

            if (!_translations[extensionId].ContainsKey(language))
                _translations[extensionId][language] = new Dictionary<string, string>();

            _translations[extensionId][language][key] = value;
            
            LoggingService.Instance.Info($"Added translation for extension '{extensionId}': '{key}' = '{value}' ({language})");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to add translation for extension '{extensionId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get translated text for an extension
    /// </summary>
    public string GetTranslation(string extensionId, string key, string? fallbackValue = null)
    {
        try
        {
            // Try current language first
            if (_translations.TryGetValue(extensionId, out var extensionTranslations))
            {
                if (extensionTranslations.TryGetValue(_currentLanguage, out var currentLangTranslations))
                {
                    if (currentLangTranslations.TryGetValue(key, out var translation))
                    {
                        return translation;
                    }
                }

                // Fallback to English
                if (_currentLanguage != "en-US" && extensionTranslations.TryGetValue("en-US", out var englishTranslations))
                {
                    if (englishTranslations.TryGetValue(key, out var englishTranslation))
                    {
                        return englishTranslation;
                    }
                }

                // Fallback to any available language
                foreach (var langDict in extensionTranslations.Values)
                {
                    if (langDict.TryGetValue(key, out var anyTranslation))
                    {
                        return anyTranslation;
                    }
                }
            }

            // Return fallback or key itself
            return fallbackValue ?? key;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get translation for extension '{extensionId}', key '{key}': {ex.Message}");
            return fallbackValue ?? key;
        }
    }

    /// <summary>
    /// Set the current language
    /// </summary>
    public void SetLanguage(string language)
    {
        _currentLanguage = language;
        LoggingService.Instance.Info($"Extension translation language set to: {language}");
    }

    /// <summary>
    /// Get the current language
    /// </summary>
    public string GetCurrentLanguage()
    {
        return _currentLanguage;
    }

    /// <summary>
    /// Get all available languages for an extension
    /// </summary>
    public string[] GetAvailableLanguages(string extensionId)
    {
        try
        {
            if (_translations.TryGetValue(extensionId, out var extensionTranslations))
            {
                return extensionTranslations.Keys.ToArray();
            }
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to get available languages for extension '{extensionId}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Remove all translations for an extension
    /// </summary>
    public void RemoveExtensionTranslations(string extensionId)
    {
        try
        {
            if (_translations.ContainsKey(extensionId))
            {
                _translations.Remove(extensionId);
                LoggingService.Instance.Info($"Removed all translations for extension '{extensionId}'");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error($"Failed to remove translations for extension '{extensionId}': {ex.Message}");
        }
    }
}