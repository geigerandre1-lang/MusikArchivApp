using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public class DuplicateCleanupService
    {
        private readonly string connectionString;

        public DuplicateCleanupService(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task<IReadOnlyList<DuplicatePieceGroup>> FindPieceDuplicateGroupsAsync()
        {
            var entries = new List<DuplicatePieceEntry>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT p.id, p.title, p.composer, p.arranger, p.cabinet, p.compartment, p.slot,
       p.sync_uid, p.updated_at,
       (SELECT COUNT(*) FROM sheet_files sf WHERE sf.piece_id = p.id) AS sheet_count
FROM pieces p
ORDER BY lower(p.title), p.id";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                entries.Add(new DuplicatePieceEntry
                {
                    Id = reader.GetInt64(0),
                    Title = reader.GetString(1),
                    Composer = ReadNullableString(reader, 2),
                    Arranger = ReadNullableString(reader, 3),
                    Cabinet = ReadNullableString(reader, 4),
                    Compartment = ReadNullableString(reader, 5),
                    Slot = ReadNullableString(reader, 6),
                    SyncUid = ReadNullableString(reader, 7),
                    UpdatedAt = ReadNullableDateTime(reader, 8),
                    SheetCount = reader.GetInt32(9)
                });
            }

            return entries
                .GroupBy(BuildPieceMatchKey)
                .Where(group => group.Count() > 1)
                .Select(group =>
                {
                    var list = group.OrderByDescending(ScorePieceEntry).ThenBy(entry => entry.Id).ToList();
                    var first = list[0];
                    return new DuplicatePieceGroup
                    {
                        MatchKey = group.Key,
                        Summary = $"{first.Title} ({list.Count} Einträge)",
                        Entries = list,
                        RecommendedKeepId = list[0].Id
                    };
                })
                .OrderBy(group => group.Summary, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<IReadOnlyList<DuplicateSheetGroup>> FindSheetDuplicateGroupsAsync()
        {
            var entries = new List<DuplicateSheetEntry>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT sf.id, sf.piece_id, p.title, sf.file_name, sf.sync_uid, sf.updated_at, sf.content_hash
FROM sheet_files sf
INNER JOIN pieces p ON p.id = sf.piece_id
ORDER BY lower(p.title), lower(sf.file_name), sf.id";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                entries.Add(new DuplicateSheetEntry
                {
                    Id = reader.GetInt64(0),
                    PieceId = reader.GetInt64(1),
                    PieceTitle = reader.GetString(2),
                    FileName = reader.GetString(3),
                    SyncUid = ReadNullableString(reader, 4),
                    UpdatedAt = ReadNullableDateTime(reader, 5),
                    ContentHash = ReadNullableString(reader, 6)
                });
            }

            return entries
                .GroupBy(entry => $"{entry.PieceId}|{entry.FileName.ToLowerInvariant()}")
                .Where(group => group.Count() > 1)
                .Select(group =>
                {
                    var list = group.OrderByDescending(ScoreSheetEntry).ThenBy(entry => entry.Id).ToList();
                    var first = list[0];
                    return new DuplicateSheetGroup
                    {
                        MatchKey = group.Key,
                        Summary = $"{first.PieceTitle} · {first.FileName} ({list.Count} Einträge)",
                        Entries = list,
                        RecommendedKeepId = list[0].Id
                    };
                })
                .OrderBy(group => group.Summary, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<DuplicateCleanupResult> CleanupPieceGroupAsync(long keepId, IEnumerable<long> removeIds)
        {
            var result = new DuplicateCleanupResult();
            var removeIdList = removeIds.Where(id => id != keepId).Distinct().ToList();
            if (removeIdList.Count == 0)
            {
                return result;
            }

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            foreach (var removeId in removeIdList)
            {
                result.MergedSheets += await MoveSheetsToKeeperAsync(connection, transaction, keepId, removeId)
                    .ConfigureAwait(false);
                await MergeInstrumentsAsync(connection, transaction, keepId, removeId).ConfigureAwait(false);
                await SyncTombstoneStore.RecordPieceDeletionAsync(connection, transaction, removeId)
                    .ConfigureAwait(false);

                using var deletePiece = connection.CreateCommand();
                deletePiece.Transaction = transaction;
                deletePiece.CommandText = "DELETE FROM pieces WHERE id = $id";
                deletePiece.Parameters.AddWithValue("$id", removeId);
                await deletePiece.ExecuteNonQueryAsync().ConfigureAwait(false);
                result.RemovedPieces++;
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);
            return result;
        }

        public async Task<DuplicateCleanupResult> CleanupSheetGroupAsync(long keepId, IEnumerable<long> removeIds)
        {
            var result = new DuplicateCleanupResult();
            var removeIdList = removeIds.Where(id => id != keepId).Distinct().ToList();
            if (removeIdList.Count == 0)
            {
                return result;
            }

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            foreach (var removeId in removeIdList)
            {
                await SyncTombstoneStore.RecordSheetDeletionAsync(connection, transaction, removeId)
                    .ConfigureAwait(false);

                using var deleteSheet = connection.CreateCommand();
                deleteSheet.Transaction = transaction;
                deleteSheet.CommandText = "DELETE FROM sheet_files WHERE id = $id";
                deleteSheet.Parameters.AddWithValue("$id", removeId);
                await deleteSheet.ExecuteNonQueryAsync().ConfigureAwait(false);
                result.RemovedSheets++;
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);
            return result;
        }

        private static async Task<int> MoveSheetsToKeeperAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long keepId,
            long removeId)
        {
            var moved = 0;
            var keeperFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT lower(file_name) FROM sheet_files WHERE piece_id = $pieceId";
                command.Parameters.AddWithValue("$pieceId", keepId);
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    keeperFileNames.Add(reader.GetString(0));
                }
            }

            var toMove = new List<(long Id, string FileName)>();
            var toDelete = new List<long>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT id, lower(file_name) FROM sheet_files WHERE piece_id = $pieceId";
                command.Parameters.AddWithValue("$pieceId", removeId);
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var sheetId = reader.GetInt64(0);
                    var fileName = reader.GetString(1);
                    if (keeperFileNames.Contains(fileName))
                    {
                        toDelete.Add(sheetId);
                    }
                    else
                    {
                        toMove.Add((sheetId, fileName));
                    }
                }
            }

            foreach (var sheetId in toDelete)
            {
                await SyncTombstoneStore.RecordSheetDeletionAsync(connection, transaction, sheetId)
                    .ConfigureAwait(false);
                using var deleteSheet = connection.CreateCommand();
                deleteSheet.Transaction = transaction;
                deleteSheet.CommandText = "DELETE FROM sheet_files WHERE id = $id";
                deleteSheet.Parameters.AddWithValue("$id", sheetId);
                await deleteSheet.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var (sheetId, fileName) in toMove)
            {
                using var updateSheet = connection.CreateCommand();
                updateSheet.Transaction = transaction;
                updateSheet.CommandText = "UPDATE sheet_files SET piece_id = $keepId WHERE id = $id";
                updateSheet.Parameters.AddWithValue("$keepId", keepId);
                updateSheet.Parameters.AddWithValue("$id", sheetId);
                await updateSheet.ExecuteNonQueryAsync().ConfigureAwait(false);
                keeperFileNames.Add(fileName);
                moved++;
            }

            return moved;
        }

        private static async Task MergeInstrumentsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long keepId,
            long removeId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT OR IGNORE INTO piece_instruments (piece_id, instrument_id)
SELECT $keepId, instrument_id
FROM piece_instruments
WHERE piece_id = $removeId";
            command.Parameters.AddWithValue("$keepId", keepId);
            command.Parameters.AddWithValue("$removeId", removeId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string BuildPieceMatchKey(DuplicatePieceEntry entry)
        {
            return string.Join("|", new[]
            {
                Normalize(entry.Title),
                Normalize(entry.Composer),
                Normalize(entry.Arranger),
                Normalize(entry.Cabinet),
                Normalize(entry.Compartment),
                Normalize(entry.Slot)
            });
        }

        private static int ScorePieceEntry(DuplicatePieceEntry entry)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(entry.SyncUid))
            {
                score += 100;
            }

            score += Math.Min(entry.SheetCount, 50);
            if (entry.UpdatedAt.HasValue)
            {
                score += 10;
            }

            return score;
        }

        private static int ScoreSheetEntry(DuplicateSheetEntry entry)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(entry.SyncUid))
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(entry.ContentHash))
            {
                score += 20;
            }

            if (entry.UpdatedAt.HasValue)
            {
                score += 10;
            }

            return score;
        }

        private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

        private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return DateTime.Parse(reader.GetString(ordinal));
        }
    }
}
