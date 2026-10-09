using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record KobayashiSearchProgress(int Checked, int Total, bool Finalizing, double Fraction = 0);
public sealed record KobayashiSearchResult(Kobayashi3DSettings Settings, int Checked);
public sealed record KobayashiShapeMetrics(double Score, double Coverage, double RadialContrast, double Growth);

/// <summary>Bounded GPU trials and validation on the original grid. All trials use private fields.</summary>
public static class Kobayashi3DRandomizer
{
    public const int TrialCount = 8;

    public static Kobayashi3DSettings Candidate(Kobayashi3DSettings current, bool variation, Random random)
    {
        double Between(double a, double b) => a + random.NextDouble() * (b - a);
        double Jitter(double value, double lo, double hi, double strength = .16) =>
            Math.Clamp(value * Math.Exp(Between(-strength, strength)), lo, hi);
        var shape = variation && current.SeedShape != Kobayashi3DSeed.Empty ? current.SeedShape :
            random.Next(8) switch { < 4 => Kobayashi3DSeed.Sphere, < 6 => Kobayashi3DSeed.RandomSpheres,
                6 => Kobayashi3DSeed.Ring, _ => Kobayashi3DSeed.EightSpheres };
        double radius = shape == Kobayashi3DSeed.Sphere ? Between(.068, .095) : Between(.06, .08);
        double steps64 = shape == Kobayashi3DSeed.Sphere ? Between(4500, 8500) : Between(2200, 4400);
        double anisotropy = Between(.04, .059) * (random.Next(2) == 0 ? -1 : 1);
        return current with
        {
            Field = null, Live = null,
            Seed = variation ? current.Seed : random.Next(int.MinValue, int.MaxValue), SeedShape = shape,
            SeedCount = variation ? current.SeedCount : random.Next(2, 5),
            SeedRadius = variation ? Jitter(current.SeedRadius, .035, .16, .08) : radius,
            SeedSpread = variation ? Jitter(current.SeedSpread, 0, .3, .1) : Between(.09, .17),
            InterfaceWidth = variation ? Jitter(current.InterfaceWidth, .5, 2, .08) : Between(.5, .68),
            Anisotropy = variation ? (current.Anisotropy == 0 ? Between(-.006, .006) :
                Math.CopySign(Jitter(Math.Abs(current.Anisotropy), 0, .06, .1), current.Anisotropy)) : anisotropy,
            Mobility = variation ? Jitter(current.Mobility, .1, 4) : Between(.8, 1.3),
            ThermalDiffusion = variation ? Jitter(current.ThermalDiffusion, .1, 4) : Between(.25, .85),
            LatentHeat = variation ? Jitter(current.LatentHeat, 0, 3) : Between(1.4, 2.3),
            Undercooling = variation ? Jitter(current.Undercooling, .05, 1.5, .1) : Between(.43, .7),
            Noise = variation ? Jitter(current.Noise, 0, .1) : Between(.02, .09),
            TimeStep = variation ? current.TimeStep : .04,
            WarmupSteps = variation ? (int)Jitter(Math.Max(1200, current.WarmupSteps), 1200, 20000, .12) :
                Math.Clamp((int)(steps64 * Math.Pow(current.Size / 64.0, 1.5)), 1200, 20000)
        };
    }

    public static double SolidMass(Kobayashi3DField field)
    {
        double sum = 0; var values = field.Concentrations;
        for (int i = 0; i < values.Length; i += 2) sum += values[i];
        return sum;
    }

