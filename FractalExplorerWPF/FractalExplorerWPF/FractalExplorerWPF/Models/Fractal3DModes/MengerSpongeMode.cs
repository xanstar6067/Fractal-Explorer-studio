namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class MengerSpongeMode() : Fractal3DMode(Fractal3DKind.MengerSponge, new(
    "Губка Менгера", "Губка Менгера",
    "Куб, из которого рекурсивно вырезаются крестообразные тоннели; классический самоподобный объект размерности log20/log3.",
    "Fractal3DMengerSponge", "menger-sponge"))
{
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 5;
        // С этого расстояния куб целиком помещается в квадратную карточку каталога.
        state.CameraDistance = 4.2;
        state.CameraYaw = 28;
        state.CameraPitch = 24;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("5 уровней — общий вид", _ => { }),
        Preset("3 уровня — крупные тоннели", s =>
        {
            s.Iterations = 3;
            s.CameraDistance = 3.6;
        }),
        Preset("Взгляд вдоль тоннеля", s =>
        {
            s.CameraYaw = 0;
            s.CameraPitch = 0;
            s.CameraDistance = 2.2;
            s.MaxSteps = 256;
        }),
        Preset("Угловой крупный план", s =>
        {
            s.CameraDistance = 1.5;
            s.CameraYaw = 45;
            s.CameraPitch = 35;
            s.TargetX = 0.55;
            s.TargetY = 0.55;
            s.TargetZ = 0.55;
            s.Iterations = 6;
            s.Detail = 0.6;
        }),
        Preset("Студийная глина", s =>
        {
            s.ShadingStyle = Fractal3DShadingStyle.Studio;
            s.ColoringMode = Fractal3DColoringMode.Occlusion;
            s.Palette = Fractal3DPalettes.Get("Сепия");
            s.ColorRepeat = Fractal3DColorRepeat.Clamp;
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) => "Рекурсивные тоннели куба";
}
