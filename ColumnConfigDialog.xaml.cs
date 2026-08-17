using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusikArchivApp.Models;

namespace MusikArchivApp
{
    public partial class ColumnConfigDialog : Window
    {
        public ObservableCollection<ColumnEntry> Available { get; } = new ObservableCollection<ColumnEntry>();
        public ObservableCollection<ColumnEntry> Selected { get; } = new ObservableCollection<ColumnEntry>();

        // Result after OK
        public List<ColumnEntry>? ResultColumns { get; private set; }

        public ColumnConfigDialog(IEnumerable<ColumnEntry> allColumns, IEnumerable<ColumnEntry> currentSelected)
        {
            InitializeComponent();

            var selectedKeys = currentSelected.Select(c => c.Key).ToHashSet();

            // Selected in current order
            foreach (var e in currentSelected)
                Selected.Add(new ColumnEntry { Key = e.Key, Header = e.Header, IsVisible = true });

            // Available = all that are not selected
            foreach (var e in allColumns)
                if (!selectedKeys.Contains(e.Key))
                    Available.Add(new ColumnEntry { Key = e.Key, Header = e.Header, IsVisible = false });

            AvailableListBox.ItemsSource = Available;
            AvailableListBox.DisplayMemberPath = "Header";
            SelectedListBox.ItemsSource = Selected;
            SelectedListBox.DisplayMemberPath = "Header";
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableListBox.SelectedItem is not ColumnEntry entry) return;
            Available.Remove(entry);
            entry.IsVisible = true;
            Selected.Add(entry);
            SelectedListBox.SelectedItem = entry;
        }

        private void AddAllButton_Click(object sender, RoutedEventArgs e)
        {
            var items = Available.ToList();
            Available.Clear();
            foreach (var item in items)
            {
                item.IsVisible = true;
                Selected.Add(item);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedListBox.SelectedItem is not ColumnEntry entry) return;
            Selected.Remove(entry);
            entry.IsVisible = false;
            Available.Add(entry);
            AvailableListBox.SelectedItem = entry;
        }

        private void RemoveAllButton_Click(object sender, RoutedEventArgs e)
        {
            var items = Selected.ToList();
            Selected.Clear();
            foreach (var item in items)
            {
                item.IsVisible = false;
                Available.Add(item);
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedListBox.SelectedItem is not ColumnEntry entry) return;
            int idx = Selected.IndexOf(entry);
            if (idx <= 0) return;
            Selected.Move(idx, idx - 1);
            SelectedListBox.SelectedItem = entry;
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedListBox.SelectedItem is not ColumnEntry entry) return;
            int idx = Selected.IndexOf(entry);
            if (idx < 0 || idx >= Selected.Count - 1) return;
            Selected.Move(idx, idx + 1);
            SelectedListBox.SelectedItem = entry;
        }

        private void AvailableListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => AddButton_Click(sender, e);

        private void SelectedListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => RemoveButton_Click(sender, e);

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ResultColumns = Selected.ToList();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
