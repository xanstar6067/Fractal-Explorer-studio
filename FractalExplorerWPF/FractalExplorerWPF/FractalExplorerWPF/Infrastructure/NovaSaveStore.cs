using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class NovaSaveStore(NovaVariant variant) : FractalSaveStore<NovaState>(
    variant == NovaVariant.Julia ? "NovaJulia" : "NovaMandelbrot", state => state.SaveName);
