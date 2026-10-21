using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal abstract class CubeRuleMode(Fractal3DKind kind, Fractal3DDefinition definition) : Fractal3DMode(kind, definition)
{
    public override bool HasCubeRule => true;

    public override void Validate(Fractal3DState state)
    {
        if (state.CubeRuleMask is uint mask) state.CubeRuleMask = mask & CubeRule.All;
    }

    public override int? GetStripPixelBudget(Fractal3DState state) => state.CubeRuleMask.HasValue
        ? (int)Math.Clamp(12_000 / Math.Max(1, state.Iterations * (double)state.MaxSteps / 640), 1024, 12_000)
        : base.GetStripPixelBudget(state);

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        state.CubeRuleMask is uint mask
            ? new(new Vector3(1, 0, 0),
                // Две целые половины точно представимы в float; BoxInversion.w отличает своё правило от классики.
                new Vector4(mask & 0x3fff, (mask & CubeRule.All) >> 14, 1, 0), 0, new Vector4(0, 0, 0, 1))
            : base.ShapeConstants(state);

    protected override string ShapeSummary(Fractal3DState state) => state.CubeRuleMask is uint mask
        ? $"Своё правило: {CubeRule.Count(mask)} из 27 кубиков · {mask & CubeRule.All:X7}"
        : base.ShapeSummary(state);
}
