using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Physarum3DMode() : GpuSimulation3DMode(Fractal3DKind.Physarum3D, new(
    "Physarum 3D · живая паутина", "Physarum · 3D",
    "Тысячи агентов чувствуют след соседей, прокладывают пути и перестраивают объёмную сеть. Светящиеся нити, нейронные сферы и живые торы; камера открывает глубину переплетений.",
    "Fractal3DPhysarum", "physarum3d"))
{
    public override string? ShapeHeader => "Агенты и живая сеть";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По плотности нитей" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Glow;
        state.EffectStrength = 1.4;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Неон");
        state.CameraDistance = 2.8;
        state.BackgroundTop = Color.FromRgb(3, 4, 12);
        state.BackgroundBottom = Color.FromRgb(1, 2, 6);
        state.SoftShadows = false; state.AmbientOcclusion = false;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp; state.ColorScale = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Нейронная туманность", s => { }),
        Preset("Живой тор", s => { s.Physarum = new() { AgentCount = 8192, SeedShape = Physarum3DSeed.Torus, SensorDistance = 2.5, Decay = .05, WarmupSteps = 100 }; s.Palette = Fractal3DPalettes.Get("Северное сияние"); }),
        Preset("Космическая оболочка", s => { s.Physarum = new() { AgentCount = 8192, SeedShape = Physarum3DSeed.Shell, SensorDistance = 3, TurnAngle = 15, WarmupSteps = 110 }; s.Palette = Fractal3DPalettes.Get("Аметист"); }),
        Preset("Две звезды", s => { s.Physarum = new() { AgentCount = 8192, SeedShape = Physarum3DSeed.TwinStars, SensorDistance = 4, WarmupSteps = 80 }; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Золотой мицелий", s => { s.Physarum = new() { AgentCount = 8192, SensorAngle = 55, TurnAngle = 35, Diffusion = .08, Decay = .045, WarmupSteps = 120 }; s.Palette = Fractal3DPalettes.Get("Раскалённый металл"); })
    ];

    public override void Validate(Fractal3DState state) => state.Physarum.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Physarum3DSettings s = state.Physarum;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.Exposure, 0), 0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.Physarum;
        return $"{prefix} · {s.AgentCount:N0} агентов · сетка {s.Size}³\n" +
            (s.Field is { } field ? $"Шаг {field.Step:N0}" : $"Новая сеть, {s.WarmupSteps} шагов подготовки") +
            $" · дальность {s.SensorDistance:G3} · испарение {s.Decay:G3}\nПалитра: {state.ResolvePalette().Name}";
    }
}
