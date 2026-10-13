using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Lyapunov3DMode() : Fractal3DMode(Fractal3DKind.Lyapunov3D, new(
    "Ляпунов 3D", "Ляпунов · A / B / C",
    "Экспонента логистического отображения в трёхмерном пространстве параметров A, B, C. Последовательность задаёт их чередование: λ < 0 — устойчивость, λ > 0 — хаос. Поверхность, объём и цветные срезы открывают структуру границы.",
    "Fractal3DLyapunov", "lyapunov3d"))
{
    public override bool IsDensityVolume => true;

    public override bool UsesIterations => false;

    public override string? ShapeHeader => "Параметры и экспонента";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По экспоненте λ",
            [Fractal3DColoringMode.CrossTrap] = "По параметру A",
            [Fractal3DColoringMode.IterationIndex] = "По параметру B",
            [Fractal3DColoringMode.Escape] = "По параметру C"
        };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 4.2;
        state.CameraYaw = 35; state.CameraPitch = 22;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.Palette = Fractal3DPalette.FromPair("Ляпунов · устойчивость / хаос",
            Color.FromRgb(40, 95, 210), Color.FromRgb(255, 190, 60));
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.SoftShadows = false; state.AmbientOcclusion = true;
        state.BackgroundTop = Color.FromRgb(42, 58, 88);
        state.BackgroundBottom = Color.FromRgb(9, 14, 24);
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("ABCCAAB · области устойчивости", _ => { }),
        Preset("ABCBBACC · устойчивые острова", s => s.Lyapunov = new() {
            Pattern = "ABCBBACC", MinA = 3.4, MinB = 3.4, MinC = 3.4, MaxA = 3.9, MaxB = 3.9, MaxC = 3.9 }),
        Preset("ABCCAAB · граница λ = 0", s => s.Lyapunov = new() { Region = Lyapunov3DRegion.Boundary }),
        Preset("AABBC · прозрачный объём", s => {
            s.Lyapunov = new() { Pattern = "AABBC", Region = Lyapunov3DRegion.Stable, CutAxis = 3 };
            s.ShadingStyle = Fractal3DShadingStyle.Density; s.EffectStrength = .7; }),
        Preset("ABCCAAB · карта при C = 3,5", s => {
            s.Lyapunov = new() { SliceOnly = true, CutAxis = 3 };
            s.CameraYaw = 0; s.CameraPitch = 0; s.CameraDistance = 3; })
    ];

    public override void Validate(Fractal3DState state) => state.Lyapunov.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Lyapunov3DSettings s = state.Lyapunov;
        return new(
            new Vector3(s.Size, .5f, (float)s.ColorRange),
            new Vector4(s.CutAxis, (float)s.CutPosition, (int)s.Region, (float)s.BandWidth),
            0,
            new Vector4((float)s.Level, s.SliceOnly ? 1 : 0, 0, 0));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.Lyapunov;
        string region = s.Region switch { Lyapunov3DRegion.Stable => "устойчивость", Lyapunov3DRegion.Chaotic => "хаос", _ => "граница" };
        return $"{prefix} · {s.Sequence} · {s.Size}³ · прогрев {s.Transient}, измерение {s.Samples}\n" +
            $"A: {s.MinA:G6}…{s.MaxA:G6}; B: {s.MinB:G6}…{s.MaxB:G6}; C: {s.MinC:G6}…{s.MaxC:G6}\n" +
            $"{region} · уровень λ = {s.Level:G5}" + (s.SliceOnly ? " · плоскость среза" : "") + "\n" +
            $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°, расстояние {state.CameraDistance:G6}";
    }
}
