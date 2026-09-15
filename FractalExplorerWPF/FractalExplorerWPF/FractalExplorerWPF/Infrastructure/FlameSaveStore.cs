using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class FlameSaveStore() : FractalSaveStore<FlameState>("Flame", state => state.SaveName);
