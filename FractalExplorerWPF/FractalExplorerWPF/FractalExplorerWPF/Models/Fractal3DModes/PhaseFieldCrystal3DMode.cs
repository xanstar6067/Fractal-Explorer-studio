using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class PhaseFieldCrystal3DMode() : GpuSimulation3DMode(Fractal3DKind.PhaseFieldCrystal3D, new(
    "Фазовое поле кристалла 3D · PFC", "Фазовое поле кристалла · 3D",
    "Периодическая плотность кристалла с сохранением среднего: ОЦК-решётка, рост из ядра, зёрна и их границы. БПФ на ГП, пики плотности, модуляция и точное продолжение.",
    "Fractal3DPhaseFieldCrystal", "phase-field-crystal3d"))
{
    public override string? ShapeHeader => "Плотность и кристаллизация";
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По плотности ψ",
            [Fractal3DColoringMode.CrossTrap] = "По местной модуляции"
        };
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.CrossTrap;
        state.Palette = Fractal3DPalettes.Get("Медь и патина"); state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.ColorScale = 1; state.CameraDistance = 3.6; state.SoftShadows = false; state.AmbientOcclusion = false;
    }
    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("ОЦК · монокристалл", _ => { }),
        Preset("Рост из ядра", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.Nucleus, Noise=0, WarmupSteps=800 }),
        Preset("Два растущих зерна", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.TwoGrains, Noise=0, WarmupSteps=1200 }),
        Preset("Граница зёрен", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.GrainBoundary, WarmupSteps=1200 }),
        Preset("Кристаллизация из шума", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.Noise, Noise=.1, WarmupSteps=1600 }),
        Preset("Слоистая структура", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.Layers, Mean=-.05, Threshold=.15 }),
        Preset("Колонны плотности", s => s.PhaseFieldCrystal = new() { SeedShape=PhaseFieldCrystal3DSeed.Columns, Mean=-.2, Threshold=.25 }),
        Preset("Оболочка растущего кристалла", s => s.PhaseFieldCrystal = new()
        { SeedShape=PhaseFieldCrystal3DSeed.Nucleus, Noise=0, View=PhaseFieldCrystal3DView.Modulation, Threshold=.3, WarmupSteps=600 })
    ];
    public override void Validate(Fractal3DState state) => state.PhaseFieldCrystal.Validate();
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s=state.PhaseFieldCrystal;
        return new(new Vector3(s.Size,(float)s.Threshold,(float)s.DisplayRange),
            new Vector4(s.CutAxis,(float)s.CutPosition,(float)s.View,(float)s.Mean),0,BoxInversion(state));
    }
    public override string Describe(Fractal3DState state,string prefix)
    {
        var s=state.PhaseFieldCrystal;
        return $"{prefix} · сетка {s.Size}³ · периодические границы\nε = {s.Undercooling:G4} · средняя ψ = {s.Mean:G5} · q₀ = {s.WaveNumber:G4}\n" +
            (s.Field is { } f ? $"Шаг {f.Step:N0} · t = {f.Step*s.TimeStep:G6}" : $"Новое поле, {s.WarmupSteps} шагов подготовки") +
            $" · M = {s.Mobility:G4} · Δt = {s.TimeStep:G4}\nПалитра: {state.ResolvePalette().Name} · Окраска: {ColoringModeName(state.ColoringMode)}";
    }
}
