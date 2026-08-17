using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using MusikArchivApp.Data;
using MusikArchivApp.Models;

namespace MusikArchivApp
{
    public partial class SheetImportAssignmentDialog : Window
    {
        private readonly Piece piece;
        private readonly string sourceFilePath;
        private readonly ObservableCollection<InstrumentImportOption> instrumentOptions = new();

        public bool Saved { get; private set; }
        public IReadOnlyList<SheetImportTarget> Targets { get; private set; } = new List<SheetImportTarget>();

        public SheetImportAssignmentDialog(Piece piece, string sourceFilePath, IReadOnlyList<Instrument> pieceInstruments)
        {
            this.piece = piece;
            this.sourceFilePath = sourceFilePath;
            InitializeComponent();

            SourceFileText.Text = sourceFilePath;
            foreach (var instrument in pieceInstruments.OrderBy(i => i.Name))
            {
                var option = new InstrumentImportOption(instrument);
                option.PropertyChanged += (_, _) => UpdatePreview();
                instrumentOptions.Add(option);
            }

            InstrumentsList.ItemsSource = instrumentOptions;
            UpdatePreview();
        }

        private void GeneralCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (GeneralCheckBox.IsChecked == true)
            {
                foreach (var option in instrumentOptions)
                {
                    option.IsSelected = false;
                }
            }

            UpdatePreview();
        }

        private void InstrumentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (instrumentOptions.Any(o => o.IsSelected))
            {
                GeneralCheckBox.IsChecked = false;
            }

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var extension = Path.GetExtension(sourceFilePath);
            var targets = BuildTargets(extension);
            PreviewText.Text = targets.Count == 0
                ? "Bitte mindestens eine Option wählen."
                : string.Join("\n", targets.Select(t => t.FileName));
        }

        private List<SheetImportTarget> BuildTargets(string extension)
        {
            var targets = new List<SheetImportTarget>();

            if (GeneralCheckBox.IsChecked == true)
            {
                targets.Add(new SheetImportTarget
                {
                    FileName = SheetMusicPaths.GenerateFileName(piece, "Gesamt", extension),
                    InstrumentId = null,
                    InstrumentGroupId = null
                });
                return targets;
            }

            foreach (var option in instrumentOptions.Where(o => o.IsSelected))
            {
                targets.Add(new SheetImportTarget
                {
                    FileName = SheetMusicPaths.GenerateFileName(piece, option.Instrument.Name, extension),
                    InstrumentId = option.Instrument.Id,
                    InstrumentGroupId = null,
                    InstrumentName = option.Instrument.Name
                });
            }

            return targets;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var extension = Path.GetExtension(sourceFilePath);
            var targets = BuildTargets(extension);
            if (targets.Count == 0)
            {
                UiMessage.Show("Bitte mindestens ein Instrument oder „Allgemein / Gesamt“ wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Targets = targets;
            Saved = true;
            DialogResult = true;
            Close();
        }
    }

    public class InstrumentImportOption : INotifyPropertyChanged
    {
        private bool isSelected;

        public InstrumentImportOption(Instrument instrument)
        {
            Instrument = instrument;
        }

        public Instrument Instrument { get; }
        public string Name => Instrument.Name;

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

    public class SheetImportTarget
    {
        public string FileName { get; init; } = string.Empty;
        public long? InstrumentId { get; init; }
        public int? InstrumentGroupId { get; init; }
        public string? InstrumentName { get; init; }
    }
}
