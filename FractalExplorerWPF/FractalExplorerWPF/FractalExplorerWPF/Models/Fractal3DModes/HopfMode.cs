using System.Numerics;
using FractalExplorerWPF.Core.Rendering3D;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class HopfMode() : Fractal3DMode(Fractal3DKind.Hopf, new(
    "Расслоение Хопфа", "Расслоение Хопфа · 4D → 3D",
    "Каждая точка базовой сферы задаёт окружность в четырёхмерной сфере. В стереографической проекции любые два разных кольца сцеплены один раз; широты превращаются в торы. Выбирайте точки и вращайте всю конструкцию в 4D.",
    "Fractal3DHopf", "hopf"))
{
    public override bool UsesIterations => false;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По долготе на базовой сфере",
            [Fractal3DColoringMode.CrossTrap] = "По широте на базовой сфере",
            [Fractal3DColoringMode.IterationIndex] = "По номеру кольца"
        };

    public override int? StripPixelBudget => 24_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 6.2;
        state.CameraPitch = 22;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalettes.Get("Спектр");
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
        state.MaxSteps = 256;
        state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Вложенные торы", _ => { }),
        Preset("Один тор · 32 кольца", s => { s.Hopf = new() { Family = HopfFamily.Latitude, Fibers = 32, Latitude = 30 }; }),
        Preset("Три сцепленных тора", s => { s.Hopf = new() { Family = HopfFamily.LinkedTori, Fibers = 24, Layers = 3, Latitude = 65, XW = 25 }; }),
        Preset("Кольца по всей сфере", s => { s.Hopf = new() { Family = HopfFamily.Sphere, Fibers = 64, Thickness = .012, XW = 30, YW = -20 }; }),
        Preset("Зацепление Хопфа · два кольца", s =>
        {
            s.Hopf = new() { Family = HopfFamily.Custom, Points = [new(0, 0), new(90, 0)], Thickness = .045, ClipRadius = 3 };
            s.CameraDistance = 3.5;
        }),
        Preset("Кольцо через бесконечность", s =>
        {
            s.Hopf = new() { Family = HopfFamily.Custom, Points = [new(0, -90), new(0, 90), new(90, 10)], Thickness = .03, ClipRadius = 2.5 };
            s.ColoringMode = Fractal3DColoringMode.IterationIndex;
        })
    ];

    public override void Validate(Fractal3DState state) => state.Hopf.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.Hopf.ClipRadius,
            state.Hopf.OnlySelected ? 1 : state.Hopf.BasePoints().Count, HopfGeometry.Scale * HopfGeometry.Scale),
        JuliaConstant(state), 0, BoxInversion(state));

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {state.Hopf.BasePoints().Count} колец · толщина {state.Hopf.Thickness:G4}\n" +
        $"Видимая область: {state.Hopf.ClipRadius:G4} · " +
        (state.Hopf.OnlySelected ? $"только кольцо {state.Hopf.SelectedFiber + 1}" : "всё семейство") + "\n" +
        $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
}
