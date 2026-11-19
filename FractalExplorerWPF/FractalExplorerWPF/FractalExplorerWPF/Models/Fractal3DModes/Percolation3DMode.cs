using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Percolation3DMode() : Fractal3DMode(Fractal3DKind.Percolation3D, new(
    "Перколяция и пористые среды 3D", "Перколяция · 3D",
    "Откройте сквозной путь в случайной или пористой среде. Меняйте порог, исследуйте кластеры и срезы кубической решётки.",
    "Fractal3DPercolation", "percolation3d"))
{
    public override bool UsesIterations => false;
    public override bool IsDensityVolume => true;
    public override bool NeedsWidePanel => true;
    public override string? ShapeHeader => "Кубическая решётка";
    public override int? GetStripPixelBudget(Fractal3DState state) =>
        28_000 * 48 / (state.Percolation3D?.Size ?? 48);

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По кластерам",
            [Fractal3DColoringMode.CrossTrap] = "По размеру кластера"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Percolation3D = new();
        state.CameraDistance = 5.55;
        state.CameraYaw = 34;
        state.CameraPitch = 22;
        state.FieldOfView = 36;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalettes.Get("Спектр");
        state.BackgroundTop = Color.FromRgb(19, 33, 56);
        state.BackgroundBottom = Color.FromRgb(5, 10, 19);
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Сквозной маршрут · случайная решётка", _ => { }),
        Preset("До перехода · разобщённые островки", s =>
            s.Percolation3D = new() { Probability = .278, View = Percolation3DView.Clusters }),
        Preset("Около критического порога · крупнейший кластер", s =>
            s.Percolation3D = new() { Probability = Percolation3DSettings.CriticalProbability, View = Percolation3DView.Largest }),
        Preset("Выше порога · сквозные кластеры в разрезе", s =>
            s.Percolation3D = new() { Probability = .37, View = Percolation3DView.Spanning, Cut = Percolation3DCut.Cutaway }),
        Preset("Пористая порода · разрез", s =>
        {
            s.Percolation3D = new() { Size = 72, Probability = .415, Medium = Percolation3DMedium.Correlated,
                View = Percolation3DView.Clusters, Cut = Percolation3DCut.Cutaway, CutPosition = .62 };
            s.ColoringMode = Fractal3DColoringMode.CrossTrap;
            s.Palette = Fractal3DPalettes.Get("Медь и патина");
        }),
        Preset("Пористая порода · маршрут внутри образца", s =>
            s.Percolation3D = new() { Size = 72, Probability = .35, Medium = Percolation3DMedium.Correlated }),
        Preset("Слой XY · поперечный срез пор", s =>
        {
            s.Percolation3D = new() { Size = 72, Probability = .47, Medium = Percolation3DMedium.Correlated,
                View = Percolation3DView.Clusters, Cut = Percolation3DCut.Layer };
            s.Palette = Fractal3DPalettes.Get("Аметист");
            s.CameraYaw = 15;
            s.CameraPitch = 15;
        })
    ];

    public override void Validate(Fractal3DState state) => (state.Percolation3D ?? new()).Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = state.Percolation3D ?? new();
        return new(new Vector3(s.Size, (int)s.View, 0),
            new Vector4((int)s.Cut, (int)s.CutAxis, s.SliceIndex, s.ShowPath ? 1 : 0), 0, Vector4.Zero);
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.Percolation3D ?? new();
        string medium = s.Medium == Percolation3DMedium.Correlated ? "Коррелированные поры" : "Независимые ячейки";
        return $"{prefix} · {medium}\nРешётка {s.Size}³ · p = {s.Probability:G6} · seed {s.Seed} · проход по {s.Axis}";
    }
}
