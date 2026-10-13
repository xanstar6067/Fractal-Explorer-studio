namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class MandelbulbMode() : Fractal3DMode(Fractal3DKind.Mandelbulb, new(
    "Мандельбульб", "Мандельбульб",
    "Трёхмерное обобщение множества Мандельброта через сферические координаты: z → zⁿ + c с произвольной степенью n.",
    "Fractal3DMandelbulb", "mandelbulb"))
{
    public override bool UsesPower => true;

    public override bool UsesBailout => true;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Power = 8;
        state.Iterations = 9;
        state.CameraDistance = 2.8;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Степень 8 — классический вид", _ => { }),
        Preset("Степень 8 — северный полюс", s =>
        {
            s.CameraPitch = 68;
            s.CameraYaw = 12;
            s.CameraDistance = 2.4;
        }),
        Preset("Степень 3 — гладкие лепестки", s =>
        {
            s.Power = 3;
            s.Iterations = 12;
            s.CameraDistance = 3;
        }),
        Preset("Степень 16 — игольчатая корона", s =>
        {
            s.Power = 16;
            s.Iterations = 8;
            s.Palette = Fractal3DPalettes.Get("Лёд");
        }),
        Preset("Светящиеся складки", s =>
        {
            s.Iterations = 10;
            s.ShadingStyle = Fractal3DShadingStyle.Glow;
            s.ColoringMode = Fractal3DColoringMode.IterationIndex;
            s.Palette = Fractal3DPalettes.Get("Неон");
            s.ColorScale = 1.4;
            s.EffectStrength = 1.3;
        }),
        Preset("Крупный план складки", s =>
        {
            s.CameraDistance = 1.35;
            s.CameraYaw = 118;
            s.CameraPitch = 6;
            s.TargetX = 0.32;
            s.TargetY = 0.12;
            s.Detail = 0.5;
            s.MaxSteps = 256;
            s.Iterations = 12;
        })
    ];
}
