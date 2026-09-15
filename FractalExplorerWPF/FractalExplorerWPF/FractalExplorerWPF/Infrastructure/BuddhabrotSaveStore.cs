using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class BuddhabrotSaveStore() : FractalSaveStore<BuddhabrotState>("Buddhabrot", state => state.SaveName);
