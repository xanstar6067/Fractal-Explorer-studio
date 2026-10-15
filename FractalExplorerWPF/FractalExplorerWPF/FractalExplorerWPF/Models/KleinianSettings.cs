namespace FractalExplorerWPF.Models;

public enum KleinianConfiguration { Octahedron, Tetrahedron, Cube }

/// <summary>
/// Disjoint (or tangent) inversion balls define a discrete reflection group in R³ ∪ {∞}.
/// Its even subgroup consists of Möbius transformations and has the same limit set.
/// </summary>
public sealed record KleinianSettings
{
    public KleinianConfiguration Configuration { get; init; }
    /// <summary>Radius / half the shortest edge: 1 is tangency, smaller values open gaps.</summary>
    public double RadiusRatio { get; init; } = 1;
    /// <summary>Stop subdividing a cylinder ball once its radius is below this world distance.</summary>
    public double MinRadius { get; init; } = .006;
    /// <summary>World-space thickening of the finite approximation, independent of pixel size.</summary>
    public double Thickness { get; init; } = .003;

    public void Validate()
    {
        if (!Enum.IsDefined(Configuration)) throw new ArgumentException("Неизвестная конфигурация группы Клейна.");
        Range(RadiusRatio, .65, 1, "Относительный радиус сфер");
        Range(MinRadius, .001, .05, "Минимальный радиус деталей");
        Range(Thickness, 0, .04, "Утолщение множества");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: {min} … {max}.");
    }

    public static string ConfigurationName(KleinianConfiguration configuration) => configuration switch
    {
        KleinianConfiguration.Tetrahedron => "Тетраэдр + центр · 5 сфер",
        KleinianConfiguration.Cube => "Куб + центр · 9 сфер",
        _ => "Октаэдр + центр · 7 сфер"
    };
}
