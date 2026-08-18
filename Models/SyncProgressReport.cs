namespace MusikArchivApp.Models
{
    public sealed class SyncProgressReport
    {
        public string PhaseLabel { get; init; } = string.Empty;

        public int PercentComplete { get; init; }

        public long BytesTransferred { get; init; }

        public long? TotalBytes { get; init; }

        public double? BytesPerSecond { get; init; }
    }
}
