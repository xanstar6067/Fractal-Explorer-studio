using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SandpileSaveStore() : FractalSaveStore<SandpileState>("Sandpile", s => s.SaveName,
    s => { _ = new SandpileSimulation(s); return true; });
