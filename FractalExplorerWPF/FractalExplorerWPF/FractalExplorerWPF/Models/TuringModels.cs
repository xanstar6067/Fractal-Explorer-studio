using System.Text.Json.Serialization;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure.Serialization;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace FractalExplorerWPF.Models;

public enum TuringBoundary { Wrap, Reflect }
public enum TuringColoring { Field, Relief, Scales }
public enum TuringBrush { Light, Dark, Noise }
public enum TuringBackend { Cpu, Gpu }

public sealed class TuringScale
{
    public bool Enabled { get; set; } = true;
    // Radii are measured on a reference grid of 256: quality changes do not change the motif size.
    public double Radius { get; set; }
    public double Amount { get; set; }
    public TuringScale Clone() => new() { Enabled = Enabled, Radius = Radius, Amount = Amount };
}

public sealed class TuringCheckpoint
{
    public int Size { get; set; }
    public long StepCount { get; set; }
    [JsonConverter(typeof(CompressedFloatArrayJsonConverter))]
    public float[] Field { get; set; } = [];
    public byte[] Scales { get; set; } = [];
    public TuringCheckpoint Clone() => new() { Size = Size, StepCount = StepCount, Field = [.. Field], Scales = [.. Scales] };
}

public sealed class TuringState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? PresetId { get; set; } = "coral";
    public const int MaxGridSize = 2048;
    public int GridSize { get; set; } = 512;
    public TuringBackend Backend { get; set; } = TuringBackend.Gpu;
    public bool AutoFrameSize { get; set; } = true;
    public int FrameWidth { get; set; } = 1024;
    public int FrameHeight { get; set; } = 1024;
    public int RandomSeed { get; set; } = 1729;
    public int WarmupSteps { get; set; } = 160;
    public int StepsPerFrame { get; set; } = 1;
    public double DetailSize { get; set; } = 1;
    public double EvolutionRate { get; set; } = 1;
    public double InhibitorRatio { get; set; } = 2;
    public int Symmetry { get; set; } = 1;
    public bool Mirror { get; set; }
    public TuringBoundary Boundary { get; set; } = TuringBoundary.Wrap;
    public List<TuringScale> Layers { get; set; } = DefaultLayers();
    public DynamicPalette Palette { get; set; } = TuringPalettes.All()[0].Clone();
    public TuringColoring Coloring { get; set; } = TuringColoring.Relief;
    public double Contrast { get; set; } = 1;
    public double Relief { get; set; } = .35;
    public bool ReversePalette { get; set; }
    public TuringBrush Brush { get; set; }
    public double BrushRadius { get; set; } = .04;
    public double BrushStrength { get; set; } = .35;
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public TuringCheckpoint? Checkpoint { get; set; }

    public static List<TuringScale> DefaultLayers() =>
    [
        new() { Radius = 2, Amount = .012 }, new() { Radius = 5, Amount = .02 },
        new() { Radius = 12, Amount = .035 }, new() { Radius = 28, Amount = .05 },
        new() { Radius = 60, Amount = .08 }
    ];

    public TuringState Clone(string? name = null, bool includeCheckpoint = true) => new()
    {
        SaveName = name ?? SaveName, Timestamp = Timestamp, PresetId = PresetId,
        GridSize = GridSize, RandomSeed = RandomSeed, WarmupSteps = WarmupSteps, StepsPerFrame = StepsPerFrame,
        Backend = Backend, AutoFrameSize = AutoFrameSize, FrameWidth = FrameWidth, FrameHeight = FrameHeight,
        DetailSize = DetailSize, EvolutionRate = EvolutionRate, InhibitorRatio = InhibitorRatio,
        Symmetry = Symmetry, Mirror = Mirror, Boundary = Boundary, Layers = Layers.Select(l => l.Clone()).ToList(),
        Palette = Palette.Clone(), Coloring = Coloring, Contrast = Contrast, Relief = Relief, ReversePalette = ReversePalette,
        Brush = Brush, BrushRadius = BrushRadius, BrushStrength = BrushStrength,
        Zoom = Zoom, PanX = PanX, PanY = PanY, Checkpoint = includeCheckpoint ? Checkpoint?.Clone() : null
    };

    public void Validate()
    {
        static bool Range(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
        if (GridSize is < 32 or > MaxGridSize || StepsPerFrame is < 1 or > 8 || WarmupSteps is < 0 or > 2000)
            throw new ArgumentException("Сетка: 32–2048; шагов на кадр: 1–8; начальное развитие: 0–2000 шагов.");
        if (!Enum.IsDefined(Backend) || FrameWidth is < 32 or > 8192 || FrameHeight is < 32 or > 8192 || (long)FrameWidth * FrameHeight > 16_777_216)
            throw new ArgumentException("Буфер: 32–8192 пикселей по стороне, не более 16 миллионов пикселей.");
        if (!Range(DetailSize, .25, 3) || !Range(EvolutionRate, .1, 3) || !Range(InhibitorRatio, 1.2, 4) || Symmetry is < 1 or > 16)
            throw new ArgumentException("Проверьте размер деталей, отклик и симметрию (1–16 лучей).");
        if (Layers is null || Layers.Count is < 1 or > 8 || Layers.Any(l => l is null) || !Layers.Any(l => l.Enabled) ||
            Layers.Any(l => !Range(l.Radius, 1, 100) || !Range(l.Amount, .001, .15)))
            throw new ArgumentException("Включите хотя бы один масштаб. Радиусы: 1–100; отклик: 0,001–0,15.");
        if (!Enum.IsDefined(Boundary) || !Enum.IsDefined(Coloring) || !Enum.IsDefined(Brush) ||
            !Range(Contrast, .3, 3) || !Range(Relief, 0, 2) || !Range(BrushRadius, .005, .2) || !Range(BrushStrength, .05, 1) ||
            !Range(Zoom, 1, 12) || !Range(PanX, -2, 2) || !Range(PanY, -2, 2) ||
            Palette?.Colors is not { Count: >= 2 and <= 256 })
            throw new ArgumentException("Некорректная окраска, кисть или положение поля.");
        if (Checkpoint is { } cp && (cp.Size != GridSize || cp.StepCount is < 0 or > 1_000_000_000 ||
            cp.Field is null || cp.Scales is null || cp.Field.Length != GridSize * GridSize || cp.Scales.Length != cp.Field.Length ||
            cp.Field.Any(v => !float.IsFinite(v) || v is < -1.001f or > 1.001f) || cp.Scales.Any(v => v >= 8)))
            throw new ArgumentException("Сохранённое поле повреждено или не соответствует размеру сетки.");
    }
}

