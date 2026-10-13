using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class AbcFlowMode() : Fractal3DMode(Fractal3DKind.AbcFlow, new(
    "Поток ABC", "ABC · пространственный хаос",
    "Периодическое поле Арнольда–Бельтрами–Чайлдресса: пространственные линии, движущиеся частицы, сечения Пуанкаре и конечновременная оценка расхождения. Коэффициенты A/B/C и группы начальных точек.",
    "Fractal3DAbcFlow", "abc-flow"))
{
    public override bool UsesIterations => false;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По расхождению · оценка λ(t)",
            [Fractal3DColoringMode.CrossTrap] = "По скорости потока",
            [Fractal3DColoringMode.IterationIndex] = "По начальной точке",
            [Fractal3DColoringMode.Escape] = "По времени вдоль линии"
        };

    public override int? StripPixelBudget => 12_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 3.8; state.CameraYaw = 28; state.CameraPitch = 20;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.ColoringMode = Fractal3DColoringMode.IterationIndex;
        state.Palette = Fractal3DPalettes.Get("Спектр"); state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.SoftShadows = false; state.AmbientOcclusion = false; state.Ssaa = 2;
        state.MaxSteps = 256; state.Detail = .3;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Симметричный поток · A = B = C", _ => { }),
        Preset("Кольцо начальных точек", s => s.AbcFlow = new() { Group = AbcSeedGroup.Ring, SeedCount = 16, Spread = 1.2, B = .7, C = .43 }),
        Preset("Соседние траектории · расхождение", s => { s.AbcFlow = new() { Group = AbcSeedGroup.Neighbours, Spread = .003, SeedCount = 8, Samples = 1536 }; s.ColoringMode = Fractal3DColoringMode.OrbitTrap; }),
        Preset("Упорядоченный поток · B = C = 0", s => s.AbcFlow = new() { B = 0, C = 0, SeedCount = 16, Group = AbcSeedGroup.Plane, Spread = 3 }),
        Preset("Сечение Пуанкаре · z = 0", s => { s.AbcFlow = new() { SectionOnly = true, SeedCount = 24, Samples = 2048, Thickness = .012 }; s.CameraYaw = 0; s.CameraPitch = 0; }),
        Preset("Частицы без следа", s => s.AbcFlow = new() { ShowTrails = false, SeedCount = 32, Thickness = .015 })
    ];

    public override void Validate(Fractal3DState state) => state.AbcFlow.Validate();

    /// <summary>В ShapeB.w — верхняя оценка скорости потока |A|+|C|, |A|+|B|, |B|+|C| по трём осям.</summary>
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        AbcFlowSettings flow = state.AbcFlow;
        return new(
            new Vector3((float)flow.Phase, flow.ShowTrails ? 1 : 0, flow.ShowParticles ? 1 : 0),
            new Vector4(flow.Samples, flow.SeedCount, (float)flow.LyapunovRange,
                (float)Math.Sqrt(Math.Pow(Math.Abs(flow.A) + Math.Abs(flow.C), 2) +
                    Math.Pow(Math.Abs(flow.A) + Math.Abs(flow.B), 2) + Math.Pow(Math.Abs(flow.B) + Math.Abs(flow.C), 2))),
            0, BoxInversion(state));
    }
}
