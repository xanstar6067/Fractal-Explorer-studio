namespace FractalExplorerWPF.Models;

/// <summary>Similar, disjoint solid tori, nested in their parent torus.</summary>
public sealed record AntoineSettings
{
    public int Links { get; init; } = 24;
    public double ScaleFactor { get; init; } = 1.4;
    public double Thickness { get; init; } = .65;
    public double Tilt { get; init; } = 90;

    public void Validate()
    {
        if (Links < 24 || Links > 48 || Links % 2 != 0)
            throw new ArgumentException("Звеньев в цепочке: чётное число от 24 до 48.");
        Range(ScaleFactor, 1.35, 1.5, "Масштаб звеньев");
        Range(Thickness, 0, 1, "Толщина звеньев");
        Range(Tilt, 60, 90, "Наклон звеньев");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: {min} … {max}.");
    }
}
