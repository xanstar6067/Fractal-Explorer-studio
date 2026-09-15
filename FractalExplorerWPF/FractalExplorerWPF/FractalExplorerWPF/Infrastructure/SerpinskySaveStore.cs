using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SerpinskySaveStore() : FractalSaveStore<SerpinskySaveState>("Serpinsky", state => state.SaveName);
