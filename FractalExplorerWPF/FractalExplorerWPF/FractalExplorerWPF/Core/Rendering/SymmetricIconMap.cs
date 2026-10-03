using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static class SymmetricIconMap
{
    public sealed record Analysis(double Radius, double Lyapunov, int OccupiedCells);
    public sealed record SearchResult(SymmetricIconSettings Settings, Analysis Analysis, int Attempts);

    // F(z) = (λ + α|z|² + β Re(zⁿ) + iω)z + γ conj(z)ⁿ⁻¹.
    // F commutes with rotations by 2π/n, and with conjugation when ω = 0.
    public static void Iterate(SymmetricIconSettings settings, ref double x, ref double y)
    {
        double real = x, imaginary = y;
        for (int i = 1; i < settings.Degree - 1; i++)
            (real, imaginary) = (real * x - imaginary * y, imaginary * x + real * y);
        double factor = settings.Lambda + settings.Alpha * (x * x + y * y) +
            settings.Beta * (x * real - y * imaginary);
        double omega = settings.Mirror ? 0 : settings.Omega;
        (x, y) = (factor * x + settings.Gamma * real - omega * y,
            factor * y - settings.Gamma * imaginary + omega * x);
    }

    public static bool IsBounded(double x, double y) => double.IsFinite(x) && double.IsFinite(y) && x * x + y * y < 64;

    public static Analysis? Analyze(SymmetricIconSettings settings, CancellationToken token,
        double startX = .01, double startY = .01)
    {
        settings.Validate();
        double x = startX, y = startY;
        for (int i = 0; i < 2_000; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            Iterate(settings, ref x, ref y);
            if (!IsBounded(x, y)) return null;
        }
        const double epsilon = 1e-7;
        double nearbyX = x + epsilon, nearbyY = y, sum = 0, radius = 0;
        HashSet<int> cells = [];
        for (int i = 0; i < 8_192; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            Iterate(settings, ref x, ref y);
            Iterate(settings, ref nearbyX, ref nearbyY);
            if (!IsBounded(x, y) || !IsBounded(nearbyX, nearbyY)) return null;
            radius = Math.Max(radius, Math.Sqrt(x * x + y * y));
            cells.Add((int)Math.Floor((x + 8) * 32) + 512 * (int)Math.Floor((y + 8) * 32));
            double dx = nearbyX - x, dy = nearbyY - y;
            double separation = Math.Sqrt(dx * dx + dy * dy);
            if (separation < 1e-20) return null;
            sum += Math.Log(separation / epsilon);
            nearbyX = x + epsilon * dx / separation;
            nearbyY = y + epsilon * dy / separation;
        }
        return new(radius, sum / 8_192, cells.Count);
    }

    public static SearchResult Search(SymmetricIconSettings basis, int seed, CancellationToken token,
        IProgress<int>? progress = null)
    {
        basis.Validate();
        var random = new Random(seed);
        for (int attempt = 1; attempt <= 500; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var candidate = basis.Clone();
            double strength = attempt < 150 ? .025 : .12;
            candidate.Lambda += (random.NextDouble() * 2 - 1) * strength;
            candidate.Alpha += (random.NextDouble() * 2 - 1) * strength * Math.Max(1, Math.Abs(basis.Alpha));
            candidate.Beta += (random.NextDouble() * 2 - 1) * strength * Math.Max(.1, Math.Abs(basis.Beta));
            candidate.Gamma += (random.NextDouble() * 2 - 1) * strength;
            if (!candidate.Mirror) candidate.Omega += (random.NextDouble() * 2 - 1) * strength;
            Analysis? analysis = Analyze(candidate, token);
            if (analysis is { Lyapunov: > .015, OccupiedCells: > 80, Radius: > .1 })
            {
                candidate.Span = analysis.Radius * 2.25;
                candidate.Seed = seed;
                return new(candidate, analysis, attempt);
            }
            progress?.Report(attempt * 100 / 500);
        }
        throw new InvalidOperationException("Не удалось найти хаотический орнамент. Выберите готовую форму и повторите поиск.");
    }
}
