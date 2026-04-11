using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace XIVLauncher.Xaml
{
    public class BoolToIsVisibleConverter : IValueConverter
    {
        public static readonly BoolToIsVisibleConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true;
        }
    }
}
