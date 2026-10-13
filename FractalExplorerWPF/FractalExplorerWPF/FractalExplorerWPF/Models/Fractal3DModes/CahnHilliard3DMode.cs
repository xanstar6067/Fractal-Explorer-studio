using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class CahnHilliard3DMode() : GpuSimulation3DMode(Fractal3DKind.CahnHilliard3D, new(
    "Кан–Хиллиард 3D · спинодальный распад", "Кан–Хиллиард · 3D",
    "Смесь расслаивается на две фазы, сохраняя средний состав. При равных долях возникают взаимопроникающие губчатые домены, которые постепенно укрупняются. Срез открывает внутренние каналы.",
    "Fractal3DCahnHilliard", "cahn-hilliard3d"))
{
    public override string? ShapeHeader => "Смесь и эволюция";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По составу смеси" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
        state.CameraDistance = 3.6;
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.ColorScale = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Спинодальная губка · 50/50", _ => { }),
        Preset("Капли в матрице · 30/70", s => { s.CahnHilliard = new() { Mean = -.4, WarmupSteps = 650, Seed = 314 }; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Крупные домены", s => { s.CahnHilliard = new() { Kappa = 2, WarmupSteps = 900, Seed = 2718 }; s.Palette = Fractal3DPalettes.Get("Огонь"); }),
        Preset("Гироидная затравка", s => { s.CahnHilliard = new() { SeedShape = CahnHilliard3DSeed.Gyroid, WarmupSteps = 180 }; }),
        Preset("Слоистая затравка", s => { s.CahnHilliard = new() { SeedShape = CahnHilliard3DSeed.Layers, WarmupSteps = 250, Seed = 808 }; s.Palette = Fractal3DPalettes.Get("Лёд"); })
    ];

    public override void Validate(Fractal3DState state) => state.CahnHilliard.Validate();

    /// <summary>Уровень поверхности переводится из состава [−1; 1] в долю [0; 1]; вторая фаза — тот же уровень с обратным знаком.</summary>
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        CahnHilliard3DSettings s = state.CahnHilliard;
        return new(
            new Vector3(s.Size, (float)(.5 + .5 * (s.Invert ? -s.Level : s.Level)), 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, s.Invert ? 1 : 0, 0),
            0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.CahnHilliard;
        return $"{prefix} · сетка {s.Size}³ · " +
            (s.Field is { } field ? $"шаг {field.Step:N0} · t = {field.Time:G6}" : $"новая смесь, {s.WarmupSteps} шагов подготовки") + "\n" +
            $"Состав: {s.Field?.Mean ?? s.Mean:G4} · κ = {s.Kappa:G4} · M = {s.Mobility:G4} · Δt = {s.TimeStep:G4}\n" +
            $"Палитра: {state.ResolvePalette().Name} · " + (s.Invert ? "вторая фаза" : "первая фаза");
    }
}
