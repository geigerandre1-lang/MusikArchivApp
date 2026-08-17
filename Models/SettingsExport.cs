using System.Collections.Generic;

namespace MusikArchivApp.Models
{
    public class SettingsExport
    {
        public string Version { get; set; } = "1.0";
        public string ExportedAt { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> Genres { get; set; } = new List<string>();
        public List<CabinetExportEntry> Cabinets { get; set; } = new List<CabinetExportEntry>();
        public List<string> Compartments { get; set; } = new List<string>();
        public List<string> Slots { get; set; } = new List<string>();
        public Dictionary<string, int> GroupAssignments { get; set; } = new Dictionary<string, int>();
    }

    public class CabinetExportEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#FFFFFF";
    }
}
