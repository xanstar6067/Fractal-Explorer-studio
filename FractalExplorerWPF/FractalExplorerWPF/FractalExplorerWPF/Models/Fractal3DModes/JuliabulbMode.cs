namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class JuliabulbMode() : Fractal3DMode(Fractal3DKind.Juliabulb, new(
    "Жюлиабульб", "Жюлиабульб",
    "Julia-вариант Мандельбульба: вместо точки пространства в итерацию подставляется фиксированная константа C.",
    "Fractal3DJuliabulb", "juliabulb"))
{
    public override bool UsesPower => true;

    public override bool UsesJuliaConstant => true;

    public override bool UsesBailout => true;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Power = 8;
        state.Iterations = 9;
        state.JuliaCX = -0.2;
        state.JuliaCY = 0.6;
        state.JuliaCZ = 0.2;
        state.CameraDistance = 2.9;
        state.ColorScale = 2.5;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("C = (−0.2; 0.6; 0.2)", _ => { }),
        Preset("C = (0.4; 0.0; 0.0) — симметричный", s =>
        {
            s.JuliaCX = 0.4;
            s.JuliaCY = 0;
            s.JuliaCZ = 0;
        }),
        Preset("C = (−0.5; 0.2; 0.1) — ветвистый", s =>
        {
            s.JuliaCX = -0.5;
            s.JuliaCY = 0.2;
            s.JuliaCZ = 0.1;
            s.Iterations = 11;
            s.CameraDistance = 3.1;
        }),
        Preset("Степень 4, C = (0.3; 0.3; 0.3)", s =>
        {
            s.Power = 4;
            s.JuliaCX = 0.3;
            s.JuliaCY = 0.3;
            s.JuliaCZ = 0.3;
            s.Iterations = 12;
        }),
        Preset("Плотность без поверхности", s =>
        {
            // Ветвистая константа: у сплошного шара просвет одинаков везде и смотреть не на что.
            s.JuliaCX = -0.5;
            s.JuliaCY = 0.2;
            s.JuliaCZ = 0.1;
            s.Iterations = 11;
            s.CameraDistance = 3.1;
            s.ShadingStyle = Fractal3DShadingStyle.Density;
            s.Palette = Fractal3DPalettes.Get("Северное сияние");
            s.ColorRepeat = Fractal3DColorRepeat.Clamp;
            s.EffectStrength = 0.5;
            s.MaxSteps = 220;
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) =>
        $"Степень: {state.Power:G6} · C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}";
}
