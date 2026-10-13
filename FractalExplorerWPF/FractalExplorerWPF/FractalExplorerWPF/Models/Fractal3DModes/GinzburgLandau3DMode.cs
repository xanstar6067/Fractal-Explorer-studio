using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class GinzburgLandau3DMode() : GpuSimulation3DMode(Fractal3DKind.GinzburgLandau3D, new(
    "Гинзбург–Ландау 3D · комплексное поле", "Гинзбург–Ландау · 3D",
    "Комплексное уравнение Гинзбурга–Ландау: амплитуда, фаза, вихревые кольца и нити, беспорядочное поле и фазовые волны.",
    "Fractal3DGinzburgLandau", "ginzburg-landau3d"))
{
    public override string? ShapeHeader => "Комплексное поле и вихри";
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По фазе",
            [Fractal3DColoringMode.CrossTrap] = "По амплитуде |A|"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Cycle;
        state.Palette = Fractal3DPalettes.Get("Неон");
        state.CameraDistance = 2.2; state.SoftShadows = false; state.AmbientOcclusion = false;
        state.ColorScale = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Вихревое кольцо", _ => { }),
        Preset("Прямая вихревая нить", s =>
        {
            s.CameraDistance = 3.6;
            s.GinzburgLandau = new() { SeedShape = GinzburgLandau3DSeed.Vortex };
        }),
        Preset("Пара противоположных вихрей", s =>
        {
            s.CameraDistance = 3.6;
            s.GinzburgLandau = new() { SeedShape = GinzburgLandau3DSeed.VortexPair };
        }),
        Preset("Беспорядочная сеть вихрей", s =>
        {
            s.CameraDistance = 3.6;
            s.GinzburgLandau = new()
            {
                SeedShape = GinzburgLandau3DSeed.Random, Boundary = GinzburgLandau3DBoundary.Periodic,
                C1 = 1.5, C3 = -1.5, WarmupSteps = 300
            };
        }),
        Preset("Ядро вихревого кольца", s => s.GinzburgLandau = new() { View = GinzburgLandau3DView.AmplitudeDefects, Threshold = .3 }),
        Preset("Фазовая волна · срез", s =>
        {
            s.CameraDistance = 3.6;
            s.GinzburgLandau = new()
            {
                SeedShape = GinzburgLandau3DSeed.PlaneWave, Boundary = GinzburgLandau3DBoundary.Periodic,
                View = GinzburgLandau3DView.Amplitude, Threshold = .5, CutAxis = 3, CutPosition = 0
            };
        })
    ];

    public override void Validate(Fractal3DState state) => state.GinzburgLandau.Validate();
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = state.GinzburgLandau;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.View, 0), 0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.GinzburgLandau;
        string view = s.View switch { GinzburgLandau3DView.Filaments => "вихревые нити",
            GinzburgLandau3DView.AmplitudeDefects => "дефекты амплитуды", _ => "амплитуда" };
        return $"{prefix} · сетка {s.Size}³ · c₁ = {s.C1:G4} · c₃ = {s.C3:G4}\n" +
            (s.Field is { } f ? $"Шаг {f.Step:N0} · t = {f.Step * s.TimeStep:G6}" : $"Новое поле, {s.WarmupSteps} шагов подготовки") +
            $" · Δt = {s.TimeStep:G4} · Δx = {s.Spacing:G4}\n" +
            $"{view} · {(s.Boundary == GinzburgLandau3DBoundary.Periodic ? "периодические границы" : "непроницаемые стенки")}\n" +
            $"Палитра: {state.ResolvePalette().Name} · Окраска: {ColoringModeName(state.ColoringMode)}";
    }
}
