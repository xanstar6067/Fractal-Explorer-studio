using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class BulbBoxHybridMode() : Fractal3DMode(Fractal3DKind.BulbBoxHybrid, new(
    "Гибрид Мандельбульб × Мандельбокс", "Гибрид Мандельбульб × Мандельбокс",
    "Бульб и бокс чередуются сериями или собственной цепочкой. Настраивайте каждый этап, вставляйте повороты и сдвиги орбиты; порядок и смешивание операций меняют геометрию.",
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
        }),
        Preset("Цепочка · закрученные складки", s =>
        {
            s.HybridSequence = new() { Stages = [
                new() { Operation = BulbBoxOperation.Bulb },
                new() { Operation = BulbBoxOperation.Rotate, Z = 45, X = 30 },
                new() { Operation = BulbBoxOperation.Box, Scale = -2.2, FoldLimit = .8 }
            ] };
        }),
        Preset("Цепочка · смещённые ветви", s =>
        {
            s.HybridSequence = new() { Stages = [
                new() { Operation = BulbBoxOperation.Bulb },
                new() { Operation = BulbBoxOperation.Translate, X = .65, Z = -.35 },
                new() { Operation = BulbBoxOperation.Box }
            ] };
        }),
        Preset("Цепочка · две степени", s =>
        {
            s.HybridSequence = new() { Stages = [
                new() { Operation = BulbBoxOperation.Bulb, Power = 2 },
                new() { Operation = BulbBoxOperation.Box, Scale = -1.6 },
                new() { Operation = BulbBoxOperation.Bulb, Power = 5 },
                new() { Operation = BulbBoxOperation.Rotate, Y = 12 }
            ] };
        })
    ];

    public override void Validate(Fractal3DState state) => state.HybridSequence?.Validate();

    public override int? GetStripPixelBudget(Fractal3DState state)
    {
        if (state.HybridSequence is not { } sequence) return null;
        sequence.Validate();
        // Transform-only stages add work without consuming a fractal iteration.
        double nonlinear = sequence.Stages.Where(s => s.Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box).Sum(s => s.Repeats);
        double cost = Math.Max(.25, state.MaxSteps / 160.0 * Math.Max(state.Iterations, 1) / 8.0) *
            (1 + sequence.OperationCount / nonlinear);
        return (int)Math.Clamp(1_000_000 / cost, 8_000, 500_000);
    }

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
        state.HybridSequence is { } sequence ? $"Цепочка: {sequence}" :
        $"Бульб n={state.Power:G4} × {state.HybridBulbSteps} · Бокс {state.BoxScale:G4} × {state.HybridBoxSteps} · " +
        $"смесь {state.HybridMix:G3} · инверсия {Fractal3DCatalog.BoxInversionShapeName(state.BoxInversionShape)} · " +
        (state.HybridOrder == Hybrid3DOrder.BulbFirst ? "сначала бульб" : "сначала бокс");
}
