namespace FractalExplorerWPF.Models;

public enum LSystemShapeFamily { Plants, Radial, Curves, Geometric }
public enum LSystemCurveKind { Mixed, Folding, Dragon, Hilbert, Meander, Spiral }

public sealed record LSystemRandomizationSettings
{
    public LSystemShapeFamily Family { get; init; }
    public int Branching { get; init; } = 3;
    public int Detail { get; init; } = 3;
    public double Variation { get; init; } = .4;
    public bool Symmetric { get; init; } = true;
    public int Seed { get; init; } = 42;
    public LSystemCurveKind CurveKind { get; init; }
    public int RuleComplexity { get; init; } = 2;
    public int StemLength { get; init; } = 2;
    public double AngleMinimum { get; init; } = 15;
    public double AngleMaximum { get; init; } = 90;
    public double PitchMinimum { get; init; } = 20;
    public double PitchMaximum { get; init; } = 100;
    public double RollMinimum { get; init; } = 10;
    public double RollMaximum { get; init; } = 160;
    public double Radius { get; init; } = .12;
    public double BranchTaper { get; init; } = .72;
    public double StepDecay { get; init; } = .8;

    public void Validate()
    {
        if (!Enum.IsDefined(Family) || Branching is < 2 or > 5 || Detail is < 1 or > 5 ||
            !double.IsFinite(Variation) || Variation is < 0 or > 1 || Seed < 0 ||
            !Enum.IsDefined(CurveKind) || RuleComplexity is < 1 or > 4 || StemLength is < 1 or > 4 ||
            !ValidRange(AngleMinimum, AngleMaximum) || !ValidRange(PitchMinimum, PitchMaximum) ||
            !ValidRange(RollMinimum, RollMaximum) || !double.IsFinite(Radius) || Radius is < .01 or > .5 ||
            !double.IsFinite(BranchTaper) || BranchTaper is < .3 or > 1 ||
            !double.IsFinite(StepDecay) || StepDecay is < .3 or > 1)
            throw new InvalidOperationException("Некорректные настройки генератора L-систем.");
    }

    private static bool ValidRange(double min, double max) =>
        double.IsFinite(min) && double.IsFinite(max) && min >= 1 && max <= 180 && min <= max;
}

public sealed record LSystemRandomizationResult(LSystemDefinition? Planar, LSystem3DSettings? Spatial,
    int Seed, string Description, int Segments);
