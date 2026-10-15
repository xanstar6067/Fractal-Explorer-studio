using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class KleinianMode() : Fractal3DMode(Fractal3DKind.Kleinian, new(
    "Группы Клейна · 3D", "Группы Клейна · пространственные множества",
    "Инверсии в сферах порождают пространственное предельное множество. Касающиеся сферы создают сцепленный ажур, уменьшение их радиуса разделяет его на фрактальные скопления. Показано конечное приближение с управляемой глубиной и толщиной.",
    "Fractal3DKleinian", "kleinian"))
{
    public override Fractal3DField IterationsField { get; } = new("Глубина приближения (1–10)", "Глубина", 1, 10, IsInteger: true);
    public override string ShapeHeader => "Сферы и предельное множество";
    public override int? StripPixelBudget => 12_000;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По исходной сфере",
            [Fractal3DColoringMode.CrossTrap] = "По размеру детали",
            [Fractal3DColoringMode.IterationIndex] = "По глубине приближения"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 7;
        state.CameraDistance = 2.4;
        state.CameraYaw = 32;
        state.CameraPitch = 22;
        state.MaxSteps = 256;
        state.Detail = .4;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SurfaceColor = System.Windows.Media.Color.FromRgb(100, 190, 204);
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Октаэдр · сцепленный ажур", _ => { }),
        Preset("Тетраэдр · четыре переплетения", s => s.Kleinian = new() { Configuration = KleinianConfiguration.Tetrahedron }),
        Preset("Куб · восемь вершин", s => s.Kleinian = new() { Configuration = KleinianConfiguration.Cube, MinRadius = .006 }),
        Preset("Островки Шоттки", s => { s.Kleinian = new() { RadiusRatio = .86, Thickness = .006 }; s.ColoringMode = Fractal3DColoringMode.OrbitTrap; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Внутри переплетения", s => { s.CameraDistance = 1.15; s.CameraYaw = 45; s.CameraPitch = 0; s.Kleinian = new() { MinRadius = .002, Thickness = .002 }; s.Iterations = 9; }),
        Preset("Первое поколение · исходные сферы", s => { s.Iterations = 1; s.CameraDistance = 4.2; s.Kleinian = new() { Thickness = 0 }; s.ColoringMode = Fractal3DColoringMode.OrbitTrap; })
    ];

    public override void Validate(Fractal3DState state)
    {
        state.Kleinian ??= new();
        state.Kleinian.Validate();
        state.Iterations = Math.Clamp(state.Iterations, 1, 10);
    }

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        new(new Vector3(2, (float)state.Kleinian.Thickness, 0), Vector4.Zero, 0, Vector4.Zero);

    protected override string ShapeSummary(Fractal3DState state) =>
        $"{KleinianSettings.ConfigurationName(state.Kleinian.Configuration)} · Радиус: {state.Kleinian.RadiusRatio:G4} · " +
        $"Деталь: {state.Kleinian.MinRadius:G4} · Утолщение: {state.Kleinian.Thickness:G4}";
}
