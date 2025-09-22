using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling; // For ThemeVariant

namespace AvaloniaApp.ViewModel;

public class AuthorToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageAuthor author)
        {
            return author == MessageAuthor.Me ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class AuthorToBackgroundBrushConverter : IValueConverter
{
    private static IBrush? TryGetBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var res) == true)
        {
            return res switch
            {
                IBrush b => b,
                Color c => new SolidColorBrush(c),
                _ => null
            };
        }
        return null;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MessageAuthor author)
            return TryGetBrush("SystemBaseLowColor") ?? new SolidColorBrush(Colors.LightGray);

        var theme = Application.Current?.ActualThemeVariant;

        // Accent palette keys (from Fluent BaseColorsPalette.xaml)
        // SystemAccentColor, SystemAccentColorLight1/2/3, SystemAccentColorDark1/2/3, SystemAccentColorForeground
        // Strategy:
        //  - "Me": primary accent (with slight dark/light variant for contrast depending on theme)
        //  - "Other": a lighter (light theme) or darker (dark theme) accent variant to differentiate while staying within palette

        if (author == MessageAuthor.AiAssistant)
            return new SolidColorBrush(Color.FromRgb(76, 217, 100));

        string key = author switch
        {
            MessageAuthor.Me => theme == ThemeVariant.Dark 
                ? "SystemAccentColorDark1" 
                : "SystemAccentColorLight1",
            MessageAuthor.AiAssistant => theme == ThemeVariant.Dark
                ? "SystemAccentColorDark2" 
                : "SystemAccentColorLight2",
            _ => theme == ThemeVariant.Dark
                ? "SystemAccentColorDark3"
                : "SystemAccentColorLight3"
        };

        return TryGetBrush(key)
               ?? TryGetBrush("SystemAccentColor")
               ?? TryGetBrush("SystemBaseLowColor")
               ?? new SolidColorBrush(author == MessageAuthor.Me ? Colors.CornflowerBlue : Colors.Gainsboro);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class AuthorToForegroundBrushConverter : IValueConverter
{
    private static IBrush? TryGetBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var res) == true)
        {
            var result =  res switch
            {
                IBrush b => b,
                Color c => new SolidColorBrush(c),
                _ => null
            };
            return result;
        }
        return null;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MessageAuthor author)
            return TryGetBrush("SystemBaseHighColor") ?? new SolidColorBrush(Colors.Black);

        // For our own messages, prefer the dedicated accent foreground (contrast guaranteed in palette)
        // For others, use a high or medium-high base foreground depending on theme.
        var theme = Application.Current?.ActualThemeVariant;

        if (author == MessageAuthor.Me)
        {
            return TryGetBrush("SystemChromeLowColor")
                   ?? new SolidColorBrush(theme == ThemeVariant.Dark ? Colors.White : Colors.Black);
        }

        // Other party message foreground
        string fallbackKey = theme == ThemeVariant.Dark ? "SystemBaseHighColor" : "SystemBaseMediumHighColor";
        return TryGetBrush("SystemChromeMediumColor")
            ?? new SolidColorBrush(theme == ThemeVariant.Dark ? Colors.White : Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BooleanToTextConverter : IValueConverter
{
    // parameter format: "TrueValue|FalseValue"
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|');
        if (parts == null || parts.Length != 2)
            return value is true ? "Working..." : string.Empty;
        return value is true ? parts[0] : parts[1];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
