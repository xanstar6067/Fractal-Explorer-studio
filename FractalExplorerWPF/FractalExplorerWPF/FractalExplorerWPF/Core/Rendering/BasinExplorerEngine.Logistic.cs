using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed partial class BasinExplorerEngine
{
    public LogisticPlaneMode LogisticPlane { get; set; }
    public Complex LogisticSeed { get; set; } = new(0.5, 0);

    public bool SetLogisticMap(out string debug, bool discover = true)
    {
        if (!IsLogisticParameter) return SetMapFormula("c*z*(1-z)", out debug, discover);
        ResetFormula();
        DebugInfo = debug = "zₙ₊₁ = λ·zₙ·(1−zₙ). Пиксель задаёт λ; z₀ фиксировано.\n" +
            "Цикл принимается после трёх совпавших оборотов и проверки |∏ λ(1−2z)| < 1.\n" +
            "Нейтральные, хаотические и не успевшие сойтись орбиты остаются нераспознанными.";
        return true;
    }

    // No parser or per-pixel allocation: each parameter has its own attractor.
    private BasinOrbitResult LogisticParameterOrbit(Complex lambda, CancellationToken token = default)
    {
        Span<Complex> history = stackalloc Complex[192];
        int count = 0, next = 0;
        Complex z = LogisticSeed;
        double escapeSquared = Math.Pow(Math.Max(EscapeRadius, 2 + 2 / Math.Max(1e-150, lambda.Magnitude)), 2);
        for (int iteration = 0; iteration <= MaxIterations; iteration++)
        {
            if ((iteration & 31) == 0) token.ThrowIfCancellationRequested();
            if (!IsFinite(z) || MagnitudeSquared(z) > escapeSquared)
                return new(BasinOrbitOutcome.Escaped, iteration, iteration, z);
            AddHistory(history, ref count, ref next, z);
            if (iteration >= 8 && (iteration % 4 == 0 || iteration == MaxIterations))
            {
                for (int period = 1; period <= MaxPeriod && period * 3 <= count; period++)
                {
                    bool matches = true;
                    Complex multiplier = Complex.One;
                    for (int j = 0; j < period; j++)
                    {
                        Complex current = GetRecent(history, next, j);
                        if (!AreClose(current, GetRecent(history, next, j + period), CycleTolerance) ||
                            !AreClose(current, GetRecent(history, next, j + 2 * period), CycleTolerance))
                        { matches = false; break; }
                        multiplier *= lambda * (1 - 2 * current);
                    }
                    if (matches && IsFinite(multiplier) && multiplier.Magnitude < 1 - 1e-7)
                        return new(BasinOrbitOutcome.Converged, iteration, iteration, z, period - 1, 0, period);
                }
            }
            z = lambda * z * (1 - z);
        }
        return new(BasinOrbitOutcome.IterationLimit, MaxIterations, MaxIterations, z);
    }

    private Color LogisticParameterColor(BasinOrbitResult result)
    {
        if (ColoringMode == BasinColoringMode.OrbitOutcome) return OutcomeColor(result);
        if (ColoringMode == BasinColoringMode.IterationCount)
            return result.Outcome is BasinOrbitOutcome.Converged or BasinOrbitOutcome.Escaped
                ? IterationHeatColor(result.Iterations) : BackgroundColor;
        if (result.Outcome != BasinOrbitOutcome.Converged || PeriodFilter > 0 && PeriodFilter != result.CyclePeriod)
            return BackgroundColor;
        Color color = ColoringMode == BasinColoringMode.Period ? PeriodPaletteColor(result.CyclePeriod) : TargetColor(result.TargetIndex);
        return ColoringMode == BasinColoringMode.Basins ? color : ShadeBySpeed(color, result.Iterations, result.CyclePeriod);
    }
}
