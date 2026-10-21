using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

/// <summary>Кубические решётки: на каждом уровне куб заменяется набором кубиков меньшего размера.</summary>
internal abstract class CubeLatticeMode(Fractal3DKind kind, Fractal3DDefinition definition) : CubeRuleMode(kind, definition)
{
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 4;
        state.CameraDistance = 4.2;
        state.CameraYaw = 28;
        state.CameraPitch = 24;
        state.MaxSteps = 220;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Классический вид", _ => { }),
        Preset("Крупные детали", s => s.Iterations = 2),
        Preset("Тонкие элементы", s => s.CubeThickness = 0.7),
        Preset("Массивные элементы", s => s.CubeThickness = 1.4)
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        state.CubeRuleMask.HasValue ? base.ShapeConstants(state) :
            new(new Vector3((float)state.CubeThickness, 0f, 0f), JuliaConstant(state), 0, BoxInversion(state));
}

internal sealed class VicsekMode() : CubeLatticeMode(Fractal3DKind.Vicsek, new(
    "Объёмный фрактал Вицека", "Объёмный фрактал Вицека",
    "На каждом уровне куб заменяется центральным кубиком и шестью кубиками на гранях.",
    "Fractal3DVicsek", "vicsek-3d"))
{
    protected override string ShapeSummary(Fractal3DState state) => state.CubeRuleMask.HasValue
        ? base.ShapeSummary(state) : $"Семь кубиков · Толщина: {state.CubeThickness:G4}";
}

internal sealed class CantorDustMode() : CubeLatticeMode(Fractal3DKind.CantorDust, new(
    "Кубическая пыль Кантора", "Кубическая пыль Кантора",
    "На каждом уровне остаются восемь угловых кубиков из 27; структура повторяется в каждом из них.",
    "Fractal3DCantorDust", "cantor-dust-3d"))
{
    protected override string ShapeSummary(Fractal3DState state) => state.CubeRuleMask.HasValue
        ? base.ShapeSummary(state) : $"Восемь угловых кубиков · Толщина: {state.CubeThickness:G4}";
}
