using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class HyperbolicHoneycombMode() : Fractal3DMode(Fractal3DKind.HyperbolicHoneycomb, new(
    "Гиперболические соты", "Соты в шаре Пуанкаре · 3D",
    "Четыре регулярных разбиения гиперболического пространства. Кубы, додекаэдры и икосаэдры уменьшаются к границе шара; геодезические рёбра, прозрачные грани, срезы и гиперболические перемещения.",
    "Fractal3DHyperbolicHoneycomb", "hyperbolic-honeycomb"))
{
    public override bool UsesIterations => false;

    public override string? ShapeHeader => "Ячейки и гиперболическое пространство";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По гиперболическому радиусу",
            [Fractal3DColoringMode.IterationIndex] = "По числу отражений",
            [Fractal3DColoringMode.CrossTrap] = "По расстоянию до зеркала",
            [Fractal3DColoringMode.Escape] = "По расстоянию до ребра"
        };

    public override int? StripPixelBudget => 12_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 3.1;
        state.CameraYaw = 24; state.CameraPitch = 15;
        state.MaxSteps = 512; state.Detail = .45; state.Iterations = 64;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.SoftShadows = false; state.AmbientOcclusion = false;
        state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Додекаэдры {5,3,4} · медная сеть", _ => { }),
        Preset("Кубы {4,3,5} · пять вокруг ребра", s => s.HyperbolicHoneycomb = new() { Tessellation = HyperbolicHoneycombKind.Cubic435 }),
        Preset("Додекаэдры {5,3,5} · плотное переплетение", s => s.HyperbolicHoneycomb = new() { Tessellation = HyperbolicHoneycombKind.Dodecahedral535, Depth = 2 }),
        Preset("Икосаэдры {3,5,3} · треугольные грани", s => { s.HyperbolicHoneycomb = new() { Tessellation = HyperbolicHoneycombKind.Icosahedral353 }; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Прозрачные додекаэдры", s => s.HyperbolicHoneycomb = new() { ShowFaces = true, FaceOpacity = .12, Depth = 2 }),
        Preset("Срез гиперболического города", s => s.HyperbolicHoneycomb = new() { ShowFaces = true, FaceOpacity = .25, CutAxis = 3, CutPosition = 0 }),
        Preset("Смещённый наблюдатель", s => s.HyperbolicHoneycomb = new() { ObserverX = .5, ObserverY = .2, Depth = 3 }),
        Preset("Внутри центральной ячейки", s => { s.CameraDistance = .18; s.CameraYaw = 15; s.CameraPitch = 5;
            s.HyperbolicHoneycomb = new() { ShowFaces = true, FaceOpacity = .08, Depth = 2 }; })
    ];

    public override void Validate(Fractal3DState state) => state.HyperbolicHoneycomb.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        HyperbolicHoneycombSettings s = state.HyperbolicHoneycomb;
        return new(
            new Vector3((float)s.EdgeRadius, (float)s.FaceThickness, 0),
            new Vector4((float)s.ObserverX, (float)s.ObserverY, (float)s.ObserverZ, (float)s.FaceOpacity),
            0,
            new Vector4(s.CutAxis, (float)s.CutPosition, s.ShowEdges ? 1 : 0, s.ShowFaces ? 1 : 0));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.HyperbolicHoneycomb;
        var (p, q, r) = s.Symbol;
        return $"{prefix} · {{{p},{q},{r}}} · глубина {s.Depth}\n" +
            $"Радиус рёбер в H³: {s.EdgeRadius:G5} · непрозрачность граней: {(s.ShowFaces ? s.FaceOpacity : 0):G4}\n" +
            $"Наблюдатель: {s.ObserverX:G4}; {s.ObserverY:G4}; {s.ObserverZ:G4}\n" +
            $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
