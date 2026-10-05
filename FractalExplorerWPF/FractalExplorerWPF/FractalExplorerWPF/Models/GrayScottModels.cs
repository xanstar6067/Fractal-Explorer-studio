using System.Windows.Media;
using System.Text.Json.Serialization;
using FractalExplorerWPF.Infrastructure.Serialization;
using Color = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace FractalExplorerWPF.Models;

public enum GrayScottSeedMode
{
    CenterSquare,
    RandomSpots,
    Ring,
    Noise
}

public enum GrayScottFieldMode
{
    V,
    U,
    Difference
}

public enum GrayScottBackend { Cpu, Gpu }

public sealed record GrayScottSnapshot(int Size,
    [property: JsonConverter(typeof(CompressedFloatArrayJsonConverter))] float[] U,
    [property: JsonConverter(typeof(CompressedFloatArrayJsonConverter))] float[] V, long StepCount)
{
    public GrayScottSnapshot Copy() => new(Size, [.. U], [.. V], StepCount);

    public GrayScottSnapshot Resize(int size)
    {
        if (size is < 32 or > GrayScottState.MaxGridSize) throw new ArgumentOutOfRangeException(nameof(size));
        var u = new float[size * size]; var v = new float[u.Length];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double sx = (x + .5) * Size / size - .5, sy = (y + .5) * Size / size - .5;
            u[y * size + x] = Sample(U, Size, sx, sy); v[y * size + x] = Sample(V, Size, sx, sy);
        }
        return new(size, u, v, StepCount);
    }

    internal static float Sample(float[] field, int size, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        int x0 = (ix % size + size) % size, y0 = (iy % size + size) % size;
        int x1 = (x0 + 1) % size, y1 = (y0 + 1) % size;
        return (float)((field[y0 * size + x0] * (1 - fx) + field[y0 * size + x1] * fx) * (1 - fy)
            + (field[y1 * size + x0] * (1 - fx) + field[y1 * size + x1] * fx) * fy);
    }
}

public sealed class GrayScottPalette
{
    public string Name { get; set; } = "Новая палитра";
    public List<Color> Colors { get; set; } = [MediaColors.Black, MediaColors.White];
    public bool IsGradient { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public double Gamma { get; set; } = 1;

    public GrayScottPalette Clone(string? name = null) => new()
    {
        Name = name ?? Name,
        Colors = [.. Colors],
        IsGradient = IsGradient,
        Gamma = Gamma
    };

    public override string ToString() => Name;
}

public sealed class GrayScottPreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required GrayScottState State { get; init; }
    public override string ToString() => Name;
}

public sealed class GrayScottState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? PresetId { get; set; }
    // These coefficients match the normalized nine-point stencil (-1, .2, .05).
    // The .16/.08 pair belongs to a different spatial scale and under-resolves spots here.
    public double DiffusionU { get; set; } = 1;
    public double DiffusionV { get; set; } = 0.5;
    public double Feed { get; set; } = 0.0545;
    public double Kill { get; set; } = 0.062;
    public double DeltaTime { get; set; } = 1;
    public const int MaxGridSize = 2048;
    public int GridSize { get; set; } = 512;
    public GrayScottBackend Backend { get; set; } = GrayScottBackend.Gpu;
    public bool AutoFrameSize { get; set; } = true;
    public int FrameWidth { get; set; } = 1024;
    public int FrameHeight { get; set; } = 1024;
    public GrayScottSnapshot? Checkpoint { get; set; }
    public int StepsPerFrame { get; set; } = 24;
    public int TargetFps { get; set; } = 30;
    public int RandomSeed { get; set; } = 1729;
    public GrayScottSeedMode SeedMode { get; set; } = GrayScottSeedMode.Noise;
    public int SeedCount { get; set; } = 18;
    public int SeedRadius { get; set; } = 6;
    public int BrushRadius { get; set; } = 6;
    public GrayScottFieldMode FieldMode { get; set; } = GrayScottFieldMode.V;
    public double RangeMinimum { get; set; }
    public double RangeMaximum { get; set; } = 0.5;
    public bool ReversePalette { get; set; }
    public GrayScottPalette Palette { get; set; } = GrayScottPalettes.Coral.Clone();

    public GrayScottState Clone(string? name = null, bool includeCheckpoint = true) => new()
    {
        SaveName = name ?? SaveName,
        Timestamp = Timestamp,
        PresetId = PresetId,
        DiffusionU = DiffusionU,
        DiffusionV = DiffusionV,
        Feed = Feed,
        Kill = Kill,
        DeltaTime = DeltaTime,
        GridSize = GridSize,
        Backend = Backend, AutoFrameSize = AutoFrameSize, FrameWidth = FrameWidth, FrameHeight = FrameHeight,
        Checkpoint = includeCheckpoint ? Checkpoint?.Copy() : null,
        StepsPerFrame = StepsPerFrame,
        TargetFps = TargetFps,
        RandomSeed = RandomSeed,
        SeedMode = SeedMode,
        SeedCount = SeedCount,
        SeedRadius = SeedRadius,
        BrushRadius = BrushRadius,
        FieldMode = FieldMode,
        RangeMinimum = RangeMinimum,
        RangeMaximum = RangeMaximum,
        ReversePalette = ReversePalette,
        Palette = Palette.Clone()
    };

    public void Validate()
    {
        static bool Range(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
        if (GridSize is < 32 or > MaxGridSize || !Enum.IsDefined(Backend) || !Enum.IsDefined(SeedMode) || !Enum.IsDefined(FieldMode))
            throw new ArgumentException("Размер сетки: 32–2048. Проверьте выбор движка и поля.");
        if (!Range(DiffusionU, double.Epsilon, 1) || !Range(DiffusionV, double.Epsilon, 1) || !Range(Feed, 0, .2) || !Range(Kill, 0, .2) || !Range(DeltaTime, .05, 1.5))
            throw new ArgumentException("Проверьте параметры уравнения Gray–Scott.");
        if (StepsPerFrame is < 1 or > 64 || TargetFps is < 1 or > 120 || SeedCount is < 1 or > 500 || SeedRadius is < 1 or > 128 || BrushRadius is < 1 or > 128)
            throw new ArgumentException("Проверьте скорость, число и радиусы затравок.");
        if (!double.IsFinite(RangeMinimum) || !double.IsFinite(RangeMaximum) || RangeMaximum <= RangeMinimum || !double.IsFinite(RangeMaximum - RangeMinimum) || Palette is null || Palette.Colors is null || !Range(Palette.Gamma, .1, 5))
            throw new ArgumentException("Проверьте диапазон цвета и палитру.");
        if (FrameWidth is < 32 or > 8192 || FrameHeight is < 32 or > 8192 || (long)FrameWidth * FrameHeight > 16_777_216)
            throw new ArgumentException("Буфер: 32–8192 пикселей по стороне, всего до 16 млн пикселей.");
        if (Checkpoint is { } cp && (cp.Size != GridSize || cp.StepCount < 0 || cp.U is null || cp.V is null || cp.U.Length != GridSize * GridSize || cp.V.Length != cp.U.Length || cp.U.Any(v => !float.IsFinite(v) || v is < 0 or > 1) || cp.V.Any(v => !float.IsFinite(v) || v is < 0 or > 1)))
            throw new ArgumentException("Сохранённое поле повреждено или не соответствует сетке.");
    }
}

