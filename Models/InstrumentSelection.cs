using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusikArchivApp.Models
{
    public class InstrumentSelection : INotifyPropertyChanged
    {
        private bool isSelected;

        public Instrument Instrument { get; }

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

        public InstrumentSelection(Instrument instrument)
        {
            Instrument = instrument;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
