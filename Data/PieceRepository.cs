using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public class PieceRepository
    {
        private readonly string connectionString;

        private const string SheetCountSelect = "(SELECT COUNT(*) FROM sheet_files sf WHERE sf.piece_id = p.id) AS sheet_file_count";

        public PieceRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task<IReadOnlyList<string>> GetTagOptionsAsync()
        {
            var result = new List<string>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM tag_options ORDER BY name";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(reader.GetString(0));
            }

            return result;
        }

        public async Task AddTagOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO tag_options(name) VALUES ($name)";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveTagOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tag_options WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetGenreOptionsAsync()
        {
            var result = new List<string>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM genre_options ORDER BY name";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(reader.GetString(0));
            }

            return result;
        }

        public async Task AddGenreOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO genre_options(name) VALUES ($name)";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveGenreOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM genre_options WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<Instrument?> AddInstrumentAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO instruments(name) VALUES ($name); SELECT id, name FROM instruments WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
                return new Instrument { Id = reader.GetInt64(0), Name = reader.GetString(1) };
            return null;
        }

        public async Task DeleteInstrumentAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            // Gruppenszuweisung + Instrument löschen (piece_instruments via CASCADE)
            cmd.CommandText = @"
DELETE FROM instrument_group_assignments WHERE instrument_name = $name;
DELETE FROM instruments WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", name);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<Instrument>> GetAllInstrumentsAsync()
        {
            var result = new List<Instrument>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name FROM instruments ORDER BY name";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var instrument = new Instrument
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1)
                };
                result.Add(instrument);
            }

            return result;
        }

        /// <summary>
        /// Feldname -> Spaltenname-Mapping für die Excel-ähnliche Filterung mit Operatoren.
        /// </summary>
        private static readonly Dictionary<string, string> FilterFieldColumns = new()
        {
            ["Title"] = "title",
            ["Composer"] = "composer",
            ["Arranger"] = "arranger",
            ["Publisher"] = "publisher",
            ["Isbn"] = "isbn",
            ["Tags"] = "tags",
            ["Genre"] = "genre"
        };

        /// <summary>
        /// Ruft Musikstücke anhand einer Liste von Filterkriterien (Feld + Operator + Wert) sowie
        /// den exakten Filtern für Schrank/Fach/Einschub/Aktiv ab. Wird von der Filter-Seite verwendet.
        /// </summary>
        public async Task<IReadOnlyList<Piece>> GetPiecesByCriteriaAsync(
            IEnumerable<FilterCriterion> criteria,
            IEnumerable<string>? selectedGenres,
            string? cabinetFilter,
            string? compartmentFilter,
            string? slotFilter,
            bool? onlyActive,
            bool? onlyWithDigitalScores = null,
            long? missingScoresForInstrumentId = null)
        {
            var result = new List<Piece>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();

            var where = "WHERE 1=1";
            var paramIndex = 0;

            foreach (var criterion in criteria)
            {
                if (string.IsNullOrWhiteSpace(criterion.Value)) continue;
                if (!FilterFieldColumns.TryGetValue(criterion.Field, out var column)) continue;

                var paramName = $"$p{paramIndex++}";
                var value = criterion.Value.Trim().ToLowerInvariant();

                switch (criterion.Operator)
                {
                    case FilterOperator.Contains:
                        where += $" AND LOWER({column}) LIKE {paramName}";
                        command.Parameters.AddWithValue(paramName, "%" + value + "%");
                        break;
                    case FilterOperator.NotContains:
                        where += $" AND (LOWER({column}) NOT LIKE {paramName} OR {column} IS NULL)";
                        command.Parameters.AddWithValue(paramName, "%" + value + "%");
                        break;
                    case FilterOperator.StartsWith:
                        where += $" AND LOWER({column}) LIKE {paramName}";
                        command.Parameters.AddWithValue(paramName, value + "%");
                        break;
                    case FilterOperator.EndsWith:
                        where += $" AND LOWER({column}) LIKE {paramName}";
                        command.Parameters.AddWithValue(paramName, "%" + value);
                        break;
                    case FilterOperator.Equals:
                        where += $" AND LOWER({column}) = {paramName}";
                        command.Parameters.AddWithValue(paramName, value);
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(cabinetFilter))
            {
                where += " AND cabinet = $cabinet";
                command.Parameters.AddWithValue("$cabinet", cabinetFilter.Trim());
            }

            if (!string.IsNullOrWhiteSpace(compartmentFilter))
            {
                where += " AND CAST(compartment AS TEXT) = $compartment";
                command.Parameters.AddWithValue("$compartment", compartmentFilter.Trim());
            }

            if (!string.IsNullOrWhiteSpace(slotFilter))
            {
                where += " AND slot = $slot";
                command.Parameters.AddWithValue("$slot", slotFilter.Trim());
            }

            if (onlyActive == true)
            {
                where += " AND is_active = 1";
            }

            var genreList = selectedGenres?
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .ToList();

            if (genreList is { Count: > 0 })
            {
                var genreConditions = new List<string>();
                foreach (var genre in genreList)
                {
                    var paramName = $"$genre{paramIndex++}";
                    genreConditions.Add($"LOWER(genre) LIKE {paramName}");
                    command.Parameters.AddWithValue(paramName, "%#" + genre.ToLowerInvariant() + "#%");
                }

                where += " AND (" + string.Join(" OR ", genreConditions) + ")";
            }

            if (onlyWithDigitalScores == true)
            {
                where += " AND EXISTS (SELECT 1 FROM sheet_files sf WHERE sf.piece_id = pieces.id)";
            }
            else if (onlyWithDigitalScores == false)
            {
                where += " AND NOT EXISTS (SELECT 1 FROM sheet_files sf WHERE sf.piece_id = pieces.id)";
            }

            if (missingScoresForInstrumentId.HasValue)
            {
                where += @" AND EXISTS (
                    SELECT 1 FROM piece_instruments pi
                    WHERE pi.piece_id = pieces.id AND pi.instrument_id = $missingScoresInstrument
                ) AND NOT EXISTS (
                    SELECT 1 FROM sheet_files sf
                    WHERE sf.piece_id = pieces.id
                    AND (
                        sf.instrument_id = $missingScoresInstrument
                        OR sf.instrument_group_id = (
                            SELECT iga.group_id
                            FROM instrument_group_assignments iga
                            JOIN instruments i ON i.name = iga.instrument_name
                            WHERE i.id = $missingScoresInstrument
                        )
                    )
                )";
                command.Parameters.AddWithValue("$missingScoresInstrument", missingScoresForInstrumentId.Value);
            }

            command.CommandText =
                "SELECT p.id, p.title, p.composer, p.arranger, p.publisher, p.isbn, p.tags, p.genre, " +
                "p.cabinet, p.compartment, p.slot, p.is_active, p.folder_path, " +
                "GROUP_CONCAT(i.name, ', ') as besetzung, MAX(co.color) as cabinet_color, " +
                SheetCountSelect + " " +
                "FROM pieces p " +
                "LEFT JOIN cabinet_options co ON co.name = p.cabinet " +
                "LEFT JOIN piece_instruments pi ON pi.piece_id = p.id " +
                "LEFT JOIN instruments i ON i.id = pi.instrument_id " +
                "WHERE p.id IN (SELECT id FROM pieces " + where + ") " +
                "GROUP BY p.id ORDER BY p.title";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(ReadPieceFromGroupedQueryReader(reader));
            }

            return result;
        }

        public async Task<IReadOnlyList<Piece>> GetPiecesAsync(
            string? titleFilter,
            string? composerFilter,
            string? arrangerFilter,
            string? publisherFilter,
            string? isbnFilter,
            string? tagsFilter,
            string? genreFilter,
            string? cabinetFilter,
            string? compartmentFilter,
            string? slotFilter,
            bool? onlyActive)
        {
            var result = new List<Piece>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();

            var where = "WHERE 1=1";

            if (!string.IsNullOrWhiteSpace(titleFilter))
            {
                where += " AND LOWER(title) LIKE $title";
                command.Parameters.AddWithValue("$title", "%" + titleFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(composerFilter))
            {
                where += " AND LOWER(composer) LIKE $composer";
                command.Parameters.AddWithValue("$composer", "%" + composerFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(arrangerFilter))
            {
                where += " AND LOWER(arranger) LIKE $arranger";
                command.Parameters.AddWithValue("$arranger", "%" + arrangerFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(publisherFilter))
            {
                where += " AND LOWER(publisher) LIKE $publisher";
                command.Parameters.AddWithValue("$publisher", "%" + publisherFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(isbnFilter))
            {
                where += " AND LOWER(isbn) LIKE $isbn";
                command.Parameters.AddWithValue("$isbn", "%" + isbnFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(tagsFilter))
            {
                where += " AND LOWER(tags) LIKE $tags";
                command.Parameters.AddWithValue("$tags", "%" + tagsFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(genreFilter))
            {
                where += " AND LOWER(genre) LIKE $genre";
                command.Parameters.AddWithValue("$genre", "%" + genreFilter.Trim().ToLowerInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(cabinetFilter))
            {
                where += " AND cabinet = $cabinet";
                command.Parameters.AddWithValue("$cabinet", cabinetFilter.Trim());
            }

            if (!string.IsNullOrWhiteSpace(compartmentFilter))
            {
                where += " AND CAST(compartment AS TEXT) = $compartment";
                command.Parameters.AddWithValue("$compartment", compartmentFilter.Trim());
            }

            if (!string.IsNullOrWhiteSpace(slotFilter))
            {
                where += " AND slot = $slot";
                command.Parameters.AddWithValue("$slot", slotFilter.Trim());
            }

            if (onlyActive == true)
            {
                where += " AND is_active = 1";
            }

            command.CommandText =
                "SELECT p.id, p.title, p.composer, p.arranger, p.publisher, p.isbn, p.tags, p.genre, " +
                "p.cabinet, p.compartment, p.slot, p.is_active, p.folder_path, " +
                "GROUP_CONCAT(i.name, ', ') as besetzung, MAX(co.color) as cabinet_color, " +
                SheetCountSelect + " " +
                "FROM pieces p " +
                "LEFT JOIN cabinet_options co ON co.name = p.cabinet " +
                "LEFT JOIN piece_instruments pi ON pi.piece_id = p.id " +
                "LEFT JOIN instruments i ON i.id = pi.instrument_id " +
                "WHERE p.id IN (SELECT id FROM pieces " + where + ") " +
                "GROUP BY p.id ORDER BY p.title";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(ReadPieceFromGroupedQueryReader(reader));
            }

            return result;
        }

        public async Task<(Piece piece, List<long> instrumentIds)> GetPieceWithInstrumentsAsync(long id)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            Piece? piece = null;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT p.id, p.title, p.composer, p.arranger, p.publisher, p.isbn, p.tags, p.genre, " +
                    "p.cabinet, p.compartment, p.slot, p.is_active, p.folder_path, co.color as cabinet_color, " +
                    SheetCountSelect + " " +
                    "FROM pieces p " +
                    "LEFT JOIN cabinet_options co ON co.name = p.cabinet " +
                    "WHERE p.id = $id";
                cmd.Parameters.AddWithValue("$id", id);

                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    piece = ReadPieceFromSingleQueryReader(reader);
                }
            }

            if (piece == null)
            {
                throw new KeyNotFoundException($"Piece with id {id} not found");
            }

            var instrumentIds = new List<long>();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT instrument_id FROM piece_instruments WHERE piece_id = $id";
                cmd.Parameters.AddWithValue("$id", id);

                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    instrumentIds.Add(reader.GetInt64(0));
                }
            }

            return (piece, instrumentIds);
        }

        public async Task DeletePieceAsync(long id)
        {
            Piece? piece = null;
            IReadOnlyList<string> instrumentNames = System.Array.Empty<string>();

            try
            {
                var (loadedPiece, _) = await GetPieceWithInstrumentsAsync(id).ConfigureAwait(false);
                piece = loadedPiece;
                instrumentNames = await GetInstrumentNamesForPieceAsync(id).ConfigureAwait(false);
                await PieceBackupService.CreateBackupAsync(PieceBackupAction.Deleted, piece, instrumentNames).ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                // Kein Backup möglich – Löschen trotzdem versuchen
            }

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pieces WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);

            if (piece != null)
            {
                AppPaths.TryDeletePieceNotenDirectory(piece.Id, piece.Title);
            }
        }

        public async Task DeleteAllPiecesAsync()
        {
            var pieces = await GetPiecesAsync(null, null, null, null, null, null, null, null, null, null, null)
                .ConfigureAwait(false);

            await PieceBackupService.CreateDeleteAllBackupAsync(pieces).ConfigureAwait(false);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pieces";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);

            AppPaths.TryDeleteAllNotenDirectories();
        }

        public async Task<long> SaveImportedPieceAsync(Piece piece)
        {
            var besetzungNames = ParseBesetzungInstrumentNames(piece.Besetzung);
            var allInstruments = await GetAllInstrumentsAsync().ConfigureAwait(false);
            var nameSet = new HashSet<string>(besetzungNames, System.StringComparer.OrdinalIgnoreCase);

            var selections = allInstruments.Select(instrument =>
            {
                var selection = new InstrumentSelection(instrument);
                if (nameSet.Contains(instrument.Name))
                {
                    selection.IsSelected = true;
                }

                return selection;
            });

            return await SavePieceAsync(piece, selections).ConfigureAwait(false);
        }

        private static IReadOnlyList<string> ParseBesetzungInstrumentNames(string? besetzung)
        {
            if (string.IsNullOrWhiteSpace(besetzung))
            {
                return System.Array.Empty<string>();
            }

            return besetzung
                .Split(',')
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .ToList();
        }

        public async Task<long> SavePieceAsync(Piece piece, IEnumerable<InstrumentSelection> selections)
        {
            var isNew = piece.Id == 0;
            var selectionList = selections.ToList();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            if (piece.Id == 0)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO pieces
(title, composer, arranger, publisher, isbn, tags, genre,
 cabinet, compartment, slot, is_active, folder_path, sync_uid, updated_at)
VALUES ($title,$composer,$arranger,$publisher,$isbn,$tags,$genre,
        $cabinet,$compartment,$slot,$isActive,$folderPath,$syncUid,$updatedAt);
SELECT last_insert_rowid();";

                BindPieceParameters(insert, piece);
                insert.Parameters.AddWithValue("$syncUid", Guid.NewGuid().ToString());
                insert.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));

                var idObj = await insert.ExecuteScalarAsync().ConfigureAwait(false);
                piece.Id = (long)(idObj ?? 0L);
            }
            else
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"UPDATE pieces SET
 title = $title,
 composer = $composer,
 arranger = $arranger,
 publisher = $publisher,
 isbn = $isbn,
 tags = $tags,
 genre = $genre,
 cabinet = $cabinet,
 compartment = $compartment,
 slot = $slot,
 is_active = $isActive,
 folder_path = $folderPath,
 updated_at = $updatedAt
