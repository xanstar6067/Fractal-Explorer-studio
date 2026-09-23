using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Independent IFS palette library; edits never touch the other 3D modes.</summary>
public sealed class Ifs3DPaletteManager : Fractal3DPaletteManager
{
    public Ifs3DPaletteManager()
        : base("custom_palettes_ifs3d.json", BuiltIns())
    {
    }

    private static IEnumerable<Fractal3DPalette> BuiltIns()
    {
        foreach (Fractal3DPalette palette in Fractal3DPalettes.All)
            yield return palette;
        foreach (Ifs3DPreset preset in Ifs3DPresets.All)
            yield return Fractal3DPalette.FromPair(
                $"IFS: {preset.Name}", preset.State.PointColor, Color.FromRgb(235, 255, 255));
    }
}
