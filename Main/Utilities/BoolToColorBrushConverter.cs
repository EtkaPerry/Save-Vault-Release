using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SaveVaultApp.Utilities
{
    public class BoolToColorBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string colors)
            {
                string[] colorValues = colors.Split(',');
                if (colorValues.Length == 2)
                {
                    string colorStr = boolValue ? colorValues[0] : colorValues[1];
                    try
                    {
                        return SolidColorBrush.Parse(colorStr);
                    }
                    catch
                    {
                        return new SolidColorBrush(Colors.White);
                    }
                }
            }
            
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}