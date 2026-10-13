using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Buddhabrot4DMode() : Fractal3DMode(Fractal3DKind.Buddhabrot4D, new(
    "Буддаброт 4D · объёмные проекции", "Буддаброт 4D",
    "Орбиты z² + c образуют облако в четырёх координатах: Re z, Im z, Re c, Im c. Вращения в 4D меняют его трёхмерную проекцию; камера позволяет рассмотреть её со всех сторон.",
    "Fractal3DBuddhabrot4D", "buddhabrot-4d"))
{
    public override bool IsDensityVolume => true;

    public override bool UsesIterations => false;

    protected override bool HasOrbit => false;

    public override string? ShapeHeader => "Проекция и орбиты";

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 2.4;
        state.CameraYaw = 0;
        state.CameraPitch = 0;
        state.Buddhabrot.ZrZi = -90;
        state.Buddhabrot.Gamma = 1.5;
        state.Buddhabrot.Exposure = 1;
        state.Buddhabrot.Density = .35;
        state.ShadingStyle = Fractal3DShadingStyle.Glow;
        state.ColoringMode = Fractal3DColoringMode.Material;
        state.BackgroundTop = Color.FromRgb(5, 7, 16);
        state.BackgroundBottom = Color.FromRgb(1, 2, 5);
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Будда · глубина Re c", _ => { }),
        Preset("Крылья · глубина Im c", s => { s.Buddhabrot.Projection = Buddhabrot4DProjection.HideCr; s.CameraYaw = 35; }),
        Preset("Карта параметров · Re z", s => { s.Buddhabrot.Projection = Buddhabrot4DProjection.HideZi; s.CameraYaw = 60; s.CameraPitch = 22; }),
        Preset("Карта параметров · Im z", s => { s.Buddhabrot.Projection = Buddhabrot4DProjection.HideZr; s.CameraYaw = 55; }),
        Preset("Между мирами · поворот 4D", s => { s.Buddhabrot.ZrCr = 40; s.Buddhabrot.ZiCi = 55; s.CameraYaw = -25; }),
        Preset("Полупрозрачная туманность", s => { s.Buddhabrot.ZrCi = 35; s.Buddhabrot.ZiCr = -30; s.ShadingStyle = Fractal3DShadingStyle.Density; s.Buddhabrot.Density = .3; })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.Buddhabrot.Exposure, (float)state.Buddhabrot.Gamma, (float)state.Buddhabrot.Density),
        JuliaConstant(state), (float)state.Buddhabrot.Saturation, BoxInversion(state));

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {state.Buddhabrot.SampleCount:N0} затравок · " +
        $"выход за {state.Buddhabrot.MinIterations}–{state.Buddhabrot.MaxIterations} итераций\n" +
        $"Проекция: {Buddhabrot4DSettings.ProjectionName(state.Buddhabrot.Projection)} · R/G/B: {state.Buddhabrot.RedLimit}/{state.Buddhabrot.GreenLimit}/{state.Buddhabrot.BlueLimit}\n" +
        $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°, расстояние {state.CameraDistance:G6}";
}
