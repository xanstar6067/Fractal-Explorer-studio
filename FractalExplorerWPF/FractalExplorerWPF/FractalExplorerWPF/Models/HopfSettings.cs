using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum HopfFamily { Latitude, Latitudes, Sphere, LinkedTori, Custom }
public enum HopfRotationPlane { XY, XZ, XW, YZ, YW, ZW }

/// <summary>A point of S², in degrees. Its fiber is a great circle of S³.</summary>
public sealed record HopfPoint(double Longitude, double Latitude)
{
    public Vector3 OnSphere()
    {
        double a = Longitude * Math.PI / 180, b = Latitude * Math.PI / 180;
        return new((float)(Math.Cos(b) * Math.Cos(a)), (float)(Math.Cos(b) * Math.Sin(a)), (float)Math.Sin(b));
    }
}

public sealed record HopfSettings
{
    public HopfFamily Family { get; init; } = HopfFamily.Latitudes;
    public int Fibers { get; init; } = 18;
    public int Layers { get; init; } = 3;
    public double Latitude { get; init; } = 15;
    public double Spread { get; init; } = 75;
    public double Phase { get; init; }
    public double Thickness { get; init; } = .018;
    public double ClipRadius { get; init; } = 2.8;
    public double XY { get; init; }
    public double XZ { get; init; }
    public double XW { get; init; }
    public double YZ { get; init; }
    public double YW { get; init; }
    public double ZW { get; init; }
    public int SelectedFiber { get; init; } = -1;
    public bool OnlySelected { get; init; }
    public HopfRotationPlane AnimationPlane { get; init; } = HopfRotationPlane.XW;
    public double AnimationSpeed { get; init; } = 12;
    public List<HopfPoint> Points { get; init; } = [];

    public HopfSettings Copy() => this with { Points = Points.ToList() };

    public bool GeometryEquals(HopfSettings other) =>
        this with { Points = other.Points, AnimationPlane = other.AnimationPlane, AnimationSpeed = other.AnimationSpeed } == other
        && Points.SequenceEqual(other.Points);

    public void Validate()
    {
        if (!Enum.IsDefined(Family) || !Enum.IsDefined(AnimationPlane) || Fibers is < 1 or > 64 || Layers is < 1 or > 4)
            throw new ArgumentException("Число колец: 1–64 на семейство; число семейств: 1–4.");
        if (!Finite(Latitude, -85, 85) || !Finite(Spread, 0, 150) || !Finite(Phase, -180, 180) ||
            !Finite(Thickness, .003, .12) || !Finite(ClipRadius, 1, 8) || !Finite(AnimationSpeed, -60, 60) ||
            new[] { XY, XZ, XW, YZ, YW, ZW }.Any(a => !Finite(a, -180, 180)))
            throw new ArgumentException("Недопустимые параметры расслоения Хопфа.");
        if (Points is null || Points.Count > 256 || Points.Any(p => p is null ||
            !Finite(p.Longitude, -180, 180) || !Finite(p.Latitude, -90, 90)))
            throw new ArgumentException("На базовой сфере допускается до 256 точек.");
        int count = BasePoints().Count;
        if (SelectedFiber < -1 || SelectedFiber >= count || (OnlySelected && SelectedFiber < 0))
            throw new ArgumentException("Выберите кольцо на базовой сфере.");
    }

    public IReadOnlyList<HopfPoint> BasePoints()
    {
        if (Family == HopfFamily.Custom) return Points;
        var result = new List<HopfPoint>();
        int layers = Family is HopfFamily.Latitudes or HopfFamily.LinkedTori ? Layers : 1;
        int count = Fibers * layers;
        for (int layer = 0; layer < layers; layer++)
            for (int i = 0; i < Fibers; i++)
            {
                if (Family == HopfFamily.Sphere)
                {
                    double z = 1 - 2 * (i + .5) / count;
                    result.Add(new(Wrap(i * 137.50776405003785 + Phase), Math.Asin(z) * 180 / Math.PI));
                    continue;
                }
                double latitude = Math.Clamp(Latitude + (layers == 1 ? 0 : Spread * (layer / (double)(layers - 1) - .5)), -85, 85);
                var point = new HopfPoint(Wrap(360.0 * i / Fibers + Phase), latitude);
                if (Family == HopfFamily.LinkedTori)
                {
                    // Small circles around different axes lift to differently oriented tori.
                    Vector3 n = new HopfPoint(point.Longitude, Latitude).OnSphere();
                    n = Vector3.Transform(n, Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(layer * 2 * Math.PI / layers)));
                    point = FromSphere(n);
                }
                result.Add(point);
            }
        return result;
    }

    public static HopfPoint FromSphere(Vector3 n) => new(
        Math.Atan2(n.Y, n.X) * 180 / Math.PI, Math.Asin(Math.Clamp(n.Z, -1, 1)) * 180 / Math.PI);
    public static double Wrap(double angle) => (angle + 180 - 360 * Math.Floor((angle + 180) / 360)) - 180;
    private static bool Finite(double value, double lo, double hi) => double.IsFinite(value) && value >= lo && value <= hi;
}
