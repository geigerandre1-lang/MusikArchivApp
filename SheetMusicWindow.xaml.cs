using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MusikArchivApp.Data;
using MusikArchivApp.Models;

namespace MusikArchivApp
{
    public partial class SheetMusicWindow : Window
    {
        private readonly Piece piece;
        private readonly SheetMusicRepository sheetRepository;
        private readonly IReadOnlyList<Instrument> pieceInstruments;
        private readonly ObservableCollection<SheetFile> files = new();
        private readonly List<SheetAssignmentOption> assignmentOptions = new();
        private readonly string pdfPreviewHost;

        private bool webViewReady;
        private bool showingPdf;
        private string? currentPreviewPath;

        public SheetMusicWindow(
            Piece piece,
            SheetMusicRepository sheetRepository,
            IEnumerable<Instrument> allInstruments,
            IEnumerable<long> pieceInstrumentIds)
        {
            this.piece = piece;
            this.sheetRepository = sheetRepository;
            var idSet = pieceInstrumentIds.ToHashSet();
            pieceInstruments = allInstruments.Where(i => idSet.Contains(i.Id)).OrderBy(i => i.Name).ToList();
            pdfPreviewHost = $"noten-piece-{piece.Id}.local";

            InitializeComponent();

            Title = $"Digitale Noten – {piece.Title}";
            TitleText.Text = piece.Title;
            PathText.Text = piece.FolderPath ?? SheetMusicPaths.BuildLogicalPath(piece);

            FilesListBox.ItemsSource = files;
            BuildAssignmentOptions(allInstruments);
            AssignmentComboBox.ItemsSource = assignmentOptions;
            AssignmentComboBox.SelectedIndex = 0;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializePdfPreviewAsync().ConfigureAwait(true);
            await ReloadFilesAsync().ConfigureAwait(true);
        }

