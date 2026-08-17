using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MusikArchivApp.Models;

namespace MusikArchivApp.Printing
{
    public static class PieceListPrintService
    {
        private const float MarginMm = 10f;
        private const float TitleBlockHeightMm = 11f;
        private const float HeaderRowHeightMm = 7f;
        private const float DataRowHeightMm = 5.5f;
        private const float BorderThicknessMm = 0.15f;
        private const float TitleFontPt = 12f;
        private const float SubtitleFontPt = 8.5f;
        private const float HeaderFontPt = 8.5f;
        private const float CellFontPt = 8f;

        public static bool PrintList(
            Window owner,
            IReadOnlyList<Piece> pieces,
            IReadOnlyList<ColumnEntry> columns,
            string jobName = "Musikstückliste",
            IReadOnlyDictionary<string, string>? cabinetColorsByName = null)
        {
            if (!owner.Dispatcher.CheckAccess())
            {
                return owner.Dispatcher.Invoke(() => PrintList(owner, pieces, columns, jobName));
            }

            if (pieces.Count == 0)
            {
                UiMessage.Show("Die Liste ist leer, es gibt nichts zu drucken.", "Listendruck",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (columns.Count == 0)
            {
                UiMessage.Show("Bitte mindestens eine Spalte für den Druck auswählen.", "Listendruck",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            try
            {
                var printDocument = new PrintDocument
                {
                    DocumentName = jobName,
                    PrinterSettings = { PrinterName = dialog.PrintQueue.FullName }
                };

                printDocument.DefaultPageSettings.Landscape = false;

                var paper = printDocument.DefaultPageSettings.PaperSize;
                var pageWidthMm = paper.Width / 100f * 25.4f;
                var pageHeightMm = paper.Height / 100f * 25.4f;
                if (pageWidthMm > pageHeightMm)
                {
                    (pageWidthMm, pageHeightMm) = (pageHeightMm, pageWidthMm);
                }

                var layout = new PrintLayout(pieces, columns, pageWidthMm, pageHeightMm, cabinetColorsByName);

                var pageIndex = 0;
                printDocument.PrintPage += (_, args) =>
                {
                    if (args.Graphics == null)
                    {
                        args.Cancel = true;
                        return;
                    }

                    DrawPage(args.Graphics, layout, pageIndex, jobName);
                    pageIndex++;
                    args.HasMorePages = pageIndex < layout.PageCount;
                };

                printDocument.Print();
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(DescribePrintException(ex), ex);
            }
        }

        public static string GetColumnValue(Piece piece, string key)
        {
            return key switch
            {
                "Name" => piece.Title,
                "Komponist" => piece.Composer ?? string.Empty,
                "Arrangeur" => piece.Arranger ?? string.Empty,
                "Gattung" => piece.Genre ?? string.Empty,
                "Tags" => piece.Tags ?? string.Empty,
                "Aktiv" => piece.IsActive ? "Ja" : "Nein",
                "Schrank" => piece.Cabinet ?? string.Empty,
                "Fach" => piece.Compartment ?? string.Empty,
                "Einschub" => piece.Slot ?? string.Empty,
                "Besetzung" => piece.Besetzung ?? string.Empty,
                "Verlag" => piece.Publisher ?? string.Empty,
                "ISBN" => piece.Isbn ?? string.Empty,
                "Ordnerpfad" => piece.FolderPath ?? string.Empty,
                "Noten" => piece.DigitalScoreCount.ToString(CultureInfo.CurrentCulture),
                _ => string.Empty
            };
        }

        private static string DescribePrintException(Exception ex)
        {
            var parts = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message)
                    && !parts.Contains(current.Message, StringComparer.Ordinal))
                {
                    parts.Add(current.Message);
                }
            }

            return parts.Count > 0
                ? string.Join("\n", parts)
                : $"{ex.GetType().Name} beim Drucken.";
        }

        private static void DrawPage(Graphics graphics, PrintLayout layout, int pageIndex, string title)
        {
            graphics.PageUnit = GraphicsUnit.Millimeter;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.White);

            var left = MarginMm;
            var top = MarginMm;
            var tableWidth = layout.PageWidthMm - MarginMm * 2;

            top = DrawTitleBlock(graphics, title, layout.Pieces.Count, left, top, tableWidth);
            top = DrawHeaderRow(graphics, layout.Columns, left, top, tableWidth);

            var startRow = pageIndex * layout.RowsPerPage;
            var endRow = Math.Min(startRow + layout.RowsPerPage, layout.Pieces.Count);
            for (var rowIndex = startRow; rowIndex < endRow; rowIndex++)
            {
                DrawDataRow(graphics, layout.Pieces[rowIndex], layout.Columns, left, top, tableWidth, rowIndex - startRow, layout.CabinetColorsByName);
                top += DataRowHeightMm;
            }

            if (layout.PageCount > 1)
            {
                DrawFooter(graphics, pageIndex + 1, layout.PageCount, layout.PageWidthMm, layout.PageHeightMm);
            }
        }