    // Radial spread is measured separately around each connected crystal's centroid:
    // scattered balls alone do not earn a branching score. This is a visual heuristic.
    public static KobayashiShapeMetrics Measure(Kobayashi3DField field, double initialMass)
    {
        int n = field.Size, count = n * n * n, plane = n * n;
        var values = field.Concentrations; var labels = new int[count]; var queue = new int[count];
        int solid = 0, wall = 0, minX = n, minY = n, minZ = n, maxX = 0, maxY = 0, maxZ = 0;
        double mass = 0;
        for (int i = 0; i < count; i++)
        {
            mass += values[i * 2];
            if (values[i * 2] < .5) continue;
            solid++; int x = i % n, y = i / n % n, z = i / plane;
            if (x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == n - 1) wall++;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
        }
        double coverage = solid / (double)count, growth = initialMass > 0 ? mass / initialMass : 0;
        KobayashiShapeMetrics Rejected() => new(0, coverage, 0, growth);
        if (coverage < .004 || coverage > .28 || growth < 1.15 || wall > solid * .002 ||
            Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) > n * .9) return Rejected();
        var centers = new List<(int Count, double X, double Y, double Z)> { default };
        for (int start = 0; start < count; start++)
        {
            if (values[start * 2] < .5 || labels[start] != 0) continue;
            int id = centers.Count, head = 0, tail = 1; queue[0] = start; labels[start] = id;
            double sx = 0, sy = 0, sz = 0;
            while (head < tail)
            {
                int i = queue[head++], x = i % n, y = i / n % n, z = i / plane;
                sx += x; sy += y; sz += z;
                // No closure/local function captures a ReadOnlySpan.
                for (int d = 0; d < 6; d++)
                {
                    int j = d switch { 0 when x > 0 => i - 1, 1 when x < n - 1 => i + 1,
                        2 when y > 0 => i - n, 3 when y < n - 1 => i + n,
                        4 when z > 0 => i - plane, 5 when z < n - 1 => i + plane, _ => -1 };
                    if (j >= 0 && labels[j] == 0 && values[j * 2] >= .5) { labels[j] = id; queue[tail++] = j; }
                }
            }
            centers.Add((tail, sx / tail, sy / tail, sz / tail));
        }
        var radii = new double[centers.Count]; var squares = new double[centers.Count]; var surfaces = new int[centers.Count];
        for (int i = 0; i < count; i++)
        {
            int id = labels[i]; if (id == 0) continue;
            int x = i % n, y = i / n % n, z = i / plane;
            if (x > 0 && x < n - 1 && y > 0 && y < n - 1 && z > 0 && z < n - 1 &&
                labels[i - 1] == id && labels[i + 1] == id && labels[i - n] == id && labels[i + n] == id &&
                labels[i - plane] == id && labels[i + plane] == id) continue;
            var c = centers[id]; double r2 = Math.Pow(x - c.X, 2) + Math.Pow(y - c.Y, 2) + Math.Pow(z - c.Z, 2);
            radii[id] += Math.Sqrt(r2); squares[id] += r2; surfaces[id]++;
        }
        double contrast = 0; int substantial = 0;
        for (int id = 1; id < centers.Count; id++)
        {
            var c = centers[id]; if (c.Count < Math.Max(12, solid / 200) || surfaces[id] < 12) continue;
            double mean = radii[id] / surfaces[id];
            contrast += c.Count * Math.Sqrt(Math.Max(0, squares[id] / surfaces[id] / (mean * mean) - 1));
            substantial += c.Count;
        }
        if (substantial < solid * .95) return Rejected();
        contrast /= substantial;
        if (!double.IsFinite(contrast) || contrast < .09) return new(0, coverage, contrast, growth);
        double score = Math.Pow(coverage, 1.0 / 3) * Math.Min(contrast, .8) * Math.Log(1 + growth);
        return new(score, coverage, contrast, growth);
    }

    public static KobayashiSearchResult? Search(Direct3DDeviceHost host, Kobayashi3DSettings current,
        bool variation, IProgress<KobayashiSearchProgress>? progress, CancellationToken token, int? randomSeed = null)
    {
        current.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(randomSeed ?? Random.Shared.Next());
        var ranked = new List<(Kobayashi3DSettings Settings, double Score)>();
        using var simulation = new Kobayashi3DGpuSimulation(host, current with
            { Size = Math.Min(current.Size, 48), Field = null, Live = null });
        for (int trial = 0; trial < TrialCount; trial++)
        {
            token.ThrowIfCancellationRequested(); var candidate = Candidate(current, variation, random);
            int size = Math.Min(candidate.Size, 48);
            var probe = candidate with { Size = size,
                WarmupSteps = Math.Clamp((int)(candidate.WarmupSteps * Math.Pow(size / (double)candidate.Size, 1.5)), 1200, 20000) };
            simulation.Reset(probe); double mass = SolidMass(simulation.ReadCurrent());
            Grow(probe.WarmupSteps, trial, false);
            var metrics = Measure(simulation.ReadCurrent(), mass);
            if (metrics.Score > 0) ranked.Add((candidate, metrics.Score));
            progress?.Report(new(trial + 1, TrialCount, false));
        }
        // Different grids change the physical domain. A coarse success is never published directly.
        foreach (var candidate in ranked.OrderByDescending(x => x.Score).Take(3))
        {
            token.ThrowIfCancellationRequested(); simulation.Reset(candidate.Settings);
            double mass = SolidMass(simulation.ReadCurrent());
            // Select a growing stage before long arms reach the walls; do not weaken
            // the full-grid filter just because the shorter trial looked promising.
            foreach (int target in new[] { (int)(candidate.Settings.WarmupSteps * .8), candidate.Settings.WarmupSteps })
            {
                Grow(target, TrialCount, true);
                token.ThrowIfCancellationRequested(); var field = simulation.ReadCurrent();
                if (Measure(field, mass).Score > 0)
                    return new(candidate.Settings with { Field = field, WarmupSteps = (int)field.Step }, TrialCount);
            }
        }
        return null;

        void Grow(int steps, int completed, bool finalizing)
        {
            int done = (int)simulation.Step;
            while (done < steps)
            {
                token.ThrowIfCancellationRequested();
                done += simulation.Advance(Math.Min(128, steps - done), token);
                token.ThrowIfCancellationRequested();
                progress?.Report(new(completed, TrialCount, finalizing, done / (double)steps));
            }
        }
    }
}