public static class GrayScottPalettes
{
    public static GrayScottPalette Coral => new()
    {
        Name = "Коралловый риф",
        Colors =
        [
            Color.FromRgb(2, 6, 23), Color.FromRgb(8, 47, 73),
            Color.FromRgb(8, 145, 178), Color.FromRgb(103, 232, 249),
            Color.FromRgb(255, 247, 237), Color.FromRgb(251, 113, 133)
        ]
    };
}

public static class GrayScottPresets
{
    public static IReadOnlyList<GrayScottPreset> All { get; } =
    [
        Preset("coral", "Коралл — ветвящиеся лабиринты", 0.060, 0.062, GrayScottSeedMode.Noise, 18, 6,
            "Шум собирается в ветвящиеся лабиринты. По мере заполнения поля рост замедляется."),
        Preset("worms", "Черви — движущиеся нити", 0.026, 0.055, GrayScottSeedMode.RandomSpots, 24, 6,
            "Пятна растут в нити: они изгибаются, соединяются и перестраиваются. Сначала дайте затравкам вырасти."),
        Preset("mitosis", "Митоз — делящиеся пятна", 0.0367, 0.0649, GrayScottSeedMode.RandomSpots, 28, 6,
            "Пятна сначала уменьшаются, затем вытягиваются и делятся. Первые деления — примерно через 1000–2000 шагов; на заполненном поле они прекращаются."),
        Preset("solitons", "Солитоны — устойчивые импульсы", 0.046, 0.067, GrayScottSeedMode.CenterSquare, 1, 6,
            "Затравка сжимается в устойчивое отдельное пятно. Оно сохраняет форму и может стоять на месте. Кистью можно добавить другие пятна."),
        Preset("waves", "Волны — кольцевой фронт", 0.014, 0.045, GrayScottSeedMode.Ring, 1, 3,
            "Кольцо расходится двумя фронтами. При столкновении волны гасят друг друга, и поле может опустеть. Добавьте затравку кистью или перезапустите режим."),
        Preset("chaos", "Хаос — взаимодействующие домены", 0.026, 0.051, GrayScottSeedMode.Noise, 1, 5,
            "Домены появляются и исчезают, волны сталкиваются. Краткое затихание возможно; рисунок не обязан сохранять отдельные пятна.")
    ];

    private static GrayScottPreset Preset(
        string id,
        string name,
        double feed,
        double kill,
        GrayScottSeedMode seedMode,
        int seedCount,
        int seedRadius,
        string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        State = new GrayScottState
        {
            PresetId = id,
            Feed = feed,
            Kill = kill,
            SeedMode = seedMode,
            SeedCount = seedCount,
            SeedRadius = seedRadius
        }
    };
}
