using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace SaveVaultApp.Models;

/// <summary>
/// Custom JSON converter for ExtensionCategory enum to handle string values from server
/// </summary>
public class ExtensionCategoryConverter : JsonConverter<ExtensionCategory>
{
    public override ExtensionCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var categoryString = reader.GetString();
            return categoryString?.ToLower() switch
            {
                "fixes" => ExtensionCategory.Fixes,
                "localization" => ExtensionCategory.Localization,
                "theming" => ExtensionCategory.Theming,
                _ => ExtensionCategory.Other
            };
        }
        
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (ExtensionCategory)reader.GetInt32();
        }
        
        return ExtensionCategory.Other;
    }

    public override void Write(Utf8JsonWriter writer, ExtensionCategory value, JsonSerializerOptions options)
    {
        var categoryString = value switch
        {
            ExtensionCategory.Fixes => "Fixes",
            ExtensionCategory.Localization => "Localization",
            ExtensionCategory.Theming => "Theming",
            _ => "Other"
        };
        writer.WriteStringValue(categoryString);
    }
}

public class Extension : INotifyPropertyChanged
{    
    private bool _isInstalled;
    private bool _isEnabled;
    private bool _isDownloading;
    private string _iconUrl = string.Empty;
    private Bitmap? _iconBitmap;
    
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    [JsonPropertyName("category")]
    [JsonConverter(typeof(ExtensionCategoryConverter))]
    public ExtensionCategory Category { get; set; }      [JsonPropertyName("iconUrl")]
    public string IconUrl 
    { 
        get => _iconUrl; 
        set 
        {
            if (_iconUrl != value)
            {
                _iconUrl = value;
                
                // Try to load the icon bitmap when the URL is set
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        _iconBitmap = Utilities.ExtensionIconLoader.LoadIconForExtension(this);
                    }
                    catch (Exception ex)
                    {
                        Services.LoggingService.Instance.Error($"Extension {Id}: Failed to load icon bitmap: {ex.Message}");
                        _iconBitmap = null;
                    }
                }
                else
                {
                    _iconBitmap = null;
                }
                
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(IconBitmap));
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }
    
    [JsonIgnore]
    public Bitmap? IconBitmap => _iconBitmap;
      [JsonIgnore]
    public bool HasIcon => _iconBitmap != null;
    
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public List<string> Screenshots { get; set; } = new();
    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; }
    [JsonPropertyName("updatedDate")]
    public DateTime UpdatedDate { get; set; }
    [JsonPropertyName("downloads")]
    public long DownloadCount { get; set; }
    [JsonPropertyName("rating")]
    public double Rating { get; set; }
    public string MinimumVersion { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    
    [JsonPropertyName("isOfficial")]
    public bool IsOfficial { get; set; } = false;
    
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(InstallButtonText));
            }
        }
    }
      [JsonIgnore]
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                System.Diagnostics.Debug.WriteLine($"Extension '{Name}' IsEnabled changed to: {value}");
            }
        }
    }
    
    [JsonIgnore]
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading != value)
            {
                _isDownloading = value;
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(InstallButtonText));
            }
        }
    }
    
    [JsonIgnore]
    public string InstallButtonText => IsDownloading ? "Downloading..." : IsInstalled ? "Uninstall" : "Install";
      [JsonIgnore]
    public string CategoryDisplayName => Category switch
    {
        ExtensionCategory.Fixes => "Fixes",
        ExtensionCategory.Localization => "Localization", 
        ExtensionCategory.Theming => "Theming",
        ExtensionCategory.Other => "Other",
        _ => "All"
    };
    
    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && 
                          !string.IsNullOrWhiteSpace(Name) && 
                          !string.IsNullOrWhiteSpace(Author);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum ExtensionCategory
{
    All = 0,
    Fixes = 1,
    Localization = 2,
    Theming = 3,
    Other = 4
}