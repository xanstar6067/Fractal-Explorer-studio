using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Newton3DMode() : Fractal3DMode(Fractal3DKind.Newton3D, new(
    "Ньютон 3D", "Ньютон 3D",
    "Трёхмерный срез четырёхмерного бикомплексного Ньютона qⁿ − 1: границы сходимости, отдельные бассейны и пары корней. Координата w меняет срез; отсечение открывает внутреннее строение.",
    "Fractal3DNewton", "newton3d"))
{
    public override bool UsesIterations => false;
    public override bool IsDensityVolume => true;
    public override int VolumePreparationProgress => 60;
    public override string? ShapeHeader => "Бассейны сходимости";
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По паре корней",
            [Fractal3DColoringMode.CrossTrap] = "По корню +",
            [Fractal3DColoringMode.IterationIndex] = "По скорости сходимости",
            [Fractal3DColoringMode.Escape] = "По корню −"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 3.8;
        state.CameraYaw = 32;
        state.CameraPitch = 24;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false;
        state.AmbientOcclusion = true;
        state.Ssaa = 2;
        state.Palette = Fractal3DPalettes.Get("Спектр");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Кубические границы · w = 0,22", _ => { }),
        Preset("Один бассейн · три корня", s => s.Newton = new() { Region = Newton3DRegion.PlusBasin, PlusRoot = 1 }),
        Preset("Пересечение бассейнов · пара 1 / 2", s => s.Newton = new() {
            Region = Newton3DRegion.RootPair, PlusRoot = 0, MinusRoot = 1, SliceW = -.2, CutAxis = 0 }),
        Preset("Пятая степень · внутренние границы", s => s.Newton = new() {
            Degree = 5, SliceW = .38, BoundaryIterations = 16, CutPosition = -.15 }),
        Preset("Медленная сходимость · релаксация 1,45", s => {
            s.Newton = new() { Relaxation = 1.45, BoundaryIterations = 22, SliceW = .1 };
            s.ColoringMode = Fractal3DColoringMode.IterationIndex;
        }),
        Preset("Классический Ньютон · плоскость z = 0", s => {
            s.Newton = new() { SliceW = 0, SliceOnly = true, CutAxis = 3, CutPosition = 0 };
            s.CameraYaw = 0; s.CameraPitch = 0; s.CameraDistance = 3;
        })
    ];

    public override void Validate(Fractal3DState state) => state.Newton.Validate();
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3(state.Newton.Size, .5f, state.Newton.Degree),
        new Vector4(state.Newton.CutAxis, (float)state.Newton.CutPosition, 0, 0), 0,
        new Vector4(0, state.Newton.SliceOnly ? 1 : 0, 0, 0));
    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.Newton;
        return $"{prefix} · q^{s.Degree} − 1 · {s.Size}³ · {s.MaxIterations} итераций\n" +
            $"w = {s.SliceW:G5}; полуразмер {s.Extent:G5}; релаксация {s.Relaxation:G5}\n" +
            (s.Region switch { Newton3DRegion.Boundary => $"Границы: от {s.BoundaryIterations} итераций",
                Newton3DRegion.PlusBasin => $"Бассейн +: {s.PlusRoot + 1}",
                _ => $"Пара корней: {s.PlusRoot + 1} / {s.MinusRoot + 1}" }) +
            (s.SliceOnly ? " · плоскость среза" : "") + "\n" +
            $"Камера: азимут {state.CameraYaw:G5}°, наклон {state.CameraPitch:G5}°, расстояние {state.CameraDistance:G5}";
    }
}
