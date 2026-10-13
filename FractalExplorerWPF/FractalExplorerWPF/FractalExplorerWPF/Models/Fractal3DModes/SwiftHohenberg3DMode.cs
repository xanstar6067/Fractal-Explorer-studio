using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class SwiftHohenberg3DMode() : GpuSimulation3DMode(Fractal3DKind.SwiftHohenberg3D, new(
    "Свифт–Хоэнберг 3D · пространственные узоры", "Свифт–Хоэнберг · 3D",
    "Вещественное поле с выбранной длиной волны: лабиринты, слои, колонны и решётки. Спектральное моделирование на ГП, срезы, кисть и точное продолжение.",
    "Fractal3DSwiftHohenberg", "swift-hohenberg3d"))
{
    public override string? ShapeHeader => "Уравнение и пространственные узоры";
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По полю φ",
            [Fractal3DColoringMode.CrossTrap] = "По модулю |φ|"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
        state.CameraDistance = 3.6; state.SoftShadows = false; state.AmbientOcclusion = false;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp; state.ColorScale = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Лабиринт из шума", _ => { }),
        Preset("Параллельные слои", s => s.SwiftHohenberg = new() { SeedShape = SwiftHohenberg3DSeed.Layers, Noise = .005 }),
        Preset("Наклонные слои", s => s.SwiftHohenberg = new() { SeedShape = SwiftHohenberg3DSeed.TiltedLayers, Noise = .005 }),
        Preset("Шестигранные колонны", s => s.SwiftHohenberg = new()
        { SeedShape = SwiftHohenberg3DSeed.HexagonalColumns, R = .15, Quadratic = .8, Noise = .005 }),
        Preset("Пространственная решётка", s => s.SwiftHohenberg = new()
        { SeedShape = SwiftHohenberg3DSeed.CubicSpots, R = .15, Quadratic = 1, Noise = .005 }),
        Preset("Крупные ячейки", s => s.SwiftHohenberg = new() { WaveNumber = .5, WarmupSteps = 650 }),
        Preset("Локальная затравка", s => s.SwiftHohenberg = new()
        { SeedShape = SwiftHohenberg3DSeed.Localized, Quadratic = .7, Noise = 0, WarmupSteps = 160 })
    ];

    public override void Validate(Fractal3DState state) => state.SwiftHohenberg.Validate();
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = state.SwiftHohenberg;
        return new(new Vector3(s.Size, (float)s.Threshold, (float)s.DisplayRange),
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.View, 0), 0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.SwiftHohenberg;
        string view = s.View switch { SwiftHohenberg3DView.Negative => "отрицательная фаза",
            SwiftHohenberg3DView.Interfaces => "границы φ ≈ 0", _ => "положительная фаза" };
        return $"{prefix} · сетка {s.Size}³ · периодические границы\n" +
            $"r = {s.R:G4} · b = {s.Quadratic:G4} · q₀ = {s.WaveNumber:G4} · λ₀ = {Math.Tau / s.WaveNumber:G4}\n" +
            (s.Field is { } f ? $"Шаг {f.Step:N0} · t = {f.Step * s.TimeStep:G6}" : $"Новое поле, {s.WarmupSteps} шагов подготовки") +
            $" · Δt = {s.TimeStep:G4} · Δx = {s.Spacing:G4}\n{view} · Палитра: {state.ResolvePalette().Name}";
    }
}
