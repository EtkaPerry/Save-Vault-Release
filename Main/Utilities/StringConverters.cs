using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SaveVaultApp.Utilities
{
    public class StringNotEmptyConverter : IValueConverter
    {
        public static readonly StringNotEmptyConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringEmptyConverter : IValueConverter
    {
        public static readonly StringEmptyConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public static class StringConverters
    {
        public static StringNotEmptyConverter IsNotNullOrEmpty => StringNotEmptyConverter.Instance;
        public static StringEmptyConverter IsNullOrEmpty => StringEmptyConverter.Instance;
    }
}
