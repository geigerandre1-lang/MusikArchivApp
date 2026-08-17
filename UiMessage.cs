using System.Windows;

namespace MusikArchivApp
{
    public static class UiMessage
    {
        public static void Show(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                MessageBox.Show(message, title, button, icon));
        }

        public static MessageBoxResult Confirm(string message, string title, MessageBoxButton button = MessageBoxButton.YesNo, MessageBoxImage icon = MessageBoxImage.Warning)
        {
            var result = MessageBoxResult.None;
            Application.Current?.Dispatcher.Invoke(() =>
                result = MessageBox.Show(message, title, button, icon));
            return result;
        }
    }
}
