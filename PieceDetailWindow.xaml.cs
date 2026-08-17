using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MusikArchivApp.Models;
using MusikArchivApp.Printing;

namespace MusikArchivApp
{
    public enum PieceDetailResult
    {
        Closed,
        Saved,
        Deleted,
        OpenInEditor
    }

    public partial class PieceDetailWindow : Window
    {
        private readonly PieceDetailHost host;
        private Piece workingPiece = null!;
        private Piece? editSnapshot;
        private Dictionary<long, bool>? editInstrumentSnapshot;

        public PieceDetailResult Result { get; private set; } = PieceDetailResult.Closed;

        public PieceDetailWindow(PieceDetailHost host)
        {
            this.host = host;
            workingPiece = ClonePiece(host.Piece);
            InitializeComponent();
            DataContext = workingPiece;

            RefreshBesetzungView();

            TagsEditListBox.ItemsSource = host.AvailableTags;
            GenresEditListBox.ItemsSource = host.AvailableGenres;
            CabinetEditComboBox.ItemsSource = host.AvailableCabinets;
            CabinetEditComboBox.DisplayMemberPath = nameof(CabinetOption.Name);
            CompartmentEditComboBox.ItemsSource = host.AvailableCompartments;
            SlotEditComboBox.ItemsSource = host.AvailableSlots;

            SetupInstrumentEditLists();
            SetEditMode(false);
        }

        private void SetupInstrumentEditLists()
        {
            InstrumentsEditGroup1.ItemsSource = host.InstrumentSelections
                .Where(i => host.Group1InstrumentNames.Contains(i.Instrument.Name));
            InstrumentsEditGroup2.ItemsSource = host.InstrumentSelections
                .Where(i => host.Group2InstrumentNames.Contains(i.Instrument.Name));
            InstrumentsEditGroup3.ItemsSource = host.InstrumentSelections
                .Where(i => host.Group3InstrumentNames.Contains(i.Instrument.Name));
            InstrumentsEditGroup4.ItemsSource = host.InstrumentSelections
                .Where(i => host.Group4InstrumentNames.Contains(i.Instrument.Name));
        }

        private void RefreshBesetzungView()
        {
            Group1List.ItemsSource = SelectedInstrumentNames(host.Group1InstrumentNames);
            Group2List.ItemsSource = SelectedInstrumentNames(host.Group2InstrumentNames);
            Group3List.ItemsSource = SelectedInstrumentNames(host.Group3InstrumentNames);
            Group4List.ItemsSource = SelectedInstrumentNames(host.Group4InstrumentNames);
        }

        private List<string> SelectedInstrumentNames(IReadOnlyList<string> groupNames)
            => host.InstrumentSelections
                .Where(i => groupNames.Contains(i.Instrument.Name) && i.IsSelected)
                .Select(i => i.Instrument.Name)
                .OrderBy(n => n)
                .ToList();

        private void SetEditMode(bool editing)
        {
            ViewPanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            EditPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            BesetzungViewPanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            ViewButtonsPanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            EditButtonsPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            Title = editing ? "Musikstück – Schnellbearbeitung" : "Musikstück – Details";

            if (editing)
            {
                ApplyTagsFromPieceToUi();
                ApplyGenresFromPieceToUi();
            }
        }

        private void InlineEditButton_Click(object sender, RoutedEventArgs e)
        {
            editSnapshot = ClonePiece(workingPiece);
            SnapshotInstrumentSelections();
            SetEditMode(true);
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (editSnapshot != null)
            {
                workingPiece = ClonePiece(editSnapshot);
                DataContext = null;
                DataContext = workingPiece;
                editSnapshot = null;
            }

            RestoreInstrumentSelections();
            SetEditMode(false);
        }

