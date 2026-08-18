using System;
using System.Collections.Generic;

namespace MusikArchivApp.Models
{
    public sealed class DuplicatePieceEntry
    {
        public long Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Composer { get; set; }

        public string? Arranger { get; set; }

        public string? Cabinet { get; set; }

        public string? Compartment { get; set; }

        public string? Slot { get; set; }

        public string? SyncUid { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public int SheetCount { get; set; }

        public string DisplayText =>
            $"ID {Id} · {Title} · {SheetCount} Noten · Sync: {FormatSyncUid(SyncUid)}";

        private static string FormatSyncUid(string? syncUid)
        {
            if (string.IsNullOrWhiteSpace(syncUid))
            {
                return "—";
            }

            return syncUid.Length <= 8 ? syncUid : syncUid[..8] + "…";
        }
    }

    public sealed class DuplicatePieceGroup
    {
        public string MatchKey { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public List<DuplicatePieceEntry> Entries { get; set; } = new();

        public long RecommendedKeepId { get; set; }
    }

    public sealed class DuplicateSheetEntry
    {
        public long Id { get; set; }

        public long PieceId { get; set; }

        public string PieceTitle { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string? SyncUid { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string? ContentHash { get; set; }

        public string DisplayText =>
            $"ID {Id} · {FileName} · Stück {PieceId} ({PieceTitle})";
    }

    public sealed class DuplicateSheetGroup
    {
        public string MatchKey { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public List<DuplicateSheetEntry> Entries { get; set; } = new();

        public long RecommendedKeepId { get; set; }
    }

    public sealed class DuplicateCleanupResult
    {
        public int RemovedPieces { get; set; }

        public int RemovedSheets { get; set; }

        public int MergedSheets { get; set; }
    }
}
