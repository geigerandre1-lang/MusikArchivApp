using System;
using System.Windows.Media;

namespace MusikArchivApp.Converters
{
    internal static class ColorContrastHelper
    {
        public static Color ParseHexColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        public static Brush GetForegroundBrush(string? backgroundHex)
        {
            var background = ParseHexColor(backgroundHex, Colors.White);
            return GetForegroundBrush(background);
        }

        public static Brush GetForegroundBrush(Color background)
        {
            return GetRelativeLuminance(background) > 0.55 ? Brushes.Black : Brushes.White;
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Linearize(byte channel)
            {
                var value = channel / 255.0;
                return value <= 0.03928
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(color.R)
                 + 0.7152 * Linearize(color.G)
                 + 0.0722 * Linearize(color.B);
        }
    }
}
