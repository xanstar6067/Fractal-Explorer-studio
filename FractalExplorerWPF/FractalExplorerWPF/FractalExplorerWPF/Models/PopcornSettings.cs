namespace FractalExplorerWPF.Models;

public enum PopcornPlotMode { GridOrbits, HilbertCurve }

public sealed class PopcornSettings
{
    public double H { get; set; } = .05;
    public double K { get; set; } = 3;
    public int OrbitIterations { get; set; } = 80;
    public int GridSize { get; set; } = 192;
    public int HilbertOrder { get; set; } = 8;
    public double SeedSpan { get; set; } = 6;
    public double Span { get; set; } = 6.4;
    public PopcornPlotMode PlotMode { get; set; }

    public PopcornSettings Clone() => (PopcornSettings)MemberwiseClone();

    public void Validate()
    {
        if (!double.IsFinite(H) || H is < .0001 or > .5 ||
            !double.IsFinite(K) || K is < .1 or > 12)
            throw new InvalidOperationException("Шаг h должен быть от 0,0001 до 0,5, частота k — от 0,1 до 12.");
        if (OrbitIterations is < 1 or > 1_000 || GridSize is < 8 or > 512 || HilbertOrder is < 2 or > 9)
            throw new InvalidOperationException("Итерации: 1–1000, сторона сетки: 8–512, порядок Гильберта: 2–9.");
        if (!double.IsFinite(SeedSpan) || SeedSpan is < .1 or > 40 ||
            !double.IsFinite(Span) || Span is < .1 or > 100 || !Enum.IsDefined(PlotMode))
            throw new InvalidOperationException("Размер затравки: 0,1–40, размер кадра: 0,1–100. Выберите способ построения.");
        long seeds = PlotMode == PopcornPlotMode.GridOrbits ? GridSize * GridSize : 1 << (2 * HilbertOrder);
        if (seeds * OrbitIterations > 100_000_000)
            throw new InvalidOperationException("Уменьшите число затравок или итераций: предел — 100 млн шагов.");
    }
}

public sealed record PopcornPreset(string Name, string Id, PopcornSettings Settings, string PaletteName)
{
    public override string ToString() => Name;
}

public static class PopcornPresets
{
    public static IReadOnlyList<PopcornPreset> All { get; } =
    [
        new("Классические вихри", "classic", new(), "Popcorn — океан"),
        new("Тонкое кружево", "lace", new() { H = .015, GridSize = 220, OrbitIterations = 160 }, "Popcorn — аметист"),
        new("Крупные острова", "islands", new() { H = .1, K = 2.5, OrbitIterations = 100 }, "Popcorn — медь"),
        new("Гильберт — ткань", "hilbert_fabric", new() { PlotMode = PopcornPlotMode.HilbertCurve, H = .035, OrbitIterations = 12 }, "Popcorn — океан"),
        new("Гильберт — завитки", "hilbert_swirl", new() { PlotMode = PopcornPlotMode.HilbertCurve, OrbitIterations = 30, Span = 6.8 }, "Popcorn — золото")
    ];

    public static void Apply(DynamicSystemState state, int index)
    {
        PopcornPreset preset = All[index];
        state.Popcorn = preset.Settings.Clone();
        state.PaletteName = preset.PaletteName;
        state.CenterX = state.CenterY = 0;
        state.Zoom = 1;
        state.DensityGamma = .65;
        state.PointOfInterestId = "popcorn_" + preset.Id;
    }

    public static IReadOnlyList<DynamicSystemState> PointsOfInterest() =>
        Enumerable.Range(0, All.Count).Select(index =>
        {
            var state = new DynamicSystemState { Kind = DynamicSystemKind.Popcorn };
            Apply(state, index);
            state.SaveName = All[index].Name;
            state.Timestamp = DateTime.MinValue;
            return state;
        }).ToArray();
}
