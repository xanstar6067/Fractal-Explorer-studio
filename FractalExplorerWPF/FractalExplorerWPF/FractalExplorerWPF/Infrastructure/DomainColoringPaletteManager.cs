using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class DomainColoringPaletteManager
{
    private readonly CustomPaletteFile _file = new("custom_palettes_domain_coloring.json");

    public List<DomainColoringPalette> Palettes { get; } =
    [
        BuiltIn("Классическая (HSV)", HueWheel(1, 1)),
        BuiltIn("Пастельное колесо", HueWheel(0.55, 1)),
        BuiltIn("Термография", [Rgb(0, 0, 40), Colors.DarkBlue, Colors.Magenta, Colors.Red,
            Colors.Orange, Colors.Yellow, Colors.White]),
        BuiltIn("Неоновый цикл", [Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red,
            Colors.Orange, Colors.Yellow, Colors.Lime, Colors.Cyan]),
        BuiltIn("Монохромная фаза", [Colors.Black, Colors.White, Colors.Black]),
        BuiltIn("Дискретный спектр", HueWheel(0.9, 0.95, 8), gradient: false)
    ];

    public DomainColoringPalette ActivePalette { get; set; }

    public DomainColoringPaletteManager()
    {
        Palettes.AddRange(_file.Load(json => CustomPaletteFile.ParseList<DomainColoringPalette>(json)
            .Where(palette => !palette.IsBuiltIn && palette.Colors.Count > 0)));
        ActivePalette = Palettes[0];
    }

    public string? LoadWarning => _file.LoadWarning;

    /// <summary>После неудачной загрузки ничего не пишет: окно палитр предупредило при открытии.</summary>
    public void SaveCustomPalettes() => _file.SaveIfLoaded(Palettes.Where(palette => !palette.IsBuiltIn));

    private static DomainColoringPalette BuiltIn(string name, List<Color> colors, bool gradient = true) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true,
        IsGradient = gradient,
        Gamma = 1
    };

    private static List<Color> HueWheel(double saturation, double value, int steps = 12)
    {
        var colors = new List<Color>(steps);
        for (int index = 0; index < steps; index++)
            colors.Add(FromHsv(index * 360.0 / steps, saturation, value));
        return colors;
    }

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double sector = hue / 60;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        (double red, double green, double blue) = sector switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double match = value - chroma;
        return Color.FromRgb((byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255), (byte)Math.Round((blue + match) * 255));
    }
}
