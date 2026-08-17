using System;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public static class LocalChangeTracker
    {
        public static async Task RecordChangeAsync()
        {
            var config = SyncConfigStore.Load();
            config.LastLocalChangeAt = DateTime.UtcNow;
            await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);
        }

        public static bool ShouldShowSyncWarning(SyncConfig config)
        {
            if (config.SyncWarningDays <= 0)
            {
                return false;
            }

            if (!config.LastSyncAt.HasValue)
            {
                return true;
            }

            return (DateTime.UtcNow - config.LastSyncAt.Value).TotalDays > config.SyncWarningDays;
        }

        public static string GetSyncWarningMessage(SyncConfig config)
        {
            if (!config.LastSyncAt.HasValue)
            {
                return "Es wurde noch nie synchronisiert. Bitte Daten unter Einstellungen zum Server hochladen.";
            }

            var days = (int)Math.Floor((DateTime.UtcNow - config.LastSyncAt.Value).TotalDays);
            var dayLabel = days == 1 ? "1 Tag" : $"{days} Tagen";
            return $"Letzte Synchronisation ist mehr als {config.SyncWarningDays} Tage her (vor {dayLabel}, am {config.LastSyncAt.Value.ToLocalTime():g}). Bitte synchronisieren.";
        }
    }
}
