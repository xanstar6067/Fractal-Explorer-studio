using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class InverseCollatzPaletteManager
{
    private const string FileName = "custom_palettes_inverse_collatz.json";

    public List<InverseCollatzPalette> Palettes { get; } =
    [
        BuiltIn("Глубина Коллатца", [Colors.DarkBlue, Colors.Cyan, Colors.Yellow, Colors.Red]),
        BuiltIn("Неоновое дерево", [Rgb(4, 4, 24), Colors.Blue, Colors.Magenta, Colors.Cyan, Colors.White]),
        BuiltIn("Золотой рост", [Rgb(10, 4, 0), Colors.DarkRed, Colors.OrangeRed, Colors.Gold, Colors.LightYellow]),
        BuiltIn("Ледяные ветви", [Rgb(0, 4, 18), Colors.DarkBlue, Colors.DeepSkyBlue, Colors.Aquamarine, Colors.White]),
        BuiltIn("Лес уровней", [Rgb(0, 12, 4), Colors.DarkGreen, Colors.LimeGreen, Colors.GreenYellow, Colors.White]),
        BuiltIn("Монохромная структура", [Colors.Black, Colors.DimGray, Colors.White]),
        BuiltIn("Кольца глубины", [Colors.DarkBlue, Colors.Cyan, Colors.White, Colors.Magenta, Colors.DarkBlue],
            InverseCollatzPaletteMapping.RepeatByLevel, 10),
        BuiltIn("Дискретные уровни", [Colors.Cyan, Colors.Yellow, Colors.OrangeRed, Colors.Magenta],
            InverseCollatzPaletteMapping.RepeatByLevel, 8, gradient: false)
    ];

    public InverseCollatzPalette ActivePalette { get; set; }

    public InverseCollatzPaletteManager()
    {
        try { LoadCustomPalettes(); } catch { }
        ActivePalette = Palettes[0];
    }

    public void SaveCustomPalettes()
    {
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetPaletteFile(FileName));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            Palettes.Where(palette => !palette.IsBuiltIn), JsonOptionsFactory.Create()));
        File.Move(temporary, path, true);
    }

    private void LoadCustomPalettes()
    {
        string path = AppPaths.GetPaletteFile(FileName);
        if (!File.Exists(path)) return;
        List<InverseCollatzPalette>? custom = JsonSerializer.Deserialize<List<InverseCollatzPalette>>(
            File.ReadAllText(path), JsonOptionsFactory.Create());
        if (custom is not null)
            Palettes.AddRange(custom.Where(palette => !palette.IsBuiltIn && palette.Colors.Count > 0));
    }

    private static InverseCollatzPalette BuiltIn(string name, List<Color> colors,
        InverseCollatzPaletteMapping mapping = InverseCollatzPaletteMapping.StretchToDepth,
        int levelsPerCycle = 12, bool gradient = true) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true,
        IsGradient = gradient,
        Gamma = 1,
        Mapping = mapping,
        LevelsPerCycle = levelsPerCycle
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
}
