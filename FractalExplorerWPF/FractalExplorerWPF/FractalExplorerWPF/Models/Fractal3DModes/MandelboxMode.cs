using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class MandelboxMode() : Fractal3DMode(Fractal3DKind.Mandelbox, new(
    "Мандельбокс", "Мандельбокс",
    "Чередование свёртки по кубу и инверсии по сфере, кубу или вытянутой форме с масштабированием даёт архитектурную структуру с галереями и арками.",
    "Fractal3DMandelbox", "mandelbox"))
{
    public override bool UsesBailout => true;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 12;
        state.BoxScale = 2;
        state.BoxMinRadius = 0.7;
        state.BoxFoldingLimit = 1;
        state.Bailout = 100;
        state.CameraDistance = 24;
        state.CameraPitch = 22;
        state.MaxDistance = 120;
        state.MaxSteps = 200;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Масштаб 2 — классическая коробка", _ => { }),
        Preset("Кубическая инверсия", s => s.BoxInversionShape = BoxInversionShape.Cube),
        Preset("Вытянутая инверсия", s =>
        {
            s.BoxInversionShape = BoxInversionShape.Ellipsoid;
            s.BoxInversionStretch = 1.7;
        }),
        Preset("Скруглённый куб", s =>
        {
            s.BoxInversionShape = BoxInversionShape.RoundedCube;
            s.BoxInversionPower = 6;
        }),
        Preset("Масштаб 3 — плотная решётка", s =>
        {
            s.BoxScale = 3;
            s.CameraDistance = 14;
            s.Iterations = 14;
        }),
        Preset("Масштаб −1.5 — вывернутый", s =>
        {
            s.BoxScale = -1.5;
            s.CameraDistance = 10;
            s.Iterations = 14;
            s.Palette = Fractal3DPalettes.Get("Медь и патина");
        }),
        Preset("Полированный металл", s =>
        {
            s.ShadingStyle = Fractal3DShadingStyle.Metal;
            s.ColoringMode = Fractal3DColoringMode.CrossTrap;
            s.Palette = Fractal3DPalettes.Get("Мрамор");
            s.BackgroundTop = Color.FromRgb(72, 96, 140);
            s.BackgroundBottom = Color.FromRgb(12, 14, 20);
            s.Ambient = 0.35;
            s.Specular = 0.9;
            s.SkyLightMix = 0.35;
        }),
        Preset("Внутри галереи", s =>
        {
            s.CameraDistance = 6;
            s.CameraYaw = 62;
            s.CameraPitch = 4;
            s.TargetX = 1.6;
            s.TargetZ = 0.8;
            s.MaxSteps = 256;
            s.Detail = 0.6;
        })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3(
            (float)state.BoxScale,
            (float)(state.BoxMinRadius * state.BoxMinRadius),
            (float)state.BoxFoldingLimit),
        JuliaConstant(state), 0, BoxInversion(state));

    protected override string ShapeSummary(Fractal3DState state) =>
        $"Масштаб: {state.BoxScale:G6} · Мин. радиус: {state.BoxMinRadius:G6} · Свёртка: {state.BoxFoldingLimit:G6} · Инверсия: {Fractal3DCatalog.BoxInversionShapeName(state.BoxInversionShape)}";
}
