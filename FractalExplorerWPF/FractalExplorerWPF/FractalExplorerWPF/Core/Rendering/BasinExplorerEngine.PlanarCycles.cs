using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public sealed partial class BasinExplorerEngine
{
    private PlanarBasinAttractor? FindPlanarCycle(Complex z, CancellationToken token)
    {
        double time = 0, h = Math.Min(0.05, _planar.TimeStep);
        double transient = Math.Min(20, _planar.MaxTime / 4);
        Complex anchor = Complex.Zero, normal = Complex.Zero, previousCrossing = Complex.Zero;
        double lastCrossingTime = 0, previousPeriod = 0, divergenceIntegral = 0;
        bool sectionReady = false, hasCrossing = false;
        int repeats = 0;
        var lap = new List<Complex>();
        double maxStep = Math.Min(0.025, _planar.TimeStep);
        for (int step = 0; step < MaxIterations && time < PlanarEndTime; step++)
        {
            if ((step & 15) == 0) token.ThrowIfCancellationRequested();
            Complex speed = _planarField!(z);
            if (!IsFinite(speed) || z.Magnitude > _planar.EscapeRadius || speed.Magnitude < 1e-8 || PlanarPointTarget(z, speed) >= 0) return null;
            if (!sectionReady && time >= transient)
            {
                anchor = z; normal = speed / speed.Magnitude; sectionReady = true;
            }
            if (!PlanarFlowIntegrator.Step(_planarField, z, ref h, maxStep, _planar.IntegrationTolerance,
                    _planar.MaxTime - time, out Complex next, out double dt, token)) return null;
            if (sectionReady && CrossesSection(z, next, anchor, normal))
            {
                (Complex crossing, double fraction) = RefineSection(z, dt, anchor, normal);
                double crossingTime = time + dt * fraction;
                double period = crossingTime - lastCrossingTime;
                double tolerance = Math.Max(5 * _planar.ConvergenceTolerance, 30 * _planar.IntegrationTolerance) * (1 + crossing.Magnitude);
                if (hasCrossing && period > maxStep * 8)
                {
                    lap.Add(crossing);
                    double integral = divergenceIntegral + 0.5 * dt * fraction * (Divergence(z) + Divergence(crossing));
                    bool closed = (crossing - previousCrossing).Magnitude < tolerance &&
                        Math.Abs(period - previousPeriod) < tolerance * (1 + period);
                    repeats = closed ? repeats + 1 : 0;
                    // Сходство возвратов само по себе не доказывает устойчивость: центр (μ=1)
                    // или отталкивающий цикл запрещено принимать за притягивающий.
                    if (repeats >= 3 && integral < -0.001 && double.IsFinite(integral) && lap.Count >= 8 &&
                        lap.Max(p => (p - crossing).Magnitude) > 100 * tolerance)
                        return new() { Points = [.. lap], Period = period, TransverseMultiplier = Math.Exp(integral) };
                    previousPeriod = period;
                }
                hasCrossing = true; previousCrossing = crossing; lastCrossingTime = crossingTime;
                lap.Clear(); lap.Add(crossing);
                divergenceIntegral = 0.5 * dt * (1 - fraction) * (Divergence(crossing) + Divergence(next));
            }
            else if (hasCrossing)
                divergenceIntegral += 0.5 * dt * (Divergence(z) + Divergence(next));
            if (hasCrossing && lap.Count < 20000) lap.Add(next);
            z = next; time += dt;
        }
        return null;
    }

    /// <summary>
    /// Тот же ли это цикл: траектория из точки за полтора периода известного цикла пересекает его
    /// секцию у опорной точки. Расстояние до ломаной точек цикла для этого ненадёжно: на быстрых
    /// участках релаксационных колебаний хорды проходят далеко от самой кривой.
    /// </summary>
    private bool PassesThroughCycleAnchor(Complex start, PlanarBasinAttractor known)
    {
        Complex anchor = known.Points[0], speed = _planarField!(anchor);
        if (!(speed.Magnitude > 0)) return false;
        Complex normal = speed / speed.Magnitude;
        double tolerance = Math.Max(1e-3, 50 * _planar.ConvergenceTolerance) * (1 + anchor.Magnitude);
        double maxStep = Math.Min(0.025, _planar.TimeStep), h = maxStep, time = 0, end = 1.5 * known.Period;
        Complex z = start;
        for (int step = 0; step < 1_000_000 && time < end * (1 - 1e-9); step++)
        {
            if (!PlanarFlowIntegrator.Step(_planarField, z, ref h, maxStep, _planar.IntegrationTolerance,
                    end - time, out Complex next, out double dt, CancellationToken.None)) return false;
            if (CrossesSection(z, next, anchor, normal) && (RefineSection(z, dt, anchor, normal).Point - anchor).Magnitude < tolerance)
                return true;
            z = next; time += dt;
        }
        return false;
    }

    private double Divergence(Complex z)
    {
        var (a, _, _, d) = _planarJacobian!(z);
        return a + d;
    }

    private static double Dot(Complex left, Complex right) => left.Real * right.Real + left.Imaginary * right.Imaginary;
    private static bool CrossesSection(Complex from, Complex to, Complex anchor, Complex normal) =>
        Dot(from - anchor, normal) < 0 && Dot(to - anchor, normal) >= 0;

    private (Complex Point, double Fraction) RefineSection(Complex from, double dt, Complex anchor, Complex normal)
    {
        double lo = 0, hi = 1;
        Complex midpoint = from;
        for (int i = 0; i < 22; i++)
        {
            double fraction = (lo + hi) / 2;
            midpoint = PlanarFlowIntegrator.Rk4(_planarField!, from, dt * fraction / 2);
            midpoint = PlanarFlowIntegrator.Rk4(_planarField!, midpoint, dt * fraction / 2);
            if (Dot(midpoint - anchor, normal) < 0) lo = fraction;
            else hi = fraction;
        }
        return (midpoint, (lo + hi) / 2);
    }

    private static double DistanceToCycle(Complex point, IReadOnlyList<Complex> points)
    {
        double distance = double.PositiveInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            Complex a = points[i], edge = points[(i + 1) % points.Count] - a;
            double squared = MagnitudeSquared(edge);
            double fraction = squared == 0 ? 0 : Math.Clamp(Dot(point - a, edge) / squared, 0, 1);
            distance = Math.Min(distance, (point - a - fraction * edge).Magnitude);
        }
        return distance;
    }
}
