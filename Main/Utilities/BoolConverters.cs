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