using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Turing3DMode() : GpuSimulation3DMode(Fractal3DKind.Turing3D, new(
    "Узоры Тьюринга 3D", "Тьюринг · 3D",
    "Маккейб и классические реакции Брюсселятора, Шнакенберга и Гирера–Мейнхардта формируют узор в каждой точке объёма. Симметрии многогранников, шар, оболочка и срез открывают внутреннюю структуру.",
    "Fractal3DTuring", "turing3d"))
{
    public override string? ShapeHeader => "Узор и эволюция";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По масштабу узора",
            [Fractal3DColoringMode.CrossTrap] = "По расстоянию от центра"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Turing = new() { Region = Turing3DRegion.Sphere, DetailSize = .6, Layers = Turing3DSettings.DefaultLayers(3) };
        state.ShadingStyle = Fractal3DShadingStyle.Classic;
        // По расстоянию от центра: внешняя оболочка светлая, глубина в просветах — тёмная.
        state.ColoringMode = Fractal3DColoringMode.CrossTrap;
        state.ColorScale = 1.4;
        state.ColorOffset = -.4;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
        state.CameraDistance = 3.1;
        state.SoftShadows = false;
        state.AmbientOcclusion = true;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Коралловый шар", _ => { }),
        Preset("Радиолярия · икосаэдр", s =>
        {
            s.Turing = new() { Symmetry = Turing3DSymmetry.Icosahedral, Mirror = true, Boundary = TuringBoundary.Reflect,
                Region = Turing3DRegion.Shell, ShellThickness = .12, DetailSize = .5, Seed = 4242 };
            s.Palette = Fractal3DPalettes.Get("Лёд");
            s.ColoringMode = Fractal3DColoringMode.CrossTrap;
            s.ColorScale = 8; s.ColorOffset = -6.8;
        }),
        Preset("Оболочка · октаэдр", s =>
        {
            s.Turing = new() { Symmetry = Turing3DSymmetry.Octahedral, Mirror = true, Boundary = TuringBoundary.Reflect,
                Region = Turing3DRegion.Shell, ShellThickness = .15, DetailSize = .55, Seed = 808 };
            s.Palette = Fractal3DPalettes.Get("Аметист");
            s.ColorScale = 6.5; s.ColorOffset = -5.3;
        }),
        Preset("Тетраэдрический орнамент", s =>
        {
            s.Turing = new() { Symmetry = Turing3DSymmetry.Tetrahedral, Mirror = true, Boundary = TuringBoundary.Reflect,
                Region = Turing3DRegion.Sphere, DetailSize = .6, Seed = 31415 };
            s.Palette = Fractal3DPalettes.Get("Мрамор");
            s.ShadingStyle = Fractal3DShadingStyle.Clay;
            s.ColoringMode = Fractal3DColoringMode.Material;
        }),
        Preset("Шестилучевой шар · срез", s =>
        {
            s.Turing = new() { Symmetry = Turing3DSymmetry.Axial, Arms = 6, Mirror = true, Boundary = TuringBoundary.Reflect,
                Region = Turing3DRegion.Sphere, DetailSize = .6, CutAxis = 2, CutPosition = .2, Seed = 602 };
            s.Palette = Fractal3DPalettes.Get("Закат");
            s.CameraPitch = 40;
        }),
        Preset("Губчатый куб · срез", s =>
        {
            s.Turing = new() { Region = Turing3DRegion.Cube, CutAxis = 3, CutPosition = .3, Seed = 2718 };
            s.Palette = Fractal3DPalettes.Get("Виридис");
            s.ShadingStyle = Fractal3DShadingStyle.Studio;
            s.ColoringMode = Fractal3DColoringMode.Height;
            s.ColorScale = .5; s.ColorOffset = .5;
            s.CameraDistance = 3.6;
        })
    ];

    public override void Validate(Fractal3DState state) => state.Turing.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Turing3DSettings s = state.Turing;
        return new(
            new Vector3(s.Size, (float)s.Level, (float)s.SheetThickness),
            new Vector4(s.CutAxis, (float)s.CutPosition, (int)s.Region, (float)s.ShellThickness),
            0, BoxInversion(state));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        Turing3DSettings turing = state.Turing;
        string region = turing.Region switch
        {
            Turing3DRegion.Sphere => "шар",
            Turing3DRegion.Shell => $"оболочка {turing.ShellThickness:P0} радиуса",
            _ => "весь куб"
        };
        return $"{prefix} · сетка {turing.Size}³ · " +
               (turing.Field is { } field ? $"шаг {field.Step:N0}" : $"новое поле, {turing.WarmupSteps} шагов развития") + "\n" +
               $"Масштабов: {turing.Layers.Count(layer => layer.Enabled)} · детали {turing.DetailSize:G3} · " +
               $"{Turing3DSettings.SymmetryName(turing.Symmetry, turing.Arms, turing.Mirror)} · {region}\n" +
               $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
