using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public static class AppConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetConfigPath() => Path.Combine(AppPaths.GetDataRoot(), "app_config.json");

        public static AppConfig Load()
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                return new AppConfig();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static async Task SaveAsync(AppConfig config)
        {
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
    }
}
