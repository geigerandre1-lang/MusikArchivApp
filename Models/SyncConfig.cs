using System;

namespace MusikArchivApp.Models
{
    public class SyncConfig
    {
        public string ServerUrl { get; set; } = "http://localhost:3000";
        public string? ApiKey { get; set; }
        public string WebViewPassword { get; set; } = "admin";
        public DateTime? LastSyncAt { get; set; }
        public DateTime? LastLocalChangeAt { get; set; }
        public int SyncWarningDays { get; set; } = 7;
    }
}
