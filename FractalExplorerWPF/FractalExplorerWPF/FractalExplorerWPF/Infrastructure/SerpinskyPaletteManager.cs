using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SerpinskyPaletteManager
{
    private readonly CustomPaletteFile _file = new("serpinsky_palettes.json");

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
        // Встроенные палитры остаются доступны, даже если файл пользовательских повреждён.
        Palettes.AddRange(_file.Load(json => CustomPaletteFile.ParseList<SerpinskyPalette>(json)
            .Where(palette => !palette.IsBuiltIn)));
        ActivePalette = Palettes[0];
    }

    public string? LoadWarning => _file.LoadWarning;

    /// <summary>После неудачной загрузки ничего не пишет: окно палитр предупредило при открытии.</summary>
    public void SaveCustomPalettes() => _file.SaveIfLoaded(Palettes.Where(palette => !palette.IsBuiltIn));

    private static SerpinskyPalette BuiltIn(string name, Color fractal, Color background) =>
        new()
        {
            Name = name,
            FractalColor = fractal,
            BackgroundColor = background,
            IsBuiltIn = true
        };
}
