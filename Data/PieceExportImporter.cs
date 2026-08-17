using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    /// <summary>
    /// Export und Import der Musikstückliste als JSON oder CSV.
    /// </summary>
    public static class PieceExportImporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static async Task ExportAsJsonAsync(IEnumerable<Piece> pieces, string filePath)
        {
            var json = JsonSerializer.Serialize(pieces.ToList(), JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public static async Task ExportAsCsvAsync(IEnumerable<Piece> pieces, string filePath)
        {
            var lines = new List<string>
            {
                "Titel;Komponist;Arrangeur;Verlag;ISBN;Tags;Gattung;Schrank;Fach;Einschub;Aktiv;Ordnerpfad;Besetzung"
            };

            foreach (var piece in pieces)
            {
                lines.Add(string.Join(";", new[]
                {
                    Escape(piece.Title),
                    Escape(piece.Composer),
                    Escape(piece.Arranger),
                    Escape(piece.Publisher),
                    Escape(piece.Isbn),
                    Escape(piece.Tags),
                    Escape(piece.Genre),
                    Escape(piece.Cabinet),
                    Escape(piece.Compartment),
                    Escape(piece.Slot),
                    piece.IsActive ? "Ja" : "Nein",
                    Escape(piece.FolderPath),
                    Escape(piece.Besetzung)
                }));
            }

            await File.WriteAllLinesAsync(filePath, lines);
        }

        public static async Task<IReadOnlyList<Piece>> ImportFromJsonAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            var pieces = JsonSerializer.Deserialize<List<Piece>>(json, JsonOptions) ?? new List<Piece>();

            // Beim Import immer als neuer Datensatz behandeln, um vorhandene Einträge nicht versehentlich zu überschreiben
            foreach (var piece in pieces)
            {
                piece.Id = 0;
            }

            return pieces;
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
