namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class QuaternionJuliaMode() : Fractal3DMode(Fractal3DKind.QuaternionJulia, new(
    "Кватернионное Жюлиа", "Кватернионное Жюлиа",
    "Множество Жюлиа в алгебре кватернионов: трёхмерный срез четырёхмерного множества с настраиваемой координатой среза.",
    "Fractal3DQuaternionJulia", "quaternion-julia"))
{
    public override bool UsesJuliaConstant => true;

    public override bool UsesBailout => true;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 11;
        state.Bailout = 16;
        state.JuliaCX = -0.0909;
        state.JuliaCY = 0.2727;
        state.JuliaCZ = 0.6818;
        state.JuliaCW = -0.2727;
        state.CameraDistance = 3;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("C = (−0.09; 0.27; 0.68; −0.27)", _ => { }),
        Preset("C = (−1; 0.2; 0; 0)", s =>
        {
            s.JuliaCX = -1;
            s.JuliaCY = 0.2;
            s.JuliaCZ = 0;
            s.JuliaCW = 0;
            s.CameraDistance = 3;
        }),
        Preset("Срез w = 0.35", s =>
        {
            s.QuaternionSlice = 0.35;
            s.CameraDistance = 2.8;
        }),
        Preset("C = (−0.2; 0.6; 0.2; 0) — ветвистый", s =>
        {
            s.JuliaCX = -0.2;
            s.JuliaCY = 0.6;
            s.JuliaCZ = 0.2;
            s.JuliaCW = 0;
            s.Iterations = 12;
            s.CameraDistance = 3.2;
            s.Palette = Fractal3DPalettes.Get("Аметист");
        }),
        Preset("Просвечивающее стекло", s =>
        {
            s.ShadingStyle = Fractal3DShadingStyle.Translucent;
            s.ColoringMode = Fractal3DColoringMode.Fresnel;
            s.Palette = Fractal3DPalettes.Get("Лёд");
            s.EffectStrength = 1.5;
            s.Specular = 0.8;
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) =>
        $"C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}; {state.JuliaCW:G5} · срез w: {state.QuaternionSlice:G5}";
}
