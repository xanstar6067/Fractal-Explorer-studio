using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class GrayScottPaletteManager
{
    private const string FileName = "custom_palettes_gray_scott.json";

    public List<GrayScottPalette> Palettes { get; } =
    [
        BuiltIn(GrayScottPalettes.Coral),
        BuiltIn("Монохром", [Colors.Black, Colors.White]),
        BuiltIn("Чернила", [Rgb(248,250,252), Rgb(191,219,254), Rgb(59,130,246), Rgb(30,64,175), Rgb(15,23,42)]),
        BuiltIn("Тепловая карта", [Colors.Black, Rgb(127,29,29), Rgb(239,68,68), Rgb(250,204,21), Colors.White]),
        BuiltIn("Биолюминесценция", [Rgb(2,6,23), Rgb(49,46,129), Rgb(126,34,206), Rgb(236,72,153), Rgb(34,211,238), Colors.White]),
        BuiltIn("Виридис", [Rgb(68,1,84), Rgb(59,82,139), Rgb(33,145,140), Rgb(94,201,98), Rgb(253,231,37)]),
        BuiltIn("Сигнальная", [Colors.Black, Rgb(0,255,0), Rgb(255,255,0), Rgb(255,0,0), Colors.White]),
        BuiltIn("Медь и бирюза", [Rgb(7,19,20), Rgb(14,116,144), Rgb(94,234,212), Rgb(254,215,170), Rgb(194,65,12)])
    ];

    public GrayScottPalette ActivePalette { get; set; }

    public GrayScottPaletteManager()
    {
        try { LoadCustomPalettes(); } catch { }
        ActivePalette = Palettes[0];
    }

    public void SaveCustomPalettes()
    {
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetPaletteFile(FileName));
        File.WriteAllText(path, JsonSerializer.Serialize(
            Palettes.Where(palette => !palette.IsBuiltIn), JsonOptionsFactory.Create()));
    }

    private void LoadCustomPalettes()
    {
        string path = AppPaths.GetPaletteFile(FileName);
        if (!File.Exists(path)) return;
        List<GrayScottPalette>? custom = JsonSerializer.Deserialize<List<GrayScottPalette>>(
            File.ReadAllText(path), JsonOptionsFactory.Create());
        if (custom is not null)
            Palettes.AddRange(custom.Where(palette => !palette.IsBuiltIn && palette.Colors.Count > 0));
    }

    private static GrayScottPalette BuiltIn(GrayScottPalette palette)
    {
        palette.IsBuiltIn = true;
        return palette;
    }

    private static GrayScottPalette BuiltIn(string name, List<Color> colors) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
}
