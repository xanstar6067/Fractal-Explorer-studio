using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class PhoenixSaveStore() : FractalSaveStore<PhoenixState>("Phoenix", state => state.SaveName);
