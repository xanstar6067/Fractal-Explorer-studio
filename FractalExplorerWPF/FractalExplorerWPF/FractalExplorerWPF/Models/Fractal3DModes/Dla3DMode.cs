namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Dla3DMode() : Fractal3DMode(Fractal3DKind.Dla3D, new(
    "DLA в 3D — кораллы и дендриты", "Объёмный DLA",
    "Частицы блуждают в пространстве и прилипают к затравке. Наблюдайте рождение ветвей, задавайте поток и окрашивайте кластер по времени присоединения частиц.",
    "Fractal3DDla", "dla-3d"))
{
    public override bool IsDensityVolume => true;

    public override bool UsesIterations => false;

    public override string? ShapeHeader => "Рост и затравка";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.IterationIndex] = "По возрасту частиц" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 1;
        state.Dla = new();
        state.CameraDistance = .9;
        state.CameraPitch = 20;
        state.ColoringMode = Fractal3DColoringMode.IterationIndex;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalettes.Get("Спектр");
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false;
        state.Ambient = .3;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Коралл · свободный рост", _ => { }),
        Preset("Плотный коралл · слабое прилипание", s => { s.Dla.Stickiness = .15; s.Dla.Seed = 137; s.Palette = Fractal3DPalettes.Get("Медь и патина"); }),
        Preset("Дендриты на стержне", s => { s.Dla.SeedShape = Dla3DSeedShape.Line; s.Dla.Seed = 91; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Кольцевая колония", s => { s.Dla.SeedShape = Dla3DSeedShape.Ring; s.Dla.Seed = 256; s.Palette = Fractal3DPalettes.Get("Закат"); }),
        Preset("Коралловый сад · поток сверху", s => { s.Dla.SeedShape = Dla3DSeedShape.Plane; s.Dla.SeedSize = 14; s.Dla.FlowStrength = .15; s.Dla.Seed = 73; s.Palette = Fractal3DPalettes.Get("Спектр"); })
    ];

    /// <summary>Описание без даты: число частиц важнее времени сохранения.</summary>
    public override string Describe(Fractal3DState state, string prefix)
    {
        Dla3DSettings dla = (state.Dla ?? new()).Normalized();
        return $"Частиц: {dla.ParticleCount:N0} / {dla.TargetParticles:N0} · Затравка: {Dla3DSettings.ShapeName(dla.SeedShape)}\n" +
            $"Прилипание: {dla.Stickiness:P0} · Поток: {dla.FlowStrength:P0} · Случайное число: {dla.Seed}\n" +
            $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
