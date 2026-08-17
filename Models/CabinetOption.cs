using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MusikArchivApp.Models
{
    public class CabinetOption : INotifyPropertyChanged
    {
        private string color = "#FFFFFF";

        public string Name { get; set; } = string.Empty;

        public string Color
        {
            get => color;
            set
            {
                color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayBrush));
            }
        }

        public SolidColorBrush DisplayBrush
        {
            get
            {
                try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color)); }
                catch { return new SolidColorBrush(Colors.LightGray); }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
