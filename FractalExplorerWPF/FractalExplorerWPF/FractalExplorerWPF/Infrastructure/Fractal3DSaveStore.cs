using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Сохранения окна трёхмерных фракталов — отдельный каталог на каждый вид.</summary>
public sealed class Fractal3DSaveStore(Fractal3DKind kind) : FractalSaveStore<Fractal3DState>(
    Fractal3DCatalog.GetDefinition(kind).SaveCategory, state => state.SaveName);
