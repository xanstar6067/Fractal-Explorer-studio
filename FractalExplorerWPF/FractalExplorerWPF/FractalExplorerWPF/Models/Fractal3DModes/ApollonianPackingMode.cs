using FractalExplorerWPF.Core.Rendering3D;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class ApollonianPackingMode() : Fractal3DMode(Fractal3DKind.ApollonianPacking, new(
    "Аполлонова упаковка сфер", "Аполлонова упаковка сфер",
    "Рекурсивная упаковка взаимно касающихся сфер внутри внешней сферы. Число поколений задаёт глубину заполнения промежутков.",
    "Fractal3DApollonian", "apollonian-spheres"))
{
    public override Fractal3DField IterationsField { get; } =
        new("Поколения сфер (1–5)", "Поколения сфер", 1, ApollonianSpherePacking.MaxGeneration, IsInteger: true);

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По диаметру сферы",
            [Fractal3DColoringMode.CrossTrap] = "По положению сферы",
            [Fractal3DColoringMode.IterationIndex] = "По масштабу сферы",
            [Fractal3DColoringMode.Escape] = "По радиусу сферы"
        };

    /// <summary>
    /// Поиск ближайшей сферы обходит дерево, а не одну формулу: полосы короче, чтобы экспорт
    /// не занимал ГП дольше сторожевого таймера драйвера.
    /// </summary>
    public override double StripCostFactor => 5;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 4;
        state.CameraDistance = 3.7;
        state.CameraYaw = 32;
        state.CameraPitch = 20;
        state.MaxDistance = 30;
        state.MaxSteps = 192;
        state.ColoringMode = Fractal3DColoringMode.IterationIndex;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Четыре поколения — общий вид", _ => { }),
        Preset("Два поколения — крупные сферы", s =>
        {
            s.Iterations = 2;
            s.CameraYaw = 25;
        }),
        Preset("Пять поколений — сеть касаний", s =>
        {
            s.Iterations = 5;
            s.CameraDistance = 3.2;
            s.MaxSteps = 240;
            s.Detail = 0.6;
        }),
        Preset("Центральный просвет", s =>
        {
            s.Iterations = 5;
            s.CameraDistance = 1.7;
            s.CameraYaw = 44;
            s.CameraPitch = 8;
            s.TargetX = 0.08;
            s.TargetY = -0.05;
            s.Detail = 0.5;
            s.MaxSteps = 280;
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) => "Взаимно касающиеся сферы";
}
