using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SnowCrystalSaveStore() : FractalSaveStore<SnowCrystalState>("SnowCrystal", state => state.SaveName);
