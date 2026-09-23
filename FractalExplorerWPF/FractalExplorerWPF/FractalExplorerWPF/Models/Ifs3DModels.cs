using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public sealed class Ifs3DTransform
{
    public double M11 { get; set; }
    public double M12 { get; set; }
    public double M13 { get; set; }
    public double M21 { get; set; }
    public double M22 { get; set; }
    public double M23 { get; set; }
    public double M31 { get; set; }
    public double M32 { get; set; }
    public double M33 { get; set; }
    public double Tx { get; set; }
    public double Ty { get; set; }
    public double Tz { get; set; }
    public double Probability { get; set; } = 1;

    public Ifs3DTransform Clone() => (Ifs3DTransform)MemberwiseClone();

    public static Ifs3DTransform Contract(double scale, double x, double y, double z, double probability = 1) =>
        new() { M11 = scale, M22 = scale, M33 = scale, Tx = x, Ty = y, Tz = z, Probability = probability };
}

public sealed class Ifs3DState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? PointOfInterestId { get; set; }
    public int Iterations { get; set; } = 350_000;
    public double Yaw { get; set; } = 35;
    public double Pitch { get; set; } = 25;
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public Color PointColor { get; set; } = Colors.Aquamarine;
    public Color BackgroundColor { get; set; } = Colors.Black;
    public List<Ifs3DTransform> Transforms { get; set; } = [];

    public Ifs3DState Clone(string? name = null) => new()
    {
        SaveName = name ?? SaveName, Timestamp = Timestamp, PointOfInterestId = PointOfInterestId,
        Iterations = Iterations, Yaw = Yaw, Pitch = Pitch, Zoom = Zoom, PanX = PanX, PanY = PanY,
        PointColor = PointColor, BackgroundColor = BackgroundColor,
        Transforms = Transforms.Select(transform => transform.Clone()).ToList()
    };
}

public sealed class Ifs3DPreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Ifs3DState State { get; init; }
    public override string ToString() => Name;
}

public static class Ifs3DPresets
{
    public static IReadOnlyList<Ifs3DPreset> All { get; } =
    [
        new()
        {
            Id = "tetrahedron", Name = "Тетраэдр Серпинского",
            State = new Ifs3DState
            {
                Transforms =
                [
                    Ifs3DTransform.Contract(.5, -.5, -.5, -.5),
                    Ifs3DTransform.Contract(.5, .5, -.5, .5),
                    Ifs3DTransform.Contract(.5, -.5, .5, .5),
                    Ifs3DTransform.Contract(.5, .5, .5, -.5)
                ]
            }
        },
        new()
        {
            Id = "menger", Name = "Губка Менгера",
            State = new Ifs3DState
            {
                Iterations = 650_000, Yaw = 40, Pitch = 30,
                Transforms = CreateMenger()
            }
        },
        new()
        {
            Id = "fern", Name = "Объёмный папоротник",
            State = new Ifs3DState
            {
                Iterations = 450_000, Yaw = 30, Pitch = 15, PointColor = Colors.LimeGreen,
                Transforms =
                [
                    new() { M22 = .16, M33 = .35, Probability = .01 },
                    new() { M11 = .85, M12 = .04, M21 = -.04, M22 = .85, M31 = .08, M33 = .55, Ty = 1.6, Probability = .85 },
                    new() { M11 = .2, M12 = -.26, M21 = .23, M22 = .22, M32 = .08, M33 = .45, Ty = 1.6, Tz = .35, Probability = .07 },
                    new() { M11 = -.15, M12 = .28, M21 = .26, M22 = .24, M32 = -.08, M33 = .45, Ty = .44, Tz = -.35, Probability = .07 }
                ]
            }
        }
    ];

    private static List<Ifs3DTransform> CreateMenger()
    {
        var transforms = new List<Ifs3DTransform>();
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if ((x == 0 ? 1 : 0) + (y == 0 ? 1 : 0) + (z == 0 ? 1 : 0) >= 2)
                continue;
            transforms.Add(Ifs3DTransform.Contract(1d / 3, x * 2d / 3, y * 2d / 3, z * 2d / 3));
        }
        return transforms;
    }
}
