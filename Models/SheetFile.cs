using System;
using System.IO;

namespace MusikArchivApp.Models
{
    public class SheetFile
    {
        public long Id { get; set; }
        public long PieceId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public long? InstrumentId { get; set; }
        public string? InstrumentName { get; set; }
        public int? InstrumentGroupId { get; set; }
        public int SortOrder { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? ContentType { get; set; }
        public string? ContentHash { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool HasFileData { get; set; }

        public string AssignmentDisplay
        {
            get
            {
                if (InstrumentId.HasValue && !string.IsNullOrWhiteSpace(InstrumentName))
                {
                    return InstrumentName;
                }

                return InstrumentGroupId switch
                {
                    1 => "Gruppe: Partitur / Direktion",
                    2 => "Gruppe: Holz",
                    3 => "Gruppe: Schlagwerk",
                    4 => "Gruppe: Blechbläser / Gesang",
                    _ => "Allgemein / Gesamt"
                };
            }
        }

        public bool IsPdf => FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        public bool IsImage =>
            FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
            || FileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)
            || FileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}
