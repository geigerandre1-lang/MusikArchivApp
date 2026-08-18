using System;
using System.Collections.Generic;

namespace MusikArchivApp.Models
{
    public class SyncPushRequest
    {
        public DateTime? ClientLastSyncAt { get; set; }
        public string? WebViewPassword { get; set; }
        public List<PieceSyncDto> Pieces { get; set; } = new();
        public List<SheetSyncDto> Sheets { get; set; } = new();
        public List<SyncTombstoneDto> Tombstones { get; set; } = new();
    }

    public class SyncPullResponse
    {
        public DateTime ServerTime { get; set; }
        public List<PieceSyncDto> Pieces { get; set; } = new();
        public List<SheetSyncDto> Sheets { get; set; } = new();
        public List<SyncTombstoneDto> Tombstones { get; set; } = new();
    }

    public class SyncTombstoneDto
    {
        public string SyncUid { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
    }

    public class SyncHealthResponse
    {
        public bool Ok { get; set; }
        public string? Version { get; set; }
    }

    public class PieceSyncDto
    {
        public string SyncUid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Composer { get; set; }
        public string? Arranger { get; set; }
        public string? Publisher { get; set; }
        public string? Isbn { get; set; }
        public string? Tags { get; set; }
        public string? Genre { get; set; }
        public string? Cabinet { get; set; }
        public string? Compartment { get; set; }
        public string? Slot { get; set; }
        public bool IsActive { get; set; } = true;
        public string? FolderPath { get; set; }
        public List<string> InstrumentNames { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }

    public class SheetSyncDto
    {
        public string SyncUid { get; set; } = string.Empty;
        public string PieceSyncUid { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
        public long? InstrumentId { get; set; }
        public string? InstrumentName { get; set; }
        public int? InstrumentGroupId { get; set; }
        public int SortOrder { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
