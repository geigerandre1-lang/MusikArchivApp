using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public class SheetMusicRepository
    {
        private readonly string connectionString;

        public SheetMusicRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task<IReadOnlyList<SheetFile>> GetFilesForPieceAsync(long pieceId)
        {
            var result = new List<SheetFile>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT sf.id, sf.piece_id, sf.file_name, sf.stored_path, sf.instrument_id, sf.instrument_group_id,
       sf.sort_order, sf.uploaded_at, i.name, sf.content_type, sf.content_hash, sf.updated_at,
       CASE WHEN sf.file_data IS NOT NULL AND length(sf.file_data) > 0 THEN 1 ELSE 0 END
FROM sheet_files sf
LEFT JOIN instruments i ON i.id = sf.instrument_id
WHERE sf.piece_id = $pieceId
ORDER BY sf.sort_order, sf.file_name";
            command.Parameters.AddWithValue("$pieceId", pieceId);

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(ReadSheetFile(reader));
            }

            return result;
        }

        public async Task<byte[]?> GetFileContentAsync(long sheetFileId)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT file_data, stored_path FROM sheet_files WHERE id = $id";
            command.Parameters.AddWithValue("$id", sheetFileId);

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                return null;
            }

            if (!reader.IsDBNull(0))
            {
                var bytes = (byte[])reader.GetValue(0);
                if (bytes.Length > 0)
                {
                    return bytes;
                }
            }

            if (reader.IsDBNull(1))
            {
                return null;
            }

            var storedPath = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return null;
            }

            var fullPath = SheetMusicPaths.ResolveStoredPath(storedPath);
            return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false) : null;
        }

        public async Task<SheetFile> AddFileAsync(
            long pieceId,
            string sourceFilePath,
            string? targetFileName = null,
            long? instrumentId = null,
            int? instrumentGroupId = null)
        {
            var extension = Path.GetExtension(sourceFilePath);
            var fileName = string.IsNullOrWhiteSpace(targetFileName)
                ? Path.GetFileName(sourceFilePath)
                : targetFileName;
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                fileName += extension;
            }

            fileName = await EnsureUniqueFileNameAsync(pieceId, fileName).ConfigureAwait(false);

            var bytes = await File.ReadAllBytesAsync(sourceFilePath).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var contentType = SheetMusicPaths.GetContentType(fileName);
            var uploadedAt = DateTime.Now;
            var updatedAt = DateTime.UtcNow;

            var pieceTitle = await GetPieceTitleAsync(pieceId).ConfigureAwait(false);
            var storedPath = SheetMusicPaths.BuildRelativeStoredPath(pieceId, pieceTitle, fileName);
            var physicalPath = SheetMusicPaths.ResolveStoredPath(storedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllBytesAsync(physicalPath, bytes).ConfigureAwait(false);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO sheet_files (
    piece_id, file_name, stored_path, instrument_id, instrument_group_id,
    sort_order, uploaded_at, file_data, content_type, content_hash, updated_at)
VALUES (
    $pieceId, $fileName, $storedPath, $instrumentId, $instrumentGroupId,
    $sortOrder, $uploadedAt, $fileData, $contentType, $contentHash, $updatedAt);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$pieceId", pieceId);
            command.Parameters.AddWithValue("$fileName", fileName);
            command.Parameters.AddWithValue("$storedPath", storedPath);
            command.Parameters.AddWithValue("$instrumentId", instrumentId.HasValue ? instrumentId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$instrumentGroupId", instrumentGroupId.HasValue ? instrumentGroupId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$sortOrder", await GetNextSortOrderAsync(connection, pieceId).ConfigureAwait(false));
            command.Parameters.AddWithValue("$uploadedAt", uploadedAt.ToString("o"));
            command.Parameters.AddWithValue("$fileData", bytes);
            command.Parameters.AddWithValue("$contentType", contentType);
            command.Parameters.AddWithValue("$contentHash", hash);
            command.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("o"));

            var id = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);

            await TouchPieceUpdatedAsync(pieceId).ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);

            return new SheetFile
            {
                Id = id,
                PieceId = pieceId,
                FileName = fileName,
                StoredPath = storedPath,
                InstrumentId = instrumentId,
                InstrumentGroupId = instrumentGroupId,
                SortOrder = 0,
                UploadedAt = uploadedAt,
                ContentType = contentType,
                ContentHash = hash,
                UpdatedAt = updatedAt,
                HasFileData = true
            };
        }

        public async Task UpdateAssignmentAsync(long sheetFileId, long? instrumentId, int? instrumentGroupId)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE sheet_files
