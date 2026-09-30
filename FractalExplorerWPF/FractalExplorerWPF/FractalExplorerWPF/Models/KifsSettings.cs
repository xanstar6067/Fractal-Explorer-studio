namespace FractalExplorerWPF.Models;

public enum KifsSymmetry { Tetrahedral, Octahedral, Cubic, Dihedral }
public enum KifsSeed { Sphere, Cube, Octahedron }

/// <summary>Отражения → поворот X/Y/Z → масштаб → смещение на каждой итерации.</summary>
public sealed class KifsSettings
{
    public KifsSymmetry Symmetry { get; set; }
    public KifsSeed Seed { get; set; } = KifsSeed.Sphere;
    public int Sectors { get; set; } = 6;
    public double Scale { get; set; } = 2;
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double RotationZ { get; set; }
    public double OffsetX { get; set; } = 1;
    public double OffsetY { get; set; } = 1;
    public double OffsetZ { get; set; } = 1;
    public double Radius { get; set; } = 1;

    public KifsSettings Clone() => (KifsSettings)MemberwiseClone();
    public KifsSettings Normalized() => new()
    {
        Symmetry = Enum.IsDefined(Symmetry) ? Symmetry : KifsSymmetry.Tetrahedral,
        Seed = Enum.IsDefined(Seed) ? Seed : KifsSeed.Sphere,
        Sectors = Math.Clamp(Sectors, 3, 16),
        Scale = Limit(Scale, 1.2, 4, 2), Radius = Limit(Radius, .1, 2, 1),
        RotationX = Limit(RotationX, -180, 180, 0),
        RotationY = Limit(RotationY, -180, 180, 0),
        RotationZ = Limit(RotationZ, -180, 180, 0),
        OffsetX = Limit(OffsetX, -2, 2, 1),
        OffsetY = Limit(OffsetY, -2, 2, 1),
        OffsetZ = Limit(OffsetZ, -2, 2, 1)
    };
    private static double Limit(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static string SymmetryName(KifsSymmetry symmetry) => symmetry switch
    {
        KifsSymmetry.Octahedral => "Октаэдр · восемь граней",
        KifsSymmetry.Cubic => "Куб · зеркала по осям",
        KifsSymmetry.Dihedral => "Звезда · радиальные зеркала",
        _ => "Тетраэдр · четыре вершины"
    };
}
