using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class SierpinskiTetrahedronMode() : Fractal3DMode(Fractal3DKind.SierpinskiTetrahedron, new(
    "Тетраэдр Серпинского", "Тетраэдр Серпинского",
    "Трёхмерный аналог треугольника Серпинского: складывание пространства к ближайшей вершине тетраэдра.",
    "Fractal3DSierpinski", "sierpinski-tetrahedron"))
{
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 13;
        state.SierpinskiScale = 2;
        state.CameraDistance = 3.2;
        state.CameraYaw = 30;
        state.CameraPitch = 16;
        state.MaxDistance = 30;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Классический тетраэдр", _ => { }),
        Preset("Масштаб 1.8 — сросшийся", s =>
        {
            s.SierpinskiScale = 1.8;
            s.CameraDistance = 4.4;
        }),
        Preset("Вид вдоль ребра", s =>
        {
            s.CameraYaw = 45;
            s.CameraPitch = 0;
            s.CameraDistance = 3.4;
        }),
        Preset("Крупный план вершины", s =>
        {
            s.CameraDistance = 1.4;
            s.TargetX = 0.7;
            s.TargetY = 0.7;
            s.TargetZ = 0.7;
            s.Detail = 0.6;
            s.MaxSteps = 220;
        }),
        Preset("Контурный чертёж", s =>
        {
            s.ShadingStyle = Fractal3DShadingStyle.Toon;
            s.ColoringMode = Fractal3DColoringMode.Height;
            s.Palette = Fractal3DPalettes.Get("Закат");
            s.ColorScale = 0.4;
            s.ColorOffset = 0.5;
            s.ColorRepeat = Fractal3DColorRepeat.Clamp;
            s.EffectStrength = 1.2;
        })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        new(new Vector3((float)state.SierpinskiScale, 0f, 0f), JuliaConstant(state), 0, BoxInversion(state));

    protected override string ShapeSummary(Fractal3DState state) => $"Масштаб складывания: {state.SierpinskiScale:G6}";
}
