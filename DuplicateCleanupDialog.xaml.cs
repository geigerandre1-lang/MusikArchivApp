using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MusikArchivApp.Data;
using MusikArchivApp.Models;

namespace MusikArchivApp
{
    public partial class DuplicateCleanupDialog : Window
    {
        private readonly DuplicateCleanupService cleanupService;

        public ObservableCollection<DuplicatePieceGroupItem> PieceGroups { get; } = new();
        public ObservableCollection<DuplicateSheetGroupItem> SheetGroups { get; } = new();

        public bool ChangesMade { get; private set; }

        public DuplicateCleanupDialog(string connectionString)
        {
            cleanupService = new DuplicateCleanupService(connectionString);
            InitializeComponent();
            WindowIcons.Apply(this);
            PieceGroupsGrid.ItemsSource = PieceGroups;
            SheetGroupsGrid.ItemsSource = SheetGroups;
            Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync().ConfigureAwait(true);
        }

        private async Task RefreshAsync()
        {
            var pieceGroups = await cleanupService.FindPieceDuplicateGroupsAsync().ConfigureAwait(true);
            var sheetGroups = await cleanupService.FindSheetDuplicateGroupsAsync().ConfigureAwait(true);

            PieceGroups.Clear();
            foreach (var group in pieceGroups)
            {
                PieceGroups.Add(new DuplicatePieceGroupItem(group));
            }

            SheetGroups.Clear();
            foreach (var group in sheetGroups)
            {
                SheetGroups.Add(new DuplicateSheetGroupItem(group));
            }

            UpdateSummary();
            UpdateButtons();
        }

        private void UpdateSummary()
        {
            SummaryText.Text =
                $"{PieceGroups.Count} Stück-Gruppen · {SheetGroups.Count} Noten-Gruppen";
        }

        private void UpdateButtons()
        {
            CleanupPiecesButton.IsEnabled = PieceGroups.Count > 0;
            CleanupSheetsButton.IsEnabled = SheetGroups.Count > 0;
            CleanupAllButton.IsEnabled = PieceGroups.Count > 0 || SheetGroups.Count > 0;
        }

        private async void CleanupPiecesButton_Click(object sender, RoutedEventArgs e)
        {
            await CleanupPieceGroupsAsync(PieceGroups.ToList()).ConfigureAwait(true);
        }

        private async void CleanupSheetsButton_Click(object sender, RoutedEventArgs e)
        {
            await CleanupSheetGroupsAsync(SheetGroups.ToList()).ConfigureAwait(true);
        }

        private async void CleanupAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (PieceGroups.Count == 0 && SheetGroups.Count == 0)
            {
                return;
            }

            var message =
                $"Es werden bis zu {PieceGroups.Count} Stück-Gruppen und {SheetGroups.Count} Noten-Gruppen bereinigt.\n\nFortfahren?";
            if (UiMessage.Confirm(message, "Duplikate bereinigen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            await CleanupPieceGroupsAsync(PieceGroups.ToList()).ConfigureAwait(true);
            await CleanupSheetGroupsAsync(SheetGroups.ToList()).ConfigureAwait(true);
        }

        private async Task CleanupPieceGroupsAsync(IReadOnlyList<DuplicatePieceGroupItem> groups)
        {
            if (groups.Count == 0)
            {
                return;
            }

            if (groups.Count > 1
                && UiMessage.Confirm(
                    $"{groups.Count} Stück-Duplikatgruppen bereinigen?",
                    "Duplikate bereinigen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var totalRemoved = 0;
            var totalMerged = 0;
            foreach (var group in groups)
            {
                var removeIds = group.Entries.Select(entry => entry.Id).Where(id => id != group.SelectedKeepId).ToList();
                var result = await cleanupService.CleanupPieceGroupAsync(group.SelectedKeepId, removeIds).ConfigureAwait(true);
                totalRemoved += result.RemovedPieces;
                totalMerged += result.MergedSheets;
            }

            if (totalRemoved > 0)
            {
                ChangesMade = true;
                UiMessage.Show(
                    $"{totalRemoved} doppelte Stücke entfernt.\n{totalMerged} Notendateien wurden auf die behaltenen Stücke übernommen.",
                    "Duplikate bereinigen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await RefreshAsync().ConfigureAwait(true);
        }

        private async Task CleanupSheetGroupsAsync(IReadOnlyList<DuplicateSheetGroupItem> groups)
        {
            if (groups.Count == 0)
            {
                return;
            }

            if (groups.Count > 1
                && UiMessage.Confirm(
                    $"{groups.Count} Noten-Duplikatgruppen bereinigen?",
                    "Duplikate bereinigen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var totalRemoved = 0;
            foreach (var group in groups)
            {
                var removeIds = group.Entries.Select(entry => entry.Id).Where(id => id != group.SelectedKeepId).ToList();
                var result = await cleanupService.CleanupSheetGroupAsync(group.SelectedKeepId, removeIds).ConfigureAwait(true);
                totalRemoved += result.RemovedSheets;
            }

            if (totalRemoved > 0)
            {
                ChangesMade = true;
                UiMessage.Show(
                    $"{totalRemoved} doppelte Notendateien entfernt.",
                    "Duplikate bereinigen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await RefreshAsync().ConfigureAwait(true);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = ChangesMade;
            Close();
        }

        public sealed class DuplicatePieceGroupItem
        {
            public DuplicatePieceGroupItem(DuplicatePieceGroup group)
            {
                Group = group;
                SelectedKeepId = group.RecommendedKeepId;
            }

            public DuplicatePieceGroup Group { get; }

            public string Summary => Group.Summary;

            public int Count => Group.Entries.Count;

            public IReadOnlyList<DuplicatePieceEntry> Entries => Group.Entries;

            public long SelectedKeepId { get; set; }
        }

        public sealed class DuplicateSheetGroupItem
        {
            public DuplicateSheetGroupItem(DuplicateSheetGroup group)
            {
                Group = group;
                SelectedKeepId = group.RecommendedKeepId;
            }

            public DuplicateSheetGroup Group { get; }

            public string Summary => Group.Summary;

            public int Count => Group.Entries.Count;

            public IReadOnlyList<DuplicateSheetEntry> Entries => Group.Entries;

            public long SelectedKeepId { get; set; }
        }
    }
}
