using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public static class SyncConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetConfigPath() => Path.Combine(AppPaths.GetDataRoot(), "sync_config.json");

        public static SyncConfig Load()
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                return new SyncConfig();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SyncConfig>(json) ?? new SyncConfig();
            }
            catch
            {
                return new SyncConfig();
            }
        }

        public static async Task SaveAsync(SyncConfig config)
        {
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
    }
}
