using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class BuddhabrotPaletteManager
{
    private string FilePath => AppPaths.GetPaletteFile("buddhabrot_palettes.json");
    public List<BuddhabrotColorPalette> Palettes { get; } = CreateBuiltIns();
    public BuddhabrotColorPalette ActivePalette { get; set; }

    public BuddhabrotPaletteManager()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                List<BuddhabrotColorPalette>? custom = JsonSerializer.Deserialize<List<BuddhabrotColorPalette>>(File.ReadAllText(FilePath), JsonOptionsFactory.Create());
                if (custom is not null) Palettes.AddRange(custom.Select(p => { p.IsBuiltIn = false; return p; }));
            }
        }
        catch { }
        ActivePalette = Palettes[0];
    }

    public void Save()
    {
        File.WriteAllText(AppPaths.EnsureDirectoryFor(FilePath), JsonSerializer.Serialize(Palettes.Where(p => !p.IsBuiltIn), JsonOptionsFactory.Create()));
    }

    public static Color Evaluate(BuddhabrotColorPalette palette, double normalized, int renderIterations)
    {
        IReadOnlyList<Color> colors = palette.Colors.Count == 0 ? [Colors.Black, Colors.White] : palette.Colors;
        double mapped = palette.ColoringMode switch
        {
            BuddhabrotColoringMode.Linear => Math.Clamp(normalized, 0, 1),
            BuddhabrotColoringMode.Sqrt => Math.Sqrt(Math.Clamp(normalized, 0, 1)),
            _ => Math.Log(1 + Math.Clamp(normalized, 0, 1) * 15) / Math.Log(16)
        };
        if (colors.Count == 1) return Gamma(colors[0], palette.Gamma);
        if (!palette.IsGradient)
        {
            int cycle = palette.AlignWithRenderIterations ? Math.Max(2, renderIterations) : Math.Max(2, palette.MaxColorIterations);
            mapped = mapped >= 1 ? 1 : Math.Floor(mapped * cycle) / (cycle - 1d);
        }
        double position = Math.Clamp(mapped, 0, 1) * (colors.Count - 1);
        int left = Math.Min((int)Math.Floor(position), colors.Count - 1), right = Math.Min(left + 1, colors.Count - 1);
        double f = position - left; Color a = Gamma(colors[left], palette.Gamma), b = Gamma(colors[right], palette.Gamma);
        return Color.FromArgb(255, Lerp(a.R, b.R, f), Lerp(a.G, b.G, f), Lerp(a.B, b.B, f));
    }

    private static List<BuddhabrotColorPalette> CreateBuiltIns() =>
    [
        BuiltIn("Стандартный Ч/Б", [Colors.Black, Colors.White], BuddhabrotColoringMode.Linear),
        BuiltIn("Классический Буддаброт", [Colors.Black, Colors.DarkBlue, Colors.Cyan, Colors.White], BuddhabrotColoringMode.Logarithmic),
        BuiltIn("Стандартный серый", [Colors.Black, Colors.DimGray, Colors.Silver, Colors.White], BuddhabrotColoringMode.Linear),
        BuiltIn("Ультрафиолет", [Colors.Black, Colors.DarkViolet, Colors.Violet, Colors.White], BuddhabrotColoringMode.Logarithmic, 520, 1.15),
        BuiltIn("Огонь", [Colors.Black, Colors.DarkRed, Colors.OrangeRed, Colors.Gold, Colors.White], BuddhabrotColoringMode.Logarithmic, 420, .95),
        BuiltIn("Лёд", [Colors.Black, Colors.DarkBlue, Colors.Blue, Colors.Cyan, Colors.White], BuddhabrotColoringMode.Sqrt, 560, 1.1),
        BuiltIn("Огонь и лед", [Colors.Black, Colors.DarkBlue, Colors.Cyan, Colors.White, Colors.Yellow, Colors.Red, Colors.DarkRed], BuddhabrotColoringMode.Logarithmic, 760),
        BuiltIn("Психоделика", [Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta], BuddhabrotColoringMode.Linear, 24, 1, false),
        BuiltIn("Черно-белый", [Colors.Black, Colors.White], BuddhabrotColoringMode.Linear),
        BuiltIn("Зеленый", [Colors.Black, C("008000"), C("00CC00"), C("3CFF3C"), C("D5FFD5"), Colors.White], BuddhabrotColoringMode.Logarithmic, 320),
        BuiltIn("Сепия", [C("140A00"), C("FFF0C0")], BuddhabrotColoringMode.Linear, 460),
        BuiltIn("Белый ультрафиолет", [Colors.White, Colors.Lavender, Colors.Violet, Colors.DarkViolet, Colors.Indigo, Colors.Black], BuddhabrotColoringMode.Logarithmic, 520, 1.12),
        BuiltIn("Белый огонь", [Colors.White, Colors.LightYellow, Colors.Yellow, Colors.Orange, Colors.Red, Colors.DarkRed, Colors.Maroon], BuddhabrotColoringMode.Logarithmic, 420, .95),
        BuiltIn("Белый лед", [Colors.White, Colors.LightCyan, Colors.Cyan, Colors.DeepSkyBlue, Colors.Blue, Colors.DarkBlue, Colors.Navy], BuddhabrotColoringMode.Sqrt, 560, 1.1),
        BuiltIn("Белый зеленый", [Colors.White, C("B4FFB4"), C("3CDC3C"), C("007800"), Colors.Black], BuddhabrotColoringMode.Logarithmic, 340),
        BuiltIn("Бело-черный", [Colors.White, Colors.Black], BuddhabrotColoringMode.Linear),
        BuiltIn("Закат", [Colors.Black, C("191970"), C("4B0082"), C("8B008B"), C("DC143C"), C("FF8C00"), C("FFD700"), Colors.White], BuddhabrotColoringMode.Logarithmic, 620, 1.08),
        BuiltIn("Океан", [Colors.Black, C("001428"), C("003250"), C("006496"), C("0096C8"), C("64C8FF"), C("C8F0FF"), Colors.White], BuddhabrotColoringMode.Sqrt, 540),
        BuiltIn("Золото", [Colors.Black, C("554100"), C("8B7300"), C("CDAD00"), C("FFD700"), C("FFF8DC"), Colors.White], BuddhabrotColoringMode.Logarithmic, 380, .9),
        BuiltIn("Медь", [Colors.Black, C("483D14"), C("8A360F"), C("B87333"), C("F0932B"), C("FFC87C"), Colors.White], BuddhabrotColoringMode.Linear, 360, .95),
        BuiltIn("Неон", [Colors.Black, C("4B004B"), Colors.Magenta, Colors.Cyan, Colors.Lime, Colors.Yellow, C("FF64FF"), Colors.White], BuddhabrotColoringMode.Sqrt, 640, 1.25),
        BuiltIn("Радуга", [Colors.Black, C("9400D3"), Colors.Indigo, Colors.Blue, Colors.Lime, Colors.Yellow, C("FF7F00"), Colors.Red, Colors.White], BuddhabrotColoringMode.Sqrt, 660, 1.05),
        BuiltIn("Аметист", [Colors.Black, C("191970"), C("483D8B"), C("7B68EE"), C("DDA0DD"), C("EECBEE"), Colors.White], BuddhabrotColoringMode.Logarithmic, 620, 1.12),
        BuiltIn("Лес", [Colors.Black, C("002700"), C("004500"), C("228B22"), C("32CD32"), C("ADFF2F"), Colors.White], BuddhabrotColoringMode.Logarithmic),
        BuiltIn("Космос", [Colors.Black, C("191970"), C("483D8B"), C("8A2BE2"), C("FF1493"), C("FF69B4"), C("FFB6C1"), Colors.White], BuddhabrotColoringMode.Logarithmic, 780, 1.18),
        BuiltIn("Бирюза", [Colors.Black, C("006464"), C("008B8B"), C("48D1CC"), C("AFEEEE"), C("E0FFFF"), Colors.White], BuddhabrotColoringMode.Sqrt, 520),
        BuiltIn("Лава", [Colors.Black, C("8B0000"), C("CD0000"), C("FF4500"), C("FF8C00"), C("FFD700"), C("FFFFE0"), Colors.White], BuddhabrotColoringMode.Linear, 360, .82),
        BuiltIn("Монохром синий", [Colors.Black, C("00008B"), C("0000CD"), C("4169E1"), C("87CEEB"), C("B0E0E6"), Colors.White], BuddhabrotColoringMode.Sqrt, 600),
        BuiltIn("Монохром красный", [Colors.Black, C("8B0000"), C("CD0000"), C("DC143C"), C("FF69B4"), C("FFB6C1"), Colors.White], BuddhabrotColoringMode.Sqrt, 560)
    ];

    private static BuddhabrotColorPalette BuiltIn(string name, List<Color> colors, BuddhabrotColoringMode mode, int steps = 500, double gamma = 1, bool gradient = true) => new()
    { Name = name, Colors = colors, ColoringMode = mode, MaxColorIterations = steps, Gamma = gamma, IsGradient = gradient, IsBuiltIn = true };
    private static Color C(string rgb) => Color.FromRgb(Convert.ToByte(rgb[..2], 16), Convert.ToByte(rgb[2..4], 16), Convert.ToByte(rgb[4..], 16));
    private static Color Gamma(Color c, double gamma) { double g = 1 / Math.Clamp(gamma, .1, 5); return Color.FromArgb(c.A, (byte)(255 * Math.Pow(c.R / 255d, g)), (byte)(255 * Math.Pow(c.G / 255d, g)), (byte)(255 * Math.Pow(c.B / 255d, g))); }
    private static byte Lerp(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
}