        private async void SaveEditButton_Click(object sender, RoutedEventArgs e)
        {
            SyncTagsFromUiToPiece();
            SyncGenresFromUiToPiece();

            if (!ValidateInlineEdit())
            {
                return;
            }

            SaveEditButton.IsEnabled = false;
            try
            {
                await host.SavePieceAsync(workingPiece, host.InstrumentSelections).ConfigureAwait(true);
                await host.RefreshPieceMetadataAsync(workingPiece).ConfigureAwait(true);
                editSnapshot = null;
                editInstrumentSnapshot = null;
                DataContext = null;
                DataContext = workingPiece;
                RefreshBesetzungView();
                SetEditMode(false);
                Result = PieceDetailResult.Saved;
                UiMessage.Show("Musikstück gespeichert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveEditButton.IsEnabled = true;
            }
        }

        private void OpenInEditorButton_Click(object sender, RoutedEventArgs e)
        {
            host.OpenInEditor(workingPiece.Id);
            Result = PieceDetailResult.OpenInEditor;
            Close();
        }

        private async void PrintLabelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = await host.GetWebViewUrlAsync(workingPiece.Id).ConfigureAwait(true);
                var label = FolderLabelData.FromPiece(workingPiece, url);
                if (string.IsNullOrWhiteSpace(label.CabinetColor) && !string.IsNullOrWhiteSpace(workingPiece.Cabinet))
                {
                    label.CabinetColor = host.AvailableCabinets
                        .FirstOrDefault(c => c.Name == workingPiece.Cabinet)?.Color;
                }

                FolderLabelPrintService.PrintLabels(this, new[] { label });
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Label konnte nicht gedruckt werden: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSheetMusicButton_Click(object sender, RoutedEventArgs e)
        {
            host.OpenSheetMusic(workingPiece);
        }

        private async void OpenWebViewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = await host.GetWebViewUrlAsync(workingPiece.Id).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(url))
                {
                    UiMessage.Show(
                        "Web-Link konnte nicht erstellt werden. Bitte Server-URL in den Einstellungen prüfen und ggf. zuerst zum Server hochladen.",
                        "Hinweis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Web-Ansicht konnte nicht geöffnet werden: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton.IsEnabled = false;
            try
            {
                var deleted = await host.DeletePieceAsync(workingPiece.Id, workingPiece.Title).ConfigureAwait(true);
                if (deleted)
                {
                    Result = PieceDetailResult.Deleted;
                    Close();
                }
            }
            finally
            {
                DeleteButton.IsEnabled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private bool ValidateInlineEdit()
        {
            if (string.IsNullOrWhiteSpace(workingPiece.Title))
            {
                UiMessage.Show("Bitte einen Titel eingeben.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (CabinetEditComboBox.SelectedItem == null)
            {
                UiMessage.Show("Bitte einen Schrank auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (CompartmentEditComboBox.SelectedItem == null)
            {
                UiMessage.Show("Bitte ein Fach auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (SlotEditComboBox.SelectedItem == null)
            {
                UiMessage.Show("Bitte einen Einschub auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (GenresEditListBox.SelectedItems.Count == 0)
            {
                UiMessage.Show("Bitte mindestens eine Musikgattung auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void SyncTagsFromUiToPiece()
        {
            var selected = TagsEditListBox.SelectedItems.Cast<string>().ToList();
            workingPiece.Tags = selected.Count == 0 ? null : "#" + string.Join("#", selected) + "#";
        }

        private void SyncGenresFromUiToPiece()
        {
            var selected = GenresEditListBox.SelectedItems.Cast<string>().ToList();
            workingPiece.Genre = selected.Count == 0 ? null : "#" + string.Join("#", selected) + "#";
        }

        private void ApplyTagsFromPieceToUi()
        {
            TagsEditListBox.SelectedItems.Clear();
            if (string.IsNullOrWhiteSpace(workingPiece.Tags))
            {
                return;
            }

            var parts = workingPiece.Tags.Split('#').Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet();
            foreach (var item in host.AvailableTags)
            {
                if (parts.Contains(item))
                {
                    TagsEditListBox.SelectedItems.Add(item);
                }
            }
        }

        private void ApplyGenresFromPieceToUi()
        {
            GenresEditListBox.SelectedItems.Clear();
            if (string.IsNullOrWhiteSpace(workingPiece.Genre))
            {
                return;
            }

            var parts = workingPiece.Genre.Split('#').Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet();
            foreach (var item in host.AvailableGenres)
            {
                if (parts.Contains(item))
                {
                    GenresEditListBox.SelectedItems.Add(item);
                }
            }
        }

        private void SnapshotInstrumentSelections()
        {
            editInstrumentSnapshot = host.InstrumentSelections.ToDictionary(s => s.Instrument.Id, s => s.IsSelected);
        }

        private void RestoreInstrumentSelections()
        {
            if (editInstrumentSnapshot == null)
            {
                return;
            }

            foreach (var selection in host.InstrumentSelections)
            {
                if (editInstrumentSnapshot.TryGetValue(selection.Instrument.Id, out var isSelected))
                {
                    selection.IsSelected = isSelected;
                }
            }

            editInstrumentSnapshot = null;
        }

        private static Piece ClonePiece(Piece source) => new()
        {
            Id = source.Id,
            Title = source.Title,
            Composer = source.Composer,
            Arranger = source.Arranger,
            Publisher = source.Publisher,
            Isbn = source.Isbn,
            Tags = source.Tags,
            Genre = source.Genre,
            Cabinet = source.Cabinet,
            Compartment = source.Compartment,
            Slot = source.Slot,
            IsActive = source.IsActive,
            FolderPath = source.FolderPath,
            CabinetColor = source.CabinetColor,
            Besetzung = source.Besetzung
        };
    }
}