public sealed record TuringPreset(string Id, string Name, string Description, double Size, int Depth, int Symmetry, bool Mirror, int Seed)
{
    public TuringState CreateState()
    {
        var state = new TuringState { PresetId = Id, DetailSize = Size, Symmetry = Symmetry, Mirror = Mirror, RandomSeed = Seed };
        state.Layers = state.Layers.Take(Depth).ToList();
        return state;
    }
    public override string ToString() => Name;
}

public static class TuringPresets
{
    public static IReadOnlyList<TuringPreset> All { get; } =
    [
        new("coral", "Коралловый лабиринт", "Крупные каналы с тонкими ветвящимися деталями. Попробуйте изменить размер деталей прямо во время развития.", 1, 5, 1, false, 1729),
        new("lace", "Тонкое кружево", "Плотное переплетение нескольких масштабов. Приблизьте поле колесом, чтобы рассмотреть самые мелкие нити.", .55, 5, 1, false, 31415),
        new("cells", "Клеточная ткань", "Мягкие органические области и вложенные островки. Кисть создаёт новые области, которые постепенно встраиваются в узор.", 1.6, 4, 1, false, 2718),
        new("diatom", "Диатомея", "Шесть лучей и зеркальная симметрия образуют живой орнамент. Кисть повторяется вокруг центра.", 1, 5, 6, true, 602),
        new("rosette", "Восьмилучевая розетка", "Восемь одинаковых секторов с органическими деталями. Рельеф подчёркивает перепады поля.", .8, 5, 8, true, 808),
        new("islands", "Острова и проливы", "Спокойная структура из трёх масштабов. Карта масштабов покажет, где преобладают мелкие и крупные процессы.", 1.2, 3, 1, false, 12345)
    ];
}

public static class TuringPalettes
{
    public static List<DynamicPalette> All() =>
    [
        P("Медь и бирюза", "#08252B", "#146B78", "#80CCC0", "#F4E6C2", "#CE9053"),
        P("Фарфор", "#162B46", "#4A7F9D", "#B8DCE1", "#F7F3E8"),
        P("Биолюминесценция", "#0C102D", "#423880", "#BC68A6", "#70E6D1", "#F4FFE9"),
        P("Золотая гравюра", "#17140E", "#655024", "#C3A264", "#F8EAC4"),
        P("Монохром", "#111820", "#F5F5F0")
    ];
    private static DynamicPalette P(string name, params string[] colors) => new()
    {
        Name = name, Mode = "Gradient", IsBuiltIn = true,
        Colors = colors.Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToList()
    };
}
