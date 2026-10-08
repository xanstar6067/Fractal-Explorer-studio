using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class MandelbrotFamilyRenderer
{
    private static bool IsCubicReflected(ReflectKind kind) =>
        kind is ReflectKind.CubicQuasiBurningShip or ReflectKind.CubicFlyingSquirrel;

    // These two families have a fixed cubic degree, independent of the saved Power field.
    private static double SmoothingPower(MandelbrotState state) =>
        MandelbrotVariantDefinition.ParameterVariant(state.Variant) is
            MandelbrotVariant.CubicQuasiBurningShip or MandelbrotVariant.CubicFlyingSquirrel ? 3 : 2;

    private static Jacobian2 FoldedCubicJacobian(bool quasi, double x, double y,
        int? exactInputSign = null, int? exactOutputSign = null)
    {
        double w = quasi ? System.Math.Abs(x) : x;
        double inputSign = quasi ? exactInputSign ?? System.Math.Sign(x) : 1;
        double v = y * (3 * w * w - y * y);
        double outputSign = (quasi ? -1 : 1) * (exactOutputSign ?? System.Math.Sign(v));
        double a = 3 * (w * w - y * y), b = 6 * w * y;
        return new Jacobian2(a * inputSign, -b, outputSign * b * inputSign, outputSign * a);
    }

    // Exact (W+d)^3-W^3 = 3W^2d+3Wd^2+d^3. Folding deltas avoids subtracting
    // nearly equal orbit values, including when the output imaginary part changes sign.
    private static (double Real, double Imaginary, Jacobian2 Jacobian) FoldedCubicDelta(
        ReflectKind kind, double x, double y, double referenceV, double dx, double dy, bool estimateDistance)
    {
        bool quasi = kind == ReflectKind.CubicQuasiBurningShip;
        double currentX = x + dx, currentY = y + dy;
        if (quasi) { dx = FoldedDelta(x, dx); x = System.Math.Abs(x); }
        double a = x * x - y * y, b = 2 * x * y;
        double d2r = dx * dx - dy * dy, d2i = 2 * dx * dy;
        double du = 3 * (a * dx - b * dy) + 3 * (x * d2r - y * d2i) + dx * (dx * dx - 3 * dy * dy);
        double dv = 3 * (a * dy + b * dx) + 3 * (x * d2i + y * d2r) + dy * (3 * dx * dx - dy * dy);
        double folded = FoldedDelta(referenceV, dv);
        Jacobian2 jacobian = estimateDistance
            ? FoldedCubicJacobian(quasi, currentX, currentY, System.Math.Sign(currentX), System.Math.Sign(referenceV + dv))
            : Jacobian2.Zero;
        return (du, quasi ? -folded : folded, jacobian);
    }

    // Reference components and the unfurled imaginary cube stay in FloatExp too.
    // A tiny X beside the input fold, or V beside an output fold, can determine the
    // sign even when its double projection is zero. V is computed from BigFloat.
    private static (FloatExp Real, FloatExp Imaginary, Jacobian2 Jacobian) FoldedCubicDeltaExp(
        ReflectKind kind, FloatExp x, FloatExp y, FloatExp referenceV, FloatExp dx, FloatExp dy, bool estimateDistance)
    {
        bool quasi = kind == ReflectKind.CubicQuasiBurningShip;
        FloatExp currentX = x + dx, currentY = y + dy;
        if (quasi) { dx = FoldedDeltaFullExp(x, dx); x = FloatExp.Abs(x); }
        FloatExp a = x * x - y * y, b = x * y * 2;
        FloatExp d2r = dx * dx - dy * dy, d2i = dx * dy * 2;
        FloatExp du = (dx * a - dy * b) * 3 + (d2r * x - d2i * y) * 3 + dx * (dx * dx - dy * dy * 3);
        FloatExp dv = (dy * a + dx * b) * 3 + (d2i * x + d2r * y) * 3 + dy * (dx * dx * 3 - dy * dy);
        FloatExp folded = FoldedDeltaFullExp(referenceV, dv);
        Jacobian2 jacobian = estimateDistance
            ? FoldedCubicJacobian(quasi, currentX.ToDouble(), currentY.ToDouble(), currentX.Sign, (referenceV + dv).Sign)
            : Jacobian2.Zero;
        return (du, quasi ? -folded : folded, jacobian);
    }

    private static FloatExp FoldedDeltaFullExp(FloatExp reference, FloatExp delta)
    {
        if (reference.Sign > 0) return delta > -reference ? delta : -(delta + reference * 2);
        if (reference.Sign < 0) return delta < -reference ? -delta : delta + reference * 2;
        return FloatExp.Abs(delta);
    }
}
