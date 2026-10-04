using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class TuringSaveStore() : FractalSaveStore<TuringState>("TuringPatterns", state => state.SaveName,
    state => { state.Validate(); return true; });
