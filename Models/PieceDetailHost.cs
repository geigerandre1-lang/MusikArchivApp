using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusikArchivApp.Models
{
    public class PieceDetailHost
    {
        public Piece Piece { get; init; } = null!;
        public IReadOnlyList<string> Group1 { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group2 { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group3 { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group4 { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group1InstrumentNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group2InstrumentNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group3InstrumentNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Group4InstrumentNames { get; init; } = Array.Empty<string>();
        public List<InstrumentSelection> InstrumentSelections { get; init; } = new();
        public IReadOnlyList<string> AvailableTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AvailableGenres { get; init; } = Array.Empty<string>();
        public IReadOnlyList<CabinetOption> AvailableCabinets { get; init; } = Array.Empty<CabinetOption>();
        public IReadOnlyList<string> AvailableCompartments { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AvailableSlots { get; init; } = Array.Empty<string>();

        public Func<Piece, IReadOnlyList<InstrumentSelection>, Task> SavePieceAsync { get; init; } = (_, _) => Task.CompletedTask;
        public Func<long, string, Task<bool>> DeletePieceAsync { get; init; } = (_, _) => Task.FromResult(false);
        public Action<long> OpenInEditor { get; init; } = _ => { };
        public Action<Piece> OpenSheetMusic { get; init; } = _ => { };
        public Func<Piece, Task> RefreshPieceMetadataAsync { get; init; } = _ => Task.CompletedTask;
        public Func<long, Task<string?>> GetWebViewUrlAsync { get; init; } = _ => Task.FromResult<string?>(null);
    }
}
