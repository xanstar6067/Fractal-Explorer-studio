using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>IFS palette editor with its own library and live IFS preview.</summary>
public sealed class Ifs3DPaletteWindow : Fractal3DPaletteWindow
{
    public Ifs3DPaletteWindow(
        Ifs3DPaletteManager manager,
        Fractal3DPalette current,
        Func<Fractal3DPalette, int, int, CancellationToken, Task<BitmapSource>>? renderPreview = null)
        : base(manager, current, renderPreview)
    {
        Title = "Палитры конструктора объёмных IFS";
    }
}
