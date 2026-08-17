namespace MusikArchivApp.Models
{
    public class Piece
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Composer { get; set; }
        public string? Arranger { get; set; }
        public string? Publisher { get; set; }
        public string? Isbn { get; set; }
        public string? Tags { get; set; }
        public string? Genre { get; set; }
        public string? Cabinet { get; set; }
        public string? Compartment { get; set; }
        public string? Slot { get; set; }
        public bool IsActive { get; set; } = true;
        public string? FolderPath { get; set; }
        /// <summary>Anzahl digitaler Notendateien</summary>
        public int DigitalScoreCount { get; set; }
        public bool HasDigitalScores => DigitalScoreCount > 0;
        /// <summary>Hex-Farbe des Schranks (aus cabinet_options, nicht in pieces gespeichert)</summary>
        public string? CabinetColor { get; set; }
        /// <summary>Komma-getrennte Instrument-Namen (nicht persistent, aus JOIN befüllt)</summary>
        public string? Besetzung { get; set; }
    }
}
