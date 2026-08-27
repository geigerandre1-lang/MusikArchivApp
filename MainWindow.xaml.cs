using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MusikArchivApp.Data;
using MusikArchivApp.Localization;
using MusikArchivApp.Models;
using MusikArchivApp.Printing;

namespace MusikArchivApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly PieceRepository repository;
        private readonly SheetMusicRepository sheetMusicRepository;
        private readonly SyncService syncService;
        private readonly SyncRepository syncRepository;
        private SyncConfig syncConfig = new();
        private AppUpdateInfo? pendingAppUpdate;
        private CancellationTokenSource? syncCancellation;
        private SyncOperation activeSyncOperation = SyncOperation.None;
        private DateTime lastSyncProgressUiUpdate = DateTime.MinValue;
        private Piece currentPiece = new Piece();
        private bool isAdminLoggedIn;
        private const string AdminPassword = "Admin17";

        /// <summary>Delegiert an <see cref="AppResources.Current"/> und löst FilterOperatorOptions-Refresh aus.</summary>
        public string AppLanguage
        {
            get => AppResources.Current.Language;
            set
            {
                AppResources.Current.Language = value;
                OnPropertyChanged();
                // Operator-ComboBoxen zeigen jetzt lokalisierten Text – Items neu zeichnen lassen
                var tmp = FilterOperatorOptions.ToList();
                FilterOperatorOptions.Clear();
                foreach (var op in tmp) FilterOperatorOptions.Add(op);
            }
        }

        public ObservableCollection<InstrumentSelection> Instruments { get; } = new ObservableCollection<InstrumentSelection>();
        public ObservableCollection<Piece> Pieces { get; } = new ObservableCollection<Piece>();
        public ObservableCollection<Piece> FilteredPieces { get; } = new ObservableCollection<Piece>();
        public ObservableCollection<LabelPrintRow> LabelPrintRows { get; } = new ObservableCollection<LabelPrintRow>();
        public ObservableCollection<LabelPrintRow> LabelSelectedRows { get; } = new ObservableCollection<LabelPrintRow>();

        private readonly HashSet<long> labelSelectedPieceIds = new();
        private bool suppressLabelSelectionSync;

        public ObservableCollection<string> AvailableTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableGenres { get; } = new ObservableCollection<string>();
        public ObservableCollection<CabinetOption> AvailableCabinets { get; } = new ObservableCollection<CabinetOption>();
        public ObservableCollection<string> AvailableCompartments { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableSlots { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> CabinetFilterOptions { get; } = new ObservableCollection<string> { "" };
        public ObservableCollection<string> CompartmentFilterOptions { get; } = new ObservableCollection<string> { "" };
        public ObservableCollection<string> SlotFilterOptions { get; } = new ObservableCollection<string> { "" };
        public ObservableCollection<Instrument> FilterInstrumentOptions { get; } = new ObservableCollection<Instrument>();

        private CabinetOption? selectedCabinetSetting;
        public CabinetOption? SelectedCabinetSetting
        {
            get => selectedCabinetSetting;
            set
            {
                selectedCabinetSetting = value;
                OnPropertyChanged();
                EditCabinetColor = value?.Color ?? "#FFFFFF";
            }
        }

        private string newCabinetColor = "#FFFFFF";
        public string NewCabinetColor
        {
            get => newCabinetColor;
            set { newCabinetColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(NewCabinetColorBrush)); }
        }
        public SolidColorBrush NewCabinetColorBrush => ParseColorBrush(newCabinetColor);

        private string editCabinetColor = "#FFFFFF";
        public string EditCabinetColor
        {
            get => editCabinetColor;
            set { editCabinetColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditCabinetColorBrush)); }
        }
        public SolidColorBrush EditCabinetColorBrush => ParseColorBrush(editCabinetColor);

        private static SolidColorBrush ParseColorBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.White); }
        }

        private string? titleFilter;
        private string? composerFilter;
        private string? arrangerFilter;
        private string? publisherFilter;
        private string? isbnFilter;
        private string? tagsFilter;
        private string? cabinetFilter = "";
        private string? compartmentFilter = "";
        private string? slotFilter = "";
        private bool onlyActiveFilter = false;
        private bool onlyWithDigitalScoresFilter = false;
        private bool missingScoresForInstrumentFilter = false;
        private Instrument? selectedFilterInstrument;

        private FilterOperator titleFilterOperator = FilterOperator.Contains;
        private FilterOperator composerFilterOperator = FilterOperator.Contains;
        private FilterOperator arrangerFilterOperator = FilterOperator.Contains;
        private FilterOperator publisherFilterOperator = FilterOperator.Contains;
        private FilterOperator isbnFilterOperator = FilterOperator.Contains;
        private FilterOperator tagsFilterOperator = FilterOperator.Contains;

        public ObservableCollection<FilterOperator> FilterOperatorOptions { get; } =
            new ObservableCollection<FilterOperator>((FilterOperator[])System.Enum.GetValues(typeof(FilterOperator)));

        public FilterOperator TitleFilterOperator
        {
            get => titleFilterOperator;
            set { if (titleFilterOperator == value) return; titleFilterOperator = value; OnPropertyChanged(); }
        }

        public FilterOperator ComposerFilterOperator
        {
            get => composerFilterOperator;
            set { if (composerFilterOperator == value) return; composerFilterOperator = value; OnPropertyChanged(); }
        }

        public FilterOperator ArrangerFilterOperator
        {
            get => arrangerFilterOperator;
            set { if (arrangerFilterOperator == value) return; arrangerFilterOperator = value; OnPropertyChanged(); }
        }

        public FilterOperator PublisherFilterOperator
        {
            get => publisherFilterOperator;
            set { if (publisherFilterOperator == value) return; publisherFilterOperator = value; OnPropertyChanged(); }
        }

        public FilterOperator IsbnFilterOperator
        {
            get => isbnFilterOperator;
            set { if (isbnFilterOperator == value) return; isbnFilterOperator = value; OnPropertyChanged(); }
        }

        public FilterOperator TagsFilterOperator
        {
            get => tagsFilterOperator;
            set { if (tagsFilterOperator == value) return; tagsFilterOperator = value; OnPropertyChanged(); }
        }

        private Piece? selectedPiece;
        private Piece? selectedFilteredPiece;

        public Piece? SelectedFilteredPiece
        {
            get => selectedFilteredPiece;
            set
            {
                if (Equals(selectedFilteredPiece, value)) return;
                selectedFilteredPiece = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public Piece CurrentPiece
        {
            get => currentPiece;
            set
            {
                if (Equals(currentPiece, value))
                {
                    return;
                }

                currentPiece = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EditorHeaderText));
                OnPropertyChanged(nameof(IsEditingExistingPiece));
            }
        }

        public bool IsAdminLoggedIn
        {
            get => isAdminLoggedIn;
            private set
            {
                if (isAdminLoggedIn == value) return;
                isAdminLoggedIn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AdminLoginVisibility));
                OnPropertyChanged(nameof(AdminActionsVisibility));
                CommandManager.InvalidateRequerySuggested();
                UpdateAdminCredentialsDisplay();
            }
        }

        public Visibility AdminLoginVisibility => IsAdminLoggedIn ? Visibility.Collapsed : Visibility.Visible;

        public Visibility AdminActionsVisibility => IsAdminLoggedIn ? Visibility.Visible : Visibility.Collapsed;

        public string EditorHeaderText => CurrentPiece.Id == 0 ? "Neues Musikstück" : "Musikstück bearbeiten";

        public bool IsEditingExistingPiece => CurrentPiece.Id != 0;

        public string? TitleFilter
        {
            get => titleFilter;
            set
            {
                if (titleFilter == value)
                {
                    return;
                }

                titleFilter = value;
                OnPropertyChanged();
            }
        }

        public string? ComposerFilter
        {
            get => composerFilter;
            set
            {
                if (composerFilter == value)
                {
                    return;
                }

                composerFilter = value;
                OnPropertyChanged();
            }
        }

        public string? ArrangerFilter
        {
            get => arrangerFilter;
            set
            {
                if (arrangerFilter == value)
                {
                    return;
                }

                arrangerFilter = value;
                OnPropertyChanged();
            }
        }

        public string? PublisherFilter
        {
            get => publisherFilter;
            set
            {
                if (publisherFilter == value)
                {
                    return;
                }

                publisherFilter = value;
                OnPropertyChanged();
            }
        }

        public string? IsbnFilter
        {
            get => isbnFilter;
            set
            {
                if (isbnFilter == value)
                {
                    return;
                }

                isbnFilter = value;
                OnPropertyChanged();
            }
        }

        public string? TagsFilter
        {
            get => tagsFilter;
            set
            {
                if (tagsFilter == value)
                {
                    return;
                }

                tagsFilter = value;
                OnPropertyChanged();
            }
        }

        public string? CabinetFilter
        {
            get => cabinetFilter;
            set
            {
                if (cabinetFilter == value)
                {
                    return;
                }

                cabinetFilter = value;
                OnPropertyChanged();
            }
        }

        public string? CompartmentFilter
        {
            get => compartmentFilter;
            set
            {
                if (compartmentFilter == value)
                {
                    return;
                }

                compartmentFilter = value;
                OnPropertyChanged();
            }
        }

        public string? SlotFilter
        {
            get => slotFilter;
            set
            {
                if (slotFilter == value)
                {
                    return;
                }

                slotFilter = value;
                OnPropertyChanged();
            }
        }

        public bool OnlyActiveFilter
        {
            get => onlyActiveFilter;
            set
            {
                if (onlyActiveFilter == value)
                {
                    return;
                }

                onlyActiveFilter = value;
                OnPropertyChanged();
            }
        }

        public bool OnlyWithDigitalScoresFilter
        {
            get => onlyWithDigitalScoresFilter;
            set
            {
                if (onlyWithDigitalScoresFilter == value)
                {
                    return;
                }

                onlyWithDigitalScoresFilter = value;
                OnPropertyChanged();
            }
        }

        public bool MissingScoresForInstrumentFilter
        {
            get => missingScoresForInstrumentFilter;
            set
            {
                if (missingScoresForInstrumentFilter == value)
                {
                    return;
                }

                missingScoresForInstrumentFilter = value;
                OnPropertyChanged();
            }
        }

        public Instrument? SelectedFilterInstrument
        {
            get => selectedFilterInstrument;
            set
            {
                if (Equals(selectedFilterInstrument, value))
                {
                    return;
                }

                selectedFilterInstrument = value;
                OnPropertyChanged();
            }
        }

        public Piece? SelectedPiece
        {
            get => selectedPiece;
            set
            {
                if (Equals(selectedPiece, value))
                {
                    return;
                }

                selectedPiece = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand DeleteFromListCommand { get; }
        public ICommand DeleteFromFilterCommand { get; }
        public ICommand DeleteAllPiecesCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand ResetFilterCommand { get; }
        public ICommand LoadFromSelectionCommand { get; }
        public ICommand LoadFromFilterSelectionCommand { get; }

        public MainWindow(PieceRepository repository)
        {
            this.repository = repository;
            sheetMusicRepository = new SheetMusicRepository(DatabaseInitializer.GetConnectionString());
            syncRepository = new SyncRepository(DatabaseInitializer.GetConnectionString());
            syncService = new SyncService(syncRepository);
            syncConfig = SyncConfigStore.Load();

            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !string.IsNullOrWhiteSpace(CurrentPiece.Title));
            DeleteCommand = new RelayCommand(async _ => await DeleteCurrentPieceAsync(), _ => CurrentPiece.Id != 0);
            DeleteFromListCommand = new RelayCommand(async _ => await DeleteSelectedFromListAsync(), _ => SelectedPiece != null);
            DeleteFromFilterCommand = new RelayCommand(async _ => await DeleteSelectedFromFilterAsync(), _ => SelectedFilteredPiece != null);
            DeleteAllPiecesCommand = new RelayCommand(async _ => await DeleteAllPiecesAsync(), _ => IsAdminLoggedIn);
            NewCommand = new RelayCommand(_ => ResetForm(), _ => true);
            FilterCommand = new RelayCommand(async _ => await LoadFilteredPiecesAsync(), _ => true);
            ResetFilterCommand = new RelayCommand(_ => ResetFilter(), _ => true);
            LoadFromSelectionCommand = new RelayCommand(async _ => await LoadSelectedPieceAsync(), _ => SelectedPiece != null);
            LoadFromFilterSelectionCommand = new RelayCommand(async _ => await LoadSelectedFilteredPieceAsync(), _ => SelectedFilteredPiece != null);

            DataContext = this;
            InitializeComponent();
            WindowIcons.Apply(this);

            // Gespeicherte Spalten-Konfiguration anwenden
            ApplyColumnConfig(LoadColumnConfig());
            ApplyFilterColumnConfig(LoadFilterColumnConfig());
            LoadSyncConfigToUi();
            LoadAppConfigToUi();
            Title = $"Musikarchiv {AppVersion.Current}";
            if (AppVersionText != null)
            {
                AppVersionText.Text = $"Installierte Version: {AppVersion.Current}";
            }

            if (UpdateStatusText != null)
            {
                UpdateStatusText.Text = "Beim Start wird automatisch nach Updates gesucht.";
            }

            MainTabControl.SelectionChanged += (_, _) => RefreshSyncStatusUi();

            _ = InitializeTagAndGenreOptionsAsync();
            _ = LoadInstrumentsAsync();
            _ = LoadPiecesAsync();
            _ = CheckForUpdatesOnStartupAsync();
        }

        // Dynamische Instrument-Gruppen (aus DB)
        public System.Collections.ObjectModel.ObservableCollection<string> Group1InstrumentNames { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> Group2InstrumentNames { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> Group3InstrumentNames { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> Group4InstrumentNames { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> UnassignedInstrumentNames { get; } = new();

        public System.Collections.Generic.IEnumerable<InstrumentSelection> InstrumentsGroup1
            => Instruments.Where(i => Group1InstrumentNames.Contains(i.Instrument.Name));

        public System.Collections.Generic.IEnumerable<InstrumentSelection> InstrumentsGroup2
            => Instruments.Where(i => Group2InstrumentNames.Contains(i.Instrument.Name));

        public System.Collections.Generic.IEnumerable<InstrumentSelection> InstrumentsGroup3
            => Instruments.Where(i => Group3InstrumentNames.Contains(i.Instrument.Name));

        public System.Collections.Generic.IEnumerable<InstrumentSelection> InstrumentsGroup4
            => Instruments.Where(i => Group4InstrumentNames.Contains(i.Instrument.Name));

        private async Task LoadInstrumentsAsync()
        {
            try
            {
                Instruments.Clear();
                var instruments = await repository.GetAllInstrumentsAsync().ConfigureAwait(false);
                var groupAssignments = await repository.GetGroupAssignmentsAsync().ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Group1InstrumentNames.Clear();
                    Group2InstrumentNames.Clear();
                    Group3InstrumentNames.Clear();
                    Group4InstrumentNames.Clear();
                    UnassignedInstrumentNames.Clear();
                    FilterInstrumentOptions.Clear();

                    foreach (var instrument in instruments)
                    {
                        Instruments.Add(new InstrumentSelection(instrument));
                        FilterInstrumentOptions.Add(instrument);
                        var name = instrument.Name;
                        if (groupAssignments.TryGetValue(name, out int gid))
                        {
                            switch (gid)
                            {
                                case 1: Group1InstrumentNames.Add(name); break;
                                case 2: Group2InstrumentNames.Add(name); break;
                                case 3: Group3InstrumentNames.Add(name); break;
                                case 4: Group4InstrumentNames.Add(name); break;
                                default: UnassignedInstrumentNames.Add(name); break;
                            }
                        }
                        else
                        {
                            UnassignedInstrumentNames.Add(name);
                        }
                    }

                    OnPropertyChanged(nameof(InstrumentsGroup1));
                    OnPropertyChanged(nameof(InstrumentsGroup2));
                    OnPropertyChanged(nameof(InstrumentsGroup3));
                    OnPropertyChanged(nameof(InstrumentsGroup4));
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Laden der Instrumente: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadPiecesAsync()
        {
            try
            {
                var pieces = await repository.GetPiecesAsync(
                    null, null, null, null, null, null, null, null, null, null, null).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Pieces.Clear();
                    foreach (var piece in pieces)
                    {
                        Pieces.Add(piece);
                    }
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Laden der Liste: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadFilteredPiecesAsync()
        {
            try
            {
                if (MissingScoresForInstrumentFilter && SelectedFilterInstrument == null)
                {
                    UiMessage.Show("Bitte ein Instrument für den Filter „Fehlende Noten“ auswählen.", "Hinweis",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var criteria = new List<FilterCriterion>
                {
                    new() { Field = "Title",     Operator = TitleFilterOperator,     Value = TitleFilter ?? string.Empty },
                    new() { Field = "Composer",  Operator = ComposerFilterOperator,  Value = ComposerFilter ?? string.Empty },
                    new() { Field = "Arranger",  Operator = ArrangerFilterOperator,  Value = ArrangerFilter ?? string.Empty },
                    new() { Field = "Publisher", Operator = PublisherFilterOperator, Value = PublisherFilter ?? string.Empty },
                    new() { Field = "Isbn",      Operator = IsbnFilterOperator,      Value = IsbnFilter ?? string.Empty },
                    new() { Field = "Tags",      Operator = TagsFilterOperator,      Value = TagsFilter ?? string.Empty },
                };

                var selectedGenres = Application.Current.Dispatcher.Invoke(() =>
                    GenreFilterListBox.SelectedItems.Cast<string>().ToList());

                var pieces = await repository.GetPiecesByCriteriaAsync(
                    criteria,
                    selectedGenres,
                    CabinetFilter,
                    CompartmentFilter,
                    SlotFilter,
                    OnlyActiveFilter,
                    OnlyWithDigitalScoresFilter ? true : null,
                    MissingScoresForInstrumentFilter && SelectedFilterInstrument != null
                        ? SelectedFilterInstrument.Id
                        : null).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilteredPieces.Clear();
                    foreach (var piece in pieces)
                    {
                        FilteredPieces.Add(piece);
                    }
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Filtern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadLabelPrintRowsAsync()
        {
            try
            {
                var criteria = new List<FilterCriterion>
                {
                    new() { Field = "Title",     Operator = TitleFilterOperator,     Value = TitleFilter ?? string.Empty },
                    new() { Field = "Composer",  Operator = ComposerFilterOperator,  Value = ComposerFilter ?? string.Empty },
                    new() { Field = "Arranger",  Operator = ArrangerFilterOperator,  Value = ArrangerFilter ?? string.Empty },
                    new() { Field = "Publisher", Operator = PublisherFilterOperator, Value = PublisherFilter ?? string.Empty },
                    new() { Field = "Isbn",      Operator = IsbnFilterOperator,      Value = IsbnFilter ?? string.Empty },
                    new() { Field = "Tags",      Operator = TagsFilterOperator,      Value = TagsFilter ?? string.Empty },
                };

                var selectedGenres = Application.Current.Dispatcher.Invoke(() =>
                    LabelGenreFilterListBox.SelectedItems.Cast<string>().ToList());

                var pieces = await repository.GetPiecesByCriteriaAsync(
                    criteria,
                    selectedGenres,
                    CabinetFilter,
                    CompartmentFilter,
                    SlotFilter,
                    OnlyActiveFilter,
                    OnlyWithDigitalScoresFilter ? true : null,
                    null).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ClearLabelPrintRows();
                    foreach (var piece in pieces)
                    {
                        var row = new LabelPrintRow(piece);
                        row.PropertyChanged += LabelPrintRow_PropertyChanged;
                        row.IsSelected = labelSelectedPieceIds.Contains(piece.Id);
                        LabelPrintRows.Add(row);
                    }

                    UpdateLabelSelectedCountText();
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Filtern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearLabelPrintRows()
        {
            foreach (var row in LabelPrintRows)
            {
                row.PropertyChanged -= LabelPrintRow_PropertyChanged;
            }

            LabelPrintRows.Clear();
        }

        private void LabelPrintRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (suppressLabelSelectionSync || e.PropertyName != nameof(LabelPrintRow.IsSelected) || sender is not LabelPrintRow row)
            {
                return;
            }

            SyncLabelSelection(row);
        }

        private void SyncLabelSelection(LabelPrintRow row)
        {
            if (row.IsSelected)
            {
                if (labelSelectedPieceIds.Add(row.Piece.Id))
                {
                    LabelSelectedRows.Add(new LabelPrintRow(row.Piece));
                }
            }
            else if (labelSelectedPieceIds.Contains(row.Piece.Id))
            {
                RemoveFromLabelSelection(row.Piece.Id, updateFilterRow: false);
            }

            UpdateLabelSelectedCountText();
        }

        private void RemoveFromLabelSelection(long pieceId, bool updateFilterRow = true)
        {
            if (!labelSelectedPieceIds.Remove(pieceId))
            {
                return;
            }

            var selectedRow = LabelSelectedRows.FirstOrDefault(r => r.Piece.Id == pieceId);
            if (selectedRow != null)
            {
                LabelSelectedRows.Remove(selectedRow);
            }

            if (updateFilterRow)
            {
                var filterRow = LabelPrintRows.FirstOrDefault(r => r.Piece.Id == pieceId);
                if (filterRow != null && filterRow.IsSelected)
                {
                    suppressLabelSelectionSync = true;
                    filterRow.IsSelected = false;
                    suppressLabelSelectionSync = false;
                }
            }

            UpdateLabelSelectedCountText();
        }

        private void UpdateLabelSelectedCountText()
        {
            if (LabelSelectedCountText != null)
            {
                LabelSelectedCountText.Text = $"Ausgewählt für Druck ({LabelSelectedRows.Count})";
            }
        }

        private void ClearLabelSelection()
        {
            labelSelectedPieceIds.Clear();
            LabelSelectedRows.Clear();

            suppressLabelSelectionSync = true;
            foreach (var row in LabelPrintRows)
            {
                row.IsSelected = false;
            }
            suppressLabelSelectionSync = false;

            UpdateLabelSelectedCountText();
        }

        private void SetLabelPrintRowSelected(LabelPrintRow row, bool selected)
        {
            if (row.IsSelected == selected)
            {
                return;
            }

            suppressLabelSelectionSync = true;
            row.IsSelected = selected;
            suppressLabelSelectionSync = false;
            SyncLabelSelection(row);
        }

        private void LabelPrintGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            var gridRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (gridRow?.Item is LabelPrintRow labelRow)
            {
                SetLabelPrintRowSelected(labelRow, !labelRow.IsSelected);
                e.Handled = true;
            }
        }

        private void LabelSelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LabelSelectedList.SelectedItem is LabelPrintRow row)
            {
                RemoveFromLabelSelection(row.Piece.Id);
            }
        }

        private void LabelClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            ClearLabelSelection();
        }

        private async void LabelFilterButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadLabelPrintRowsAsync().ConfigureAwait(true);
        }

        private void LabelResetFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ResetFilter();
            LabelGenreFilterListBox?.SelectedItems.Clear();
            ClearLabelPrintRows();
        }

        private void LabelSelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in LabelPrintRows)
            {
                SetLabelPrintRowSelected(row, true);
            }
        }

        private void LabelSelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in LabelPrintRows.ToList())
            {
                SetLabelPrintRowSelected(row, false);
            }
        }

        private async void PrintSelectedLabelsButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPieces = LabelSelectedRows.Select(row => row.Piece).ToList();
            if (selectedPieces.Count == 0)
            {
                UiMessage.Show("Bitte mindestens ein Stück für den Label-Druck auswählen.", "Label-Druck",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var labels = new List<FolderLabelData>();
                foreach (var piece in selectedPieces)
                {
                    labels.Add(await BuildFolderLabelDataAsync(piece).ConfigureAwait(true));
                }

                FolderLabelPrintService.PrintLabels(this, labels);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Label-Druck: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<FolderLabelData> BuildFolderLabelDataAsync(Piece piece)
        {
            var url = await BuildWebViewUrlAsync(piece.Id).ConfigureAwait(true);
            var label = FolderLabelData.FromPiece(piece, url);

            if (string.IsNullOrWhiteSpace(label.CabinetColor) && !string.IsNullOrWhiteSpace(piece.Cabinet))
            {
                label.CabinetColor = AvailableCabinets.FirstOrDefault(c => c.Name == piece.Cabinet)?.Color;
            }

            return label;
        }

        private void ResetFilter()
        {
            TitleFilter = null;
            ComposerFilter = null;
            ArrangerFilter = null;
            PublisherFilter = null;
            IsbnFilter = null;
            TagsFilter = null;
            CabinetFilter = "";
            CompartmentFilter = "";
            SlotFilter = "";
            OnlyActiveFilter = false;
            OnlyWithDigitalScoresFilter = false;
            MissingScoresForInstrumentFilter = false;
            SelectedFilterInstrument = null;

            TitleFilterOperator = FilterOperator.Contains;
            ComposerFilterOperator = FilterOperator.Contains;
            ArrangerFilterOperator = FilterOperator.Contains;
            PublisherFilterOperator = FilterOperator.Contains;
            IsbnFilterOperator = FilterOperator.Contains;
            TagsFilterOperator = FilterOperator.Contains;

            GenreFilterListBox?.SelectedItems.Clear();
            FilteredPieces.Clear();
        }

        private async Task SaveAsync()
        {
            try
            {
                SyncTagsFromUiToPiece();
                SyncGenresFromUiToPiece();

                if (!ValidateRequiredFields())
                {
                    return;
                }

                await repository.SavePieceAsync(CurrentPiece, Instruments).ConfigureAwait(false);
                CurrentPiece.FolderPath = SheetMusicPaths.BuildLogicalPath(CurrentPiece);
                UiMessage.Show("Musikstück gespeichert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadPiecesAsync().ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(EditorHeaderText));
                    OnPropertyChanged(nameof(IsEditingExistingPiece));
                    RefreshSyncStatusUi();
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenCurrentSheetMusic_Click(object sender, RoutedEventArgs e)
        {
            _ = OpenSheetMusicAsync(CurrentPiece);
        }

        private async Task OpenSheetMusicAsync(Piece piece)
        {
            if (piece.Id == 0)
            {
                UiMessage.Show("Bitte zuerst das Musikstück speichern.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var (_, instrumentIds) = await repository.GetPieceWithInstrumentsAsync(piece.Id).ConfigureAwait(false);
                var instruments = await repository.GetAllInstrumentsAsync().ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new SheetMusicWindow(piece, sheetMusicRepository, instruments, instrumentIds) { Owner = this };
                    window.ShowDialog();
                });

                await LoadPiecesAsync().ConfigureAwait(false);
                if (FilteredPieces.Count > 0)
                {
                    await LoadFilteredPiecesAsync().ConfigureAwait(false);
                }

                Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Öffnen der Noten: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteCurrentPieceAsync()
        {
            if (CurrentPiece.Id == 0)
            {
                return;
            }

            await DeletePieceByIdAsync(CurrentPiece.Id, CurrentPiece.Title).ConfigureAwait(false);
        }

        private async Task DeleteSelectedFromListAsync()
        {
            if (SelectedPiece == null)
            {
                return;
            }

            await DeletePieceByIdAsync(SelectedPiece.Id, SelectedPiece.Title).ConfigureAwait(false);
        }

        private async Task DeleteSelectedFromFilterAsync()
        {
            if (SelectedFilteredPiece == null)
            {
                return;
            }

            await DeletePieceByIdAsync(SelectedFilteredPiece.Id, SelectedFilteredPiece.Title).ConfigureAwait(false);
        }

        private async Task DeletePieceByIdAsync(long id, string title)
        {
            var result = UiMessage.Confirm(
                $"Musikstück \"{title}\" endgültig löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await repository.DeletePieceAsync(id).ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CurrentPiece.Id == id)
                    {
                        ResetForm();
                    }
                });
                await LoadPiecesAsync().ConfigureAwait(false);
                if (FilteredPieces.Count > 0)
                {
                    await LoadFilteredPiecesAsync().ConfigureAwait(false);
                }
                UiMessage.Show("Musikstück gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Löschen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteAllPiecesAsync()
        {
            if (!IsAdminLoggedIn)
            {
                return;
            }

            var count = Pieces.Count;
            if (count == 0)
            {
                UiMessage.Show("Es sind keine Musikstücke vorhanden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = UiMessage.Confirm(
                $"Wirklich alle {count} Musikstücke endgültig löschen? Diese Aktion kann nicht rückgängig gemacht werden.",
                "Alle löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await repository.DeleteAllPiecesAsync().ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ResetForm();
                    FilteredPieces.Clear();
                });
                await LoadPiecesAsync().ConfigureAwait(false);
                UiMessage.Show("Alle Musikstücke wurden gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Löschen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AdminLoginButton_Click(object sender, RoutedEventArgs e)
        {
            TryAdminLogin();
        }

        private void AdminPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryAdminLogin();
            }
        }

        private void TryAdminLogin()
        {
            var password = AdminPasswordBox?.Password ?? string.Empty;
            if (password == AdminPassword)
            {
                IsAdminLoggedIn = true;
                if (AdminPasswordBox != null)
                {
                    AdminPasswordBox.Password = string.Empty;
                }
                UiMessage.Show("Admin-Anmeldung erfolgreich.", "Administration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UiMessage.Show("Falsches Passwort.", "Administration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void UpdateAdminCredentialsDisplay()
        {
            if (AdminWebPasswordText == null || AdminApiKeyText == null)
            {
                return;
            }

            if (!IsAdminLoggedIn)
            {
                AdminWebPasswordText.Text = string.Empty;
                AdminApiKeyText.Text = string.Empty;
                return;
            }

            syncConfig = SyncConfigStore.Load();
            var webPassword = string.IsNullOrWhiteSpace(syncConfig.WebViewPassword)
                ? "(nicht gesetzt)"
                : new string('•', Math.Min(syncConfig.WebViewPassword.Length, 18));
            var apiKey = string.IsNullOrWhiteSpace(syncConfig.ApiKey)
                ? "(nicht gesetzt)"
                : syncConfig.ApiKey;

            AdminWebPasswordText.Text = $"Web-App Passwort: {webPassword}";
            AdminApiKeyText.Text = $"API-Schlüssel: {apiKey}";
        }

        private void AdminLogoutButton_Click(object sender, RoutedEventArgs e)
        {
            IsAdminLoggedIn = false;
            if (AdminPasswordBox != null)
            {
                AdminPasswordBox.Password = string.Empty;
            }
        }

        private async void DuplicateCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdminLoggedIn)
            {
                return;
            }

            var dialog = new DuplicateCleanupDialog(DatabaseInitializer.GetConnectionString())
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadPiecesAsync().ConfigureAwait(true);
                RefreshSyncStatusUi();
            }
        }

        private void ResetForm()
        {
            CurrentPiece = new Piece();
            foreach (var selection in Instruments)
            {
                selection.IsSelected = false;
            }

            if (TagsListBox != null)
            {
                TagsListBox.SelectedItems.Clear();
            }

            if (GenresListBox != null)
            {
                GenresListBox.SelectedItems.Clear();
            }
        }

        private async Task LoadSelectedPieceAsync()
        {
            if (SelectedPiece == null)
            {
                UiMessage.Show("Bitte zuerst ein Musikstück aus der Liste auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await LoadPieceIntoEditorAsync(SelectedPiece.Id).ConfigureAwait(false);
        }

        private async Task LoadSelectedFilteredPieceAsync()
        {
            if (SelectedFilteredPiece == null)
            {
                UiMessage.Show("Bitte zuerst ein Musikstück aus der Ergebnisliste auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await LoadPieceIntoEditorAsync(SelectedFilteredPiece.Id).ConfigureAwait(false);
        }

        private async Task LoadPieceIntoEditorAsync(long pieceId)
        {
            try
            {
                var result = await repository.GetPieceWithInstrumentsAsync(pieceId).ConfigureAwait(false);
                var piece = result.Item1;
                var instrumentIds = result.Item2;

                await Dispatcher.InvokeAsync(() =>
                {
                    CurrentPiece = piece;

                    foreach (var selection in Instruments)
                    {
                        selection.IsSelected = instrumentIds.Contains(selection.Instrument.Id);
                    }

                    ApplyTagsFromPieceToUi();
                    ApplyGenresFromPieceToUi();
                    MainTabControl.SelectedIndex = 0;
                    Activate();
                    Focus();
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Laden des ausgewählten Musikstücks: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializeTagAndGenreOptionsAsync()
        {
            try
            {
                AvailableTags.Clear();
                AvailableGenres.Clear();
                AvailableCabinets.Clear();
                AvailableCompartments.Clear();
                AvailableSlots.Clear();

                var tags = await repository.GetTagOptionsAsync().ConfigureAwait(false);
                var genres = await repository.GetGenreOptionsAsync().ConfigureAwait(false);
                var cabinets = await repository.GetCabinetOptionsAsync().ConfigureAwait(false);
                var compartments = await repository.GetCompartmentOptionsAsync().ConfigureAwait(false);
                var slots = await repository.GetSlotOptionsAsync().ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var tag in tags) AvailableTags.Add(tag);
                    foreach (var genre in genres) AvailableGenres.Add(genre);
                    foreach (var c in cabinets) { AvailableCabinets.Add(c); CabinetFilterOptions.Add(c.Name); }
                    foreach (var c in compartments) { AvailableCompartments.Add(c); CompartmentFilterOptions.Add(c); }
                    foreach (var s in slots) { AvailableSlots.Add(s); SlotFilterOptions.Add(s); }
                });
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Laden der Einstellungs-Optionen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SyncTagsFromUiToPiece()
        {
            if (TagsListBox == null)
            {
                return;
            }

            var selected = TagsListBox.SelectedItems.Cast<string>().ToList();
            CurrentPiece.Tags = selected.Count == 0 ? null : "#" + string.Join("#", selected) + "#";
        }

        private void SyncGenresFromUiToPiece()
        {
            if (GenresListBox == null)
            {
                return;
            }

            var selected = GenresListBox.SelectedItems.Cast<string>().ToList();
            CurrentPiece.Genre = selected.Count == 0 ? null : "#" + string.Join("#", selected) + "#";
        }

        private void ApplyTagsFromPieceToUi()
        {
            if (TagsListBox == null)
            {
                return;
            }

            TagsListBox.SelectedItems.Clear();

            if (string.IsNullOrWhiteSpace(CurrentPiece.Tags))
            {
                return;
            }

            var parts = CurrentPiece.Tags.Split('#').Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet();
            foreach (var item in AvailableTags)
            {
                if (parts.Contains(item))
                {
                    TagsListBox.SelectedItems.Add(item);
                }
            }
        }

        private void RequiredComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                UpdateRequiredComboBoxVisual(comboBox);
            }
        }

        private void RequiredComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                UpdateRequiredComboBoxVisual(comboBox);
            }
        }

        private void UpdateRequiredComboBoxVisual(ComboBox comboBox)
        {
            bool isEmpty = comboBox.SelectedItem == null ||
                           (comboBox.SelectedValue is string s && string.IsNullOrWhiteSpace(s));

            Border? border = null;
            if (comboBox == CabinetComboBox)
            {
                border = CabinetBorder;
            }
            else if (comboBox == CompartmentComboBox)
            {
                border = CompartmentBorder;
            }
            else if (comboBox == SlotComboBox)
            {
                border = SlotBorder;
            }

            if (border == null)
            {
                return;
            }

            if (isEmpty)
            {
                border.BorderBrush = new SolidColorBrush(Colors.Red);
                border.BorderThickness = new Thickness(2);
                border.Background = new SolidColorBrush(Colors.MistyRose);
            }
            else
            {
                border.BorderBrush = new SolidColorBrush(Colors.Gray);
                border.BorderThickness = new Thickness(1);
                border.Background = Brushes.Transparent;
            }
        }

        private void ApplyGenresFromPieceToUi()
        {
            if (GenresListBox == null)
            {
                return;
            }

            GenresListBox.SelectedItems.Clear();

            if (string.IsNullOrWhiteSpace(CurrentPiece.Genre))
            {
                return;
            }

            var parts = CurrentPiece.Genre.Split('#').Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet();
            foreach (var item in AvailableGenres)
            {
                if (parts.Contains(item))
                {
                    GenresListBox.SelectedItems.Add(item);
                }
            }
        }

        private bool ValidateRequiredFields()
        {
            bool ok = true;

            if (string.IsNullOrWhiteSpace(CurrentPiece.Title))
            {
                ok = false;
            }

            if (CabinetComboBox.SelectedItem == null)
            {
                ok = false;
            }

            if (CompartmentComboBox.SelectedItem == null)
            {
                ok = false;
            }

            if (SlotComboBox.SelectedItem == null)
            {
                ok = false;
            }

            if (GenresListBox.SelectedItems.Count == 0)
            {
                ok = false;
            }

            if (!ok)
            {
                UiMessage.Show("Bitte alle Pflichtfelder ausfüllen (rot markiert).", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return ok;
        }

        private void ToggleGroup1_Click(object sender, RoutedEventArgs e)
        {
            var group = InstrumentsGroup1.ToList();
            bool anySelected = group.Any(i => i.IsSelected);
            foreach (var item in group) item.IsSelected = !anySelected;
        }

        private void ToggleGroup2_Click(object sender, RoutedEventArgs e)
        {
            var group = InstrumentsGroup2.ToList();
            bool anySelected = group.Any(i => i.IsSelected);
            foreach (var item in group) item.IsSelected = !anySelected;
        }

        private void ToggleGroup3_Click(object sender, RoutedEventArgs e)
        {
            var group = InstrumentsGroup3.ToList();
            bool anySelected = group.Any(i => i.IsSelected);
            foreach (var item in group) item.IsSelected = !anySelected;
        }

        private void ToggleGroup4_Click(object sender, RoutedEventArgs e)
        {
            var group = InstrumentsGroup4.ToList();
            bool anySelected = group.Any(i => i.IsSelected);
            foreach (var item in group) item.IsSelected = !anySelected;
        }

        private static void SelectNext(ListBox listBox, System.Collections.ObjectModel.ObservableCollection<string> collection, int removedIndex)
        {
            if (collection.Count == 0) return;
            listBox.SelectedIndex = System.Math.Min(removedIndex, collection.Count - 1);
        }

        private async Task MoveInstrumentToGroup(string name, int groupId)
        {
            Group1InstrumentNames.Remove(name);
            Group2InstrumentNames.Remove(name);
            Group3InstrumentNames.Remove(name);
            Group4InstrumentNames.Remove(name);
            UnassignedInstrumentNames.Remove(name);

            switch (groupId)
            {
                case 1: Group1InstrumentNames.Add(name); OnPropertyChanged(nameof(InstrumentsGroup1)); break;
                case 2: Group2InstrumentNames.Add(name); OnPropertyChanged(nameof(InstrumentsGroup2)); break;
                case 3: Group3InstrumentNames.Add(name); OnPropertyChanged(nameof(InstrumentsGroup3)); break;
                case 4: Group4InstrumentNames.Add(name); OnPropertyChanged(nameof(InstrumentsGroup4)); break;
            }

            // Nach dem Verschieben nächstes Element in UnassignedListBox wählen
            if (UnassignedListBox != null && UnassignedInstrumentNames.Count > 0)
                UnassignedListBox.SelectedIndex = 0;

            try { await repository.SetGroupAssignmentAsync(name, groupId).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void AddInstrumentButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NewInstrumentTextBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return;

            if (Instruments.Any(i => i.Instrument.Name == name))
            {
                UiMessage.Show($"Instrument \"{name}\" existiert bereits.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var instrument = await repository.AddInstrumentAsync(name).ConfigureAwait(false);
                if (instrument != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Instruments.Add(new InstrumentSelection(instrument));
                        UnassignedInstrumentNames.Add(name);
                        if (NewInstrumentTextBox != null) NewInstrumentTextBox.Text = string.Empty;
                        // Neu angelegtes Element gleich selektieren
                        if (UnassignedListBox != null)
                            UnassignedListBox.SelectedItem = name;
                    });
                }
            }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void DeleteUnassignedButton_Click(object sender, RoutedEventArgs e)
        {
            if (UnassignedListBox?.SelectedItem is not string name) return;

            var result = UiMessage.Confirm($"Instrument \"{name}\" endgültig löschen?", "Löschen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            int idx = UnassignedListBox.SelectedIndex;
            UnassignedInstrumentNames.Remove(name);
            var sel = Instruments.FirstOrDefault(i => i.Instrument.Name == name);
            if (sel != null) Instruments.Remove(sel);
            SelectNext(UnassignedListBox, UnassignedInstrumentNames, idx);

            try { await repository.DeleteInstrumentAsync(name).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void AssignInstrumentButton_Click(object sender, RoutedEventArgs e)
        {
            if (UnassignedListBox?.SelectedItem is not string name) return;
            if (AssignGroupComboBox?.SelectedItem is not ComboBoxItem item) return;
            if (!int.TryParse(item.Tag?.ToString(), out int groupId)) return;

            await MoveInstrumentToGroup(name, groupId);
        }

        private async void RemoveFromGroup1_Click(object sender, RoutedEventArgs e)
        {
            if (Group1MembersListBox?.SelectedItem is not string name) return;
            int idx = Group1MembersListBox.SelectedIndex;
            Group1InstrumentNames.Remove(name);
            UnassignedInstrumentNames.Add(name);
            OnPropertyChanged(nameof(InstrumentsGroup1));
            SelectNext(Group1MembersListBox, Group1InstrumentNames, idx);
            try { await repository.RemoveGroupAssignmentAsync(name).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void RemoveFromGroup2_Click(object sender, RoutedEventArgs e)
        {
            if (Group2MembersListBox?.SelectedItem is not string name) return;
            int idx = Group2MembersListBox.SelectedIndex;
            Group2InstrumentNames.Remove(name);
            UnassignedInstrumentNames.Add(name);
            OnPropertyChanged(nameof(InstrumentsGroup2));
            SelectNext(Group2MembersListBox, Group2InstrumentNames, idx);
            try { await repository.RemoveGroupAssignmentAsync(name).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void RemoveFromGroup3_Click(object sender, RoutedEventArgs e)
        {
            if (Group3MembersListBox?.SelectedItem is not string name) return;
            int idx = Group3MembersListBox.SelectedIndex;
            Group3InstrumentNames.Remove(name);
            UnassignedInstrumentNames.Add(name);
            OnPropertyChanged(nameof(InstrumentsGroup3));
            SelectNext(Group3MembersListBox, Group3InstrumentNames, idx);
            try { await repository.RemoveGroupAssignmentAsync(name).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void RemoveFromGroup4_Click(object sender, RoutedEventArgs e)
        {
            if (Group4MembersListBox?.SelectedItem is not string name) return;
            int idx = Group4MembersListBox.SelectedIndex;
            Group4InstrumentNames.Remove(name);
            UnassignedInstrumentNames.Add(name);
            OnPropertyChanged(nameof(InstrumentsGroup4));
            SelectNext(Group4MembersListBox, Group4InstrumentNames, idx);
            try { await repository.RemoveGroupAssignmentAsync(name).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void AddCabinetButton_Click(object sender, RoutedEventArgs e)
        {
            var text = NewCabinetTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!AvailableCabinets.Any(c => c.Name == text))
            {
                var opt = new CabinetOption { Name = text, Color = newCabinetColor };
                AvailableCabinets.Add(opt);
                CabinetFilterOptions.Add(text);
                try { await repository.AddCabinetOptionAsync(text, newCabinetColor).ConfigureAwait(false); }
                catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            if (NewCabinetTextBox != null) NewCabinetTextBox.Text = string.Empty;
        }

        private async void UpdateCabinetColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCabinetSetting == null) return;
            SelectedCabinetSetting.Color = editCabinetColor;
            try { await repository.UpdateCabinetColorAsync(SelectedCabinetSetting.Name, editCabinetColor).ConfigureAwait(false); }
            catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void RemoveCabinetButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCabinetSetting is not CabinetOption opt)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Schrank „{opt.Name}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var count = await repository.CountPiecesUsingCabinetAsync(opt.Name).ConfigureAwait(false);
                if (count > 0)
                {
                    UiMessage.Show(
                        $"Der Schrank „{opt.Name}“ wird in {count} Stück(en) verwendet und kann nicht gelöscht werden.",
                        "Löschen nicht möglich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await repository.RemoveCabinetOptionAsync(opt.Name).ConfigureAwait(false);
                AvailableCabinets.Remove(opt);
                CabinetFilterOptions.Remove(opt.Name);
                SelectedCabinetSetting = null;
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddCompartmentButton_Click(object sender, RoutedEventArgs e)
        {
            var text = NewCompartmentTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!AvailableCompartments.Contains(text))
            {
                AvailableCompartments.Add(text);
                CompartmentFilterOptions.Add(text);
                try { await repository.AddCompartmentOptionAsync(text).ConfigureAwait(false); }
                catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            if (NewCompartmentTextBox != null) NewCompartmentTextBox.Text = string.Empty;
        }

        private async void RemoveCompartmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (CompartmentSettingsListBox?.SelectedItem is not string val)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Fach „{val}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var count = await repository.CountPiecesUsingCompartmentAsync(val).ConfigureAwait(false);
                if (count > 0)
                {
                    UiMessage.Show(
                        $"Das Fach „{val}“ wird in {count} Stück(en) verwendet und kann nicht gelöscht werden.",
                        "Löschen nicht möglich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await repository.RemoveCompartmentOptionAsync(val).ConfigureAwait(false);
                AvailableCompartments.Remove(val);
                CompartmentFilterOptions.Remove(val);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddSlotButton_Click(object sender, RoutedEventArgs e)
        {
            var text = NewSlotTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!AvailableSlots.Contains(text))
            {
                AvailableSlots.Add(text);
                SlotFilterOptions.Add(text);
                try { await repository.AddSlotOptionAsync(text).ConfigureAwait(false); }
                catch (System.Exception ex) { UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            if (NewSlotTextBox != null) NewSlotTextBox.Text = string.Empty;
        }

        private async void RemoveSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (SlotSettingsListBox?.SelectedItem is not string val)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Einschub „{val}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var count = await repository.CountPiecesUsingSlotAsync(val).ConfigureAwait(false);
                if (count > 0)
                {
                    UiMessage.Show(
                        $"Der Einschub „{val}“ wird in {count} Stück(en) verwendet und kann nicht gelöscht werden.",
                        "Löschen nicht möglich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await repository.RemoveSlotOptionAsync(val).ConfigureAwait(false);
                AvailableSlots.Remove(val);
                SlotFilterOptions.Remove(val);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            var text = NewTagTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!AvailableTags.Contains(text))
            {
                AvailableTags.Add(text);
                try
                {
                    await repository.AddTagOptionAsync(text).ConfigureAwait(false);
                }
                catch (System.Exception ex)
                {
                    UiMessage.Show($"Fehler beim Speichern des Tags: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (NewTagTextBox != null)
            {
                NewTagTextBox.Text = string.Empty;
            }
        }

        private async void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (TagsSettingsListBox?.SelectedItem is not string tag)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Tag „{tag}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var count = await repository.CountPiecesUsingTagAsync(tag).ConfigureAwait(false);
                if (count > 0)
                {
                    UiMessage.Show(
                        $"Der Tag „{tag}“ wird in {count} Stück(en) verwendet und kann nicht gelöscht werden.",
                        "Löschen nicht möglich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await repository.RemoveTagOptionAsync(tag).ConfigureAwait(false);
                AvailableTags.Remove(tag);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Löschen des Tags: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddGenreButton_Click(object sender, RoutedEventArgs e)
        {
            var text = NewGenreTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!AvailableGenres.Contains(text))
            {
                AvailableGenres.Add(text);
                try
                {
                    await repository.AddGenreOptionAsync(text).ConfigureAwait(false);
                }
                catch (System.Exception ex)
                {
                    UiMessage.Show($"Fehler beim Speichern der Musikgattung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (NewGenreTextBox != null)
            {
                NewGenreTextBox.Text = string.Empty;
            }
        }

        private async void RemoveGenreButton_Click(object sender, RoutedEventArgs e)
        {
            if (GenresSettingsListBox?.SelectedItem is not string genre)
            {
                return;
            }

            var result = UiMessage.Confirm(
                $"Musikgattung „{genre}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var count = await repository.CountPiecesUsingGenreAsync(genre).ConfigureAwait(false);
                if (count > 0)
                {
                    UiMessage.Show(
                        $"Die Musikgattung „{genre}“ wird in {count} Stück(en) verwendet und kann nicht gelöscht werden.",
                        "Löschen nicht möglich",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await repository.RemoveGenreOptionAsync(genre).ConfigureAwait(false);
                AvailableGenres.Remove(genre);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Löschen der Musikgattung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InlineNewTagTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (InlineNewTagTextBox.Text == "Neuen Tag hinzufügen …")
            {
                InlineNewTagTextBox.Text = string.Empty;
                InlineNewTagTextBox.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void InlineNewTagTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InlineNewTagTextBox.Text))
            {
                InlineNewTagTextBox.Text = "Neuen Tag hinzufügen …";
                InlineNewTagTextBox.Foreground = Brushes.Gray;
            }
        }

        private async void InlineNewTagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var text = InlineNewTagTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!AvailableTags.Contains(text))
            {
                AvailableTags.Add(text);
                try { await repository.AddTagOptionAsync(text).ConfigureAwait(false); }
                catch (System.Exception ex) { UiMessage.Show($"Fehler beim Speichern des Tags: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
            }

            if (!TagsListBox.SelectedItems.Contains(text))
                TagsListBox.SelectedItems.Add(text);
            TagsListBox.ScrollIntoView(text);

            InlineNewTagTextBox.Text = "Neuen Tag hinzufügen …";
            InlineNewTagTextBox.Foreground = Brushes.Gray;
            e.Handled = true;
        }

        // ── Spalten-Konfiguration ──────────────────────────────────────────────

        private static string ColumnConfigPath =>
            Path.Combine(AppPaths.GetDataRoot(), "column_config.json");

        private static string FilterColumnConfigPath =>
            Path.Combine(AppPaths.GetDataRoot(), "filter_column_config.json");

        // Master-Liste aller verfügbaren Spalten (Key → Column-Referenz)
        private Dictionary<string, DataGridColumn> AllDataGridColumns() => new()
        {
            ["Name"]        = ColName,
            ["Komponist"]   = ColComposer,
            ["Arrangeur"]   = ColArranger,
            ["Gattung"]     = ColGenre,
            ["Tags"]        = ColTags,
            ["Aktiv"]       = ColIsActive,
            ["Schrank"]     = ColCabinet,
            ["Fach"]        = ColCompartment,
            ["Einschub"]    = ColSlot,
            ["Noten"]       = ColDigitalScores,
            ["Besetzung"]   = ColBesetzung,
            ["Verlag"]      = ColPublisher,
            ["ISBN"]        = ColIsbn,
            ["Ordnerpfad"]  = ColFolderPath,
        };

        private Dictionary<string, DataGridColumn> FilterAllDataGridColumns() => new()
        {
            ["Name"]        = FilterColName,
            ["Komponist"]   = FilterColComposer,
            ["Arrangeur"]   = FilterColArranger,
            ["Gattung"]     = FilterColGenre,
            ["Tags"]        = FilterColTags,
            ["Aktiv"]       = FilterColIsActive,
            ["Schrank"]     = FilterColCabinet,
            ["Fach"]        = FilterColCompartment,
            ["Einschub"]    = FilterColSlot,
            ["Noten"]       = FilterColDigitalScores,
            ["Besetzung"]   = FilterColBesetzung,
            ["Verlag"]      = FilterColPublisher,
            ["ISBN"]        = FilterColIsbn,
            ["Ordnerpfad"]  = FilterColFolderPath,
        };

        private static readonly List<ColumnEntry> DefaultColumnConfig = new()
        {
            new() { Key = "Name",       Header = "Name",       IsVisible = true },
            new() { Key = "Komponist",  Header = "Komponist",  IsVisible = true },
            new() { Key = "Arrangeur",  Header = "Arrangeur",  IsVisible = true },
            new() { Key = "Gattung",    Header = "Gattung",    IsVisible = true },
            new() { Key = "Tags",       Header = "Tags",       IsVisible = true },
            new() { Key = "Aktiv",      Header = "Aktiv",      IsVisible = true },
            new() { Key = "Schrank",    Header = "Schrank",    IsVisible = true },
            new() { Key = "Fach",       Header = "Fach",       IsVisible = true },
            new() { Key = "Einschub",   Header = "Einschub",   IsVisible = true },
        };

        private static readonly List<ColumnEntry> DefaultFilterColumnConfig = new()
        {
            new() { Key = "Name",       Header = "Name",       IsVisible = true },
            new() { Key = "Komponist",  Header = "Komponist",  IsVisible = true },
            new() { Key = "Arrangeur",  Header = "Arrangeur",  IsVisible = true },
            new() { Key = "Gattung",    Header = "Gattung",    IsVisible = true },
            new() { Key = "Tags",       Header = "Tags",       IsVisible = true },
            new() { Key = "Aktiv",      Header = "Aktiv",      IsVisible = true },
            new() { Key = "Schrank",    Header = "Schrank",    IsVisible = true },
            new() { Key = "Fach",       Header = "Fach",       IsVisible = true },
            new() { Key = "Einschub",   Header = "Einschub",   IsVisible = true },
            new() { Key = "Noten",      Header = "Noten",      IsVisible = true },
        };

        private static readonly List<ColumnEntry> AllColumnsMaster = new()
        {
            new() { Key = "Name",       Header = "Name"       },
            new() { Key = "Komponist",  Header = "Komponist"  },
            new() { Key = "Arrangeur",  Header = "Arrangeur"  },
            new() { Key = "Gattung",    Header = "Gattung"    },
            new() { Key = "Tags",       Header = "Tags"       },
            new() { Key = "Aktiv",      Header = "Aktiv"      },
            new() { Key = "Schrank",    Header = "Schrank"    },
            new() { Key = "Fach",       Header = "Fach"       },
            new() { Key = "Einschub",   Header = "Einschub"   },
            new() { Key = "Besetzung",  Header = "Besetzung"  },
            new() { Key = "Verlag",     Header = "Verlag"     },
            new() { Key = "ISBN",       Header = "ISBN"       },
            new() { Key = "Ordnerpfad", Header = "Ordnerpfad" },
            new() { Key = "Noten",      Header = "Noten"      },
        };

        private List<ColumnEntry> LoadColumnConfig()
            => LoadColumnConfigFromPath(ColumnConfigPath, DefaultColumnConfig);

        private List<ColumnEntry> LoadFilterColumnConfig()
            => LoadColumnConfigFromPath(FilterColumnConfigPath, DefaultFilterColumnConfig);

        private static List<ColumnEntry> LoadColumnConfigFromPath(string path, List<ColumnEntry> defaultConfig)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<ColumnConfig>(json);
                    if (config?.Columns?.Count > 0)
                    {
                        return config.Columns;
                    }
                }
            }
            catch { /* Fall through to default */ }

            return defaultConfig.Select(e => new ColumnEntry { Key = e.Key, Header = e.Header, IsVisible = e.IsVisible }).ToList();
        }

        private void SaveColumnConfig(List<ColumnEntry> columns)
            => SaveColumnConfigToPath(ColumnConfigPath, columns);

        private void SaveFilterColumnConfig(List<ColumnEntry> columns)
            => SaveColumnConfigToPath(FilterColumnConfigPath, columns);

        private static void SaveColumnConfigToPath(string path, List<ColumnEntry> columns)
        {
            try
            {
                var dir = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new ColumnConfig { Columns = columns }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { /* Non-critical */ }
        }

        private void ApplyColumnConfig(List<ColumnEntry> selected)
            => ApplyColumnConfigToGrid(AllDataGridColumns(), selected);

        private void ApplyFilterColumnConfig(List<ColumnEntry> selected)
            => ApplyColumnConfigToGrid(FilterAllDataGridColumns(), selected);

        private static void ApplyColumnConfigToGrid(Dictionary<string, DataGridColumn> allColumns, List<ColumnEntry> selected)
        {
            var selectedKeys = selected.Select(e => e.Key).ToList();
            var hiddenKeys = AllColumnsMaster.Select(e => e.Key).Where(k => !selectedKeys.Contains(k)).ToList();
            var ordered = selectedKeys.Concat(hiddenKeys).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                if (allColumns.TryGetValue(ordered[i], out var col))
                {
                    col.DisplayIndex = i;
                }
            }

            foreach (var (key, col) in allColumns)
            {
                col.Visibility = selectedKeys.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OpenColumnConfig_Click(object sender, RoutedEventArgs e)
        {
            var currentSelected = LoadColumnConfig();

            var dlg = new ColumnConfigDialog(AllColumnsMaster, currentSelected) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.ResultColumns == null)
            {
                return;
            }

            var result = dlg.ResultColumns;
            ApplyColumnConfig(result);
            SaveColumnConfig(result);
        }

        private void OpenFilterColumnConfig_Click(object sender, RoutedEventArgs e)
        {
            var currentSelected = LoadFilterColumnConfig();

            var dlg = new ColumnConfigDialog(AllColumnsMaster, currentSelected) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.ResultColumns == null)
            {
                return;
            }

            var result = dlg.ResultColumns;
            ApplyFilterColumnConfig(result);
            SaveFilterColumnConfig(result);
        }

        private Dictionary<string, string> BuildCabinetColorLookup()
        {
            return AvailableCabinets
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Color))
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Color, StringComparer.OrdinalIgnoreCase);
        }

        private void EnrichPiecesWithCabinetColors(IEnumerable<Piece> pieces)
        {
            var lookup = BuildCabinetColorLookup();
            foreach (var piece in pieces)
            {
                if (string.IsNullOrWhiteSpace(piece.CabinetColor) && !string.IsNullOrWhiteSpace(piece.Cabinet))
                {
                    if (lookup.TryGetValue(piece.Cabinet, out var color))
                    {
                        piece.CabinetColor = color;
                    }
                }
            }
        }

        private void PrintPiecesWithColumnSelection(IReadOnlyList<Piece> pieces, List<ColumnEntry> currentColumns, string dialogTitle, string printJobName, string emptyMessage)
        {
            if (pieces.Count == 0)
            {
                UiMessage.Show(emptyMessage, "Listendruck", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new ColumnConfigDialog(AllColumnsMaster, currentColumns)
            {
                Owner = this,
                Title = dialogTitle
            };

            if (dlg.ShowDialog() != true || dlg.ResultColumns == null)
            {
                return;
            }

            if (dlg.ResultColumns.Count == 0)
            {
                UiMessage.Show("Bitte mindestens eine Spalte für den Druck auswählen.", "Listendruck",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var piecesToPrint = pieces.ToList();
            EnrichPiecesWithCabinetColors(piecesToPrint);

            try
            {
                PieceListPrintService.PrintList(this, piecesToPrint, dlg.ResultColumns, printJobName, BuildCabinetColorLookup());
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Listendruck: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintListButton_Click(object sender, RoutedEventArgs e)
        {
            PrintPiecesWithColumnSelection(
                Pieces.ToList(),
                LoadColumnConfig(),
                "Spalten für Listendruck",
                "Musikstückliste",
                "Die Liste ist leer, es gibt nichts zu drucken.");
        }

        private void PrintFilterListButton_Click(object sender, RoutedEventArgs e)
        {
            PrintPiecesWithColumnSelection(
                FilteredPieces.ToList(),
                LoadFilterColumnConfig(),
                "Spalten für Filterlistendruck",
                "Filterergebnis",
                "Keine Filterergebnisse zum Drucken. Bitte zuerst filtern.");
        }

        private async void PieceListGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (FindVisualParent<DataGridRow>(source) == null) return;
            if (SelectedPiece == null) return;

            await ShowPieceDetailAsync(SelectedPiece);
        }

        private async void FilterResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (FindVisualParent<DataGridRow>(source) == null) return;
            if (SelectedFilteredPiece == null) return;

            await ShowPieceDetailAsync(SelectedFilteredPiece);
        }

        private async Task ShowPieceDetailAsync(Piece piece)
        {
            try
            {
                var (detailPiece, instrumentIds) = await repository.GetPieceWithInstrumentsAsync(piece.Id).ConfigureAwait(false);
                var selectedIds = instrumentIds.ToHashSet();

                if (string.IsNullOrWhiteSpace(detailPiece.CabinetColor) && !string.IsNullOrWhiteSpace(detailPiece.Cabinet))
                {
                    detailPiece.CabinetColor = Application.Current.Dispatcher.Invoke(() =>
                        AvailableCabinets.FirstOrDefault(c => c.Name == detailPiece.Cabinet)?.Color);
                }

                IEnumerable<string> ForGroup(ObservableCollection<string> groupNames)
                    => groupNames
                        .Where(n => Instruments.Any(i => i.Instrument.Name == n && selectedIds.Contains(i.Instrument.Id)))
                        .OrderBy(n => n);

                var instrumentSnapshot = Instruments
                    .Select(i => new InstrumentSelection(i.Instrument) { IsSelected = selectedIds.Contains(i.Instrument.Id) })
                    .ToList();

                long? editorPieceId = null;

                var host = new PieceDetailHost
                {
                    Piece = detailPiece,
                    Group1 = ForGroup(Group1InstrumentNames).ToList(),
                    Group2 = ForGroup(Group2InstrumentNames).ToList(),
                    Group3 = ForGroup(Group3InstrumentNames).ToList(),
                    Group4 = ForGroup(Group4InstrumentNames).ToList(),
                    Group1InstrumentNames = Group1InstrumentNames.ToList(),
                    Group2InstrumentNames = Group2InstrumentNames.ToList(),
                    Group3InstrumentNames = Group3InstrumentNames.ToList(),
                    Group4InstrumentNames = Group4InstrumentNames.ToList(),
                    InstrumentSelections = instrumentSnapshot,
                    AvailableTags = AvailableTags.ToList(),
                    AvailableGenres = AvailableGenres.ToList(),
                    AvailableCabinets = AvailableCabinets.ToList(),
                    AvailableCompartments = AvailableCompartments.ToList(),
                    AvailableSlots = AvailableSlots.ToList(),
                    SavePieceAsync = async (p, selections) => await repository.SavePieceAsync(p, selections).ConfigureAwait(false),
                    OpenInEditor = id => editorPieceId = id,
                    OpenSheetMusic = p => _ = OpenSheetMusicAsync(p),
                    DeletePieceAsync = async (id, title) => await TryDeletePieceFromDetailAsync(id, title).ConfigureAwait(false),
                    RefreshPieceMetadataAsync = async p =>
                    {
                        var (updated, _) = await repository.GetPieceWithInstrumentsAsync(p.Id).ConfigureAwait(false);
                        p.CabinetColor = updated.CabinetColor;
                        if (string.IsNullOrWhiteSpace(p.CabinetColor) && !string.IsNullOrWhiteSpace(p.Cabinet))
                        {
                            p.CabinetColor = Application.Current.Dispatcher.Invoke(() =>
                                AvailableCabinets.FirstOrDefault(c => c.Name == p.Cabinet)?.Color);
                        }
                    },
                    GetWebViewUrlAsync = BuildWebViewUrlAsync
                };

                PieceDetailResult dialogResult = PieceDetailResult.Closed;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = new PieceDetailWindow(host) { Owner = this };
                    win.ShowDialog();
                    dialogResult = win.Result;

                    if (editorPieceId.HasValue)
                    {
                        MainTabControl.SelectedIndex = 0;
                        Activate();
                        Focus();
                    }
                });

                if (editorPieceId.HasValue)
                {
                    await LoadPieceIntoEditorAsync(editorPieceId.Value).ConfigureAwait(true);
                }
                else switch (dialogResult)
                {
                    case PieceDetailResult.Saved:
                    case PieceDetailResult.Deleted:
                        await LoadPiecesAsync().ConfigureAwait(false);
                        if (FilteredPieces.Count > 0)
                        {
                            await LoadFilteredPiecesAsync().ConfigureAwait(false);
                        }

                        Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Laden der Details: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> TryDeletePieceFromDetailAsync(long id, string title)
        {
            var result = UiMessage.Confirm(
                $"Musikstück \"{title}\" endgültig löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            try
            {
                await repository.DeletePieceAsync(id).ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CurrentPiece.Id == id)
                    {
                        ResetForm();
                    }
                });
                UiMessage.Show("Musikstück gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
                return true;
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Löschen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private async void ExportListJson_Click(object sender, RoutedEventArgs e)
        {
            if (Pieces.Count == 0)
            {
                UiMessage.Show("Die Liste ist leer, es gibt nichts zu exportieren.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Musikstückliste als JSON exportieren",
                Filter = "JSON-Datei (*.json)|*.json",
                FileName = "Musikstueckliste.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await PieceExportImporter.ExportAsJsonAsync(Pieces, dlg.FileName).ConfigureAwait(false);
                UiMessage.Show("Liste erfolgreich exportiert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Export: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExportListCsv_Click(object sender, RoutedEventArgs e)
        {
            if (Pieces.Count == 0)
            {
                UiMessage.Show("Die Liste ist leer, es gibt nichts zu exportieren.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Musikstückliste als CSV exportieren",
                Filter = "CSV-Datei (*.csv)|*.csv",
                FileName = "Musikstueckliste.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await PieceExportImporter.ExportAsCsvAsync(Pieces, dlg.FileName).ConfigureAwait(false);
                UiMessage.Show("Liste erfolgreich exportiert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Export: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportListJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Musikstückliste aus JSON importieren",
                Filter = "JSON-Datei (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var imported = await PieceExportImporter.ImportFromJsonAsync(dlg.FileName).ConfigureAwait(false);

                if (imported.Count == 0)
                {
                    UiMessage.Show("Die Datei enthält keine Musikstücke.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = UiMessage.Confirm(
                        $"{imported.Count} Musikstück(e) gefunden. Als neue Einträge importieren?",
                        "Import bestätigen",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                foreach (var piece in imported)
                {
                    await repository.SaveImportedPieceAsync(piece).ConfigureAwait(false);
                }

                await LoadPiecesAsync().ConfigureAwait(false);

                UiMessage.Show($"{imported.Count} Musikstück(e) erfolgreich importiert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Dispatcher.Invoke(RefreshSyncStatusUi);
            }
            catch (JsonException)
            {
                UiMessage.Show("Fehler beim Lesen der Datei. Ungültiges JSON-Format.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Import: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Einstellungen exportieren",
                Filter = "JSON-Datei (*.json)|*.json",
                FileName = "Musikarchiv_Einstellungen.json"
            };
            if (dlg.ShowDialog() != true) return;

            // UI-Daten auf dem UI-Thread einsammeln
            var tags = AvailableTags.ToList();
            var genres = AvailableGenres.ToList();
            var cabinets = AvailableCabinets
                .Select(c => new CabinetExportEntry { Name = c.Name, Color = c.Color })
                .ToList();
            var compartments = AvailableCompartments.ToList();
            var slots = AvailableSlots.ToList();
            string filePath = dlg.FileName;

            try
            {
                var assignments = await repository.GetGroupAssignmentsAsync().ConfigureAwait(false);

                var exportData = new SettingsExport
                {
                    Version = "1.0",
                    ExportedAt = System.DateTime.Now.ToString("o"),
                    Tags = tags,
                    Genres = genres,
                    Cabinets = cabinets,
                    Compartments = compartments,
                    Slots = slots,
                    GroupAssignments = assignments
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
                UiMessage.Show("Einstellungen erfolgreich exportiert.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Export: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Einstellungen importieren",
                Filter = "JSON-Datei (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = await File.ReadAllTextAsync(dlg.FileName);
                var data = JsonSerializer.Deserialize<SettingsExport>(json);
                if (data == null)
                {
                    UiMessage.Show("Ungültige Datei.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Tags
                foreach (var tag in data.Tags)
                {
                    if (!AvailableTags.Contains(tag))
                    {
                        await repository.AddTagOptionAsync(tag);
                        AvailableTags.Add(tag);
                    }
                }

                // Musikgattungen
                foreach (var genre in data.Genres)
                {
                    if (!AvailableGenres.Contains(genre))
                    {
                        await repository.AddGenreOptionAsync(genre);
                        AvailableGenres.Add(genre);
                    }
                }

                // Schränke: neue hinzufügen, vorhandene Farbe aktualisieren falls abweichend
                foreach (var cab in data.Cabinets)
                {
                    if (string.IsNullOrWhiteSpace(cab.Name)) continue;

                    var existing = AvailableCabinets.FirstOrDefault(c => c.Name == cab.Name);
                    if (existing != null)
                    {
                        // Farbe aktualisieren wenn sie abweicht
                        if (existing.Color != cab.Color)
                        {
                            existing.Color = cab.Color;
                            await repository.UpdateCabinetColorAsync(cab.Name, cab.Color);
                        }
                    }
                    else
                    {
                        await repository.AddCabinetOptionAsync(cab.Name, cab.Color);
                        AvailableCabinets.Add(new CabinetOption { Name = cab.Name, Color = cab.Color });
                        CabinetFilterOptions.Add(cab.Name);
                    }
                }

                // Fächer
                foreach (var comp in data.Compartments)
                {
                    if (!AvailableCompartments.Contains(comp))
                    {
                        await repository.AddCompartmentOptionAsync(comp);
                        AvailableCompartments.Add(comp);
                        CompartmentFilterOptions.Add(comp);
                    }
                }

                // Einschübe
                foreach (var slot in data.Slots)
                {
                    if (!AvailableSlots.Contains(slot))
                    {
                        await repository.AddSlotOptionAsync(slot);
                        AvailableSlots.Add(slot);
                        SlotFilterOptions.Add(slot);
                    }
                }

                // Besetzungsgruppen-Zuweisungen
                foreach (var (instrName, groupId) in data.GroupAssignments)
                {
                    if (string.IsNullOrWhiteSpace(instrName) || groupId < 1 || groupId > 4) continue;

                    bool existsInUI = Group1InstrumentNames.Contains(instrName)
                                  || Group2InstrumentNames.Contains(instrName)
                                  || Group3InstrumentNames.Contains(instrName)
                                  || Group4InstrumentNames.Contains(instrName)
                                  || UnassignedInstrumentNames.Contains(instrName);

                    if (!existsInUI)
                    {
                        var instrument = await repository.AddInstrumentAsync(instrName);
                        if (instrument != null)
                        {
                            Instruments.Add(new InstrumentSelection(instrument));
                            UnassignedInstrumentNames.Add(instrName);
                        }
                    }

                    // Aktuelle Gruppe ermitteln
                    int currentGroup = 0;
                    if (Group1InstrumentNames.Contains(instrName)) currentGroup = 1;
                    else if (Group2InstrumentNames.Contains(instrName)) currentGroup = 2;
                    else if (Group3InstrumentNames.Contains(instrName)) currentGroup = 3;
                    else if (Group4InstrumentNames.Contains(instrName)) currentGroup = 4;

                    if (currentGroup != groupId)
                        await MoveInstrumentToGroup(instrName, groupId);
                }

                UiMessage.Show("Einstellungen erfolgreich importiert.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (JsonException)
            {
                UiMessage.Show("Fehler beim Lesen der Datei. Ungültiges JSON-Format.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.Exception ex)
            {
                UiMessage.Show($"Fehler beim Import: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAppConfigToUi()
        {
            if (LabelQrCodeEnabledCheckBox == null)
            {
                return;
            }

            var config = AppConfigStore.Load();
            LabelQrCodeEnabledCheckBox.IsChecked = config.LabelQrCodeEnabled;
        }

        private async void LabelQrCodeEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (LabelQrCodeEnabledCheckBox == null)
            {
                return;
            }

            var config = AppConfigStore.Load();
            config.LabelQrCodeEnabled = LabelQrCodeEnabledCheckBox.IsChecked == true;
            await AppConfigStore.SaveAsync(config).ConfigureAwait(true);
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                await CheckForUpdatesAsync(silent: true).ConfigureAwait(true);
            }
            catch
            {
                // Start nicht blockieren, wenn GitHub nicht erreichbar ist.
            }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(silent: false).ConfigureAwait(true);
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            if (UpdateStatusText != null)
            {
                UpdateStatusText.Text = "Suche nach Updates …";
            }

            if (CheckUpdatesButton != null)
            {
                CheckUpdatesButton.IsEnabled = false;
            }

            try
            {
                var latest = await AppUpdateService.CheckAsync().ConfigureAwait(true);
                FillUpdateVersionList(latest);
                pendingAppUpdate = latest is { IsNewer: true } ? latest : SelectedUpdate();
                ApplyUpdateUi(latest, error: null);
                if (!silent && pendingAppUpdate is not { IsNewer: true })
                {
                    UiMessage.Show(
                        latest == null
                            ? "Kein veröffentlichtes Update gefunden."
                            : $"Die App ist aktuell (Version {AppVersion.Current}).",
                        "Aktualisierung",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                pendingAppUpdate = null;
                FillUpdateVersionList(null);
                ApplyUpdateUi(null, ex.Message);
                if (!silent)
                {
                    UiMessage.Show($"Update-Prüfung fehlgeschlagen: {ex.Message}", "Aktualisierung", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                if (CheckUpdatesButton != null)
                {
                    CheckUpdatesButton.IsEnabled = true;
                }
            }
        }

        private void UpdateVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            pendingAppUpdate = SelectedUpdate();
            ApplyUpdateUi(AppUpdateService.Latest, error: null);
        }

        private AppUpdateInfo? SelectedUpdate()
        {
            return UpdateVersionComboBox?.SelectedItem as AppUpdateInfo;
        }

        private void FillUpdateVersionList(AppUpdateInfo? latest)
        {
            if (UpdateVersionComboBox == null)
            {
                return;
            }

            UpdateVersionComboBox.SelectionChanged -= UpdateVersionComboBox_SelectionChanged;
            UpdateVersionComboBox.ItemsSource = AppUpdateService.Releases;
            var pick = latest is { IsNewer: true }
                ? latest
                : AppUpdateService.Releases.FirstOrDefault(item => AppUpdateService.CompareVersions(item.Version, AppVersion.Current) == 0)
                  ?? AppUpdateService.Releases.FirstOrDefault();
            UpdateVersionComboBox.SelectedItem = pick;
            pendingAppUpdate = pick;
            UpdateVersionComboBox.SelectionChanged += UpdateVersionComboBox_SelectionChanged;
        }

        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            pendingAppUpdate = SelectedUpdate() ?? pendingAppUpdate;
            if (pendingAppUpdate == null)
            {
                await CheckForUpdatesAsync(silent: false).ConfigureAwait(true);
                if (pendingAppUpdate == null)
                {
                    return;
                }
            }

            if (!AppUpdateService.CanApplyInPlace())
            {
                UiMessage.Show(
                    "Die Aktualisierung funktioniert in der portable bzw. installierten App. Aus dem Entwicklungsordner kann sie nicht überschrieben werden.",
                    "Aktualisierung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var update = pendingAppUpdate;
            if (update == null)
            {
                return;
            }

            var sameVersion = AppUpdateService.CompareVersions(update.Version, AppVersion.Current) == 0;
            var confirm = UiMessage.Confirm(
                sameVersion
                    ? $"Version {update.Version} ist bereits installiert. Trotzdem herunterladen und neu starten?"
                    : $"Version {update.Version} herunterladen und die App neu starten?",
                "Aktualisierung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (InstallUpdateButton != null)
            {
                InstallUpdateButton.IsEnabled = false;
            }

            if (UpdateBannerButton != null)
            {
                UpdateBannerButton.IsEnabled = false;
            }

            if (CheckUpdatesButton != null)
            {
                CheckUpdatesButton.IsEnabled = false;
            }

            if (UpdateProgressBar != null)
            {
                UpdateProgressBar.Value = 0;
                UpdateProgressBar.Visibility = Visibility.Visible;
            }

            var progress = new Progress<(long received, long total)>(tuple =>
            {
                if (UpdateProgressBar == null)
                {
                    return;
                }

                if (tuple.total > 0)
                {
                    UpdateProgressBar.Value = Math.Min(100, tuple.received * 100d / tuple.total);
                    UpdateStatusText.Text = $"Lade Update {update.Version} … {tuple.received / (1024 * 1024.0):0.0} / {tuple.total / (1024 * 1024.0):0.0} MB";
                }
                else
                {
                    UpdateStatusText.Text = $"Lade Update {update.Version} …";
                }
            });

            try
            {
                await AppUpdateService.DownloadAndApplyAsync(update, progress).ConfigureAwait(true);
                UpdateStatusText.Text = "Update geladen. Die App wird neu gestartet …";
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ApplyUpdateUi(update, ex.Message);
                UiMessage.Show($"Update fehlgeschlagen: {ex.Message}", "Aktualisierung", MessageBoxButton.OK, MessageBoxImage.Error);
                if (CheckUpdatesButton != null)
                {
                    CheckUpdatesButton.IsEnabled = true;
                }
            }
        }

        private void ApplyUpdateUi(AppUpdateInfo? latest, string? error)
        {
            var selected = SelectedUpdate();
            var hasSelection = selected != null;
            var hasUpdate = latest is { IsNewer: true };
            if (InstallUpdateButton != null)
            {
                InstallUpdateButton.IsEnabled = hasSelection;
            }

            if (UpdateBannerButton != null)
            {
                UpdateBannerButton.IsEnabled = hasUpdate;
            }

            if (UpdateProgressBar != null)
            {
                UpdateProgressBar.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = error;
                }

                if (UpdateBanner != null)
                {
                    UpdateBanner.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (hasUpdate && latest != null)
            {
                var text = $"Neue Version {latest.Version} ist verfügbar (aktuell {AppVersion.Current}).";
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = text;
                }

                if (UpdateBannerText != null)
                {
                    UpdateBannerText.Text = text;
                }

                if (UpdateBanner != null)
                {
                    UpdateBanner.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = latest == null
                        ? "Kein veröffentlichtes Update gefunden."
                        : $"Die App ist aktuell (Version {AppVersion.Current}).";
                }

                if (UpdateBanner != null)
                {
                    UpdateBanner.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void LoadSyncConfigToUi()
        {
            if (SyncServerUrlTextBox == null)
            {
                return;
            }

            syncConfig = SyncConfigStore.Load();
            SyncServerUrlTextBox.Text = syncConfig.ServerUrl;
            SyncApiKeyBox.Password = syncConfig.ApiKey ?? string.Empty;
            SyncWebPasswordBox.Password = string.IsNullOrWhiteSpace(syncConfig.WebViewPassword)
                || string.Equals(syncConfig.WebViewPassword, "admin", StringComparison.Ordinal)
                ? string.Empty
                : syncConfig.WebViewPassword;
            SyncWarningDaysTextBox.Text = syncConfig.SyncWarningDays.ToString();
            RefreshSyncStatusUi();
        }

        private void RefreshSyncStatusUi()
        {
            if (SyncWarningBanner == null)
            {
                return;
            }

            syncConfig = SyncConfigStore.Load();

            LastLocalChangeText.Text = syncConfig.LastLocalChangeAt.HasValue
                ? $"Letzte lokale Änderung: {syncConfig.LastLocalChangeAt.Value.ToLocalTime():g}"
                : "Noch keine lokale Änderung erfasst.";

            SyncStatusText.Text = syncConfig.LastSyncAt.HasValue
                ? $"Letzte Synchronisation: {syncConfig.LastSyncAt.Value.ToLocalTime():g}"
                : "Noch nicht synchronisiert.";

            if (LocalChangeTracker.ShouldShowSyncWarning(syncConfig))
            {
                SyncWarningText.Text = LocalChangeTracker.GetSyncWarningMessage(syncConfig);
                SyncWarningBanner.Visibility = Visibility.Visible;
            }
            else
            {
                SyncWarningBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void ReadSyncConfigFromUi()
        {
            syncConfig.ServerUrl = SyncServerUrlTextBox.Text.Trim();
            syncConfig.ApiKey = string.IsNullOrWhiteSpace(SyncApiKeyBox.Password) ? null : SyncApiKeyBox.Password;
            if (SyncWebPasswordBox != null && !string.IsNullOrWhiteSpace(SyncWebPasswordBox.Password))
            {
                syncConfig.WebViewPassword = SyncWebPasswordBox.Password;
            }

            if (int.TryParse(SyncWarningDaysTextBox.Text.Trim(), out var warningDays) && warningDays >= 0)
            {
                syncConfig.SyncWarningDays = warningDays;
            }
            else
            {
                syncConfig.SyncWarningDays = 7;
            }
        }

        private async Task<string?> BuildWebViewUrlAsync(long pieceId)
        {
            syncConfig = SyncConfigStore.Load();
            if (string.IsNullOrWhiteSpace(syncConfig.ServerUrl))
            {
                return null;
            }

            var syncUid = await syncRepository.EnsurePieceSyncUidAsync(pieceId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(syncUid))
            {
                return null;
            }

            return $"{syncConfig.ServerUrl.TrimEnd('/')}/#/piece/{syncUid}";
        }

        private async void SyncSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            ReadSyncConfigFromUi();
            if (!WebPasswordPolicy.TryValidate(syncConfig.WebViewPassword, out var passwordError))
            {
                UiMessage.Show(passwordError, "Web-Passwort", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await SyncConfigStore.SaveAsync(syncConfig).ConfigureAwait(true);
            RefreshSyncStatusUi();
            UpdateAdminCredentialsDisplay();
            SetSyncActivityMessage("Konfiguration gespeichert.");
            if (SyncStatusText != null)
            {
                SyncStatusText.Text = "Konfiguration gespeichert.";
            }
        }

        private async void SyncTestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSyncOperation(SyncOperation.Testing, "Verbindung wird getestet …"))
            {
                return;
            }

            ReadSyncConfigFromUi();
            try
            {
                var (ok, message) = await syncService.TestConnectionAsync(syncConfig, syncCancellation!.Token).ConfigureAwait(true);
                SetSyncActivityMessage(message);
                if (SyncStatusText != null)
                {
                    SyncStatusText.Text = message;
                }

                if (!ok)
                {
                    UiMessage.Show(message, "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                SetSyncActivityMessage("Verbindungstest abgebrochen.");
            }
            finally
            {
                EndSyncOperation();
            }
        }

        private async void SyncPush_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSyncOperation(SyncOperation.Uploading, "Upload läuft …"))
            {
                return;
            }

            ReadSyncConfigFromUi();
            try
            {
                var progress = CreateSyncProgressReporter();
                var (ok, message) = await syncService.PushAsync(syncConfig, syncCancellation!.Token, progress).ConfigureAwait(true);
                RefreshSyncStatusUi();
                SetSyncActivityMessage(message);
                if (SyncStatusText != null)
                {
                    SyncStatusText.Text = message;
                }

                UiMessage.Show(message, ok ? "Synchronisation" : "Fehler",
                    MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (OperationCanceledException)
            {
                SetSyncActivityMessage("Upload abgebrochen.");
                UiMessage.Show("Upload abgebrochen.", "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                EndSyncOperation();
            }
        }

        private async void SyncPull_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSyncOperation(SyncOperation.Downloading, "Download läuft …"))
            {
                return;
            }

            ReadSyncConfigFromUi();
            try
            {
                var progress = CreateSyncProgressReporter();
                var (ok, message) = await syncService.PullAsync(syncConfig, syncCancellation!.Token, progress).ConfigureAwait(true);
                RefreshSyncStatusUi();
                SetSyncActivityMessage(message);
                if (SyncStatusText != null)
                {
                    SyncStatusText.Text = message;
                }

                if (ok)
                {
                    await LoadPiecesAsync().ConfigureAwait(true);
                }

                UiMessage.Show(message, ok ? "Synchronisation" : "Fehler",
                    MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (OperationCanceledException)
            {
                SetSyncActivityMessage("Download abgebrochen.");
                UiMessage.Show("Download abgebrochen.", "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                EndSyncOperation();
            }
        }

        private void SyncStopUpload_Click(object sender, RoutedEventArgs e)
        {
            if (activeSyncOperation != SyncOperation.Uploading)
            {
                return;
            }

            SetSyncActivityMessage("Upload wird abgebrochen …");
            syncCancellation?.Cancel();
        }

        private bool TryBeginSyncOperation(SyncOperation operation, string activityMessage)
        {
            if (activeSyncOperation != SyncOperation.None)
            {
                UiMessage.Show(
                    $"Es läuft bereits ein Sync-Vorgang ({DescribeSyncOperation(activeSyncOperation)}).",
                    "Synchronisation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            activeSyncOperation = operation;
            syncCancellation = new CancellationTokenSource();
            lastSyncProgressUiUpdate = DateTime.MinValue;
            SetSyncActivityMessage(activityMessage);
            UpdateSyncButtonsUi();
            ShowSyncProgressBar(operation is SyncOperation.Uploading or SyncOperation.Downloading);
            return true;
        }

        private void EndSyncOperation()
        {
            activeSyncOperation = SyncOperation.None;
            syncCancellation?.Dispose();
            syncCancellation = null;
            UpdateSyncButtonsUi();
            ShowSyncProgressBar(false);
        }

        private IProgress<SyncProgressReport> CreateSyncProgressReporter()
        {
            return new Progress<SyncProgressReport>(report => UpdateSyncProgressUi(report));
        }

        private void UpdateSyncProgressUi(SyncProgressReport report)
        {
            var now = DateTime.UtcNow;
            if (report.PercentComplete < 100
                && (now - lastSyncProgressUiUpdate).TotalMilliseconds < 120)
            {
                return;
            }

            lastSyncProgressUiUpdate = now;
            SetSyncActivityMessage(SyncProgressFormatter.FormatProgressLine(report));
            if (SyncProgressBar != null)
            {
                SyncProgressBar.Value = report.PercentComplete;
            }
        }

        private void ShowSyncProgressBar(bool visible)
        {
            if (SyncProgressBar == null)
            {
                return;
            }

            SyncProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                SyncProgressBar.Value = 0;
            }
        }

        private void SetSyncActivityMessage(string message)
        {
            if (SyncActivityText != null)
            {
                SyncActivityText.Text = message;
            }
        }

        private void UpdateSyncButtonsUi()
        {
            var busy = activeSyncOperation != SyncOperation.None;
            if (SyncPushButton != null)
            {
                SyncPushButton.IsEnabled = !busy;
            }

            if (SyncPullButton != null)
            {
                SyncPullButton.IsEnabled = !busy;
            }

            if (SyncStopUploadButton != null)
            {
                SyncStopUploadButton.Visibility = activeSyncOperation == SyncOperation.Uploading
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static string DescribeSyncOperation(SyncOperation operation) => operation switch
        {
            SyncOperation.Testing => "Verbindungstest",
            SyncOperation.Uploading => "Upload",
            SyncOperation.Downloading => "Download",
            _ => "Sync"
        };

        private enum SyncOperation
        {
            None,
            Testing,
            Uploading,
            Downloading
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly System.Action<object?> execute;
        private readonly System.Predicate<object?>? canExecute;

        public RelayCommand(System.Action<object?> execute, System.Predicate<object?>? canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return canExecute == null || canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            execute(parameter);
        }

        public event System.EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
