using System;
using System.Globalization;
using System.Windows.Data;

namespace MusikArchivApp.Converters
{
    public class HexColorToForegroundBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ColorContrastHelper.GetForegroundBrush(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
