using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record PhysarumSearchProgress(int Checked, int Total, bool Finalizing);
public sealed record PhysarumSearchResult(Physarum3DSettings Settings, int Checked);

/// <summary>Bounded coarse GPU trials, followed by validation at the user's actual resolution.</summary>
public static class Physarum3DRandomizer
{
    public const int TrialCount = 10;

    public static Physarum3DSettings Candidate(Physarum3DSettings current, bool variation, Random random)
    {
        double Between(double a, double b) => a + random.NextDouble() * (b - a);
        double Jitter(double x, double a, double b) => Math.Clamp(x * Math.Exp(Between(-.3, .3)), a, b);
        return current with
        {
            Field = null, Live = null,
            Seed = variation ? current.Seed : random.Next(),
            SeedShape = variation ? current.SeedShape : (Physarum3DSeed)random.Next(4),
            SensorDistance = variation ? Jitter(current.SensorDistance, 1, 16) : Between(1.3, 4.5),
            SensorAngle = variation ? Jitter(current.SensorAngle, 5, 85) : Between(25, 70),
            TurnAngle = variation ? Jitter(current.TurnAngle, 1, 70) : Between(12, 48),
            Speed = variation ? Jitter(current.Speed, .1, 2) : Between(.4, 1.15),
            Deposit = variation ? Jitter(current.Deposit, .1, 8) : Between(1, 4),
            Diffusion = variation ? Jitter(current.Diffusion, 0, 1) : Between(.035, .22),
            Decay = variation ? Jitter(current.Decay, .001, .2) : Between(.008, .055),
            WarmupSteps = variation ? Math.Clamp((int)Jitter(Math.Max(80, current.WarmupSteps), 64, 320), 64, 320) : random.Next(96, 241)
        };
    }

    // Relative density statistics reject empty/flat fields and isolated spikes. The score is
    // a visual heuristic, not a promise of biological networks or a topology classifier.
    public static (double Score, double Exposure) Measure(ReadOnlySpan<float> trail, int size)
    {
        if (trail.Length != size * size * size) throw new ArgumentException("Размер поля не совпадает с сеткой.");
        var positive = new List<float>(); double sum = 0, squares = 0;
        foreach (float value in trail)
        {
            if (!float.IsFinite(value) || value < 0 || value >= 999) return (0, .12);
            if (value > .001) positive.Add(value);
            sum += value; squares += value * (double)value;
        }
        if (positive.Count < trail.Length * .005 || sum <= 0) return (0, .12);
        positive.Sort(); double peak = positive[(int)((positive.Count - 1) * .97)];
        double mean = sum / trail.Length, contrast = Math.Sqrt(Math.Max(0, squares / trail.Length - mean * mean)) / mean;
        int visible = 0; double gradient = 0;
        for (int z = 1; z < size - 1; z++) for (int y = 1; y < size - 1; y++) for (int x = 1; x < size - 1; x++)
        {
            int i = (z * size + y) * size + x; double v = trail[i];
            if (v > peak * .22) visible++;
            gradient += Math.Abs(v - trail[i + 1]) + Math.Abs(v - trail[i + size]) + Math.Abs(v - trail[i + size * size]);
        }
        double coverage = visible / (double)trail.Length;
        if (coverage < .005 || coverage > .55 || contrast < .35) return (0, .12);
        double score = Math.Min(contrast, 5) * Math.Sqrt(coverage) * Math.Min(gradient / sum, 2);
        return (score, Math.Clamp(2.5 / Math.Max(peak, .001), .05, 4));
    }

    public static PhysarumSearchResult? Search(Direct3DDeviceHost host, Physarum3DSettings current,
        bool variation, IProgress<PhysarumSearchProgress>? progress, CancellationToken token, int? randomSeed = null)
    {
        current.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(randomSeed ?? Random.Shared.Next());
        var ranked = new List<(Physarum3DSettings Settings, double Score)>();
        using var simulation = new Physarum3DGpuSimulation(host, current with { Size = Math.Min(current.Size, 64),
            AgentCount = Math.Max(1024, (int)(current.AgentCount * Math.Pow(Math.Min(current.Size, 64) / (double)current.Size, 3))), Field = null, Live = null });
        for (int i = 0; i < TrialCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var candidate = Candidate(current, variation, random);
            int size = Math.Min(candidate.Size, 64);
            var probe = candidate with { Size = size,
                AgentCount = Math.Max(1024, (int)(candidate.AgentCount * Math.Pow(size / (double)candidate.Size, 3))) };
            simulation.Reset(probe); simulation.Advance(probe.WarmupSteps, token); token.ThrowIfCancellationRequested();
            var metrics = Measure(simulation.ReadCurrent().Trail, size);
            if (metrics.Score > 0) ranked.Add((candidate, metrics.Score));
            progress?.Report(new(i + 1, TrialCount, false));
        }
        // A coarse grid changes the scale of the model; never publish it as the final result.
        foreach (var candidate in ranked.OrderByDescending(x => x.Score).Take(3))
        {
            token.ThrowIfCancellationRequested(); progress?.Report(new(TrialCount, TrialCount, true));
            simulation.Reset(candidate.Settings); simulation.Advance(candidate.Settings.WarmupSteps, token);
            token.ThrowIfCancellationRequested(); var field = simulation.ReadCurrent();
            var metrics = Measure(field.Trail, field.Size);
            if (metrics.Score > 0)
                return new(candidate.Settings with { Field = field, Exposure = metrics.Exposure, Threshold = .3 }, TrialCount);
        }
        return null;
    }
}
