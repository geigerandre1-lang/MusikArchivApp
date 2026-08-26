using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public static class SyncTombstoneStore
    {
        public const string EntityTypePiece = "piece";
        public const string EntityTypeSheet = "sheet";

        public static void EnsureTable(SqliteConnection connection, SqliteTransaction? transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS sync_tombstones (
    sync_uid TEXT PRIMARY KEY,
    entity_type TEXT NOT NULL,
    deleted_at TEXT NOT NULL
);";
            command.ExecuteNonQuery();
        }

        public static async Task RecordDeletionAsync(string syncUid, string entityType, DateTime? deletedAt = null)
        {
            if (string.IsNullOrWhiteSpace(syncUid))
            {
                return;
            }

            var connectionString = DatabaseInitializer.GetConnectionString();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await RecordDeletionAsync(connection, null, syncUid, entityType, deletedAt).ConfigureAwait(false);
        }

        public static async Task RecordDeletionAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string syncUid,
            string entityType,
            DateTime? deletedAt = null)
        {
            if (string.IsNullOrWhiteSpace(syncUid))
            {
                return;
            }

            EnsureTable(connection, transaction);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
VALUES ($syncUid, $entityType, $deletedAt)
ON CONFLICT(sync_uid) DO UPDATE SET
    entity_type = excluded.entity_type,
    deleted_at = excluded.deleted_at";
            command.Parameters.AddWithValue("$syncUid", syncUid);
            command.Parameters.AddWithValue("$entityType", entityType);
            command.Parameters.AddWithValue("$deletedAt", (deletedAt ?? DateTime.UtcNow).ToString("o"));
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public static async Task RecordPieceDeletionAsync(long pieceId)
        {
            var connectionString = DatabaseInitializer.GetConnectionString();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await RecordPieceDeletionAsync(connection, null, pieceId).ConfigureAwait(false);
        }

        public static async Task RecordPieceDeletionAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            long pieceId)
        {
            EnsureTable(connection, transaction);
            await SyncRepository.EnsureSyncUidsAsync(connection, transaction).ConfigureAwait(false);

            string? pieceSyncUid = null;
            var sheetSyncUids = new List<string>();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT sync_uid FROM pieces WHERE id = $id";
                command.Parameters.AddWithValue("$id", pieceId);
                pieceSyncUid = await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT sync_uid FROM sheet_files WHERE piece_id = $pieceId AND sync_uid IS NOT NULL AND sync_uid != ''";
                command.Parameters.AddWithValue("$pieceId", pieceId);
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    sheetSyncUids.Add(reader.GetString(0));
                }
            }

            var deletedAt = DateTime.UtcNow;
            foreach (var sheetSyncUid in sheetSyncUids)
            {
                await RecordDeletionAsync(connection, transaction, sheetSyncUid, EntityTypeSheet, deletedAt)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(pieceSyncUid))
            {
                await RecordDeletionAsync(connection, transaction, pieceSyncUid, EntityTypePiece, deletedAt)
                    .ConfigureAwait(false);
            }
        }

        public static async Task RecordSheetDeletionAsync(long sheetFileId)
        {
            var connectionString = DatabaseInitializer.GetConnectionString();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await RecordSheetDeletionAsync(connection, null, sheetFileId).ConfigureAwait(false);
        }

        public static async Task RecordSheetDeletionAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            long sheetFileId)
        {
            await SyncRepository.EnsureSyncUidsAsync(connection, transaction).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT sync_uid FROM sheet_files WHERE id = $id";
            command.Parameters.AddWithValue("$id", sheetFileId);
            var syncUid = await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
            if (!string.IsNullOrWhiteSpace(syncUid))
            {
                await RecordDeletionAsync(connection, transaction, syncUid, EntityTypeSheet).ConfigureAwait(false);
            }
        }

        public static async Task RecordAllPiecesDeletionAsync()
        {
            var connectionString = DatabaseInitializer.GetConnectionString();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await SyncRepository.EnsureSyncUidsAsync(connection).ConfigureAwait(false);

            var tombstones = new List<(string SyncUid, string EntityType)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT sync_uid, 'piece' AS entity_type FROM pieces WHERE sync_uid IS NOT NULL AND sync_uid != ''
UNION ALL
SELECT sync_uid, 'sheet' AS entity_type FROM sheet_files WHERE sync_uid IS NOT NULL AND sync_uid != ''";
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    tombstones.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            var deletedAt = DateTime.UtcNow;
            foreach (var (syncUid, entityType) in tombstones)
            {
                await RecordDeletionAsync(connection, null, syncUid, entityType, deletedAt).ConfigureAwait(false);
            }
        }

        public static async Task<IReadOnlyList<SyncTombstoneDto>> GetAllAsync()
        {
            var connectionString = DatabaseInitializer.GetConnectionString();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            EnsureTable(connection);

            var result = new List<SyncTombstoneDto>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sync_uid, entity_type, deleted_at FROM sync_tombstones ORDER BY deleted_at";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(new SyncTombstoneDto
                {
                    SyncUid = reader.GetString(0),
                    EntityType = reader.GetString(1),
                    DeletedAt = DateTime.Parse(reader.GetString(2))
                });
            }

            return result;
        }
    }
}
