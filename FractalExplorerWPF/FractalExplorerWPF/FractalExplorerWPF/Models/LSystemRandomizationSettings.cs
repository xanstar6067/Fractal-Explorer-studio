namespace FractalExplorerWPF.Models;

public enum LSystemShapeFamily { Plants, Radial, Curves, Geometric }

public sealed record LSystemRandomizationSettings
{
    public LSystemShapeFamily Family { get; init; }
    public int Branching { get; init; } = 3;
    public int Detail { get; init; } = 3;
    public double Variation { get; init; } = .4;
    public bool Symmetric { get; init; } = true;
    public int Seed { get; init; } = 42;

    public void Validate()
    {
        if (!Enum.IsDefined(Family) || Branching is < 2 or > 5 || Detail is < 1 or > 5 ||
            !double.IsFinite(Variation) || Variation is < 0 or > 1 || Seed < 0)
            throw new InvalidOperationException("Некорректные настройки генератора L-систем.");
    }
}

public sealed record LSystemRandomizationResult(LSystemDefinition? Planar, LSystem3DSettings? Spatial,
    int Seed, string Description, int Segments);