        private static float DrawTitleBlock(Graphics g, string title, int pieceCount, float left, float top, float width)
        {
            using var titleFont = new Font("Segoe UI", TitleFontPt, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            using var subtitleFont = new Font("Segoe UI", SubtitleFontPt, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
            using var brush = new SolidBrush(Color.Black);

            g.DrawString(title, titleFont, brush, new RectangleF(left, top, width, TitleBlockHeightMm * 0.55f));
            var subtitle = $"{pieceCount} Stück(e) · {DateTime.Now:g}";
            g.DrawString(subtitle, subtitleFont, brush, new RectangleF(left, top + 5.5f, width, 4f));
            return top + TitleBlockHeightMm;
        }

        private static float DrawHeaderRow(Graphics g, IReadOnlyList<ColumnEntry> columns, float left, float top, float tableWidth)
        {
            using var font = new Font("Segoe UI", HeaderFontPt, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            using var brush = new SolidBrush(Color.Black);
            using var background = new SolidBrush(Color.FromArgb(235, 235, 235));
            using var pen = new Pen(Color.Black, BorderThicknessMm);

            var colWidth = tableWidth / columns.Count;
            var rect = new RectangleF(left, top, tableWidth, HeaderRowHeightMm);
            g.FillRectangle(background, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            for (var i = 0; i < columns.Count; i++)
            {
                var cellRect = new RectangleF(left + colWidth * i, top, colWidth, HeaderRowHeightMm);
                if (i > 0)
                {
                    g.DrawLine(pen, cellRect.Left, top, cellRect.Left, top + HeaderRowHeightMm);
                }

                DrawCellText(g, columns[i].Header, font, brush, cellRect);
            }

            return top + HeaderRowHeightMm;
        }

        private static void DrawDataRow(
            Graphics g,
            Piece piece,
            IReadOnlyList<ColumnEntry> columns,
            float left,
            float top,
            float tableWidth,
            int stripeIndex,
            IReadOnlyDictionary<string, string>? cabinetColorsByName)
        {
            using var font = new Font("Segoe UI", CellFontPt, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
            using var defaultBrush = new SolidBrush(Color.Black);
            using var stripe = new SolidBrush(stripeIndex % 2 == 1 ? Color.FromArgb(248, 248, 248) : Color.White);
            using var pen = new Pen(Color.Black, BorderThicknessMm);

            var colWidth = tableWidth / columns.Count;
            var rect = new RectangleF(left, top, tableWidth, DataRowHeightMm);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            for (var i = 0; i < columns.Count; i++)
            {
                var cellRect = new RectangleF(left + colWidth * i, top, colWidth, DataRowHeightMm);
                if (i > 0)
                {
                    g.DrawLine(pen, cellRect.Left, top, cellRect.Left, top + DataRowHeightMm);
                }

                var columnKey = columns[i].Key;
                if (columnKey == "Schrank")
                {
                    var backgroundColor = ResolveCabinetColor(piece, cabinetColorsByName);
                    using var backgroundBrush = new SolidBrush(backgroundColor);
                    using var textBrush = new SolidBrush(GetContrastingTextColor(backgroundColor));
                    g.FillRectangle(backgroundBrush, cellRect);
                    DrawCellText(g, GetColumnValue(piece, columnKey), font, textBrush, cellRect, centered: true);
                }
                else
                {
                    g.FillRectangle(stripe, cellRect);
                    DrawCellText(g, GetColumnValue(piece, columnKey), font, defaultBrush, cellRect);
                }
            }
        }

        private static Color ResolveCabinetColor(Piece piece, IReadOnlyDictionary<string, string>? cabinetColorsByName)
        {
            if (!string.IsNullOrWhiteSpace(piece.CabinetColor))
            {
                return ParseColor(piece.CabinetColor);
            }

            if (!string.IsNullOrWhiteSpace(piece.Cabinet)
                && cabinetColorsByName != null
                && cabinetColorsByName.TryGetValue(piece.Cabinet, out var color)
                && !string.IsNullOrWhiteSpace(color))
            {
                return ParseColor(color);
            }

            return Color.FromArgb(102, 45, 145);
        }

        private static Color ParseColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Color.FromArgb(102, 45, 145);
            }

            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return Color.FromArgb(102, 45, 145);
            }
        }

        private static Color GetContrastingTextColor(Color background)
        {
            var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return luminance > 0.6 ? Color.Black : Color.White;
        }

        private static void DrawCellText(Graphics g, string text, Font font, Brush brush, RectangleF rect, bool centered = false)
        {
            var format = new StringFormat
            {
                Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            var padding = 0.8f;
            var inner = new RectangleF(rect.Left + padding, rect.Top, Math.Max(1, rect.Width - padding * 2), rect.Height);
            g.DrawString(text, font, brush, inner, format);
        }

        private static void DrawFooter(Graphics g, int pageNumber, int pageCount, float pageWidthMm, float pageHeightMm)
        {
            using var font = new Font("Segoe UI", SubtitleFontPt, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
            using var brush = new SolidBrush(Color.Black);
            var text = $"Seite {pageNumber} / {pageCount}";
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (pageWidthMm - size.Width) / 2f, pageHeightMm - MarginMm + 1f);
        }

        private sealed class PrintLayout
        {
            public PrintLayout(
                IReadOnlyList<Piece> pieces,
                IReadOnlyList<ColumnEntry> columns,
                float pageWidthMm,
                float pageHeightMm,
                IReadOnlyDictionary<string, string>? cabinetColorsByName)
            {
                Pieces = pieces;
                Columns = columns;
                PageWidthMm = pageWidthMm;
                PageHeightMm = pageHeightMm;
                CabinetColorsByName = cabinetColorsByName;

                var usableHeight = pageHeightMm - MarginMm * 2 - TitleBlockHeightMm - HeaderRowHeightMm - 4f;
                RowsPerPage = Math.Max(1, (int)(usableHeight / DataRowHeightMm));
                PageCount = (pieces.Count + RowsPerPage - 1) / RowsPerPage;
            }

            public IReadOnlyList<Piece> Pieces { get; }

            public IReadOnlyList<ColumnEntry> Columns { get; }

            public float PageWidthMm { get; }

            public float PageHeightMm { get; }

            public int RowsPerPage { get; }

            public int PageCount { get; }

            public IReadOnlyDictionary<string, string>? CabinetColorsByName { get; }
        }
    }
}