WHERE id = $id";

                BindPieceParameters(update, piece);
                update.Parameters.AddWithValue("$id", piece.Id);
                update.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));

                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM piece_instruments WHERE piece_id = $pieceId";
                delete.Parameters.AddWithValue("$pieceId", piece.Id);
                await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var selection in selectionList.Where(s => s.IsSelected))
            {
                using var insertPi = connection.CreateCommand();
                insertPi.Transaction = transaction;
                insertPi.CommandText = "INSERT INTO piece_instruments (piece_id, instrument_id) VALUES ($pieceId, $instrumentId)";
                insertPi.Parameters.AddWithValue("$pieceId", piece.Id);
                insertPi.Parameters.AddWithValue("$instrumentId", selection.Instrument.Id);
                await insertPi.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);

            piece.FolderPath = SheetMusicPaths.BuildLogicalPath(piece);
            await UpdateFolderPathAsync(piece.Id, piece.FolderPath).ConfigureAwait(false);

            if (isNew)
            {
                var instrumentNames = selectionList
                    .Where(s => s.IsSelected)
                    .Select(s => s.Instrument.Name)
                    .OrderBy(name => name)
                    .ToList();
                await PieceBackupService.CreateBackupAsync(PieceBackupAction.Created, piece, instrumentNames)
                    .ConfigureAwait(false);
            }

            await LocalChangeTracker.RecordChangeAsync().ConfigureAwait(false);
            return piece.Id;
        }

        private async Task UpdateFolderPathAsync(long pieceId, string folderPath)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE pieces SET folder_path = $folderPath WHERE id = $id";
            command.Parameters.AddWithValue("$folderPath", folderPath);
            command.Parameters.AddWithValue("$id", pieceId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<string>> GetInstrumentNamesForPieceAsync(long pieceId)
        {
            var names = new List<string>();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT i.name
FROM piece_instruments pi
JOIN instruments i ON i.id = pi.instrument_id
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

        public async Task<IReadOnlyList<CabinetOption>> GetCabinetOptionsAsync()
        {
            var result = new List<CabinetOption>();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, color FROM cabinet_options ORDER BY name";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                result.Add(new CabinetOption { Name = reader.GetString(0), Color = reader.GetString(1) });
            return result;
        }

        public async Task AddCabinetOptionAsync(string name, string color)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO cabinet_options(name, color) VALUES ($name, $color)";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$color", color);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task UpdateCabinetColorAsync(string name, string color)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE cabinet_options SET color = $color WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$color", color);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveCabinetOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cabinet_options WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetCompartmentOptionsAsync()
        {
            var result = new List<string>();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM compartment_options ORDER BY name";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                result.Add(reader.GetString(0));
            return result;
        }

        public async Task AddCompartmentOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO compartment_options(name) VALUES ($name)";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveCompartmentOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM compartment_options WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetSlotOptionsAsync()
        {
            var result = new List<string>();
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM slot_options ORDER BY name";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                result.Add(reader.GetString(0));
            return result;
        }

        public async Task AddSlotOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO slot_options(name) VALUES ($name)";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveSlotOptionAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM slot_options WHERE name = $name";
            command.Parameters.AddWithValue("$name", name);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> GetGroupAssignmentsAsync()
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT instrument_name, group_id FROM instrument_group_assignments";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            var result = new Dictionary<string, int>();
            while (await reader.ReadAsync().ConfigureAwait(false))
                result[reader.GetString(0)] = reader.GetInt32(1);
            return result;
        }

        public async Task SetGroupAssignmentAsync(string instrumentName, int groupId)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO instrument_group_assignments(instrument_name, group_id) VALUES ($n, $g)";
            command.Parameters.AddWithValue("$n", instrumentName);
            command.Parameters.AddWithValue("$g", groupId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task RemoveGroupAssignmentAsync(string instrumentName)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM instrument_group_assignments WHERE instrument_name = $n";
            command.Parameters.AddWithValue("$n", instrumentName);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<int> CountPiecesUsingCabinetAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pieces WHERE cabinet = $name";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
        }

        public async Task<int> CountPiecesUsingCompartmentAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pieces WHERE CAST(compartment AS TEXT) = $name";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
        }

        public async Task<int> CountPiecesUsingSlotAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pieces WHERE slot = $name";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
        }

        public async Task<int> CountPiecesUsingTagAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pieces WHERE ('#' || COALESCE(tags, '') || '#') LIKE $pattern";
            command.Parameters.AddWithValue("$pattern", $"%#{name}#%");
            return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
        }

        public async Task<int> CountPiecesUsingGenreAsync(string name)
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pieces WHERE ('#' || COALESCE(genre, '') || '#') LIKE $pattern";
            command.Parameters.AddWithValue("$pattern", $"%#{name}#%");
            return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
        }

        private static void BindPieceParameters(SqliteCommand cmd, Piece piece)
        {
            cmd.Parameters.AddWithValue("$title", piece.Title);
            cmd.Parameters.AddWithValue("$composer", (object?)piece.Composer ?? string.Empty);
            cmd.Parameters.AddWithValue("$arranger", (object?)piece.Arranger ?? string.Empty);
            cmd.Parameters.AddWithValue("$publisher", (object?)piece.Publisher ?? string.Empty);
            cmd.Parameters.AddWithValue("$isbn", (object?)piece.Isbn ?? string.Empty);
            cmd.Parameters.AddWithValue("$tags", (object?)piece.Tags ?? string.Empty);
            cmd.Parameters.AddWithValue("$genre", (object?)piece.Genre ?? string.Empty);
            cmd.Parameters.AddWithValue("$cabinet", (object?)piece.Cabinet ?? string.Empty);
            cmd.Parameters.AddWithValue("$compartment", (object?)piece.Compartment ?? "");
            cmd.Parameters.AddWithValue("$slot", (object?)piece.Slot ?? string.Empty);
            cmd.Parameters.AddWithValue("$isActive", piece.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$folderPath", (object?)piece.FolderPath ?? string.Empty);
        }

        private static Piece ReadPieceFromGroupedQueryReader(SqliteDataReader reader)
        {
            return new Piece
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Composer = reader.IsDBNull(2) ? null : reader.GetString(2),
                Arranger = reader.IsDBNull(3) ? null : reader.GetString(3),
                Publisher = reader.IsDBNull(4) ? null : reader.GetString(4),
                Isbn = reader.IsDBNull(5) ? null : reader.GetString(5),
                Tags = reader.IsDBNull(6) ? null : reader.GetString(6),
                Genre = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cabinet = reader.IsDBNull(8) ? null : reader.GetString(8),
                Compartment = reader.IsDBNull(9) ? null : reader.GetValue(9)?.ToString(),
                Slot = reader.IsDBNull(10) ? null : reader.GetString(10),
                IsActive = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                FolderPath = reader.IsDBNull(12) ? null : reader.GetString(12),
                Besetzung = reader.IsDBNull(13) ? null : reader.GetString(13),
                CabinetColor = reader.IsDBNull(14) ? null : reader.GetString(14),
                DigitalScoreCount = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetInt32(15) : 0
            };
        }

        private static Piece ReadPieceFromSingleQueryReader(SqliteDataReader reader)
        {
            return new Piece
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Composer = reader.IsDBNull(2) ? null : reader.GetString(2),
                Arranger = reader.IsDBNull(3) ? null : reader.GetString(3),
                Publisher = reader.IsDBNull(4) ? null : reader.GetString(4),
                Isbn = reader.IsDBNull(5) ? null : reader.GetString(5),
                Tags = reader.IsDBNull(6) ? null : reader.GetString(6),
                Genre = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cabinet = reader.IsDBNull(8) ? null : reader.GetString(8),
                Compartment = reader.IsDBNull(9) ? null : reader.GetValue(9)?.ToString(),
                Slot = reader.IsDBNull(10) ? null : reader.GetString(10),
                IsActive = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                FolderPath = reader.IsDBNull(12) ? null : reader.GetString(12),
                CabinetColor = reader.IsDBNull(13) ? null : reader.GetString(13),
                DigitalScoreCount = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetInt32(14) : 0
            };
        }
    }
}
