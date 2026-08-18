using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MusikArchivApp
{
    internal static class WindowIcons
    {
        public static void Apply(Window window)
        {
            var icon = TryLoadIcon();
            if (icon != null)
            {
                window.Icon = icon;
            }
        }

        private static BitmapFrame? TryLoadIcon()
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(filePath))
            {
                return BitmapFrame.Create(new Uri(filePath, UriKind.Absolute));
            }

            try
            {
                return BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }
    }
}
