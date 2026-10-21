using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class QuaternionJuliaMode() : Fractal3DMode(Fractal3DKind.QuaternionJulia, new(
    "Кватернионное Жюлиа", "Кватернионное Жюлиа",
    "Множество Жюлиа в алгебре кватернионов: координата и наклон трёхмерного среза в 4D, проход по срезам и атлас константы C.",
    "Fractal3DQuaternionJulia", "quaternion-julia"))
{
    public override bool UsesJuliaConstant => true;

    public override bool UsesBailout => true;

    public override void Validate(Fractal3DState state) => state.QuaternionJulia?.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var constants = base.ShapeConstants(state);
        return state.QuaternionJulia is { HasRotation: true } slice
            ? constants with { BoxInversion = new Vector4((float)(slice.XW * Math.PI / 180),
                (float)(slice.YW * Math.PI / 180), (float)(slice.ZW * Math.PI / 180), 1) }
            : constants;
    }

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
        }),
        Preset("Косой срез в 4D", s =>
        {
            s.QuaternionJulia = new() { XW = 37, YW = -24, ZW = 51 };
            s.QuaternionSlice = .15;
        }),
        Preset("Перпендикулярный срез · XW = 90°", s =>
        {
            s.QuaternionJulia = new() { XW = 90 };
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) =>
        $"C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}; {state.JuliaCW:G5} · срез w: {state.QuaternionSlice:G5}" +
        (state.QuaternionJulia is { HasRotation: true } s ? $" · наклон XW/YW/ZW: {s.XW:G5}°; {s.YW:G5}°; {s.ZW:G5}°" : "");
}
