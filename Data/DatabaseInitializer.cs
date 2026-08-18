using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MusikArchivApp.Data
{
    public static class DatabaseInitializer
    {
        private const string DatabaseFileName = "musikarchiv.db";

        public static string GetAppDataDirectory() => AppPaths.GetDataRoot();

        public static string GetConnectionString()
        {
            var dbPath = AppPaths.GetDatabasePath();
            return $"Data Source={dbPath}";
        }

        private static void MigrateDatabaseFromLegacyLocationIfNeeded()
        {
            // Legacy-Migration erfolgt über AppPaths (exe-Verzeichnis → Datenordner).
        }

        public static void Initialize()
        {
            MigrateDatabaseFromLegacyLocationIfNeeded();
            var connectionString = GetConnectionString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                    command.CommandText = @"
CREATE TABLE IF NOT EXISTS pieces (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT NOT NULL,
    composer     TEXT,
    arranger     TEXT,
    publisher    TEXT,
    isbn         TEXT,
    tags         TEXT,
    genre        TEXT,
    cabinet      TEXT,
    compartment  INTEGER,
    slot         TEXT,
    is_active    INTEGER NOT NULL DEFAULT 1,
    folder_path  TEXT
);

CREATE TABLE IF NOT EXISTS instruments (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS piece_instruments (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    piece_id      INTEGER NOT NULL,
    instrument_id INTEGER NOT NULL,
    FOREIGN KEY (piece_id)      REFERENCES pieces(id)      ON DELETE CASCADE,
    FOREIGN KEY (instrument_id) REFERENCES instruments(id)
);

CREATE TABLE IF NOT EXISTS tag_options (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS genre_options (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS cabinet_options (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE,
    color TEXT NOT NULL DEFAULT '#FFFFFF'
);

CREATE TABLE IF NOT EXISTS compartment_options (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS slot_options (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS instrument_group_assignments (
    instrument_name TEXT NOT NULL PRIMARY KEY,
    group_id        INTEGER NOT NULL
);";

                command.ExecuteNonQuery();
            }

            EnsureCabinetColorColumnMigration(connection);
            EnsureSheetFilesTable(connection);
            EnsureSheetFilesBlobColumns(connection);
            EnsureSyncColumns(connection);
            SyncTombstoneStore.EnsureTable(connection);
            EnsureInstrumentsSeeded(connection);
            EnsureTagAndGenreOptionsSeeded(connection);
            EnsureCabinetCompartmentSlotOptionsSeeded(connection);
            EnsureGroupAssignmentsSeeded(connection);

            SheetFileMigration.MigrateFilesystemToDatabaseAsync(connectionString).GetAwaiter().GetResult();
            SheetFileMigration.MigrateDatabaseToFilesystemAsync(connectionString).GetAwaiter().GetResult();
        }

        private static void EnsureSheetFilesTable(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS sheet_files (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    piece_id            INTEGER NOT NULL,
    file_name           TEXT NOT NULL,
    stored_path         TEXT NOT NULL,
    instrument_id       INTEGER,
    instrument_group_id INTEGER,
    sort_order          INTEGER NOT NULL DEFAULT 0,
    uploaded_at         TEXT NOT NULL,
    FOREIGN KEY (piece_id) REFERENCES pieces(id) ON DELETE CASCADE,
    FOREIGN KEY (instrument_id) REFERENCES instruments(id)
);";
            command.ExecuteNonQuery();
        }

        private static void EnsureSheetFilesBlobColumns(SqliteConnection connection)
        {
            EnsureColumn(connection, "sheet_files", "file_data", "BLOB");
            EnsureColumn(connection, "sheet_files", "content_type", "TEXT");
            EnsureColumn(connection, "sheet_files", "content_hash", "TEXT");
            EnsureColumn(connection, "sheet_files", "updated_at", "TEXT");
        }

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info({table})";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == column)
                {
                    return;
                }
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
        }

        private static void EnsureSyncColumns(SqliteConnection connection)
        {
            EnsureColumn(connection, "pieces", "sync_uid", "TEXT");
            EnsureColumn(connection, "pieces", "updated_at", "TEXT");
            EnsureColumn(connection, "sheet_files", "sync_uid", "TEXT");
        }

        private static void EnsureCabinetColorColumnMigration(SqliteConnection connection)
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(cabinet_options)";
            bool hasColor = false;
            using (var r = pragma.ExecuteReader())
            {
                while (r.Read())
                {
                    if (r.GetString(1) == "color") { hasColor = true; break; }
                }
            }

            if (!hasColor)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE cabinet_options ADD COLUMN color TEXT NOT NULL DEFAULT '#FFFFFF'";
                alter.ExecuteNonQuery();
            }
        }

        private static void EnsureInstrumentsSeeded(SqliteConnection connection)
        {
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM instruments";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count > 0)
                {
                    return;
                }
            }

            var instrumentNames = new[]
            {
                "Partitur","Direktion",
                "Piccolo",
                "Flöte 1","Flöte 2",
                "Oboe 1","Oboe 2",
                "Fagott",
                "Klarinette in Es","Klarinette 1","Klarinette 2","Klarinette 3","Bassklarinette",
                "Altsaxophon 1","Altsaxophon 2","Tenorsaxophon","Baritonsaxophon",
                "Flügelhorn 1","Flügelhorn 2",
                "Trompete 1","Trompete 2","Trompete 3","Trompete 4",
                "Horn in F 1","Horn in F 2","Horn in F 3","Horn in F 4",
                "Horn in Es 1","Horn in Es 2","Horn in Es 3","Horn in Es 4",
                "Tenorhorn 1","Tenorhorn 2","Tenorhorn 3","Bariton",
                "Posaune in C 1","Posaune in C 2","Posaune in C 3","Bassposaune",
                "Posaune in B 1","Posaune in B 2","Posaune in B 3",
                "Tuba in C 1","Tuba in C 2","Tuba in B 1","Tuba in B 2","Tuba in Es",
                "Kleine Trommel","Große Trommel","Schlagzeug","Becken",
                "Glockenspiel / Marimba","Perkussion 1","Perkussion 2","Perkussion 3",
                "Pauke","Euphonium","Gesang","Solo"
            };

            using var tx = connection.BeginTransaction();

            foreach (var name in instrumentNames)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO instruments(name) VALUES ($name)";
                insert.Parameters.AddWithValue("$name", name);
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private static void EnsureTagAndGenreOptionsSeeded(SqliteConnection connection)
        {
            // Tags
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM tag_options";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count == 0)
                {
                    var defaultTags = new[] { "Marsch", "Polka", "Konzertstück", "Solostück", "Weihnachten", "Kirche" };

                    using var tx = connection.BeginTransaction();
                    foreach (var tag in defaultTags)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO tag_options(name) VALUES ($name)";
                        insert.Parameters.AddWithValue("$name", tag);
                        insert.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }

            // Genres
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM genre_options";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count == 0)
                {
                    var defaultGenres = new[] { "Marsch", "Polka", "Walzer", "Konzertant", "Unterhaltung", "Kirchlich" };

                    using var tx = connection.BeginTransaction();
                    foreach (var genre in defaultGenres)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO genre_options(name) VALUES ($name)";
                        insert.Parameters.AddWithValue("$name", genre);
                        insert.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
        }

        private static void EnsureCabinetCompartmentSlotOptionsSeeded(SqliteConnection connection)
        {
            // Schrank
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM cabinet_options";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count == 0)
                {
                    var defaults = new[] { ("A", "#4472C4"), ("B", "#ED7D31"), ("C", "#70AD47"), ("D", "#FF0000") };
                    using var tx = connection.BeginTransaction();
                    foreach (var (name, color) in defaults)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO cabinet_options(name, color) VALUES ($name, $color)";
                        insert.Parameters.AddWithValue("$name", name);
                        insert.Parameters.AddWithValue("$color", color);
                        insert.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }

            // Fach
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM compartment_options";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count == 0)
                {
                    var defaults = new[] { "1", "2", "3", "4", "5" };
                    using var tx = connection.BeginTransaction();
                    foreach (var value in defaults)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO compartment_options(name) VALUES ($name)";
                        insert.Parameters.AddWithValue("$name", value);
                        insert.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }

            // Einschub
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM slot_options";
                var count = Convert.ToInt32(countCmd.ExecuteScalar());
                if (count == 0)
                {
                    var defaults = new[] { "a", "b", "c", "d" };
                    using var tx = connection.BeginTransaction();
                    foreach (var value in defaults)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT INTO slot_options(name) VALUES ($name)";
                        insert.Parameters.AddWithValue("$name", value);
                        insert.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
        }

        private static void EnsureGroupAssignmentsSeeded(SqliteConnection connection)
        {
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM instrument_group_assignments";
                if (Convert.ToInt32(countCmd.ExecuteScalar()) == 0)
                {
                    var group1 = new[] { "Partitur", "Direktion", "Gesang", "Solo" };
                    var group2 = new[]
                    {
                        "Piccolo", "Flöte 1", "Flöte 2", "Oboe 1", "Oboe 2", "Fagott",
                        "Klarinette in Es", "Klarinette 1", "Klarinette 2", "Klarinette 3", "Bassklarinette",
                        "Altsaxophon 1", "Altsaxophon 2", "Tenorsaxophon", "Baritonsaxophon",
                        "Flügelhorn 1", "Flügelhorn 2",
                        "Trompete 1", "Trompete 2", "Trompete 3", "Trompete 4",
                        "Euphonium"
                    };
                    var group3 = new[]
                    {
                        "Kleine Trommel", "Große Trommel", "Schlagzeug", "Becken",
                        "Glockenspiel / Marimba", "Perkussion 1", "Perkussion 2", "Perkussion 3", "Pauke"
                    };
                    var group4 = new[]
                    {
                        "Horn in F 1", "Horn in F 2", "Horn in F 3", "Horn in F 4",
                        "Horn in Es 1", "Horn in Es 2", "Horn in Es 3", "Horn in Es 4",
                        "Tenorhorn 1", "Tenorhorn 2", "Tenorhorn 3", "Bariton",
                        "Posaune in C 1", "Posaune in C 2", "Posaune in C 3", "Bassposaune",
                        "Posaune in B 1", "Posaune in B 2", "Posaune in B 3",
                        "Tuba in C 1", "Tuba in C 2", "Tuba in B 1", "Tuba in B 2", "Tuba in Es"
                    };

                    using var tx = connection.BeginTransaction();

                    void InsertGroup(string[] names, int gid)
                    {
                        foreach (var name in names)
                        {
                            using var insert = connection.CreateCommand();
                            insert.Transaction = tx;
                            insert.CommandText = "INSERT OR IGNORE INTO instrument_group_assignments(instrument_name, group_id) VALUES ($n, $g)";
                            insert.Parameters.AddWithValue("$n", name);
                            insert.Parameters.AddWithValue("$g", gid);
                            insert.ExecuteNonQuery();
                        }
                    }

                    InsertGroup(group1, 1);
                    InsertGroup(group2, 2);
                    InsertGroup(group3, 3);
                    InsertGroup(group4, 4);

                    tx.Commit();
                }
            }

            // Migration: Gesang und Solo immer in Gruppe 1 sicherstellen
            foreach (var instr in new[] { "Gesang", "Solo" })
            {
                using var migrateCmd = connection.CreateCommand();
                migrateCmd.CommandText = "INSERT OR REPLACE INTO instrument_group_assignments(instrument_name, group_id) VALUES ($n, 1)";
                migrateCmd.Parameters.AddWithValue("$n", instr);
                migrateCmd.ExecuteNonQuery();
            }
        }
    }
}
