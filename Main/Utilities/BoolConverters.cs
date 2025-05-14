using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SaveVaultApp.Utilities;

public class BoolToAngleConverter : IValueConverter
{
    public static readonly BoolToAngleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? 180 : 0;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToBackupTypeConverter : IValueConverter
{
    public static readonly BoolToBackupTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // We're using this converter in the context of a SaveBackupInfo item,
        // so we need to determine the actual type based on the item's Description.
        // This will be called by the TextBlock binding in the UI.
        
        if (value is not bool isAutoBackup)
            return "Unknown";
            
        // We need to access the Description property of the DataContext
        var dataContext = parameter as SaveVaultApp.ViewModels.SaveBackupInfo;
        if (dataContext != null && dataContext.Description.StartsWith("Start Save"))
        {
            return "Start Save";
        }
        
        return isAutoBackup ? "Auto Save" : "Forced Save";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            // Parse comma-separated parameters: "TrueColor,FalseColor"
            string[] colors = paramString.Split(',');
            string colorStr = boolValue ? colors[0] : colors.Length > 1 ? colors[1] : "#FFFFFF";
            return new SolidColorBrush(Color.Parse(colorStr));
        }
        
        // Fallback behavior for older code
        if (value is bool isAutoBackup)
        {
            return isAutoBackup ? new SolidColorBrush(Color.Parse("#4CAF50")) : new SolidColorBrush(Color.Parse("#FF9800"));
        }
        return new SolidColorBrush(Colors.White);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToForegroundConverter : IValueConverter
{
    public static readonly BoolToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            if (Application.Current?.Resources == null) 
                return new SolidColorBrush(isEnabled ? Colors.White : Color.Parse("#666666"));
            
            var resources = Application.Current.Resources;
            
            Color textColor;
            if (resources.TryGetValue("TextColor", out object? color) && color is Color tc)
                textColor = tc;
            else
                textColor = Colors.White;

            Color secondaryTextColor;
            if (resources.TryGetValue("SecondaryTextColor", out object? secColor) && secColor is Color sc)
                secondaryTextColor = sc;
            else
                secondaryTextColor = Color.Parse("#666666");
            
            return isEnabled ? new SolidColorBrush(textColor) : new SolidColorBrush(secondaryTextColor);
        }
        return new SolidColorBrush(Colors.White);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DateTimeToTimeAgoConverter : IValueConverter
{
    public static readonly DateTimeToTimeAgoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime timestamp)
        {
            var timePassed = DateTime.Now - timestamp;
            
            if (timePassed.TotalMinutes < 1)
                return "Just now";
            if (timePassed.TotalMinutes < 60)
                return $"{(int)timePassed.TotalMinutes} minutes ago";
            if (timePassed.TotalHours < 24)
                return $"{(int)timePassed.TotalHours} hours ago";
            if (timePassed.TotalDays < 30)
                return $"{(int)timePassed.TotalDays} days ago";
            if (timePassed.TotalDays < 365)
                return $"{(int)(timePassed.TotalDays / 30)} months ago";
            
            return $"{(int)(timePassed.TotalDays / 365)} years ago";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToNextSaveTextConverter : IMultiValueConverter
{
    public static readonly BoolToNextSaveTextConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not bool isHovering || values[1] is not string nextSaveText)
            return string.Empty;

        return isHovering ? "Forced Save" : nextSaveText;
    }

