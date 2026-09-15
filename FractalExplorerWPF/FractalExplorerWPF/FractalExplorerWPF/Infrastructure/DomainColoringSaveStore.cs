using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class DomainColoringSaveStore() : FractalSaveStore<DomainColoringState>("DomainColoring", state => state.SaveName);
