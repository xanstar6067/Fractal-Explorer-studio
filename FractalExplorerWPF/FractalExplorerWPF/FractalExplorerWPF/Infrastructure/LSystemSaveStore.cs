using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class LSystemSaveStore() : FractalSaveStore<LSystemState>("LSystem", state => state.SaveName);
