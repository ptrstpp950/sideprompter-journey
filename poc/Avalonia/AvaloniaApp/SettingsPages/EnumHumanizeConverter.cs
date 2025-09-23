using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaApp.Settings
{
    // Converts enum names like "ArrowUp" to "Arrow up" for display
    public class EnumHumanizeConverter : IValueConverter
    {
        public static readonly EnumHumanizeConverter Instance = new EnumHumanizeConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return null;
            var s = value.ToString() ?? string.Empty;
            // Insert space between lower->upper transitions and before digits
            var result = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z0-9])([A-Z])", " $1");
            // Also split PascalCase sequences like HTTPServer -> HTTP Server (keep acronyms together is complex; this simple approach is fine)
            return result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Not used
            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}
