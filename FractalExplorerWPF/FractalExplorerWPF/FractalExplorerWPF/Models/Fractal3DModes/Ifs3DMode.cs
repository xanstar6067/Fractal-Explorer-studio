using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Ifs3DMode() : Fractal3DMode(Fractal3DKind.Ifs3D, new(
    "Конструктор объёмных IFS", "Конструктор объёмных IFS",
    "Редактируемые аффинные преобразования порождают трёхмерный аттрактор. Кадр строится на Direct3D 11; камера и навигация общие с другими трёхмерными фракталами.",
    "IFS3D", "ifs3d"))
{
    public override bool IsDensityVolume => true;

    protected override bool HasOrbit => false;

    public override Fractal3DField IterationsField { get; } =
        new("Точки орбиты (10 000–10 000 000)", "Итерации", 10_000, 10_000_000, IsInteger: true);

    protected override void ConfigureDefault(Fractal3DState state) => ApplyIfsPreset(state, Ifs3DPresets.All[0]);

    public override IReadOnlyList<Fractal3DState> CreatePresets() => Ifs3DPresets.All.Select(preset =>
    {
        Fractal3DState state = CreateDefaultState();
        ApplyIfsPreset(state, preset);
        state.SaveName = preset.Name;
        return state;
    }).ToList();

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {state.Iterations:N0} точек · " +
        $"{state.IfsTransforms.Count} преобразований\n" +
        $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°, " +
        $"крен {state.CameraRoll:F1}°, расстояние {state.CameraDistance:G6}";

    private static void ApplyIfsPreset(Fractal3DState target, Ifs3DPreset preset)
    {
        Ifs3DState source = preset.State;
        target.Iterations = source.Iterations;
        target.IfsTransforms = source.Transforms.Select(transform => transform.Clone()).ToList();
        target.CameraYaw = source.Yaw;
        target.CameraPitch = source.Pitch;
        target.CameraDistance = 3.5;
        target.MaxSteps = 320;
        target.SurfaceColor = source.PointColor;
        target.BackgroundTop = source.BackgroundColor;
        target.BackgroundBottom = source.BackgroundColor;
        target.ColoringMode = Fractal3DColoringMode.Material;
        target.SoftShadows = false;
        target.AmbientOcclusion = false;
        target.Palette = Fractal3DPalette.FromPair(
            $"IFS: {preset.Name}", source.PointColor, Color.FromRgb(235, 255, 255));
    }
}
