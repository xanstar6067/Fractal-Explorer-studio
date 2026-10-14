using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public readonly record struct SingularOrbitResult(TranscendentalOrbitOutcome Outcome, int Period, int Iterations, Complex Last, double Multiplier);
public readonly record struct TranscendentalParameterResult(SingularOrbitResult First, SingularOrbitResult? Second)
{
    public bool AllAttracting => First.Outcome == TranscendentalOrbitOutcome.Converged &&
        (Second is null || Second.Value.Outcome == TranscendentalOrbitOutcome.Converged);
}

/// <summary>Карта конечных сингулярных орбит. Период распознаётся вместе с проверкой притяжения.</summary>
public static class TranscendentalParameterMap
{
    public static TranscendentalParameterResult Analyze(TranscendentalJuliaKind kind, Complex lambda,
        int iterations, int maxPeriod, double tolerance, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var map = new TranscendentalMap(kind, lambda);
        var first = Follow(map, kind == TranscendentalJuliaKind.Exponential ? Complex.Zero : lambda,
            iterations, maxPeriod, tolerance, token);
        // Обе орбиты вычисляются независимо, даже при симметрии sin(−z)=−sin(z).
        SingularOrbitResult? second = kind == TranscendentalJuliaKind.Sine
            ? Follow(map, -lambda, iterations, maxPeriod, tolerance, token) : null;
        return new(first, second);
    }

    private static SingularOrbitResult Follow(TranscendentalMap map, Complex z, int iterations,
        int maxPeriod, double tolerance, CancellationToken token)
    {
        if (iterations < 1 || maxPeriod is < 1 or > 32 || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterations));
        if (map.Lambda == Complex.Zero) return new(TranscendentalOrbitOutcome.Converged, 1, 0, Complex.Zero, 0);
        Span<Complex> history = stackalloc Complex[2 * maxPeriod + 1];
        for (int i = 0; i <= iterations; i++)
        {
            if ((i & 31) == 0) token.ThrowIfCancellationRequested();
            if (!TranscendentalJuliaDynamics.IsFinite(z)) return new(TranscendentalOrbitOutcome.NumericalLimit, 0, i, z, double.NaN);
            history[i % history.Length] = z;
            if (i >= 24 && i % 4 == 0)
                for (int p = 1; p <= Math.Min(maxPeriod, i / 2); p++)
                {
                    double limit = tolerance * Math.Max(1, z.Magnitude);
                    if ((z - history[(i - p) % history.Length]).Magnitude > limit ||
                        (z - history[(i - 2 * p) % history.Length]).Magnitude > limit) continue;
                    Complex multiplier = Complex.One;
                    for (int j = 0; j < p; j++) multiplier *= map.Derivative(history[(i - j) % history.Length]);
                    if (TranscendentalJuliaDynamics.IsFinite(multiplier) && multiplier.Magnitude < 1 - 1e-8)
                        return new(TranscendentalOrbitOutcome.Converged, p, i, z, multiplier.Magnitude);
                }
            if (i == iterations) break;
            if (!map.TryStep(z, out Complex next)) return new(TranscendentalOrbitOutcome.NumericalLimit, 0, i + 1, z, double.NaN);
            z = next;
        }
        return new(TranscendentalOrbitOutcome.IterationLimit, 0, iterations, z, double.NaN);
    }
}
