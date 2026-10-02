using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum Dla3DSeedShape { Point, Line, Ring, Plane }

/// <summary>Reproducible lattice DLA. Counts exclude the seed; flow points in the direction of travel.</summary>
public sealed record Dla3DSettings
{
    public const int MaxParticles = 20_000;
    public int Seed { get; set; } = 42;
    public Dla3DSeedShape SeedShape { get; set; }
    public int SeedSize { get; set; } = 12;
    public double Stickiness { get; set; } = 1;
    public double FlowStrength { get; set; }
    public double FlowYaw { get; set; }
    public double FlowPitch { get; set; } = -90;
    public int ParticleCount { get; set; } = 6_000;
    public int TargetParticles { get; set; } = 6_000;

    public Dla3DSettings Normalized() => this with
    {
        SeedShape = Enum.IsDefined(SeedShape) ? SeedShape : Dla3DSeedShape.Point,
        SeedSize = Math.Clamp(SeedSize, 2, 24),
        Stickiness = Finite(Stickiness, .05, 1, 1),
        FlowStrength = Finite(FlowStrength, 0, .65, 0),
        FlowYaw = Finite(FlowYaw, -180, 180, 0),
        FlowPitch = Finite(FlowPitch, -90, 90, -90),
        ParticleCount = Math.Clamp(ParticleCount, 0, MaxParticles),
        TargetParticles = Math.Clamp(TargetParticles, 100, MaxParticles)
    };

    public Vector3 FlowDirection
    {
        get
        {
            double yaw = FlowYaw * Math.PI / 180, pitch = FlowPitch * Math.PI / 180;
            return new((float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch),
                (float)(Math.Cos(pitch) * Math.Cos(yaw)));
        }
    }

    public bool SameGrowth(Dla3DSettings other) => Seed == other.Seed && SeedShape == other.SeedShape &&
        SeedSize == other.SeedSize && Stickiness == other.Stickiness && FlowStrength == other.FlowStrength &&
        (FlowStrength == 0 || FlowYaw == other.FlowYaw && FlowPitch == other.FlowPitch);

    public static string ShapeName(Dla3DSeedShape shape) => shape switch
    {
        Dla3DSeedShape.Line => "Стержень", Dla3DSeedShape.Ring => "Кольцо",
        Dla3DSeedShape.Plane => "Диск", _ => "Точка"
    };

    private static double Finite(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
