using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Converter that compares an integer value with a parameter
    /// </summary>
    public class IntEqualConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;
                
            if (value is int intValue && int.TryParse(parameter.ToString(), out int intParam))
            {
                return intValue == intParam;
            }
            
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}