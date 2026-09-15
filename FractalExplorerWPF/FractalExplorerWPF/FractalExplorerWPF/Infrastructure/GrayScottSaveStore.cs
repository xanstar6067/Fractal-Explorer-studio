using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class GrayScottSaveStore() : FractalSaveStore<GrayScottState>("GrayScott", state => state.SaveName);
