using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Lichtenberg3DMode() : Fractal3DMode(Fractal3DKind.Lichtenberg3D, new(
    "Фигуры Лихтенберга · пробой диэлектрика", "Фигуры Лихтенберга 3D",
    "Ветвящийся разряд растёт по электрическому полю. Параметр η меняет ветвление от густого куста до тонкой молнии; сферический и плоский электроды задают направление роста.",
    "Fractal3DLichtenberg", "lichtenberg-3d"))
{
    public override bool IsDensityVolume => true;

    public override bool UsesIterations => false;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.IterationIndex] = "По времени роста" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 2;
        state.CameraPitch = 15;
        state.ColoringMode = Fractal3DColoringMode.IterationIndex;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalettes.Get("Лёд");
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.BackgroundTop = Color.FromRgb(3, 5, 12);
        state.BackgroundBottom = Color.FromRgb(1, 2, 5);
        state.SoftShadows = false;
        state.AmbientOcclusion = false;
        state.Ambient = .4;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Ветвящийся разряд", s => { }),
        Preset("Густой электрический куст", s => { s.Lichtenberg = new() { Eta = .6, Seed = 73, SegmentCount = 600 }; s.CameraDistance = 1.6; s.Palette = Fractal3DPalettes.Get("Раскалённый металл"); }),
        Preset("Тонкая молния", s => { s.Lichtenberg = new() { Eta = 4, Seed = 91 }; s.CameraDistance = 2.3; s.Palette = Fractal3DPalettes.Get("Спектр"); }),
        Preset("Разряд к плоскости", s => { s.Lichtenberg = new() { Electrodes = LichtenbergElectrodes.Plane, Eta = 2.5, Seed = 256, SegmentCount = 250 }; }),
        Preset("Веер к электроду", s => { s.Lichtenberg = new() { Electrodes = LichtenbergElectrodes.Plane, Eta = 1, Seed = 17, SegmentCount = 500 }; s.Palette = Fractal3DPalettes.Get("Закат"); })
    ];

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {state.Lichtenberg.SegmentCount:N0} участков · η = {state.Lichtenberg.Eta:G4}\n" +
        $"Электроды: {(state.Lichtenberg.Electrodes == LichtenbergElectrodes.Radial ? "Сфера" : "Плоскость")} · Сетка: {state.Lichtenberg.Size}³ · Число: {state.Lichtenberg.Seed}\n" +
        $"Окраска: {ColoringModeName(state.ColoringMode)}";
}
