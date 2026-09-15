using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class ApollonianSaveStore() : FractalSaveStore<ApollonianState>("Apollonian", state => state.SaveName);
