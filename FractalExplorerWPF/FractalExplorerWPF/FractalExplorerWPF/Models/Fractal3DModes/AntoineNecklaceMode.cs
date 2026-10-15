using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class AntoineNecklaceMode() : Fractal3DMode(Fractal3DKind.AntoineNecklace, new(
    "Ожерелье Антуана", "Ожерелье Антуана",
    "Каждый тор заменяется замкнутой цепочкой сцепленных колец. Следующие поколения вложены в предыдущие; показано конечное приближение пространственного канторова множества.",
    "Fractal3DAntoineNecklace", "antoine-necklace"))
{
    public override Fractal3DField IterationsField { get; } = new("Поколение (0–3)", "Поколение", 0, 3, IsInteger: true);
    public override string ShapeHeader => "Рекурсивная цепочка колец";
    public override int? StripPixelBudget => 8_000;
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По исходному звену",
            [Fractal3DColoringMode.CrossTrap] = "По наклону кольца",
            [Fractal3DColoringMode.IterationIndex] = "По звену внутри цепочки"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 2;
        state.CameraDistance = 3.3;
        state.CameraYaw = 25;
        state.CameraPitch = 52;
        state.MaxSteps = 320;
        state.Detail = .18;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SurfaceColor = System.Windows.Media.Color.FromRgb(105, 191, 206);
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Ожерелье · цепочки в цепочках", _ => { }),
        Preset("Первое поколение · 24 сцепленных кольца", s => s.Iterations = 1),
        Preset("Одно звено крупным планом", s =>
        {
            s.TargetX = 1; s.CameraDistance = .64; s.CameraYaw = 12; s.CameraPitch = 42;
            s.ColoringMode = Fractal3DColoringMode.IterationIndex; s.Palette = Fractal3DPalettes.Get("Лёд");
        }),
        Preset("Третье поколение · внутри звена", s =>
        {
            s.Iterations = 3; s.TargetX = 1; s.TargetY = .1827366691;
            s.CameraDistance = .15; s.CameraYaw = 18; s.CameraPitch = 35; s.Detail = .12;
        }),
        Preset("Наклон 60° · 32 звена", s =>
        {
            s.Antoine = new() { Links = 32, Tilt = 60, ScaleFactor = 1.5, Thickness = .8 };
            s.ColoringMode = Fractal3DColoringMode.OrbitTrap; s.Palette = Fractal3DPalettes.Get("Неон");
        }),
        Preset("Исходный тор · поколение 0", s => { s.Iterations = 0; s.CameraDistance = 3.8; })
    ];

    public override void Validate(Fractal3DState state)
    {
        state.Antoine ??= new();
        state.Antoine.Validate();
        state.Iterations = Math.Clamp(state.Iterations, 0, 3);
    }

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        new(new Vector3(1.5f, 0, 0), Vector4.Zero, 0, Vector4.Zero);

    protected override string ShapeSummary(Fractal3DState state) =>
        $"{state.Antoine.Links} звена · Масштаб: {state.Antoine.ScaleFactor:G4} · " +
        $"Толщина: {state.Antoine.Thickness:G4} · Наклон: {state.Antoine.Tilt:G4}°";
}
