using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Converter that compares two strings and returns true if they are equal
    /// </summary>
    public class StringCompareConverter : IValueConverter
    {
        public static StringCompareConverter Instance { get; } = new StringCompareConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string compareValue)
            {
                return string.Equals(stringValue, compareValue, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
