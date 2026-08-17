using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusikArchivApp.Models
{
    public class LabelPrintRow : INotifyPropertyChanged
    {
        private bool isSelected;

        public LabelPrintRow(Piece piece)
        {
            Piece = piece;
        }

        public Piece Piece { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
