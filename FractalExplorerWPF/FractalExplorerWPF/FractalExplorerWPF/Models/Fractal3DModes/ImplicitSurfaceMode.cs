using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class ImplicitSurfaceMode() : Fractal3DMode(Fractal3DKind.ImplicitSurface, new(
    "Лаборатория поверхностей F(x,y,z) = 0", "Поверхности из формул · 3D",
    "Сфера, тор, алгебраические скульптуры и самопересечения из вещественной формулы. Параметры a/b/c, уровень, область построения, срезы и средняя кривизна.",
    "Fractal3DImplicitSurface", "implicit-surface"))
{
    public override bool UsesIterations => false;

    public override string? ShapeHeader => "Формула и область построения";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По средней кривизне" };

    public override int? StripPixelBudget => 6_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 3.8;
        state.CameraYaw = 28; state.CameraPitch = 28;
        state.MaxSteps = 384; state.Detail = .35;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SurfaceColor = Color.FromRgb(93, 183, 189);
        state.SoftShadows = false; state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Тор · большое и малое кольца", _ => { }),
        Preset("Сфера · радиус a", s => s.ImplicitSurface = new() { Formula = "x^2+y^2+z^2-a^2", Extent = 1.3 }),
        Preset("Эллипсоид · три полуоси", s => s.ImplicitSurface = new() { Formula = "(x/a)^2+(y/b)^2+(z/c)^2-1", A = 1, B = .65, C = .4, Extent = 1.2 }),
        Preset("Сердце · алгебраическая скульптура", s => { s.ImplicitSurface = new() { Formula = "(x^2+2.25*y^2+z^2-1)^3-x^2*z^3-0.1125*y^2*z^3", Extent = 1.5 }; s.CameraPitch = 70; s.SurfaceColor = Color.FromRgb(216, 93, 131); }),
        Preset("Римская поверхность · самопересечения", s => s.ImplicitSurface = new() { Formula = "x^2*y^2+y^2*z^2+z^2*x^2-a*x*y*z", Extent = .65 }),
        Preset("Гироид · уровень c", s => s.ImplicitSurface = new() { Formula = "sin(a*x)*cos(a*y)+sin(a*z)*cos(a*x)+sin(a*y)*cos(a*z)-c", A = 3.141592653589793, Extent = 1 }),
        Preset("Две сферы · расстояние a", s => s.ImplicitSurface = new() { Formula = "((x-a)^2+y^2+z^2-b^2)*((x+a)^2+y^2+z^2-b^2)", A = .55, B = .7, Extent = 1.5 }),
        Preset("Тор · средняя кривизна", s => { s.ColoringMode = Fractal3DColoringMode.OrbitTrap; s.ColorRepeat = Fractal3DColorRepeat.Clamp; s.Palette = Fractal3DPalettes.Get("Спектр"); })
    ];

    public override void Validate(Fractal3DState state) => state.ImplicitSurface.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        ImplicitSurfaceSettings s = state.ImplicitSurface;
        return new(
            new Vector3((float)s.Extent, (float)s.Level, 0),
            new Vector4((float)s.A, (float)s.B, (float)s.C, 0),
            0,
            new Vector4(s.CutAxis, (float)(s.CutPosition / s.Extent), 0, 0));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.ImplicitSurface;
        return $"{prefix} · F(x,y,z) = {s.Level:G6}\n{s.Formula}\n" +
            $"a: {s.A:G6} · b: {s.B:G6} · c: {s.C:G6} · Область: ±{s.Extent:G6}\n" +
            (s.CutAxis is > 0 and <= 3 ? $"Срез {"XYZ"[s.CutAxis - 1]} ≤ {s.CutPosition:G6}\n" : "") +
            $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
