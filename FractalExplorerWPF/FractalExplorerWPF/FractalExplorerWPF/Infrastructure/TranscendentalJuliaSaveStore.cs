using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class TranscendentalJuliaSaveStore(TranscendentalJuliaKind kind) : FractalSaveStore<TranscendentalJuliaState>(
    TranscendentalJuliaCatalog.LaunchKey(kind), state => state.SaveName,
    state => { state.Validate(); return state.Kind == kind; });
