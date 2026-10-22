namespace FractalExplorerWPF.Models;

public enum PeriodicSurfaceFamily { Gyroid, SchwarzP, SchwarzD }
public enum PeriodicSurfaceRegion { Sheet, Surface, Positive, Negative, Both }

/// <summary>Nodal trigonometric approximations, clipped to the cube [-1,1]³.</summary>
public sealed record PeriodicSurfaceSettings
{
    public PeriodicSurfaceFamily Family { get; init; }
    public PeriodicSurfaceFamily BlendFamily { get; init; } = PeriodicSurfaceFamily.SchwarzP;
    /// <summary>Linear field blend; zero preserves the original family and old saves.</summary>
    public double Blend { get; init; }
    public PeriodicSurfaceRegion Region { get; init; }
    public double Period { get; init; } = 1;
    public double Level { get; init; }
    /// <summary>Full width in field values: |F − Level| ≤ Thickness / 2, not a world distance.</summary>
    public double Thickness { get; init; } = .3;
    /// <summary>0 = uniform, 1/2/3 = X/Y/Z. Width = Thickness * (1 + Gradient * coordinate).</summary>
    public int ThicknessAxis { get; init; }
    public double ThicknessGradient { get; init; }
    public double StretchX { get; init; } = 1;
    public double StretchY { get; init; } = 1;
    public double StretchZ { get; init; } = 1;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Family) || !Enum.IsDefined(BlendFamily) || !Enum.IsDefined(Region))
            throw new ArgumentException("Неизвестное семейство или область периодической поверхности.");
        Range(Period, .5, 4, "Период");
        Range(Level, -.9, .9, "Уровень поверхности");
        Range(Thickness, .02, 1.2, "Толщина стенки по F");
        Range(Blend, 0, 1, "Смешивание");
        Range(ThicknessGradient, -.9, .9, "Градиент толщины");
        if (ThicknessAxis is < 0 or > 3) throw new ArgumentException("Ось изменения толщины: нет, X, Y или Z.");
        Range(StretchX, .5, 2, "Растяжение X");
        Range(StretchY, .5, 2, "Растяжение Y");
        Range(StretchZ, .5, 2, "Растяжение Z");
        Range(CutPosition, -1, 1, "Положение среза");
        if (CutAxis is < 0 or > 3) throw new ArgumentException("Ось среза: нет, X, Y или Z.");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: {min} … {max}.");
    }
}
