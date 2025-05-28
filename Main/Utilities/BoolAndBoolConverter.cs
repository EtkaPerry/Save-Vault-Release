using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Converter that takes two boolean inputs and combines them with logical AND.
    /// </summary>
    public class BoolAndBoolConverter : IMultiValueConverter
    {
        public static BoolAndBoolConverter Instance { get; } = new BoolAndBoolConverter();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // Need at least two boolean values to perform AND
            if (values.Count < 2)
                return false;
                
            bool result = true;
            foreach (var value in values)
            {
                if (value is bool boolValue)
                {
                    result = result && boolValue;
                }
                else
                {
                    // If any value is not a boolean, return false
                    return false;
                }
            }
            
            return result;
        }
    }
}
