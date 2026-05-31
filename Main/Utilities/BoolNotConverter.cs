using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SaveVaultApp.Utilities
{
    public static class BoolConverters
    {
        public static readonly BoolNotConverter Not = new();
    }

    public class BoolNotConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
