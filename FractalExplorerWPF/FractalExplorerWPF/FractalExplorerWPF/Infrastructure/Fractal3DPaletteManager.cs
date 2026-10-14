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
    private readonly CustomPaletteFile _file;

    public Fractal3DPaletteManager()
        : this("custom_palettes_fractal3d.json", Fractal3DPalettes.All)
    {
    }

    protected Fractal3DPaletteManager(string fileName, IEnumerable<Fractal3DPalette> builtIns)
    {
        _file = new CustomPaletteFile(fileName);
        Palettes = [.. builtIns.Select(BuiltInCopy)];
        Palettes.AddRange(_file.Load(json => CustomPaletteFile.ParseList<Fractal3DPalette>(json)
            .Where(palette => palette.Colors.Count > 0)
            .Select(palette =>
            {
                palette.IsBuiltIn = false;
                return palette;
            })));
    }

    public string? LoadWarning => _file.LoadWarning;

    public List<Fractal3DPalette> Palettes { get; }

    /// <summary>Палитра с таким именем или <c>null</c>, если её в библиотеке нет.</summary>
    public Fractal3DPalette? Find(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : Palettes.FirstOrDefault(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>После неудачной загрузки ничего не пишет: окно палитр предупредило при открытии.</summary>
    public void SaveCustomPalettes() => _file.SaveIfLoaded(Palettes.Where(palette => !palette.IsBuiltIn));

    /// <summary>Копия встроенной палитры: библиотека своя у каждого окна и правится независимо.</summary>
    private static Fractal3DPalette BuiltInCopy(Fractal3DPalette palette)
    {
        Fractal3DPalette copy = palette.Clone();
        copy.IsBuiltIn = true;
        return copy;
    }
}
