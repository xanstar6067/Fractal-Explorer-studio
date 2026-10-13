using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Excitable3DMode() : GpuSimulation3DMode(Fractal3DKind.Excitable3D, new(
    "Свитковые волны 3D · возбудимые среды", "Свитковые волны · 3D",
    "Баркли, Oregonator (Белоусов–Жаботинский), FitzHugh–Nagumo и Aliev–Panfilov. Бегущие фронты, фаза и вихревые нити внутри объёма.",
    "Fractal3DExcitable", "excitable3d"))
{
    public override string? ShapeHeader => "Среда и свитковые волны";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По фазе",
            [Fractal3DColoringMode.CrossTrap] = "По возбуждению U"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Неон");
        state.CameraDistance = 3.6; state.SoftShadows = false; state.AmbientOcclusion = false;
        state.ColorRepeat = Fractal3DColorRepeat.Cycle; state.ColorScale = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Баркли · прямой свиток", s => s.Excitable = new()),
        Preset("Баркли · вихревое кольцо", s => s.Excitable = new() { SeedShape = Excitable3DSeed.Ring }),
        Preset("Oregonator · химическая спираль", s => s.Excitable = Excitable3DSettings.Default(Excitable3DModel.Oregonator)),
        Preset("FitzHugh–Nagumo · свиток", s => s.Excitable = Excitable3DSettings.Default(Excitable3DModel.FitzHughNagumo)),
        Preset("Aliev–Panfilov · волна возбуждения", s => s.Excitable = Excitable3DSettings.Default(Excitable3DModel.AlievPanfilov)),
        Preset("Вихревые нити", s => s.Excitable = new() { View = Excitable3DView.Filaments, Threshold = .15 }),
        Preset("Перекрещённые свитки", s => s.Excitable = new() { SeedShape = Excitable3DSeed.CrossedScrolls })
    ];

    public override void Validate(Fractal3DState state) => state.Excitable.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Excitable3DSettings s = state.Excitable;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.View, 0), 0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.Excitable;
        string view = s.View switch { Excitable3DView.Filaments => "вихревые нити", Excitable3DView.WavesAndFilaments => "волны и нити", _ => "фронты волн" };
        return $"{prefix} · {s.Name} · сетка {s.Size}³\n" +
            (s.Field is { } field ? $"Шаг {field.Step:N0} · t = {field.Step * s.TimeStep:G6}" : $"Новая среда, {s.InitialSteps} шагов подготовки") +
            $" · ε = {s.Epsilon:G4} · Δt = {s.TimeStep:G4}\n" +
            $"{view} · {(s.Boundary == Excitable3DBoundary.Periodic ? "периодические границы" : "непроницаемые стенки")}\n" +
            $"Палитра: {state.ResolvePalette().Name} · Окраска: {ColoringModeName(state.ColoringMode)}";
    }
}
