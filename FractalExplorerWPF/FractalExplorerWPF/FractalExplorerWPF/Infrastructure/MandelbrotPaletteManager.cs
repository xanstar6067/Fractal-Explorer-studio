using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public class MandelbrotPaletteManager
{
    private const string DefaultFileName = "custom_palettes_mandelbrot.json";
    private readonly string _fileName;

    public List<MandelbrotPalette> Palettes { get; } =
    [
        BuiltIn("Стандартный серый", [Colors.Black, Colors.White], 800,
            kind: MandelbrotPaletteKind.AlgorithmicGrayscale),
        BuiltIn("Ультрафиолет", [Colors.Black, Colors.DarkViolet, Colors.Violet, Colors.White], 1000, 1.2),
        BuiltIn("Огонь", [Colors.Black, Colors.DarkRed, Colors.Red, Colors.Orange, Colors.Yellow, Colors.White], 400, 0.9),
        BuiltIn("Лёд", [Colors.Black, Colors.DarkBlue, Colors.Blue, Colors.Cyan, Colors.White], 500, 1.2),
        BuiltIn("Огонь и лед", [Colors.Black, Colors.DarkBlue, Colors.Cyan, Colors.White, Colors.Yellow, Colors.Red, Colors.DarkRed], 700),
        BuiltIn("Психоделика", [Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta], 6, gradient: false),
        BuiltIn("Черно-белый", [Colors.Black, Colors.White], 500),
        BuiltIn("Зеленый", [Colors.Black, Rgb(0,128,0), Rgb(0,204,0), Rgb(0,234,0), Rgb(60,255,60), Rgb(145,255,145), Rgb(213,255,213), Colors.White], 120),
        BuiltIn("Сепия", [Rgb(20,10,0), Rgb(255,240,192)], 500),
        BuiltIn("Белый ультрафиолет", [Colors.White, Colors.Lavender, Colors.Violet, Colors.DarkViolet, Colors.Indigo, Colors.Black], 400, 1.2),
        BuiltIn("Белый огонь", [Colors.White, Colors.LightYellow, Colors.Yellow, Colors.Orange, Colors.Red, Colors.DarkRed, Colors.Maroon], 400, 0.9),
        BuiltIn("Белый лед", [Colors.White, Colors.LightCyan, Colors.Cyan, Colors.DeepSkyBlue, Colors.Blue, Colors.DarkBlue, Colors.Navy], 500, 1.2),
        BuiltIn("Белый зеленый", [Colors.White, Rgb(230,255,230), Rgb(180,255,180), Rgb(120,255,120), Rgb(60,220,60), Rgb(0,180,0), Rgb(0,120,0), Colors.Black], 420),
        BuiltIn("Бело-черный", [Colors.White, Colors.Black], 500),
        BuiltIn("Закат", [Colors.Black, Rgb(25,25,112), Rgb(75,0,130), Rgb(139,0,139), Rgb(220,20,60), Rgb(255,140,0), Rgb(255,215,0), Colors.White], 600, 1.1),
        BuiltIn("Океан", [Colors.Black, Rgb(0,20,40), Rgb(0,50,80), Rgb(0,100,150), Rgb(0,150,200), Rgb(100,200,255), Rgb(200,240,255), Colors.White], 450),
        BuiltIn("Золото", [Colors.Black, Rgb(85,65,0), Rgb(139,115,0), Rgb(205,173,0), Rgb(255,215,0), Rgb(255,235,128), Rgb(255,248,220), Colors.White], 300, 0.8),
        BuiltIn("Медь", [Colors.Black, Rgb(72,61,20), Rgb(138,54,15), Rgb(184,115,51), Rgb(205,127,50), Rgb(240,147,43), Rgb(255,200,124), Colors.White], 280, 0.9),
        BuiltIn("Неон", [Colors.Black, Rgb(75,0,75), Colors.Magenta, Colors.Cyan, Colors.Lime, Colors.Yellow, Rgb(255,100,255), Colors.White], 350, 1.3),
        BuiltIn("Радуга", [Colors.Black, Rgb(148,0,211), Rgb(75,0,130), Colors.Blue, Colors.Lime, Colors.Yellow, Rgb(255,127,0), Colors.Red, Colors.White], 350),
        BuiltIn("Аметист", [Colors.Black, Rgb(25,25,112), Rgb(72,61,139), Rgb(123,104,238), Rgb(147,112,219), Rgb(221,160,221), Rgb(238,203,238), Colors.White], 520, 1.1),
        BuiltIn("Лес", [Colors.Black, Rgb(0,39,0), Rgb(0,69,0), Rgb(34,139,34), Rgb(50,205,50), Rgb(124,252,0), Rgb(173,255,47), Colors.White], 380),
        BuiltIn("Космос", [Colors.Black, Rgb(25,25,112), Rgb(72,61,139), Rgb(138,43,226), Rgb(255,20,147), Rgb(255,105,180), Rgb(255,182,193), Colors.White], 650, 1.2),
        BuiltIn("Бирюза", [Colors.Black, Rgb(0,100,100), Rgb(0,139,139), Rgb(72,209,204), Rgb(175,238,238), Rgb(224,255,255), Colors.White], 420),
        BuiltIn("Лава", [Colors.Black, Rgb(139,0,0), Rgb(205,0,0), Rgb(255,69,0), Rgb(255,140,0), Rgb(255,215,0), Rgb(255,255,224), Colors.White], 250, 0.7),
        BuiltIn("Монохром синий", [Colors.Black, Rgb(0,0,139), Rgb(0,0,205), Rgb(65,105,225), Rgb(135,206,235), Rgb(176,224,230), Colors.White], 480),
        BuiltIn("Монохром красный", [Colors.Black, Rgb(139,0,0), Rgb(205,0,0), Rgb(220,20,60), Rgb(255,105,180), Rgb(255,182,193), Colors.White], 320)
    ];

    public MandelbrotPalette ActivePalette { get; set; }

    public MandelbrotPaletteManager() : this(DefaultFileName)
    {
    }

    protected MandelbrotPaletteManager(string fileName, int? builtInColorPeriod = null)
    {
        _fileName = fileName;
        if (builtInColorPeriod is not null)
        {
            foreach (MandelbrotPalette palette in Palettes.Where(palette => palette.IsBuiltIn))
                palette.ColorPeriod = builtInColorPeriod.Value;
        }

        try { LoadCustomPalettes(); } catch { }
        ActivePalette = Palettes[0];
    }

    public void SaveCustomPalettes()
    {
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetPaletteFile(_fileName));
        File.WriteAllText(path, JsonSerializer.Serialize(
            Palettes.Where(p => !p.IsBuiltIn), JsonOptionsFactory.Create()));
    }

    private void LoadCustomPalettes()
    {
        string path = AppPaths.GetPaletteFile(_fileName);
        if (!File.Exists(path)) return;
        List<MandelbrotPalette>? custom = JsonSerializer.Deserialize<List<MandelbrotPalette>>(
            File.ReadAllText(path), JsonOptionsFactory.Create());
        if (custom is not null) Palettes.AddRange(custom.Where(p => !p.IsBuiltIn));
    }

    private static MandelbrotPalette BuiltIn(
        string name, List<Color> colors, int period = 500, double gamma = 1, bool gradient = true,
        MandelbrotPaletteKind kind = MandelbrotPaletteKind.ColorSequence) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true,
        IsGradient = gradient,
        ColorPeriod = period,
        Gamma = gamma,
        InteriorColor = Colors.Black,
        Kind = kind
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
}