SET instrument_id = $instrumentId,
    instrument_group_id = $instrumentGroupId,
    updated_at = $updatedAt
WHERE id = $id";
            command.Parameters.AddWithValue("$id", sheetFileId);
            command.Parameters.AddWithValue("$instrumentId", instrumentId.HasValue ? instrumentId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$instrumentGroupId", instrumentGroupId.HasValue ? instrumentGroupId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await TouchPieceUpdatedAsync(await GetPieceIdForSheetAsync(connection, sheetFileId).ConfigureAwait(false))
                .ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);
        }

        public async Task DeleteFileAsync(SheetFile file)
        {
            var pieceId = file.PieceId;
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM sheet_files WHERE id = $id";
            command.Parameters.AddWithValue("$id", file.Id);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            DeletePhysicalFile(file);
            SheetPreviewCache.ClearForFile(file.Id);
            await TouchPieceUpdatedAsync(pieceId).ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);
        }

        private void DeletePhysicalFile(SheetFile file)
        {
            if (!string.IsNullOrWhiteSpace(file.StoredPath))
            {
                var fullPath = SheetMusicPaths.ResolveStoredPath(file.StoredPath);
                TryDeleteFile(fullPath);
                TryDeleteEmptyDirectory(Path.GetDirectoryName(fullPath));
                return;
            }

            var pieceTitle = GetPieceTitleAsync(file.PieceId).GetAwaiter().GetResult();
            var fallbackPath = Path.Combine(
                AppPaths.GetPieceNotenDirectory(file.PieceId, pieceTitle),
                file.FileName);
            TryDeleteFile(fallbackPath);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(fallbackPath));
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore locked files
            }
        }

        private static void TryDeleteEmptyDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // ignore locked directories
            }
        }

        private async Task<string> GetPieceTitleAsync(long pieceId)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            return await GetPieceTitleAsync(connection, pieceId).ConfigureAwait(false);
        }

        private static async Task<string> GetPieceTitleAsync(SqliteConnection connection, long pieceId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT title FROM pieces WHERE id = $id";
            command.Parameters.AddWithValue("$id", pieceId);
            var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return value as string ?? string.Empty;
        }

        private static async Task<long> GetPieceIdForSheetAsync(SqliteConnection connection, long sheetFileId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT piece_id FROM sheet_files WHERE id = $id";
            command.Parameters.AddWithValue("$id", sheetFileId);
            return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
        }

        private async Task TouchPieceUpdatedAsync(long pieceId)
        {
            if (pieceId <= 0)
            {
                return;
            }

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE pieces SET updated_at = $updatedAt WHERE id = $id";
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$id", pieceId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task<string> EnsureUniqueFileNameAsync(long pieceId, string fileName)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var candidate = fileName;
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;

            while (await FileNameExistsAsync(connection, pieceId, candidate).ConfigureAwait(false))
            {
                candidate = $"{baseName}_{counter}{extension}";
                counter++;
            }

            return candidate;
        }

        private static async Task<bool> FileNameExistsAsync(SqliteConnection connection, long pieceId, string fileName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sheet_files WHERE piece_id = $pieceId AND file_name = $fileName";
            command.Parameters.AddWithValue("$pieceId", pieceId);
            command.Parameters.AddWithValue("$fileName", fileName);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
            return count > 0;
        }

        private static async Task<int> GetNextSortOrderAsync(SqliteConnection connection, long pieceId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM sheet_files WHERE piece_id = $pieceId";
            command.Parameters.AddWithValue("$pieceId", pieceId);
            var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return Convert.ToInt32(value ?? 0);
        }

        private static SheetFile ReadSheetFile(SqliteDataReader reader)
        {
            return new SheetFile
            {
                Id = reader.GetInt64(0),
                PieceId = reader.GetInt64(1),
                FileName = reader.GetString(2),
                StoredPath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                InstrumentId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                InstrumentGroupId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                SortOrder = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                UploadedAt = DateTime.Parse(reader.GetString(7)),
                InstrumentName = reader.IsDBNull(8) ? null : reader.GetString(8),
                ContentType = reader.IsDBNull(9) ? null : reader.GetString(9),
                ContentHash = reader.IsDBNull(10) ? null : reader.GetString(10),
                UpdatedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
                HasFileData = !reader.IsDBNull(12) && reader.GetInt32(12) == 1
            };
        }
    }
}
