using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class IfsSaveStore() : FractalSaveStore<IfsState>("IFS", state => state.SaveName);
