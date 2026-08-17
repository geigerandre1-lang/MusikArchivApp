using System.Collections.Generic;

namespace MusikArchivApp.Models
{
    public class ColumnEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public class ColumnConfig
    {
        public List<ColumnEntry> Columns { get; set; } = new List<ColumnEntry>();
    }
}
