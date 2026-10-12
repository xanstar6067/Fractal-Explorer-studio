using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Models;

public sealed record ImplicitSurfaceSettings
{
    public string Formula { get; init; } = "(x^2+y^2+z^2+a^2-b^2)^2-4*a^2*(x^2+y^2)";
    public double A { get; init; } = 1;
    public double B { get; init; } = .35;
    public double C { get; init; }
    public double Extent { get; init; } = 1.6;
    public double Level { get; init; }
    public int CutAxis { get; init; }
    /// <summary>Position in formula coordinates; the retained half-space is coordinate ≤ position.</summary>
    public double CutPosition { get; init; }

    public CompiledRealSpaceExpression Validate()
    {
        Range(A, -10, 10, "a"); Range(B, -10, 10, "b"); Range(C, -10, 10, "c");
        Range(Extent, .1, 10, "Область ±R"); Range(Level, -100, 100, "Уровень F");
        Range(CutPosition, -10, 10, "Положение среза");
        if (CutAxis is < 0 or > 3) throw new ArgumentException("Ось среза: нет, X, Y или Z.");
        return CompiledRealSpaceExpression.Parse(Formula);
    }
    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: {min} … {max}.");
    }
}
