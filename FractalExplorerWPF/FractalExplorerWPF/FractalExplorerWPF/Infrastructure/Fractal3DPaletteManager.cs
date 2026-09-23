using System.IO;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Библиотека палитр девяти трёхмерных фракталов с дистанционной оценкой. IFS использует отдельный
/// <see cref="Ifs3DPaletteManager"/> с другим файлом пользовательских палитр.
/// </summary>
public class Fractal3DPaletteManager
{
    private readonly string _fileName;

    public Fractal3DPaletteManager()
        : this("custom_palettes_fractal3d.json", Fractal3DPalettes.All)
    {
    }

    protected Fractal3DPaletteManager(string fileName, IEnumerable<Fractal3DPalette> builtIns)
    {
        _fileName = fileName;
        Palettes = [.. builtIns.Select(BuiltInCopy)];
        try { LoadCustomPalettes(); } catch { }
    }

    public List<Fractal3DPalette> Palettes { get; }

    /// <summary>Палитра с таким именем или <c>null</c>, если её в библиотеке нет.</summary>
    public Fractal3DPalette? Find(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : Palettes.FirstOrDefault(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void SaveCustomPalettes()
    {
        string path = AppPaths.EnsureDirectoryFor(AppPaths.GetPaletteFile(_fileName));
        File.WriteAllText(path, JsonSerializer.Serialize(
            Palettes.Where(palette => !palette.IsBuiltIn), JsonOptionsFactory.Create()));
    }

    private void LoadCustomPalettes()
    {
        string path = AppPaths.GetPaletteFile(_fileName);
        if (!File.Exists(path)) return;
        List<Fractal3DPalette>? custom = JsonSerializer.Deserialize<List<Fractal3DPalette>>(
            File.ReadAllText(path), JsonOptionsFactory.Create());
        if (custom is null) return;
        Palettes.AddRange(custom
            .Where(palette => palette.Colors.Count > 0)
            .Select(palette =>
            {
                palette.IsBuiltIn = false;
                return palette;
            }));
    }

    /// <summary>Копия встроенной палитры: библиотека своя у каждого окна и правится независимо.</summary>
    private static Fractal3DPalette BuiltInCopy(Fractal3DPalette palette)
    {
        Fractal3DPalette copy = palette.Clone();
        copy.IsBuiltIn = true;
        return copy;
    }
}
