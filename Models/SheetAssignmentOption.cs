namespace MusikArchivApp.Models
{
    public class SheetAssignmentOption
    {
        public string Label { get; init; } = string.Empty;
        public long? InstrumentId { get; init; }
        public int? InstrumentGroupId { get; init; }

        public static SheetAssignmentOption General { get; } = new() { Label = "Allgemein / Gesamt" };

        public static SheetAssignmentOption Group(int groupId, string label) =>
            new() { Label = label, InstrumentGroupId = groupId };

        public static SheetAssignmentOption Instrument(long id, string name) =>
            new() { Label = name, InstrumentId = id };
    }
}
