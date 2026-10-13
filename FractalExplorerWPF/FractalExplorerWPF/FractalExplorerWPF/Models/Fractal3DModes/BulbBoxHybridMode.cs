using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class BulbBoxHybridMode() : Fractal3DMode(Fractal3DKind.BulbBoxHybrid, new(
    "Гибрид Мандельбульб × Мандельбокс", "Гибрид Мандельбульб × Мандельбокс",
    "Бульб и бокс чередуются заданными сериями итераций. Доля смешивания подмешивает вторую операцию к каждой серии; порядок, длины серий и параметры обеих форм меняют геометрию.",
    "Fractal3DBulbBoxHybrid", "bulb-box-hybrid"))
{
    public override bool UsesPower => true;

    public override bool UsesBailout => true;

    public override Fractal3DField PowerField { get; } = new("Степень Мандельбульба (2–12)", "Степень", 2, 12);

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Power = 3;
        state.BoxScale = -1.8;
        state.BoxMinRadius = 0.5;
        state.BoxFoldingLimit = 1;
        state.HybridMix = 0;
        state.Iterations = 12;
        state.Bailout = 32;
        state.CameraDistance = 5.5;
        state.MaxDistance = 60;
        state.MaxSteps = 360;
        state.Palette = Fractal3DPalettes.Get("Медь и патина");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Чередование 1 : 1", _ => { }),
        Preset("Сначала бокс · 2 : 1", s =>
        {
            s.HybridOrder = Hybrid3DOrder.BoxFirst;
            s.HybridBoxSteps = 2;
            s.HybridMix = 0.1;
            s.CameraDistance = 15;
        }),
        Preset("Преобладание бульба · 3 : 1", s =>
        {
            s.HybridBulbSteps = 3;
            s.HybridMix = 0.25;
            s.Power = 4;
        }),
        Preset("Сильное смешивание", s =>
        {
            s.HybridMix = 0.45;
            s.BoxScale = 2;
            s.Power = 5;
            s.Palette = Fractal3DPalettes.Get("Аметист");
        }),
        Preset("Ледяные складки", s =>
        {
            s.HybridOrder = Hybrid3DOrder.BoxFirst;
            s.HybridBulbSteps = 2;
            s.HybridBoxSteps = 2;
            s.HybridMix = 0.15;
            s.BoxScale = -2;
            s.Power = 6;
            s.Palette = Fractal3DPalettes.Get("Лёд");
            s.ColoringMode = Fractal3DColoringMode.CrossTrap;
            s.ColorScale = 0.6;
        })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3(
            (float)state.Power,
            (float)(state.BoxMinRadius * state.BoxMinRadius),
            (float)state.BoxFoldingLimit),
        new Vector4((float)state.BoxScale, (float)Math.Clamp(state.HybridMix, 0, 1),
            Math.Clamp(state.HybridBulbSteps, 1, 6), Math.Clamp(state.HybridBoxSteps, 1, 6)),
        (float)state.HybridOrder,
        BoxInversion(state));

    protected override string ShapeSummary(Fractal3DState state) =>
        $"Бульб n={state.Power:G4} × {state.HybridBulbSteps} · Бокс {state.BoxScale:G4} × {state.HybridBoxSteps} · " +
        $"смесь {state.HybridMix:G3} · инверсия {Fractal3DCatalog.BoxInversionShapeName(state.BoxInversionShape)} · " +
        (state.HybridOrder == Hybrid3DOrder.BulbFirst ? "сначала бульб" : "сначала бокс");
}
