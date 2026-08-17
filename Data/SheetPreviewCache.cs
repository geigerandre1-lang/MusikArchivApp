using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MusikArchivApp.Data
{
    /// <summary>
    /// Temporäre Dateien für PDF-Vorschau und Druck (WebView2 braucht einen Pfad).
    /// </summary>
    public static class SheetPreviewCache
    {
        public static string WriteTempFile(long sheetFileId, string fileName, byte[] data)
        {
            var safeName = SheetMusicPaths.SanitizePathSegment(Path.GetFileName(fileName));
            var path = Path.Combine(AppPaths.GetPreviewCacheDirectory(), $"{sheetFileId}_{safeName}");
            File.WriteAllBytes(path, data);
            return path;
        }

        public static void ClearForFile(long sheetFileId)
        {
            var dir = AppPaths.GetPreviewCacheDirectory();
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(dir, $"{sheetFileId}_*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignore locked preview files
                }
            }
        }
    }

    public static class SheetFileMigration
    {
        public static async Task MigrateFilesystemToDatabaseAsync(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var legacyRoot = GetLegacyNotenRoot();
            if (!Directory.Exists(legacyRoot) && !Directory.Exists(AppPaths.GetDataRoot()))
            {
                return;
            }

            using var select = connection.CreateCommand();
            select.CommandText = @"
SELECT id, stored_path, file_name
FROM sheet_files
WHERE (file_data IS NULL OR length(file_data) = 0)
  AND stored_path IS NOT NULL
  AND stored_path != ''";

            var pending = new List<(long Id, string StoredPath, string FileName)>();
            using (var reader = await select.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    pending.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
                }
            }

            foreach (var (id, storedPath, fileName) in pending)
            {
                var fullPath = SheetMusicPaths.ResolveStoredPath(storedPath);
                if (!File.Exists(fullPath))
                {
                    var normalized = storedPath.Replace('/', Path.DirectorySeparatorChar);
                    var notenPrefix = "Noten" + Path.DirectorySeparatorChar;
                    if (normalized.StartsWith(notenPrefix, StringComparison.OrdinalIgnoreCase)
                        || normalized.StartsWith("Noten/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fullPath = Path.Combine(AppPaths.GetNotenDirectory(), normalized);
                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }
                }

                var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                var contentType = SheetMusicPaths.GetContentType(fileName);
                var updatedAt = DateTime.UtcNow.ToString("o");

                using var update = connection.CreateCommand();
                update.CommandText = @"
UPDATE sheet_files
SET file_data = $data,
    content_type = $contentType,
    content_hash = $hash,
    updated_at = $updatedAt
WHERE id = $id";
                update.Parameters.AddWithValue("$data", bytes);
                update.Parameters.AddWithValue("$contentType", contentType);
                update.Parameters.AddWithValue("$hash", hash);
                update.Parameters.AddWithValue("$updatedAt", updatedAt);
                update.Parameters.AddWithValue("$id", id);
                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static string GetLegacyNotenRoot()
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MusikArchivApp",
                "Noten");

            if (Directory.Exists(appDataRoot))
            {
                return appDataRoot;
            }

            return Path.Combine(AppPaths.GetDataRoot(), "Noten");
        }

        public static async Task MigrateDatabaseToFilesystemAsync(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var select = connection.CreateCommand();
            select.CommandText = @"
SELECT sf.id, sf.piece_id, sf.file_name, sf.file_data, sf.stored_path, p.title
FROM sheet_files sf
JOIN pieces p ON p.id = sf.piece_id
WHERE sf.file_data IS NOT NULL AND length(sf.file_data) > 0
  AND (sf.stored_path IS NULL OR sf.stored_path = '')";

            var pending = new List<(long Id, long PieceId, string FileName, byte[] Data, string Title)>();
            using (var reader = await select.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    pending.Add((
                        reader.GetInt64(0),
                        reader.GetInt64(1),
                        reader.GetString(2),
                        (byte[])reader.GetValue(3),
                        reader.GetString(5)));
                }
            }

            foreach (var (id, pieceId, fileName, data, title) in pending)
            {
                var storedPath = SheetMusicPaths.BuildRelativeStoredPath(pieceId, title, fileName);
                var physicalPath = SheetMusicPaths.ResolveStoredPath(storedPath);
                if (!File.Exists(physicalPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
                    await File.WriteAllBytesAsync(physicalPath, data).ConfigureAwait(false);
                }

                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE sheet_files SET stored_path = $storedPath WHERE id = $id";
                update.Parameters.AddWithValue("$storedPath", storedPath);
                update.Parameters.AddWithValue("$id", id);
                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
