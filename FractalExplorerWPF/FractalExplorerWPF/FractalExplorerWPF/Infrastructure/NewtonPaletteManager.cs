using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class NewtonPaletteManager
{
    private const string FileName = "newton_palettes.json";

    public List<NewtonColorPalette> Palettes { get; } =
    [
        BuiltIn("Оттенки серого", [Colors.White, Colors.LightGray, Colors.DarkGray], true,
            NewtonPaletteExpansionMode.LinearRamp),
        BuiltIn("Классика", [], false, NewtonPaletteExpansionMode.Harmonic),
        BuiltIn("Классика — градиент", [], true, NewtonPaletteExpansionMode.Harmonic),
        BuiltIn("Чёрно-белый", [Colors.White], false, NewtonPaletteExpansionMode.RepeatFirst),
        BuiltIn("Пастель", [Rgb(255,182,193), Rgb(173,216,230), Rgb(189,252,201), Rgb(253,253,150)], false,
            NewtonPaletteExpansionMode.CyclicRamp, Rgb(40,40,40)),
        BuiltIn("Контраст", [Colors.Red, Colors.Yellow, Colors.Blue], false,
            NewtonPaletteExpansionMode.Cycle),
        BuiltIn("Огонь", [Rgb(200,0,0), Rgb(255,100,0), Rgb(255,255,100)], true,
            NewtonPaletteExpansionMode.LinearRamp),
        BuiltIn("Психоделика", [Rgb(10,0,20), Colors.Magenta, Colors.Cyan], true,
            NewtonPaletteExpansionMode.CyclicRamp),
        BuiltIn("Огонь и лёд", [Rgb(255,100,0), Rgb(0,100,255), Rgb(255,200,0), Rgb(0,200,255)], true,
            NewtonPaletteExpansionMode.Cycle)
    ];

    public NewtonColorPalette ActivePalette { get; set; }

    public NewtonPaletteManager()
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

    public static List<Color> AdjustColors(NewtonColorPalette palette, int requiredCount)
    {
        if (requiredCount <= 0) return [];
        if (palette.RootColors.Count == 0) return GenerateHarmonicColors(requiredCount);
        if (palette.RootColors.Count == requiredCount) return [.. palette.RootColors];

        return palette.ExpansionMode switch
        {
            NewtonPaletteExpansionMode.Harmonic => GenerateHarmonicColors(requiredCount),
            NewtonPaletteExpansionMode.CyclicRamp => CreateCyclicRamp(palette.RootColors, requiredCount),
            NewtonPaletteExpansionMode.Cycle => CreateCycle(palette.RootColors, requiredCount),
            NewtonPaletteExpansionMode.RepeatFirst => Enumerable.Repeat(palette.RootColors[0], requiredCount).ToList(),
            _ => CreateLinearRamp(palette.RootColors, requiredCount)
        };
    }

    public static List<Color> GenerateHarmonicColors(int count)
    {
        var colors = new List<Color>();
        for (int index = 0; index < count; index++)
            colors.Add(FromHsl(360d * index / count, 0.85, 0.6));
        return colors;
    }

    /// <summary>
    /// Перечитывает пользовательские палитры из файла. Файл общий для окна бассейнов Ньютона и всех
    /// окон бассейнов, а каждое окно держит свой менеджер: без этого сохранение из давно открытого
    /// окна перезаписало бы файл старым списком и стёрло палитры, созданные в другом окне.
    /// Активная пользовательская палитра подменяется одноимённой из файла.
    /// </summary>
    public void ReloadCustomPalettes()
    {
        List<NewtonColorPalette> custom;
        try { custom = ReadCustomPalettes(); }
        catch { return; }
        string? activeName = !ActivePalette.IsBuiltIn && Palettes.Contains(ActivePalette) ? ActivePalette.Name : null;
        Palettes.RemoveAll(palette => !palette.IsBuiltIn);
        Palettes.AddRange(custom);
        if (activeName is not null)
            ActivePalette = Palettes.FirstOrDefault(palette => palette.Name == activeName) ?? ActivePalette; // Удалённая в другом окне остаётся текущей.
    }

    private void LoadCustomPalettes() => Palettes.AddRange(ReadCustomPalettes());

    private static List<NewtonColorPalette> ReadCustomPalettes()
    {
        string path = AppPaths.GetPaletteFile(FileName);
        if (!File.Exists(path)) return [];
        List<NewtonColorPalette>? custom = JsonSerializer.Deserialize<List<NewtonColorPalette>>(
            File.ReadAllText(path), JsonOptionsFactory.Create());
        return custom?.Where(palette => palette is not null && !palette.IsBuiltIn).ToList() ?? [];
    }

    private static List<Color> CreateLinearRamp(IReadOnlyList<Color> anchors, int count)
    {
        if (anchors.Count == 1) return Enumerable.Repeat(anchors[0], count).ToList();
        if (count == 1) return [anchors[0]];

        var colors = new List<Color>(count);
        for (int index = 0; index < count; index++)
        {
            double position = index * (anchors.Count - 1d) / (count - 1d);
            int left = Math.Min((int)Math.Floor(position), anchors.Count - 2);
            colors.Add(Lerp(anchors[left], anchors[left + 1], position - left));
        }
        return colors;
    }

    private static List<Color> CreateCyclicRamp(IReadOnlyList<Color> anchors, int count)
    {
        if (anchors.Count == 1) return Enumerable.Repeat(anchors[0], count).ToList();

        var colors = new List<Color>(count);
        for (int index = 0; index < count; index++)
        {
            double position = index * anchors.Count / (double)count;
            int left = (int)Math.Floor(position) % anchors.Count;
            int right = (left + 1) % anchors.Count;
            colors.Add(Lerp(anchors[left], anchors[right], position - Math.Floor(position)));
        }
        return colors;
    }

    private static List<Color> CreateCycle(IReadOnlyList<Color> anchors, int count) =>
        Enumerable.Range(0, count).Select(index => anchors[index % anchors.Count]).ToList();

    private static Color Lerp(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(start.A + (end.A - start.A) * amount),
            (byte)Math.Round(start.R + (end.R - start.R) * amount),
            (byte)Math.Round(start.G + (end.G - start.G) * amount),
            (byte)Math.Round(start.B + (end.B - start.B) * amount));
    }

    private static NewtonColorPalette BuiltIn(
        string name,
        List<Color> colors,
        bool gradient,
        NewtonPaletteExpansionMode expansionMode,
        Color? background = null) => new()
    {
        Name = name,
        RootColors = colors,
        BackgroundColor = background ?? Colors.Black,
        IsGradient = gradient,
        IsBuiltIn = true,
        ExpansionMode = expansionMode
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = hue / 60;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        (double r, double g, double b) = sector switch
        {
            < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        double m = lightness - chroma / 2;
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
