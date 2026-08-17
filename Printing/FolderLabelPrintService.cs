using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using MusikArchivApp.Data;
using MusikArchivApp.Models;
using QRCoder;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using FontFamily = System.Drawing.FontFamily;
using Pen = System.Drawing.Pen;

namespace MusikArchivApp.Printing
{
    /// <summary>
    /// Ordner-Label: 60 × 21 mm, Rand 0,5 mm, 4 Zeilen à 5 mm (Inhalt 59 × 20 mm).
    /// </summary>
    public static class FolderLabelPrintService
    {
        private const double LabelWidthMm = 60.0;
        private const double LabelHeightMm = 21.0;
        private const double MarginMm = 0.5;
        private const double ContentWidthMm = 59.0;
        private const double RowHeightMm = 5.0;
        private const int Row2ColumnCount = 3;
        private const double LabelGapMm = 2.0;
        private const double PageMarginMm = 5.0;
        private const float BorderThicknessMm = 0.2f;
        private const float DefaultFontPt = 9f;
        private const float MinFontPt = 5f;
        private const float QrPaddingMm = 0.12f;
        private const float QrFillRatio = 0.98f;
        private const int QrPixelsPerModule = 24;
        private const float QrRenderDpi = 600f;

        public static bool PrintLabels(Window owner, IReadOnlyList<FolderLabelData> labels, string jobName = "Ordner-Labels")
        {
            if (!owner.Dispatcher.CheckAccess())
            {
                return owner.Dispatcher.Invoke(() => PrintLabels(owner, labels, jobName));
            }

            if (labels.Count == 0)
            {
                UiMessage.Show("Keine Labels zum Drucken ausgewählt.", "Label-Druck",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var includeQrCode = AppConfigStore.Load().LabelQrCodeEnabled;

            var missingQr = includeQrCode
                ? labels.Count(l => string.IsNullOrWhiteSpace(l.WebViewUrl))
                : 0;
            if (missingQr > 0)
            {
                UiMessage.Show(
                    missingQr == labels.Count
                        ? "Für kein Stück konnte ein Web-Link erzeugt werden. QR-Codes fehlen.\n\nBitte unter Einstellungen → Synchronisation eine Server-URL eintragen und speichern."
                        : $"{missingQr} Stück(e) ohne Web-Link – deren QR-Code fehlt auf dem Label.\n\nServer-URL unter Einstellungen → Synchronisation prüfen.",
                    "Label-Druck",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

                var paper = printDocument.DefaultPageSettings.PaperSize;
                var pageWidthMm = paper.Width / 100f * 25.4f;
                var pageHeightMm = paper.Height / 100f * 25.4f;
                var layout = new LabelPageLayout(labels, pageWidthMm, pageHeightMm);
                var pageIndex = 0;
                printDocument.PrintPage += (_, args) =>
                {
                    if (args.Graphics == null)
                    {
                        args.Cancel = true;
                        return;
                    }

                    DrawPage(args.Graphics, labels, pageIndex, layout, includeQrCode);
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

        private static void DrawPage(Graphics graphics, IReadOnlyList<FolderLabelData> labels, int pageIndex, LabelPageLayout layout, bool includeQrCode)
        {
            graphics.PageUnit = GraphicsUnit.Millimeter;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.White);

            for (var slot = 0; slot < layout.LabelsPerPage; slot++)
            {
                var labelIndex = pageIndex * layout.LabelsPerPage + slot;
                if (labelIndex >= labels.Count)
                {
                    break;
                }

                var col = slot % layout.Columns;
                var row = slot / layout.Columns;
                var x = PageMarginMm + col * (LabelWidthMm + LabelGapMm);
                var y = PageMarginMm + row * (LabelHeightMm + LabelGapMm);
                DrawLabel(graphics, labels[labelIndex], (float)x, (float)y, includeQrCode);
            }
        }

        private static void DrawLabel(Graphics g, FolderLabelData data, float leftMm, float topMm, bool includeQrCode)
        {
            var backgroundColor = ParseCabinetColor(data.CabinetColor);
            var foregroundColor = GetTextColor(backgroundColor);
            using var backgroundBrush = new SolidBrush(backgroundColor);
            using var foregroundBrush = new SolidBrush(foregroundColor);
            using var borderPen = new Pen(Color.Black, BorderThicknessMm);

            var labelRect = new RectangleF(leftMm, topMm, (float)LabelWidthMm, (float)LabelHeightMm);
            g.FillRectangle(backgroundBrush, labelRect);
            g.DrawRectangle(borderPen, labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height);

            var contentLeft = leftMm + (float)MarginMm;
            var contentTop = topMm + (float)MarginMm;

            var row1Rect = new RectangleF(contentLeft, contentTop, (float)ContentWidthMm, (float)RowHeightMm);
            DrawFittedText(g, data.Title, row1Rect, foregroundBrush, bold: true, centered: true);
            g.DrawLine(borderPen, leftMm, row1Rect.Bottom, leftMm + (float)LabelWidthMm, row1Rect.Bottom);

            var row2Top = row1Rect.Bottom;
            var colWidth = (float)(ContentWidthMm / Row2ColumnCount);
            var field2Right = contentLeft + colWidth * 2;
            var row2Rect = new RectangleF(contentLeft, row2Top, (float)ContentWidthMm, (float)RowHeightMm);
            var cabinetRect = new RectangleF(contentLeft, row2Top, colWidth, (float)RowHeightMm);
            var compartmentRect = new RectangleF(contentLeft + colWidth, row2Top, colWidth, (float)RowHeightMm);
            var slotRect = new RectangleF(contentLeft + colWidth * 2, row2Top, colWidth, (float)RowHeightMm);

            DrawFittedText(g, FormatCabinet(data.Cabinet), cabinetRect, foregroundBrush, bold: true, centered: true);
            DrawFittedText(g, FormatCompartment(data.Compartment), compartmentRect, foregroundBrush, bold: true, centered: true);
            DrawFittedText(g, FormatSlot(data.Slot), slotRect, foregroundBrush, bold: true, centered: true);

            g.DrawLine(borderPen, cabinetRect.Right, row2Top, cabinetRect.Right, row2Rect.Bottom);
            g.DrawLine(borderPen, compartmentRect.Right, row2Top, compartmentRect.Right, row2Rect.Bottom);
            g.DrawLine(borderPen, leftMm, row2Rect.Bottom, leftMm + (float)LabelWidthMm, row2Rect.Bottom);

            var row3Top = row2Rect.Bottom;
            var row4Top = row3Top + (float)RowHeightMm;

            if (includeQrCode)
            {
                var textWidthRows34 = colWidth * 2;
                var composerRect = new RectangleF(contentLeft, row3Top, textWidthRows34, (float)RowHeightMm);
                var arrangerRect = new RectangleF(contentLeft, row4Top, textWidthRows34, (float)RowHeightMm);

                DrawFittedText(g, data.Composer, composerRect, foregroundBrush, bold: false, centered: true);
                DrawFittedText(g, data.Arranger, arrangerRect, foregroundBrush, bold: false, centered: true);

                var qrRect = new RectangleF(field2Right, row3Top, colWidth, (float)(RowHeightMm * 2));
                g.DrawLine(borderPen, field2Right, row2Top, field2Right, qrRect.Bottom);
                g.DrawLine(borderPen, contentLeft, row4Top, field2Right, row4Top);

                using var qrImage = CreateQrBitmap(data.WebViewUrl, foregroundColor, backgroundColor, qrRect);
                if (qrImage != null)
                {
                    var targetSize = Math.Min(qrRect.Width, qrRect.Height) * QrFillRatio - QrPaddingMm * 2;
                    var drawX = qrRect.Left + (qrRect.Width - targetSize) / 2f;
                    var drawY = qrRect.Top + (qrRect.Height - targetSize) / 2f;

                    var state = g.Save();
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.SmoothingMode = SmoothingMode.None;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.DrawImage(qrImage, drawX, drawY, targetSize, targetSize);
                    g.Restore(state);
                }
            }
            else
            {
                var composerRect = new RectangleF(contentLeft, row3Top, (float)ContentWidthMm, (float)RowHeightMm);
                var arrangerRect = new RectangleF(contentLeft, row4Top, (float)ContentWidthMm, (float)RowHeightMm);

                DrawFittedText(g, data.Composer, composerRect, foregroundBrush, bold: false, centered: true);
                DrawFittedText(g, data.Arranger, arrangerRect, foregroundBrush, bold: false, centered: true);
                g.DrawLine(borderPen, contentLeft, row4Top, contentLeft + (float)ContentWidthMm, row4Top);
            }
        }

        private static void DrawFittedText(
            Graphics g,
            string? text,
            RectangleF rect,
            Brush brush,
            bool bold,
            bool centered)
        {
            var content = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (content.Length == 0)
            {
                return;
            }

            var format = new StringFormat
            {
                Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            for (var sizePt = DefaultFontPt; sizePt >= MinFontPt; sizePt -= 0.5f)
            {
                using var font = new Font(new FontFamily("Segoe UI"), sizePt, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
                var measured = g.MeasureString(content, font, new SizeF(rect.Width, rect.Height), format);
                if (measured.Width <= rect.Width + 0.1f && measured.Height <= rect.Height + 0.1f)
                {
                    g.DrawString(content, font, brush, rect, format);
                    return;
                }
            }

            using var minFont = new Font(new FontFamily("Segoe UI"), MinFontPt, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
            g.DrawString(content, minFont, brush, rect, format);
        }

        private static Bitmap? CreateQrBitmap(string? url, Color moduleColor, Color backgroundColor, RectangleF targetRectMm)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            try
            {
                using var generator = new QRCodeGenerator();
                using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                var png = new PngByteQRCode(qrData);
                var bytes = png.GetGraphic(
                    QrPixelsPerModule,
                    new[] { moduleColor.R, moduleColor.G, moduleColor.B },
                    new[] { backgroundColor.R, backgroundColor.G, backgroundColor.B });

                using var stream = new MemoryStream(bytes);
                using var source = new Bitmap(stream);

                var targetSizeMm = Math.Min(targetRectMm.Width, targetRectMm.Height) * QrFillRatio - QrPaddingMm * 2;
                var targetPx = Math.Max(128, (int)Math.Ceiling(targetSizeMm / 25.4f * QrRenderDpi));

                var result = new Bitmap(targetPx, targetPx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var resizeGraphics = Graphics.FromImage(result))
                {
                    resizeGraphics.Clear(backgroundColor);
                    resizeGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    resizeGraphics.PixelOffsetMode = PixelOffsetMode.Half;
                    resizeGraphics.SmoothingMode = SmoothingMode.None;
                    resizeGraphics.DrawImage(source, 0, 0, targetPx, targetPx);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static Color ParseCabinetColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Color.FromArgb(102, 45, 145);
            }

            try
            {
                return System.Drawing.ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return Color.FromArgb(102, 45, 145);
            }
        }

        private static Color GetTextColor(Color background)
        {
            var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return luminance > 0.6 ? Color.Black : Color.White;
        }

        private static string FormatCabinet(string? value)
            => string.IsNullOrWhiteSpace(value) ? "Schrank –" : $"Schrank {value.Trim()}";

        private static string FormatCompartment(string? value)
            => string.IsNullOrWhiteSpace(value) ? "Fach –" : $"Fach {value.Trim()}";

        private static string FormatSlot(string? value)
            => string.IsNullOrWhiteSpace(value) ? "Einschub –" : $"Einschub {value.Trim()}";

        private sealed class LabelPageLayout
        {
            public LabelPageLayout(IReadOnlyList<FolderLabelData> labels, float pageWidthMm, float pageHeightMm)
            {
                Labels = labels;
                PageWidthMm = pageWidthMm > 0 ? pageWidthMm : 210f;
                PageHeightMm = pageHeightMm > 0 ? pageHeightMm : 297f;

                var usableWidth = PageWidthMm - (float)PageMarginMm * 2;
                var usableHeight = PageHeightMm - (float)PageMarginMm * 2;
                Columns = Math.Max(1, (int)((usableWidth + LabelGapMm) / (LabelWidthMm + LabelGapMm)));
                var rows = Math.Max(1, (int)((usableHeight + LabelGapMm) / (LabelHeightMm + LabelGapMm)));
                LabelsPerPage = Columns * rows;
            }

            public IReadOnlyList<FolderLabelData> Labels { get; }

            public int Columns { get; }

            public int LabelsPerPage { get; }

            public int PageCount => (Labels.Count + LabelsPerPage - 1) / LabelsPerPage;

            public float PageWidthMm { get; }

            public float PageHeightMm { get; }
        }
    }
}
