using System;
using System.IO;
using System.Text;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    /// <summary>
    /// Logische Notenpfade und Dateinamen. Dateiinhalte liegen in der SQLite-Datenbank.
    /// </summary>
    public static class SheetMusicPaths
    {
        public static readonly string[] SupportedExtensions = { ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp" };

        public static string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".tif" or ".tiff" => "image/tiff",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
        }

        public static string BuildLogicalPath(Piece piece)
        {
            var cabinet = string.IsNullOrWhiteSpace(piece.Cabinet) ? "?" : piece.Cabinet.Trim();
            var compartment = string.IsNullOrWhiteSpace(piece.Compartment) ? "?" : piece.Compartment.Trim();
            var slot = string.IsNullOrWhiteSpace(piece.Slot) ? "?" : piece.Slot.Trim();
            var title = string.IsNullOrWhiteSpace(piece.Title) ? "?" : piece.Title.Trim();
            return $"Noten / Schrank {cabinet} / Fach {compartment} / Einschub {slot} / {title}";
        }

        public static string BuildPhysicalFolderName(long pieceId, string title)
        {
            return $"{pieceId}_{SanitizePathSegment(title)}";
        }

        public static bool IsSupportedExtension(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            foreach (var supported in SupportedExtensions)
            {
                if (extension.Equals(supported, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string SanitizePathSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "ohne-titel";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Trim().Length);
            foreach (var character in value.Trim())
            {
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '-' : character);
            }

            var sanitized = builder.ToString().Trim();
            if (sanitized.Length == 0)
            {
                return "ohne-titel";
            }

            return sanitized.Length > 60 ? sanitized[..60] : sanitized;
        }

        public static string GenerateFileName(Piece piece, string suffix, string extension)
        {
            var title = SanitizePathSegment(piece.Title);
            var part = SanitizePathSegment(suffix);
            return $"{title}_{part}{extension}";
        }

        public static string BuildRelativeStoredPath(long pieceId, string title, string fileName)
        {
            var folder = BuildPhysicalFolderName(pieceId, title);
            return Path.Combine("Noten", folder, fileName).Replace('\\', '/');
        }

        public static string ResolveStoredPath(string relativePath)
        {
            return Path.Combine(AppPaths.GetDataRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
