using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

public enum HyperbolicHoneycombKind { Cubic435, Dodecahedral534, Dodecahedral535, Icosahedral353 }

/// <summary>Compact regular honeycombs in the Poincaré ball; lengths use curvature −1.</summary>
public sealed record HyperbolicHoneycombSettings
{
    public HyperbolicHoneycombKind Tessellation { get; init; } = HyperbolicHoneycombKind.Dodecahedral534;
    /// <summary>Radial shells, not a count of cells or Coxeter word length.</summary>
    public int Depth { get; init; } = 3;
    public bool ShowEdges { get; init; } = true;
    public bool ShowFaces { get; init; }
    public double EdgeRadius { get; init; } = .055;
    public double FaceThickness { get; init; } = .018;
    public double FaceOpacity { get; init; } = .16;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    /// <summary>Origin of a Lorentz boost, expressed in the original unit ball.</summary>
    public double ObserverX { get; init; }
    public double ObserverY { get; init; }
    public double ObserverZ { get; init; }

    [JsonIgnore]
    public (int P, int Q, int R) Symbol => Tessellation switch
    {
        HyperbolicHoneycombKind.Cubic435 => (4, 3, 5),
        HyperbolicHoneycombKind.Dodecahedral534 => (5, 3, 4),
        HyperbolicHoneycombKind.Dodecahedral535 => (5, 3, 5),
        _ => (3, 5, 3)
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Tessellation) || Depth is < 1 or > 6 || CutAxis is < 0 or > 3)
            throw new ArgumentException("Неизвестные соты, глубина вне 1…6 или ось среза.");
        Range(EdgeRadius, .012, .2, "Радиус рёбер");
        Range(FaceThickness, .006, .08, "Толщина граней");
        Range(FaceOpacity, 0, 1, "Непрозрачность граней");
        Range(CutPosition, -1, 1, "Положение среза");
        if (!double.IsFinite(ObserverX) || !double.IsFinite(ObserverY) || !double.IsFinite(ObserverZ) ||
            ObserverX * ObserverX + ObserverY * ObserverY + ObserverZ * ObserverZ > .85 * .85 + 1e-12)
            throw new ArgumentException("Точка гиперболического наблюдателя должна лежать в шаре радиуса 0,85.");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: {min} … {max}.");
    }
}
