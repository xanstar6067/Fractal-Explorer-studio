using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum AbcSeedGroup { Cloud, Ring, Plane, Neighbours }
public enum AbcSectionAxis { X, Y, Z }

/// <summary>ABC on the torus [−π,π)³. Time is physical integration time, coordinates are radians.</summary>
public sealed record AbcFlowSettings
{
    public double A { get; init; } = 1;
    public double B { get; init; } = 1;
    public double C { get; init; } = 1;
    public AbcSeedGroup Group { get; init; } = AbcSeedGroup.Cloud;
    public int SeedCount { get; init; } = 12;
    public double StartX { get; init; } = .3;
    public double StartY { get; init; } = .2;
    public double StartZ { get; init; } = .1;
    public double Spread { get; init; } = 2.6;
    public int Samples { get; init; } = 768;
    public double TimeStep { get; init; } = .02;
    public const int Substeps = 4;
    public double Thickness { get; init; } = .008;
    public bool ShowTrails { get; init; } = true;
    public bool ShowParticles { get; init; } = true;
    public double Phase { get; init; } = .25;
    public double PlaybackSpeed { get; init; } = 2;
    public AbcSectionAxis SectionAxis { get; init; } = AbcSectionAxis.Z;
    public double SectionPosition { get; init; }
    public bool PositiveCrossings { get; init; } = true;
    public bool SectionOnly { get; init; }
    public double LyapunovRange { get; init; } = .3;
    public double Duration => Samples * Substeps * TimeStep;

    // Appearance and playback do not reintegrate the trajectories.
    public AbcFlowSettings IntegrationKey() => this with
    {
        Thickness = .008, ShowTrails = true, ShowParticles = true, Phase = 0,
        PlaybackSpeed = 2, SectionAxis = AbcSectionAxis.Z, SectionPosition = 0,
        PositiveCrossings = true, SectionOnly = false, LyapunovRange = .3
    };
    public AbcFlowSettings GeometryKey() => this with { Phase = 0, PlaybackSpeed = 2, ShowTrails = true, ShowParticles = true };

    public void Validate()
    {
        if (!Enum.IsDefined(Group) || !Enum.IsDefined(SectionAxis) || SeedCount is < 1 or > 48 || Samples is < 64 or > 2048)
            throw new ArgumentException("Число траекторий: 1–48; отсчётов: 64–2048.");
        if (new[] { A, B, C }.Any(v => !Finite(v, -3, 3)) ||
            new[] { StartX, StartY, StartZ, SectionPosition }.Any(v => !Finite(v, -Math.PI, Math.PI)) ||
            !Finite(Spread, .0001, Math.PI) || !Finite(TimeStep, .001, .04) ||
            !Finite(Thickness, .002, .04) || !Finite(Phase, 0, 1) || !Finite(PlaybackSpeed, .1, 20) ||
            !Finite(LyapunovRange, .01, 2))
            throw new ArgumentException("Недопустимые параметры потока ABC.");
        if (!SectionOnly && !ShowTrails && !ShowParticles)
            throw new ArgumentException("Включите линии или частицы.");
    }

    public Vector3[] Seeds()
    {
        var seeds = new Vector3[SeedCount];
        Vector3 centre = new((float)StartX, (float)StartY, (float)StartZ);
        for (int i = 0; i < seeds.Length; i++)
        {
            double u = seeds.Length == 1 ? 0 : i / (double)(seeds.Length - 1);
            double angle = Math.Tau * i / seeds.Length;
            Vector3 offset = Group switch
            {
                AbcSeedGroup.Ring => new((float)Math.Cos(angle), (float)Math.Sin(angle), 0),
                AbcSeedGroup.Plane => new((float)(2 * u - 1), (float)(2 * Fraction(i * .61803398875) - 1), 0),
                AbcSeedGroup.Neighbours => new((float)u, 0, 0),
                _ => new((float)(2 * Fraction((i + 1) * .75487766625) - 1),
                    (float)(2 * Fraction((i + 1) * .56984029099) - 1), (float)(2 * Fraction((i + 1) * .438579021) - 1))
            };
            seeds[i] = Wrap(centre + offset * (float)Spread);
        }
        return seeds;
    }
    public static Vector3 Wrap(Vector3 p) => new(Wrap(p.X), Wrap(p.Y), Wrap(p.Z));
    public static float Wrap(float x) => (float)(x - Math.Tau * Math.Floor((x + Math.PI) / Math.Tau));
    private static double Fraction(double x) => x - Math.Floor(x);
    private static bool Finite(double x, double lo, double hi) => double.IsFinite(x) && x >= lo && x <= hi;
}
