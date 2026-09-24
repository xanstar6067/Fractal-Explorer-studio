namespace FractalExplorerWPF.Models;

public enum TerrainKind { Fbm, Ridged, Hybrid }

/// <summary>Immutable geometry key; camera and lighting never invalidate the height field.</summary>
public sealed record TerrainSettings
{
    public TerrainKind Type { get; init; } = TerrainKind.Ridged;
    public int Seed { get; init; } = 42;
    public int Octaves { get; init; } = 8;
    public double Roughness { get; init; } = 0.55;
    public double Lacunarity { get; init; } = 2;
    public double Scale { get; init; } = 3;
    public double Height { get; init; } = 1.6;
    public double Size { get; init; } = 6;
    public int Resolution { get; init; } = 513;

    public void Validate()
    {
        if (!Enum.IsDefined(Type) || Seed < 0 || Octaves is < 1 or > 12 ||
            Resolution is not (257 or 513 or 1025 or 2049) ||
            !In(Roughness, 0.05, 0.95) || !In(Lacunarity, 1.5, 3) ||
            !In(Scale, 0.25, 16) || !In(Height, 0.05, 8) || !In(Size, 1, 20))
            throw new ArgumentException("Недопустимые параметры ландшафта.");
    }

    private static bool In(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
