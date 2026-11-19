using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class ParametricSurfaceMode() : Fractal3DMode(Fractal3DKind.ParametricSurface, new(
    "Суперформула и параметрические поверхности", "Параметрические поверхности · 3D",
    "Два профиля суперформулы Гилиса или три уравнения x(u,v), y(u,v), z(u,v). Пресеты, область параметров, сетка и окраска по u/v.",
    "Fractal3DParametricSurface", "parametric-surface"))
{
    public override bool UsesIterations => false;
    public override bool HasDedicatedShapeEditor => true;
    public override bool NeedsWidePanel => true;
    public override bool UsesRayMarchQuality => false;
    protected override bool HasOrbit => false;
    public override int? StripPixelBudget => 32_000;
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По параметру u",
            [Fractal3DColoringMode.CrossTrap] = "По параметру v"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ParametricSurface = new();
        state.CameraDistance = 3.9;
        state.CameraYaw = 28;
        state.CameraPitch = 40;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SurfaceColor = Color.FromRgb(116, 195, 204);
        state.SoftShadows = false;
        state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Морская звезда · суперформула", _ => { }),
        Preset("Сфера · исходные окружности", s => s.ParametricSurface = new() { Longitude = new(), Latitude = new() }),
        Preset("Кристалл · острые рёбра", s => s.ParametricSurface = new()
            { Longitude = new(4, 1, 1, 1), LinkProfiles = true }),
        Preset("Скруглённый куб", s => s.ParametricSurface = new()
            { Longitude = new(4, 4, 4, 4), LinkProfiles = true }),
        Preset("Цветок · неравные лепестки", s => s.ParametricSurface = new()
            { Longitude = new(7, .42, 1.4, 1.4, 1, .8), Latitude = new(4, .8, 1.7, 1.7) }),
        Preset("Ребристая тыква", s => s.ParametricSurface = new()
            { Longitude = new(16, 1.15, 3.25, 3.25), Latitude = new(4, 1.6, 1.4, 1.4) }),
        Equations("Тор · два радиуса", new(), new(0, 2 * Math.PI, 0, 2 * Math.PI)),
        Equations("Лента Мёбиуса", new("(a+v*cos(u/2))*cos(u)", "v*sin(u/2)", "(a+v*cos(u/2))*sin(u)"),
            new(0, 2 * Math.PI, -.42, .42), grid: true),
        Equations("Геликоид · винтовая поверхность", new("v*cos(u)", "b*u", "v*sin(u)"),
            new(-2 * Math.PI, 2 * Math.PI, -1, 1), b: .22, grid: true),
        Equations("Катеноид · поверхность вращения",
            new("a*(exp(v)+exp(-v))/2*cos(u)", "a*v", "a*(exp(v)+exp(-v))/2*sin(u)"),
            new(0, 2 * Math.PI, -1.1, 1.1), grid: true),
        Equations("Раковина · логарифмическая спираль",
            new("exp(c*u)*(a+b*cos(v))*cos(u)", "exp(c*u)*b*sin(v)-u*0.15", "exp(c*u)*(a+b*cos(v))*sin(u)"),
            new(0, 4 * Math.PI, 0, 2 * Math.PI), b: .42),
        Equations("Седло · гиперболический параболоид", new("u", "a*u^2-b*v^2", "v"),
            new(-1, 1, -1, 1), b: 1, grid: true),
        Equations("Поверхность Эннепера", new("u-u^3/3+u*v^2", "u^2-v^2", "v-v^3/3+v*u^2"),
            new(-1.4, 1.4, -1.4, 1.4))
    ];

    private Fractal3DState Equations(string name, ParametricEquations formulas, ParametricDomain domain,
        double b = .36, bool grid = false) =>
        Preset(name, s => s.ParametricSurface = new()
        {
            Family = ParametricSurfaceFamily.Equations, Equations = formulas, Domain = domain, B = b, ShowGrid = grid
        });

    public override void Validate(Fractal3DState state) => (state.ParametricSurface ?? new()).Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = state.ParametricSurface ?? new();
        return new(Vector3.Zero, new Vector4(s.ShowGrid ? s.GridLines : 0, 0, 0, 0), 0, Vector4.Zero);
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.ParametricSurface ?? new();
        string geometry = s.Family == ParametricSurfaceFamily.Superformula
            ? $"Долгота: {Profile(s.Longitude)}\nШирота: {(s.LinkProfiles ? "как долгота" : Profile(s.Latitude))}"
            : $"x = {s.Equations.X}\ny = {s.Equations.Y}\nz = {s.Equations.Z}\n" +
              $"u: {s.Domain.UMin:G5}…{s.Domain.UMax:G5} · v: {s.Domain.VMin:G5}…{s.Domain.VMax:G5}";
        return $"{prefix} · {ParametricSurfaceSettings.Name(s.Family)}\n{geometry}\nРазбиение: {s.USegments} × {s.VSegments}";
    }

    private static string Profile(SuperformulaProfile p) =>
        $"m = {p.M:G5} · n₁/n₂/n₃ = {p.N1:G5}/{p.N2:G5}/{p.N3:G5} · a/b = {p.A:G5}/{p.B:G5}";
}
