using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public class SyncRepository
    {
        private readonly string connectionString;

        public SyncRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task EnsureSyncUidsAsync()
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE pieces
SET sync_uid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                 substr(hex(randomblob(2)),2) || '-' ||
                 substr('89ab', abs(random()) % 4 + 1, 1) ||
                 substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))
WHERE sync_uid IS NULL OR sync_uid = ''";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE sheet_files
SET sync_uid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                 substr(hex(randomblob(2)),2) || '-' ||
                 substr('89ab', abs(random()) % 4 + 1, 1) ||
                 substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))
WHERE sync_uid IS NULL OR sync_uid = ''";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        public async Task<string?> EnsurePieceSyncUidAsync(long pieceId)
        {
            await EnsureSyncUidsAsync().ConfigureAwait(false);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sync_uid FROM pieces WHERE id = $id";
            command.Parameters.AddWithValue("$id", pieceId);
            var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return value as string;
        }

        public async Task TouchPieceAsync(long pieceId)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE pieces SET updated_at = $updatedAt WHERE id = $id";
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$id", pieceId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<SyncPushRequest> BuildPushPayloadAsync()
        {
            await EnsureSyncUidsAsync().ConfigureAwait(false);

            var payload = new SyncPushRequest();
            var pieceUidById = new Dictionary<long, string>();
            var pieceRows = new List<(
                long PieceId,
                string SyncUid,
                string Title,
                string? Composer,
                string? Arranger,
                string? Publisher,
                string? Isbn,
                string? Tags,
                string? Genre,
                string? Cabinet,
                string? Compartment,
                string? Slot,
                bool IsActive,
                string? FolderPath,
                DateTime UpdatedAt)>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT p.sync_uid, p.title, p.composer, p.arranger, p.publisher, p.isbn, p.tags, p.genre,
       p.cabinet, p.compartment, p.slot, p.is_active, p.folder_path, p.updated_at, p.id
FROM pieces p
ORDER BY p.id";

                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var pieceId = reader.GetInt64(14);
                    var syncUid = reader.GetString(0);
                    pieceUidById[pieceId] = syncUid;

                    pieceRows.Add((
                        pieceId,
                        syncUid,
                        reader.GetString(1),
                        ReadNullableString(reader, 2),
                        ReadNullableString(reader, 3),
                        ReadNullableString(reader, 4),
                        ReadNullableString(reader, 5),
                        ReadNullableString(reader, 6),
                        ReadNullableString(reader, 7),
                        ReadNullableString(reader, 8),
                        ReadNullableString(reader, 9),
                        ReadNullableString(reader, 10),
                        !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                        ReadNullableString(reader, 12),
                        reader.IsDBNull(13) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(13))));
                }
            }

            foreach (var row in pieceRows)
            {
                var instrumentNames = await GetInstrumentNamesAsync(connection, row.PieceId).ConfigureAwait(false);
                payload.Pieces.Add(new PieceSyncDto
                {
                    SyncUid = row.SyncUid,
                    Title = row.Title,
                    Composer = row.Composer,
                    Arranger = row.Arranger,
                    Publisher = row.Publisher,
                    Isbn = row.Isbn,
                    Tags = row.Tags,
                    Genre = row.Genre,
                    Cabinet = row.Cabinet,
                    Compartment = row.Compartment,
                    Slot = row.Slot,
                    IsActive = row.IsActive,
                    FolderPath = row.FolderPath,
                    UpdatedAt = row.UpdatedAt,
                    InstrumentNames = instrumentNames
                });
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT sf.sync_uid, p.sync_uid, sf.file_name, sf.content_type, sf.content_hash, sf.file_data,
       sf.instrument_id, i.name, sf.instrument_group_id, sf.sort_order, sf.updated_at
FROM sheet_files sf
INNER JOIN pieces p ON p.id = sf.piece_id
LEFT JOIN instruments i ON i.id = sf.instrument_id
WHERE sf.file_data IS NOT NULL AND length(sf.file_data) > 0
ORDER BY sf.id";

                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var bytes = (byte[])reader.GetValue(5);
                    payload.Sheets.Add(new SheetSyncDto
                    {
                        SyncUid = reader.GetString(0),
                        PieceSyncUid = reader.GetString(1),
                        FileName = reader.GetString(2),
                        ContentType = ReadNullableString(reader, 3),
                        ContentHash = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        ContentBase64 = Convert.ToBase64String(bytes),
                        InstrumentId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                        InstrumentName = ReadNullableString(reader, 7),
                        InstrumentGroupId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        SortOrder = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                        UpdatedAt = reader.IsDBNull(10) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(10))
                    });
                }
            }

            return payload;
        }

        public async Task ApplyPullPayloadAsync(SyncPullResponse response)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            var pieceIdBySyncUid = new Dictionary<string, long>();

            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT id, sync_uid FROM pieces WHERE sync_uid IS NOT NULL AND sync_uid != ''";
                using var reader = await select.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    pieceIdBySyncUid[reader.GetString(1)] = reader.GetInt64(0);
                }
            }

            foreach (var pieceDto in response.Pieces.OrderBy(p => p.UpdatedAt))
            {
                var pieceId = await UpsertPieceAsync(connection, transaction, pieceDto, pieceIdBySyncUid)
                    .ConfigureAwait(false);
                pieceIdBySyncUid[pieceDto.SyncUid] = pieceId;
            }

            foreach (var sheetDto in response.Sheets.OrderBy(s => s.UpdatedAt))
            {
                if (!pieceIdBySyncUid.TryGetValue(sheetDto.PieceSyncUid, out var pieceId))
                {
                    continue;
                }

                await UpsertSheetAsync(connection, transaction, pieceId, sheetDto).ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }

        private async Task<long> UpsertPieceAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            PieceSyncDto dto,
            Dictionary<string, long> pieceIdBySyncUid)
        {
            long pieceId;
            if (pieceIdBySyncUid.TryGetValue(dto.SyncUid, out pieceId))
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE pieces SET
 title = $title, composer = $composer, arranger = $arranger, publisher = $publisher,
 isbn = $isbn, tags = $tags, genre = $genre, cabinet = $cabinet, compartment = $compartment,
 slot = $slot, is_active = $isActive, folder_path = $folderPath, updated_at = $updatedAt
WHERE id = $id AND (updated_at IS NULL OR updated_at <= $updatedAt)";
                BindPieceSync(update, dto);
                update.Parameters.AddWithValue("$id", pieceId);
                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            else
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT INTO pieces (
 sync_uid, title, composer, arranger, publisher, isbn, tags, genre,
 cabinet, compartment, slot, is_active, folder_path, updated_at)
VALUES (
 $syncUid, $title, $composer, $arranger, $publisher, $isbn, $tags, $genre,
 $cabinet, $compartment, $slot, $isActive, $folderPath, $updatedAt);
SELECT last_insert_rowid();";
                BindPieceSync(insert, dto);
                insert.Parameters.AddWithValue("$syncUid", dto.SyncUid);
                pieceId = (long)(await insert.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
                pieceIdBySyncUid[dto.SyncUid] = pieceId;
            }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM piece_instruments WHERE piece_id = $pieceId";
                delete.Parameters.AddWithValue("$pieceId", pieceId);
                await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var instrumentName in dto.InstrumentNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var instrumentId = await ResolveInstrumentIdAsync(connection, transaction, instrumentName)
                    .ConfigureAwait(false);
                if (!instrumentId.HasValue)
                {
                    continue;
                }

                using var insertPi = connection.CreateCommand();
                insertPi.Transaction = transaction;
                insertPi.CommandText = "INSERT INTO piece_instruments (piece_id, instrument_id) VALUES ($pieceId, $instrumentId)";
                insertPi.Parameters.AddWithValue("$pieceId", pieceId);
                insertPi.Parameters.AddWithValue("$instrumentId", instrumentId.Value);
                await insertPi.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            return pieceId;
        }

        private static async Task UpsertSheetAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long pieceId,
            SheetSyncDto dto)
        {
            var bytes = Convert.FromBase64String(dto.ContentBase64);
            long? instrumentId = dto.InstrumentId;
            if (!instrumentId.HasValue && !string.IsNullOrWhiteSpace(dto.InstrumentName))
            {
                instrumentId = await ResolveInstrumentIdAsync(connection, transaction, dto.InstrumentName).ConfigureAwait(false);
            }

            using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT id, content_hash, updated_at FROM sheet_files WHERE sync_uid = $syncUid";
            exists.Parameters.AddWithValue("$syncUid", dto.SyncUid);

            long? existingId = null;
            string? existingHash = null;
            string? existingUpdatedAt = null;
            using (var reader = await exists.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    existingId = reader.GetInt64(0);
                    existingHash = reader.IsDBNull(1) ? null : reader.GetString(1);
                    existingUpdatedAt = reader.IsDBNull(2) ? null : reader.GetString(2);
                }
            }

            if (existingId.HasValue
                && existingUpdatedAt != null
                && DateTime.Parse(existingUpdatedAt) > dto.UpdatedAt
                && string.Equals(existingHash, dto.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (existingId.HasValue)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE sheet_files SET
 piece_id = $pieceId, file_name = $fileName, stored_path = '', instrument_id = $instrumentId,
 instrument_group_id = $instrumentGroupId, sort_order = $sortOrder, file_data = $fileData,
 content_type = $contentType, content_hash = $contentHash, updated_at = $updatedAt, uploaded_at = $uploadedAt
WHERE id = $id";
                update.Parameters.AddWithValue("$id", existingId.Value);
                BindSheetSync(update, pieceId, dto, bytes);
                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
INSERT INTO sheet_files (
 sync_uid, piece_id, file_name, stored_path, instrument_id, instrument_group_id, sort_order,
 uploaded_at, file_data, content_type, content_hash, updated_at)
VALUES (
 $syncUid, $pieceId, $fileName, '', $instrumentId, $instrumentGroupId, $sortOrder,
 $uploadedAt, $fileData, $contentType, $contentHash, $updatedAt)";
            insert.Parameters.AddWithValue("$syncUid", dto.SyncUid);
            BindSheetSync(insert, pieceId, dto, bytes);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static void BindPieceSync(SqliteCommand command, PieceSyncDto dto)
        {
            command.Parameters.AddWithValue("$title", dto.Title);
            command.Parameters.AddWithValue("$composer", (object?)dto.Composer ?? string.Empty);
            command.Parameters.AddWithValue("$arranger", (object?)dto.Arranger ?? string.Empty);
            command.Parameters.AddWithValue("$publisher", (object?)dto.Publisher ?? string.Empty);
            command.Parameters.AddWithValue("$isbn", (object?)dto.Isbn ?? string.Empty);
            command.Parameters.AddWithValue("$tags", (object?)dto.Tags ?? string.Empty);
            command.Parameters.AddWithValue("$genre", (object?)dto.Genre ?? string.Empty);
            command.Parameters.AddWithValue("$cabinet", (object?)dto.Cabinet ?? string.Empty);
            command.Parameters.AddWithValue("$compartment", (object?)dto.Compartment ?? string.Empty);
            command.Parameters.AddWithValue("$slot", (object?)dto.Slot ?? string.Empty);
            command.Parameters.AddWithValue("$isActive", dto.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$folderPath", (object?)dto.FolderPath ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", dto.UpdatedAt.ToString("o"));
        }

        private static void BindSheetSync(SqliteCommand command, long pieceId, SheetSyncDto dto, byte[] bytes)
        {
            command.Parameters.AddWithValue("$pieceId", pieceId);
            command.Parameters.AddWithValue("$fileName", dto.FileName);
            command.Parameters.AddWithValue("$instrumentId", dto.InstrumentId.HasValue ? dto.InstrumentId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$instrumentGroupId", dto.InstrumentGroupId.HasValue ? dto.InstrumentGroupId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$sortOrder", dto.SortOrder);
            command.Parameters.AddWithValue("$uploadedAt", dto.UpdatedAt.ToString("o"));
            command.Parameters.AddWithValue("$fileData", bytes);
            command.Parameters.AddWithValue("$contentType", (object?)dto.ContentType ?? SheetMusicPaths.GetContentType(dto.FileName));
            command.Parameters.AddWithValue("$contentHash", dto.ContentHash);
            command.Parameters.AddWithValue("$updatedAt", dto.UpdatedAt.ToString("o"));
        }

        private static async Task<long?> ResolveInstrumentIdAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string instrumentName)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM instruments WHERE name = $name";
            command.Parameters.AddWithValue("$name", instrumentName);
            var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return value == null ? null : Convert.ToInt64(value);
        }

        private static async Task<List<string>> GetInstrumentNamesAsync(SqliteConnection connection, long pieceId)
        {
            var names = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT i.name
FROM piece_instruments pi
INNER JOIN instruments i ON i.id = pi.instrument_id
WHERE pi.piece_id = $pieceId
ORDER BY i.name";
            command.Parameters.AddWithValue("$pieceId", pieceId);

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }
}
