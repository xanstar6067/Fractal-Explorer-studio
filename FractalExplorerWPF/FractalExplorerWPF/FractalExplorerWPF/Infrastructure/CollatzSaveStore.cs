using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class CollatzSaveStore() : FractalSaveStore<CollatzState>("Collatz", state => state.SaveName);