        private async Task InitializePdfPreviewAsync()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.GetWebViewDirectory())
                    .ConfigureAwait(true);
                await PdfPreview.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
                webViewReady = PdfPreview.CoreWebView2 != null;
            }
            catch (Exception ex)
            {
                webViewReady = false;
                System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex.Message}");
            }
        }

        private void BuildAssignmentOptions(IEnumerable<Instrument> instruments)
        {
            assignmentOptions.Clear();
            assignmentOptions.Add(SheetAssignmentOption.General);
            assignmentOptions.Add(SheetAssignmentOption.Group(1, "Gruppe: Partitur / Direktion"));
            assignmentOptions.Add(SheetAssignmentOption.Group(2, "Gruppe: Holz"));
            assignmentOptions.Add(SheetAssignmentOption.Group(3, "Gruppe: Schlagwerk"));
            assignmentOptions.Add(SheetAssignmentOption.Group(4, "Gruppe: Blechbläser / Gesang"));

            foreach (var instrument in instruments.OrderBy(i => i.Name))
            {
                assignmentOptions.Add(SheetAssignmentOption.Instrument(instrument.Id, instrument.Name));
            }
        }

        private async Task ReloadFilesAsync()
        {
            files.Clear();
            var loaded = await sheetRepository.GetFilesForPieceAsync(piece.Id).ConfigureAwait(true);
            foreach (var file in loaded)
            {
                files.Add(file);
            }

            if (files.Count > 0)
            {
                FilesListBox.SelectedIndex = 0;
            }
            else
            {
                ClearPreview();
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            await ImportFilesAsync(paths).ConfigureAwait(true);
        }

        private async void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Notendatei hinzufügen",
                Filter = "Noten (PDF, Bilder)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|Alle Dateien|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await ImportFilesAsync(dialog.FileNames).ConfigureAwait(true);
        }

        private async Task ImportFilesAsync(IEnumerable<string> filePaths)
        {
            try
            {
                foreach (var filePath in filePaths)
                {
                    if (!SheetMusicPaths.IsSupportedExtension(filePath) || !File.Exists(filePath))
                    {
                        continue;
                    }

                    var dialog = new SheetImportAssignmentDialog(piece, filePath, pieceInstruments)
                    {
                        Owner = this
                    };

                    if (dialog.ShowDialog() != true || !dialog.Saved)
                    {
                        continue;
                    }

                    foreach (var target in dialog.Targets)
                    {
                        await sheetRepository.AddFileAsync(
                            piece.Id,
                            filePath,
                            target.FileName,
                            target.InstrumentId,
                            target.InstrumentGroupId).ConfigureAwait(true);
                    }
                }

                await ReloadFilesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Fehler beim Hinzufügen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RemoveFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not SheetFile selected)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Datei \"{selected.FileName}\" wirklich entfernen?",
                "Entfernen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await sheetRepository.DeleteFileAsync(selected).ConfigureAwait(true);
                await ReloadFilesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Fehler beim Entfernen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not SheetFile selected)
            {
                return;
            }

            if (AssignmentComboBox.SelectedItem is not SheetAssignmentOption option)
            {
                return;
            }

            try
            {
                await sheetRepository.UpdateAssignmentAsync(selected.Id, option.InstrumentId, option.InstrumentGroupId)
                    .ConfigureAwait(true);
                await ReloadFilesAsync().ConfigureAwait(true);
                FilesListBox.SelectedItem = files.FirstOrDefault(f => f.Id == selected.Id);
                UiMessage.Show("Zuweisung gespeichert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void FilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not SheetFile selected)
            {
                ClearPreview();
                return;
            }

            SelectAssignmentForFile(selected);
            await ShowPreviewAsync(selected).ConfigureAwait(true);
        }

        private void SelectAssignmentForFile(SheetFile file)
        {
            SheetAssignmentOption? match;
            if (file.InstrumentId.HasValue)
            {
                match = assignmentOptions.FirstOrDefault(o => o.InstrumentId == file.InstrumentId);
            }
            else if (file.InstrumentGroupId.HasValue)
            {
                match = assignmentOptions.FirstOrDefault(o => o.InstrumentGroupId == file.InstrumentGroupId);
            }
            else
            {
                match = SheetAssignmentOption.General;
            }

            AssignmentComboBox.SelectedItem = match ?? SheetAssignmentOption.General;
        }

        private async Task ShowPreviewAsync(SheetFile file)
        {
            var content = await sheetRepository.GetFileContentAsync(file.Id).ConfigureAwait(true);
            if (content == null || content.Length == 0)
            {
                ClearPreview();
                PreviewPlaceholder.Text = "Dateiinhalt nicht in der Datenbank gefunden.";
                ShowPlaceholder();
                return;
            }

            HidePlaceholder();
            showingPdf = false;
            currentPreviewPath = SheetPreviewCache.WriteTempFile(file.Id, file.FileName, content);

            if (file.IsImage)
            {
                HidePdfPreview();
                try
                {
                    using var stream = new MemoryStream(content);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap;
                    ImageScrollViewer.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    ClearPreview();
                    PreviewPlaceholder.Text = $"Bildvorschau fehlgeschlagen: {ex.Message}";
                    ShowPlaceholder();
                }

                return;
            }

            if (file.IsPdf)
            {
                ShowPdfPreview(currentPreviewPath);
                return;
            }

            ClearPreview();
            PreviewPlaceholder.Text = "Vorschau für dieses Format nicht unterstützt.";
            ShowPlaceholder();
        }

        private void ShowPdfPreview(string fullPath)
        {
            PreviewImage.Source = null;
            ImageScrollViewer.Visibility = Visibility.Collapsed;

            if (!webViewReady || PdfPreview.CoreWebView2 == null)
            {
                ClearPreview();
                PreviewPlaceholder.Text = "PDF-Vorschau nicht verfügbar.\nBitte WebView2 Runtime installieren.";
                ShowPlaceholder();
                return;
            }

            try
            {
                var normalizedPath = Path.GetFullPath(fullPath);
                var folder = Path.GetDirectoryName(normalizedPath)!;
                var fileName = Path.GetFileName(normalizedPath);

                PdfPreview.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    pdfPreviewHost,
                    folder,
                    CoreWebView2HostResourceAccessKind.Allow);

                PdfPreview.Visibility = Visibility.Visible;
                PdfPreview.CoreWebView2.Navigate($"https://{pdfPreviewHost}/{Uri.EscapeDataString(fileName)}");
                showingPdf = true;
                HidePlaceholder();
            }
            catch (Exception ex)
            {
                ClearPreview();
                PreviewPlaceholder.Text = $"PDF-Vorschau fehlgeschlagen: {ex.Message}";
                ShowPlaceholder();
            }
        }

        private void HidePdfPreview()
        {
            if (PdfPreview.CoreWebView2 != null)
            {
                PdfPreview.CoreWebView2.Navigate("about:blank");
            }

            PdfPreview.Visibility = Visibility.Collapsed;
            showingPdf = false;
        }

        private void ShowPlaceholder()
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }

        private void HidePlaceholder()
        {
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void ClearPreview()
        {
            PreviewImage.Source = null;
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            HidePdfPreview();
            currentPreviewPath = null;
            PreviewPlaceholder.Text = "Datei auswählen oder per Drag & Drop hinzufügen.";
            ShowPlaceholder();
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.GetDataRoot(),
                UseShellExecute = true
            });
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not SheetFile selected)
            {
                UiMessage.Show("Bitte zuerst eine Datei auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (selected.IsPdf && webViewReady && PdfPreview.CoreWebView2 != null && showingPdf)
                {
                    PdfPreview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
                    return;
                }

                if ((selected.IsPdf || selected.IsImage) && !string.IsNullOrWhiteSpace(currentPreviewPath) && File.Exists(currentPreviewPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = currentPreviewPath,
                        Verb = "print",
                        UseShellExecute = true
                    });
                    return;
                }

                UiMessage.Show("Für diese Datei ist kein Druck verfügbar.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiMessage.Show($"Fehler beim Drucken: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
