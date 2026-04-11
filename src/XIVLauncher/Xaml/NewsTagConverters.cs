using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Material.Icons;

namespace XIVLauncher.Xaml;

public sealed class NewsTagToMaterialIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tag = value as string;
        return tag switch
        {
            "Important" => MaterialIconKind.AlertCircle,
            "Follow-up" => MaterialIconKind.InformationOutline,
            "DlError" => MaterialIconKind.LanDisconnect,
            _ => MaterialIconKind.Newspaper,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NewsTagToForegroundBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tag = value as string;
        return tag switch
        {
            "Important" => Brushes.Red,
            "Follow-up" => SolidColorBrush.Parse("#FFFFB900"),
            _ => AvaloniaProperty.UnsetValue,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
