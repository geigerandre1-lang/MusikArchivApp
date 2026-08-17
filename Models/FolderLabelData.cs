namespace MusikArchivApp.Models
{
    public class FolderLabelData
    {
        public string Title { get; set; } = string.Empty;
        public string? Cabinet { get; set; }
        public string? Compartment { get; set; }
        public string? Slot { get; set; }
        public string? Composer { get; set; }
        public string? Arranger { get; set; }
        public string? WebViewUrl { get; set; }
        public string? CabinetColor { get; set; }

        public static FolderLabelData FromPiece(Piece piece, string? webViewUrl = null) => new()
        {
            Title = piece.Title,
            Cabinet = piece.Cabinet,
            Compartment = piece.Compartment,
            Slot = piece.Slot,
            Composer = piece.Composer,
            Arranger = piece.Arranger,
            WebViewUrl = webViewUrl,
            CabinetColor = piece.CabinetColor
        };
    }
}
