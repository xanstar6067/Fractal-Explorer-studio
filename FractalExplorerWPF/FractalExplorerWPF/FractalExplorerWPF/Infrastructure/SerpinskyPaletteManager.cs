using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SerpinskyPaletteManager
{
    private const string FileName = "serpinsky_palettes.json";

    public List<SerpinskyPalette> Palettes { get; } =
    [
        BuiltIn("Классический Ч/Б", Colors.Black, Colors.White),
        BuiltIn("Инверсия", Colors.White, Colors.Black),
        BuiltIn("Оттенки серого", Color.FromRgb(50, 50, 50), Colors.White),
        BuiltIn("Огонь и ночь", Colors.OrangeRed, Color.FromRgb(10, 0, 20)),
        BuiltIn("Глубокий океан", Colors.Aqua, Colors.DarkSlateBlue)
    ];

    public SerpinskyPalette ActivePalette { get; set; }

    public SerpinskyPaletteManager()
    {
        try
        {
            LoadCustomPalettes();
        }
        catch
        {
            // Built-in palettes remain available if a custom palette file is damaged.
        }
        ActivePalette = Palettes[0];
    }

    public void SaveCustomPalettes()
    {
        string filePath = AppPaths.EnsureDirectoryFor(AppPaths.GetPaletteFile(FileName));
        File.WriteAllText(
            filePath,
            JsonSerializer.Serialize(
                Palettes.Where(palette => !palette.IsBuiltIn),
                JsonOptionsFactory.Create()));
    }

    private void LoadCustomPalettes()
    {
        string filePath = AppPaths.GetPaletteFile(FileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        string json = File.ReadAllText(filePath);
        List<SerpinskyPalette>? custom = JsonSerializer.Deserialize<List<SerpinskyPalette>>(
            json,
            JsonOptionsFactory.Create());
        if (custom is not null)
        {
            Palettes.AddRange(custom.Where(palette => !palette.IsBuiltIn));
        }
    }

    private static SerpinskyPalette BuiltIn(string name, Color fractal, Color background) =>
        new()
        {
            Name = name,
            FractalColor = fractal,
            BackgroundColor = background,
            IsBuiltIn = true
        };
}
