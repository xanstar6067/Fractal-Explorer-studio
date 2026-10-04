using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal readonly record struct Buddhabrot4DPoint(Vector4 Position, int EscapeIteration);

/// <summary>Bounded, unbiased reservoir of escaped orbit visits. Projection never changes this sample.</summary>
internal sealed class Buddhabrot4DOrbitCloud
{
    public const int PointLimit = 1_500_000;
    public Buddhabrot4DSettings Settings { get; }
    public ReadOnlyMemory<Buddhabrot4DPoint> Points { get; }
    public long TotalVisits { get; }

    private Buddhabrot4DOrbitCloud(Buddhabrot4DSettings settings, Buddhabrot4DPoint[] points, long visits)
    {
        Settings = settings with { };
        Points = points;
        TotalVisits = visits;
    }

    public static Buddhabrot4DOrbitCloud Build(Buddhabrot4DSettings source, CancellationToken token,
        int pointLimit = PointLimit)
    {
        token.ThrowIfCancellationRequested();
        var settings = source with { }; settings.Validate();
        if (pointLimit < 1 || pointLimit > PointLimit) throw new ArgumentOutOfRangeException(nameof(pointLimit));
        var points = new Buddhabrot4DPoint[pointLimit];
        var history = new Vector2[settings.MaxIterations];
        var seeds = new Random(settings.Seed);
        // A separate stream keeps the chosen c's independent of the reservoir capacity.
        var reservoir = new Random(settings.Seed ^ 0x36C91A7);
        long visits = 0;
        for (int sample = 0; sample < settings.SampleCount; sample++)
        {
            if ((sample & 255) == 0) token.ThrowIfCancellationRequested();
            double cr = seeds.NextDouble() * 3 - 2, ci = seeds.NextDouble() * 3 - 1.5;
            double q = (cr - .25) * (cr - .25) + ci * ci;
            if (q * (q + cr - .25) <= .25 * ci * ci || (cr + 1) * (cr + 1) + ci * ci <= .0625)
                continue; // Exact interior tests: these seeds cannot contribute to the Buddhabrot.
            int escape = TraceOrbit(cr, ci, history, token);
            if (escape < settings.MinIterations) continue;
            // z0 and the point crossing |z|=2 are excluded: neither is a pre-escape visit.
            for (int n = 0; n < escape - 1; n++)
            {
                if ((n & 255) == 0) token.ThrowIfCancellationRequested();
                long index = visits < pointLimit ? visits : reservoir.NextInt64(visits + 1);
                visits++;
                if (index >= pointLimit) continue;
                Vector2 z = history[n];
                points[(int)index] = new(new(z.X, z.Y, (float)cr, (float)ci), escape);
            }
        }
        token.ThrowIfCancellationRequested();
        if (visits < 100) throw new InvalidOperationException("Мало подходящих орбит. Увеличьте число затравок или уменьшите минимум итераций.");
        Array.Resize(ref points, (int)Math.Min(visits, pointLimit));
        return new(settings, points, visits);
    }

    /// <returns>First escape iteration, or zero for an orbit bounded through the limit.</returns>
    internal static int TraceOrbit(double cr, double ci, Span<Vector2> history, CancellationToken token)
    {
        double zr = 0, zi = 0;
        for (int i = 0; i < history.Length; i++)
        {
            if ((i & 63) == 0) token.ThrowIfCancellationRequested();
            (zr, zi) = (zr * zr - zi * zi + cr, 2 * zr * zi + ci);
            history[i] = new((float)zr, (float)zi);
            if (zr * zr + zi * zi > 4) return i + 1;
        }
        return 0;
    }
}
