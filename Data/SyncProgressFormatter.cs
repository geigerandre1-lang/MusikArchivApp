using System.Globalization;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public static class SyncProgressFormatter
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:0.#} KB";
            }

            return $"{bytes / (1024.0 * 1024.0):0.#} MB";
        }

        public static string FormatRate(double bytesPerSecond)
        {
            return $"{FormatBytes((long)bytesPerSecond)}/s";
        }

        public static string FormatProgressLine(SyncProgressReport report)
        {
            var parts = new System.Collections.Generic.List<string>
            {
                $"{report.PhaseLabel}: {report.PercentComplete.ToString(CultureInfo.InvariantCulture)}%"
            };

            if (report.TotalBytes.HasValue && report.TotalBytes.Value > 0)
            {
                parts.Add($"{FormatBytes(report.BytesTransferred)} / {FormatBytes(report.TotalBytes.Value)}");
            }
            else if (report.BytesTransferred > 0)
            {
                parts.Add($"{FormatBytes(report.BytesTransferred)} übertragen");
            }

            if (report.BytesPerSecond is > 0)
            {
                parts.Add(FormatRate(report.BytesPerSecond.Value));
            }

            return string.Join(" · ", parts);
        }
    }
}
