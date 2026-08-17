using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public enum PieceBackupAction
    {
        Created,
        Deleted,
        DeletedAll
    }

    /// <summary>
    /// Erstellt bei Anlegen/Löschen von Musikstücken DB-Sicherungen und Dump-Notes unter %AppData%/MusikArchivApp/backups/.
    /// </summary>
    public static class PieceBackupService
    {
        private const string DatabaseFileName = "musikarchiv.db";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetBackupsDirectory() => AppPaths.GetBackupsDirectory();

        public static Task CreateBackupAsync(
            PieceBackupAction action,
            Piece piece,
            IReadOnlyList<string>? instrumentNames = null)
        {
            return CreateBackupInternalAsync(action, piece, instrumentNames, null);
        }

        public static Task CreateDeleteAllBackupAsync(IReadOnlyList<Piece> pieces)
        {
            return CreateBackupInternalAsync(PieceBackupAction.DeletedAll, null, null, pieces);
        }

        private static async Task CreateBackupInternalAsync(
            PieceBackupAction action,
            Piece? piece,
            IReadOnlyList<string>? instrumentNames,
            IReadOnlyList<Piece>? allPieces)
        {
            try
            {
                var timestamp = DateTime.Now;
                var backupDir = Path.Combine(GetBackupsDirectory(), BuildFolderName(timestamp, action, piece?.Title));
                Directory.CreateDirectory(backupDir);

                CopyDatabase(backupDir);
                CopyNotenDirectory(backupDir);

                var dumpNotePath = Path.Combine(backupDir, "dump-note.txt");
                await File.WriteAllTextAsync(dumpNotePath, BuildDumpNote(timestamp, action, piece, instrumentNames, allPieces))
                    .ConfigureAwait(false);

                if (piece != null)
                {
                    var exportPiece = EnrichPiece(piece, instrumentNames);
                    var piecePath = Path.Combine(backupDir, "piece.json");
                    var json = JsonSerializer.Serialize(exportPiece, JsonOptions);
                    await File.WriteAllTextAsync(piecePath, json).ConfigureAwait(false);
                }
                else if (allPieces != null)
                {
                    var piecesPath = Path.Combine(backupDir, "pieces.json");
                    var json = JsonSerializer.Serialize(allPieces, JsonOptions);
                    await File.WriteAllTextAsync(piecesPath, json).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup fehlgeschlagen: {ex.Message}");
            }
        }

        private static void CopyDatabase(string backupDir)
        {
            var dbSource = AppPaths.GetDatabasePath();
            if (!File.Exists(dbSource))
            {
                return;
            }

            var dbDest = Path.Combine(backupDir, DatabaseFileName);
            File.Copy(dbSource, dbDest, overwrite: true);
        }

        private static void CopyNotenDirectory(string backupDir)
        {
            var source = AppPaths.GetNotenDirectory();
            if (!Directory.Exists(source))
            {
                return;
            }

            var dest = Path.Combine(backupDir, "Noten");
            CopyDirectoryContents(source, dest);
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, sourcePath);
                var targetPath = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
        }

        private static string BuildFolderName(DateTime timestamp, PieceBackupAction action, string? title)
        {
            var actionPart = action switch
            {
                PieceBackupAction.Created => "angelegt",
                PieceBackupAction.Deleted => "geloescht",
                PieceBackupAction.DeletedAll => "alle-geloescht",
                _ => "backup"
            };

            var titlePart = action == PieceBackupAction.DeletedAll
                ? string.Empty
                : $"_{SanitizeFileName(title)}";

            return $"{timestamp:yyyy-MM-dd_HHmmss}_{actionPart}{titlePart}";
        }

        private static string SanitizeFileName(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "ohne-titel";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(title.Trim().Length);
            foreach (var character in title.Trim())
            {
                builder.Append(invalid.Contains(character) ? '-' : character);
            }

            var sanitized = builder.ToString().Trim();
            if (sanitized.Length == 0)
            {
                return "ohne-titel";
            }

            return sanitized.Length > 40 ? sanitized[..40] : sanitized;
        }

        private static Piece EnrichPiece(Piece piece, IReadOnlyList<string>? instrumentNames)
        {
            if (!string.IsNullOrWhiteSpace(piece.Besetzung) || instrumentNames == null || instrumentNames.Count == 0)
            {
                return piece;
            }

            return new Piece
            {
                Id = piece.Id,
                Title = piece.Title,
                Composer = piece.Composer,
                Arranger = piece.Arranger,
                Publisher = piece.Publisher,
                Isbn = piece.Isbn,
                Tags = piece.Tags,
                Genre = piece.Genre,
                Cabinet = piece.Cabinet,
                Compartment = piece.Compartment,
                Slot = piece.Slot,
                IsActive = piece.IsActive,
                FolderPath = piece.FolderPath,
                CabinetColor = piece.CabinetColor,
                Besetzung = string.Join(", ", instrumentNames)
            };
        }

        private static string BuildDumpNote(
            DateTime timestamp,
            PieceBackupAction action,
            Piece? piece,
            IReadOnlyList<string>? instrumentNames,
            IReadOnlyList<Piece>? allPieces)
        {
            var note = new StringBuilder();
            note.AppendLine("MusikArchivApp – Sicherungsprotokoll");
            note.AppendLine("====================================");
            note.AppendLine($"Zeitpunkt: {timestamp:dd.MM.yyyy HH:mm:ss}");
            note.AppendLine($"Aktion:    {DescribeAction(action)}");
            note.AppendLine();

            switch (action)
            {
                case PieceBackupAction.Created when piece != null:
                    AppendPieceDetails(note, piece, instrumentNames);
                    note.AppendLine("Hinweis:   Datenbankstand NACH dem Anlegen.");
                    note.AppendLine("Dateien:   musikarchiv.db, piece.json, Noten/ (Spiegel der Notendateien)");
                    break;

                case PieceBackupAction.Deleted when piece != null:
                    AppendPieceDetails(note, piece, instrumentNames);
                    note.AppendLine("Hinweis:   Datenbankstand VOR dem Löschen (Stück noch enthalten).");
                    note.AppendLine("Dateien:   musikarchiv.db, piece.json, Noten/ (Spiegel der Notendateien)");
                    break;

                case PieceBackupAction.DeletedAll:
                    note.AppendLine($"Anzahl gelöschter Stücke: {allPieces?.Count ?? 0}");
                    note.AppendLine("Hinweis:   Datenbankstand VOR dem Löschen aller Stücke.");
                    note.AppendLine("Dateien:   musikarchiv.db, pieces.json, Noten/ (Spiegel der Notendateien)");
                    break;
            }

            note.AppendLine();
            note.AppendLine($"Speicherort: {GetBackupsDirectory()}");
            return note.ToString();
        }

        private static string DescribeAction(PieceBackupAction action) => action switch
        {
            PieceBackupAction.Created => "Musikstück angelegt",
            PieceBackupAction.Deleted => "Musikstück gelöscht",
            PieceBackupAction.DeletedAll => "Alle Musikstücke gelöscht (Admin)",
            _ => "Sicherung"
        };

        private static void AppendPieceDetails(StringBuilder note, Piece piece, IReadOnlyList<string>? instrumentNames)
        {
            note.AppendLine($"Stück-ID:   {piece.Id}");
            note.AppendLine($"Titel:      {piece.Title}");
            note.AppendLine($"Komponist:  {piece.Composer ?? "–"}");
            note.AppendLine($"Arrangeur:  {piece.Arranger ?? "–"}");
            note.AppendLine($"Verlag:     {piece.Publisher ?? "–"}");
            note.AppendLine($"ISBN:       {piece.Isbn ?? "–"}");
            note.AppendLine($"Gattung:    {piece.Genre ?? "–"}");
            note.AppendLine($"Tags:       {piece.Tags ?? "–"}");
            note.AppendLine($"Schrank:    {piece.Cabinet ?? "–"} / Fach {piece.Compartment ?? "–"} / Einschub {piece.Slot ?? "–"}");
            note.AppendLine($"Aktiv:      {(piece.IsActive ? "Ja" : "Nein")}");
            note.AppendLine($"Ordnerpfad: {piece.FolderPath ?? "–"}");

            var besetzung = !string.IsNullOrWhiteSpace(piece.Besetzung)
                ? piece.Besetzung
                : instrumentNames != null && instrumentNames.Count > 0
                    ? string.Join(", ", instrumentNames)
                    : "–";
            note.AppendLine($"Besetzung:  {besetzung}");
        }
    }
}
