using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class InverseCollatzSaveStore() : FractalSaveStore<InverseCollatzState>("InverseCollatzTree", state => state.SaveName);
