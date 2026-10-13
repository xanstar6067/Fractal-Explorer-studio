using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class GrayScott3DMode() : GpuSimulation3DMode(Fractal3DKind.GrayScott3D, new(
    "Gray–Scott 3D · объёмная реакция–диффузия", "Gray–Scott · 3D",
    "Два вещества реагируют и распространяются внутри объёма. Наблюдайте развитие пространственных узоров, добавляйте сферические затравки и открывайте внутреннюю структуру срезом.",
    "Fractal3DGrayScott", "gray-scott3d"))
{
    public override string? ShapeHeader => "Среда и эволюция";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По концентрации V" };

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
        Preset("Пористый объём", _ => { }),
        Preset("Островки · восемь затравок", s => { s.GrayScott = new() { SeedShape = GrayScott3DSeed.Spheres, Feed = .03, Kill = .057 }; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Кольцевой фронт", s => { s.GrayScott = new() { SeedShape = GrayScott3DSeed.Ring, Feed = .014, Kill = .045 }; s.Palette = Fractal3DPalettes.Get("Огонь"); }),
        Preset("Живые домены", s => { s.GrayScott = new() { Feed = .026, Kill = .051 }; s.ShadingStyle = Fractal3DShadingStyle.Density; s.Palette = Fractal3DPalettes.Get("Неон"); }),
        Preset("Чистая среда · добавьте затравку", s => s.GrayScott = new() { SeedShape = GrayScott3DSeed.Empty })
    ];

    public override void Validate(Fractal3DState state) => state.GrayScott.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        GrayScott3DSettings s = state.GrayScott;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, 0, 0), 0, BoxInversion(state));
    }
}
