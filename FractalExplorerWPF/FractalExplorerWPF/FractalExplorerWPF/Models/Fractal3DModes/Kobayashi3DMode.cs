using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Kobayashi3DMode() : GpuSimulation3DMode(Fractal3DKind.Kobayashi3D, new(
    "Фазовое поле Кобаяси 3D", "Кристаллизация расплава", "Рост дендритного кристалла в переохлаждённом расплаве: анизотропия границы, температурное поле и скрытая теплота.",
    "Fractal3DKobayashi", "kobayashi3d"))
{
    public override string? ShapeHeader => "Кристалл и температура";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По температуре" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Лёд");
        state.CameraDistance = 1.9; state.CameraPitch = 20;
        state.SoftShadows = false; state.AmbientOcclusion = false;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp; state.ColorScale = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Шесть осевых ветвей", s => s.Kobayashi = new()),
        Preset("Восемь диагональных ветвей", s => { s.Kobayashi = new() { Anisotropy = -.055, WarmupSteps = 8000 }; s.Palette = Fractal3DPalettes.Get("Медь и патина"); }),
        Preset("Сильное переохлаждение", s => { s.Kobayashi = new() { Undercooling = .65, LatentHeat = 1.8, Noise = .06, WarmupSteps = 4500 }; s.Palette = Fractal3DPalettes.Get("Огонь"); }),
        Preset("Восемь зародышей", s => { s.Kobayashi = new() { SeedShape = Kobayashi3DSeed.EightSpheres, WarmupSteps = 3000 }; s.CameraDistance = 3.4; }),
        Preset("Кольцевая затравка", s => { s.Kobayashi = new() { SeedShape = Kobayashi3DSeed.Ring, WarmupSteps = 3000 }; s.Palette = Fractal3DPalettes.Get("Аметист"); })
    ];

    public override void Validate(Fractal3DState state) => state.Kobayashi.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Kobayashi3DSettings s = state.Kobayashi;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.Undercooling, (float)Math.Max(s.LatentHeat, .1)),
            0, BoxInversion(state));
    }
}
