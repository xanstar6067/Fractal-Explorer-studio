using System.IO;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

public sealed class DynamicSystemSaveStore(DynamicSystemKind kind) : FractalSaveStore<DynamicSystemState>(
    kind.ToString(),
    state => state.SaveName,
    state =>
    {
        state.Kind = kind;
        return true;
    });

public sealed class DynamicPaletteStore
{
    private readonly DynamicSystemKind _kind;
    private string FilePath => AppPaths.GetPaletteFile(_kind == DynamicSystemKind.Lyapunov ? "lyapunov_palettes.json" : "logistic_map_palettes.json");
    public DynamicPaletteStore(DynamicSystemKind kind) => _kind = kind;

    public List<DynamicPalette> Load()
    {
        List<DynamicPalette> result = _kind == DynamicSystemKind.Lyapunov ? LyapunovBuiltIns() : LogisticBuiltIns();
        if (!File.Exists(FilePath)) return result;
        try
        {
            List<DynamicPalette>? custom = JsonSerializer.Deserialize<List<DynamicPalette>>(File.ReadAllText(FilePath), JsonOptionsFactory.Create());
            if (custom is not null) result.AddRange(custom.Select(p => { p.IsBuiltIn = false; return p; }));
        }
        catch
        {
            // Legacy Lyapunov palettes stored the coloring enum as a number.
            // Read that shape explicitly so existing user palettes survive migration.
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    var palette = new DynamicPalette
                    {
                        Name = item.TryGetProperty("Name", out JsonElement name) ? name.GetString() ?? "Палитра" : "Палитра",
                        Mode = ReadLegacyMode(item),
                        ExponentRange = item.TryGetProperty("ExponentRange", out JsonElement range) ? range.GetDouble() : 2,
                        ZeroBandWidth = item.TryGetProperty("ZeroBandWidth", out JsonElement zero) ? zero.GetDouble() : .05
                    };
                    if (item.TryGetProperty("Colors", out JsonElement colors))
                        foreach (JsonElement color in colors.EnumerateArray())
                            if (TryColor(color.GetString(), out Color parsed)) palette.Colors.Add(parsed);
                    if (palette.Colors.Count > 1) result.Add(palette);
                }
            }
            catch { }
        }
        return result;
    }

    public void Save(IEnumerable<DynamicPalette> palettes)
    {
        File.WriteAllText(AppPaths.EnsureDirectoryFor(FilePath), JsonSerializer.Serialize(palettes.Where(p => !p.IsBuiltIn), JsonOptionsFactory.Create()));
    }

    private static DynamicPalette P(string name, string mode, params Color[] colors) => new() { Name = name, Mode = mode, Colors = colors.ToList(), IsBuiltIn = true };
    private static string ReadLegacyMode(JsonElement item)
    {
        if (!item.TryGetProperty("Mode", out JsonElement mode)) return item.TryGetProperty("IsGradient", out JsonElement gradient) && gradient.ValueKind == JsonValueKind.False ? "Cycle" : "Gradient";
        if (mode.ValueKind == JsonValueKind.String) return mode.GetString() ?? "Diverging";
        string[] names = ["Diverging","Absolute","ZeroBandHighlight","HistogramEqualized","LegacyBuiltIn"];
        int value = mode.GetInt32(); return value >= 0 && value < names.Length ? names[value] : "Diverging";
    }
    private static bool TryColor(string? value, out Color color)
    {
        color=default;if(value is null||value.Length!=9||value[0]!='#'||!uint.TryParse(value.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out uint argb))return false;
        color=Color.FromArgb((byte)(argb>>24),(byte)(argb>>16),(byte)(argb>>8),(byte)argb);return true;
    }

    private static List<DynamicPalette> LogisticBuiltIns() =>
    [
        P("Орбиты: монохром", "Cycle", Colors.Black, Colors.White),
        P("Орбиты: периодические полосы", "Cycle", Color.FromRgb(255,235,59), Color.FromRgb(3,169,244), Color.FromRgb(233,30,99), Color.FromRgb(76,175,80), Colors.White),
        P("Плотность: холодная", "Gradient", Colors.Black, Color.FromRgb(11,35,79), Color.FromRgb(24,119,242), Color.FromRgb(127,219,255), Colors.White),
        P("Плотность: тёплая", "Gradient", Colors.Black, Color.FromRgb(76,0,0), Color.FromRgb(191,54,12), Color.FromRgb(255,167,38), Colors.White),
        P("Плотность: холодно-тёплая", "Gradient", Colors.Black, Color.FromRgb(16,38,84), Color.FromRgb(70,117,255), Color.FromRgb(210,240,255), Color.FromRgb(255,193,7), Color.FromRgb(239,83,80), Colors.White),
        P("Периодические зоны", "Cycle", Colors.Black, Color.FromRgb(142,68,173), Color.FromRgb(41,128,185), Color.FromRgb(39,174,96), Color.FromRgb(243,156,18), Colors.White)
    ];

    private static List<DynamicPalette> LyapunovBuiltIns() =>
    [
        P("Наследуемая встроенная", "LegacyBuiltIn", Color.FromRgb(20,30,80), Color.FromRgb(90,200,255), Color.FromRgb(120,140,70), Color.FromRgb(190,100,45), Color.FromRgb(255,50,30)),
        P("Классическая Ляпунова", "Diverging", Color.FromRgb(20,30,80), Color.FromRgb(90,200,255), Color.FromRgb(120,140,70), Color.FromRgb(190,100,45), Color.FromRgb(255,50,30)),
        P("Стандартный серый", "Absolute", Colors.Black, Colors.DimGray, Colors.Silver, Colors.White),
        P("Ультрафиолет", "HistogramEqualized", Colors.Black, Colors.DarkViolet, Colors.Violet, Colors.White),
        P("Огонь", "Diverging", Colors.Black, Colors.DarkRed, Colors.Red, Colors.Orange, Colors.Yellow, Colors.White),
        P("Лёд", "Absolute", Colors.Black, Colors.DarkBlue, Colors.Blue, Colors.Cyan, Colors.White),
        P("Огонь и лёд", "ZeroBandHighlight", Colors.DarkBlue, Colors.Cyan, Colors.White, Colors.Yellow, Colors.Red, Colors.DarkRed),
        P("Психоделика", "HistogramEqualized", Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta),
        P("Черно-белый", "Absolute", Colors.Black, Colors.White),
        P("Зелёный", "Diverging", Colors.Black, Colors.DarkGreen, Color.FromRgb(0,204,0), Color.FromRgb(60,255,60), Colors.Honeydew, Colors.White),
        P("Сепия", "Absolute", Color.FromRgb(20,10,0), Color.FromRgb(255,240,192)),
        P("Белый ультрафиолет", "HistogramEqualized", Colors.White, Colors.Lavender, Colors.Violet, Colors.DarkViolet, Colors.Indigo, Colors.Black),
        P("Белый огонь", "Diverging", Colors.White, Colors.LightYellow, Colors.Yellow, Colors.Orange, Colors.Red, Colors.DarkRed, Colors.Maroon),
        P("Белый лёд", "Absolute", Colors.White, Colors.LightCyan, Colors.Cyan, Colors.DeepSkyBlue, Colors.Blue, Colors.DarkBlue, Colors.Navy),
        P("Белый зелёный", "ZeroBandHighlight", Colors.White, Color.FromRgb(180,255,180), Color.FromRgb(60,220,60), Color.FromRgb(0,120,0), Colors.Black),
        P("Бело-чёрный", "Absolute", Colors.White, Colors.Black),
        P("Закат", "Diverging", Colors.Black, Color.FromRgb(25,25,112), Colors.Indigo, Colors.Crimson, Colors.DarkOrange, Colors.White),
        P("Океан", "Absolute", Colors.Black, Color.FromRgb(0,20,40), Color.FromRgb(0,100,150), Color.FromRgb(100,200,255), Colors.White),
        P("Золото", "HistogramEqualized", Colors.Black, Color.FromRgb(85,65,0), Color.FromRgb(205,173,0), Colors.Gold, Colors.White),
        P("Медь", "Diverging", Colors.Black, Color.FromRgb(72,61,20), Color.FromRgb(184,115,51), Color.FromRgb(240,147,43), Colors.White),
        P("Неон", "HistogramEqualized", Colors.Black, Color.FromRgb(75,0,75), Colors.Magenta, Colors.Cyan, Colors.Lime, Colors.White),
        P("Радуга", "HistogramEqualized", Colors.Black, Colors.Violet, Colors.Blue, Colors.Lime, Colors.Yellow, Colors.Red, Colors.White)
        ,P("Аметист", "Diverging", Colors.Black, Color.FromRgb(25,25,112), Color.FromRgb(123,104,238), Colors.Plum, Colors.White)
        ,P("Лес", "Diverging", Colors.Black, Color.FromRgb(0,39,0), Colors.ForestGreen, Colors.LawnGreen, Colors.White)
        ,P("Космос", "Absolute", Colors.Black, Color.FromRgb(25,25,112), Color.FromRgb(72,61,139), Colors.BlueViolet, Colors.DeepPink, Colors.White)
        ,P("Бирюза", "Absolute", Colors.Black, Color.FromRgb(0,100,100), Colors.MediumTurquoise, Colors.LightCyan, Colors.White)
        ,P("Лава", "ZeroBandHighlight", Colors.DarkRed, Colors.OrangeRed, Colors.DarkOrange, Colors.Ivory)
        ,P("Монохром синий", "ZeroBandHighlight", Colors.DarkBlue, Colors.RoyalBlue, Colors.PowderBlue, Colors.White)
        ,P("Монохром красный", "Absolute", Colors.Black, Colors.DarkRed, Colors.Crimson, Colors.LightPink, Colors.White)
    ];
}
