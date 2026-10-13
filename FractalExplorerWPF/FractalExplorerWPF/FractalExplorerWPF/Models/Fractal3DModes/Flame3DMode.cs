using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Flame3DMode() : Fractal3DMode(Fractal3DKind.Flame3D, new(
    "Объёмный Fractal Flame", "Объёмный Fractal Flame",
    "Цветные орбиты пространственных преобразований и нелинейных вариаций складываются в светящиеся облака, ленты и скульптуры. Редактор матриц, рандомайзер и тональная коррекция.",
    "Fractal3DFlame", "flame-3d"))
{
    public override bool IsDensityVolume => true;

    protected override bool HasOrbit => false;

    public override Fractal3DField IterationsField { get; } =
        new("Точки Flame (50 000–10 000 000)", "Итерации", 50_000, 10_000_000, IsInteger: true);

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Flame.Transforms = Flame3DPresets.Transforms(0);
        state.Iterations = 2_000_000;
        state.CameraDistance = 3.1;
        state.CameraYaw = 25;
        state.CameraPitch = 16;
        state.ShadingStyle = Fractal3DShadingStyle.Glow;
        state.ColoringMode = Fractal3DColoringMode.Material;
        state.BackgroundTop = Color.FromRgb(5, 7, 16);
        state.BackgroundBottom = Color.FromRgb(1, 2, 5);
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Неоновый вихрь · дымчатые ленты", _ => { }),
        Preset("Солнечный шёлк · волны", s => { s.Flame.Transforms = Flame3DPresets.Transforms(1); s.Flame.Exposure = 1.9; s.CameraYaw = -25; }),
        Preset("Космический коралл · ветвление", s => { s.Flame.Transforms = Flame3DPresets.Transforms(2); s.Flame.Density = .75; s.CameraPitch = 25; }),
        Preset("Световая скульптура", s => { s.Flame.Transforms = Flame3DPresets.Transforms(2); s.ShadingStyle = Fractal3DShadingStyle.Studio; }),
        Preset("Полупрозрачное облако", s => { s.Flame.Transforms = Flame3DPresets.Transforms(1); s.ShadingStyle = Fractal3DShadingStyle.Density; s.Flame.Density = .5; })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.Flame.Exposure, (float)state.Flame.Gamma, (float)state.Flame.Density),
        JuliaConstant(state), (float)state.Flame.Vibrancy, BoxInversion(state));

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {state.Iterations:N0} точек · {state.Flame.Transforms.Count} преобразований\n" +
        $"Начальное число: {state.Flame.Seed} · Экспозиция: {state.Flame.Exposure:G4} · Гамма: {state.Flame.Gamma:G4}\n" +
        $"Плотность: {state.Flame.Density:G4} · Насыщенность: {state.Flame.Vibrancy:G4}";
}