    public object[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Add a new converter that examines the Description to determine save type
public class SaveTypeConverter : IValueConverter
{
    public static readonly SaveTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string description)
            return "Unknown";
            
        // Check the description to determine the appropriate save type label
        if (description.StartsWith("Start Save"))
            return "Start Save";
        else if (description.StartsWith("Auto save"))
            return "Auto Save";
        else if (description.StartsWith("Forced save"))
            return "Forced Save";
        
        // Default case
        return "Manual Save";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            // Parse comma-separated parameters: "TrueString,FalseString"
            string[] options = paramString.Split(',');
            return boolValue ? options[0] : options.Length > 1 ? options[1] : string.Empty;
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToGridLengthConverter : IValueConverter
{
    public static readonly BoolToGridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible && parameter is string valueStr)
        {
            var values = valueStr.Split(',');
            if (values.Length == 2)
            {
                // Use the appropriate string based on the boolean value
                var gridLengthStr = isVisible ? values[0].Trim() : values[1].Trim();
                
                // Handle specific values
                if (gridLengthStr == "0")
                {
                    return new Avalonia.Controls.GridLength(0);
                }
                else if (gridLengthStr.EndsWith("*"))
                {
                    // Handle star sizing
                    string numPart = gridLengthStr.TrimEnd('*');
                    double value1 = 1;
                    if (!string.IsNullOrEmpty(numPart))
                    {
                        double.TryParse(numPart, out value1);
                    }
                    return new Avalonia.Controls.GridLength(value1, Avalonia.Controls.GridUnitType.Star);
                }
                else if (double.TryParse(gridLengthStr, out double pixelValue))
                {
                    return new Avalonia.Controls.GridLength(pixelValue);
                }
            }
        }
        
        // Default to 1* if something went wrong
        return new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isVisible && isVisible ? 1.0 : 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToWidthConverter : IMultiValueConverter
{
    public static readonly BoolToWidthConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is bool isVisible && values[1] is double width)
        {
            return isVisible ? Double.NaN : 0; // NaN means use measured width, 0 means collapse
        }
        
        return Double.NaN;
    }
}

public class BoolToSidebarPathConverter : IValueConverter
{
    public static readonly BoolToSidebarPathConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // When sidebar is visible, show icon to hide it (left arrow)
        // When sidebar is hidden, show icon to show it (right arrow)
        return value is bool isVisible && isVisible 
            ? Geometry.Parse("M 10,0 L 0,5 L 10,10") // Left arrow (hide)
            : Geometry.Parse("M 0,0 L 10,5 L 0,10"); // Right arrow (show)
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToSidebarTooltipConverter : IValueConverter
{
    public static readonly BoolToSidebarTooltipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isVisible && isVisible ? "Hide sidebar" : "Show sidebar";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColumnConverter : IValueConverter
{
    public static readonly BoolToColumnConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // When sidebar is visible, place button in column 0
        // When sidebar is hidden, place button in column 1
        return value is bool isVisible && isVisible ? 0 : 1;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToAlignmentConverter : IValueConverter
{
    public static readonly BoolToAlignmentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // When sidebar is visible, button is on the right edge of sidebar
        // When sidebar is hidden, button is on the left edge of main content
        return value is bool isVisible && isVisible 
            ? Avalonia.Layout.HorizontalAlignment.Right 
            : Avalonia.Layout.HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToCornerRadiusConverter : IValueConverter
{
    public static readonly BoolToCornerRadiusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string paramStr)
        {
            var parts = paramStr.Split(':');
            if (parts.Length == 2)
            {
                var cornerRadiusStr = value is bool isVisible && isVisible ? parts[0] : parts[1];
                if (double.TryParse(cornerRadiusStr, out var uniformRadius))
                {
                    return new CornerRadius(uniformRadius);
                }
                
                var radiusValues = cornerRadiusStr.Split(',');
                if (radiusValues.Length == 4 &&
                    double.TryParse(radiusValues[0], out var topLeft) &&
                    double.TryParse(radiusValues[1], out var topRight) &&
                    double.TryParse(radiusValues[2], out var bottomRight) &&
                    double.TryParse(radiusValues[3], out var bottomLeft))
                {
                    return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
                }
            }
        }
        
        return new CornerRadius(3);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToMarginConverter : IValueConverter
{
    public static readonly BoolToMarginConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string paramStr)
        {
            var parts = paramStr.Split(':');
            if (parts.Length == 2)
            {
                var marginStr = value is bool isVisible && isVisible ? parts[0] : parts[1];
                
                var marginValues = marginStr.Split(',');
                if (marginValues.Length == 4 &&
                    double.TryParse(marginValues[0], out var left) &&
                    double.TryParse(marginValues[1], out var top) &&
                    double.TryParse(marginValues[2], out var right) &&
                    double.TryParse(marginValues[3], out var bottom))
                {
                    return new Thickness(left, top, right, bottom);
                }
            }
        }
        
        return new Thickness(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}