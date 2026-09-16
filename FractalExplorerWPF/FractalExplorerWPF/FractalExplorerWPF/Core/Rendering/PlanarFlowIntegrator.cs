using System.Numerics;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Адаптивная пара Dormand–Prince 5(4); отклонённые шаги не продвигают время.</summary>
internal static class PlanarFlowIntegrator
{
    public static bool Step(Func<Complex, Complex> field, Complex z, ref double nextStep, double maxStep,
        double tolerance, double remainingTime, out Complex next, out double elapsed, CancellationToken token)
    {
        next = z; elapsed = 0;
        double h = Math.Min(Math.Min(nextStep, maxStep), remainingTime);
        Complex k1 = field(z);
        if (!Finite(k1)) return false;
        for (int attempt = 0; attempt < 40 && h >= 1e-12; attempt++)
        {
            token.ThrowIfCancellationRequested();
            Complex k2 = field(z + h * (k1 / 5));
            Complex k3 = field(z + h * (3.0 / 40 * k1 + 9.0 / 40 * k2));
            Complex k4 = field(z + h * (44.0 / 45 * k1 - 56.0 / 15 * k2 + 32.0 / 9 * k3));
            Complex k5 = field(z + h * (19372.0 / 6561 * k1 - 25360.0 / 2187 * k2 + 64448.0 / 6561 * k3 - 212.0 / 729 * k4));
            Complex k6 = field(z + h * (9017.0 / 3168 * k1 - 355.0 / 33 * k2 + 46732.0 / 5247 * k3 + 49.0 / 176 * k4 - 5103.0 / 18656 * k5));
            Complex fifth = z + h * (35.0 / 384 * k1 + 500.0 / 1113 * k3 + 125.0 / 192 * k4 - 2187.0 / 6784 * k5 + 11.0 / 84 * k6);
            Complex k7 = field(fifth);
            Complex error = h * (71.0 / 57600 * k1 - 71.0 / 16695 * k3 + 71.0 / 1920 * k4 - 17253.0 / 339200 * k5 + 22.0 / 525 * k6 - k7 / 40);
            double relative = error.Magnitude / (tolerance * (1 + Math.Max(z.Magnitude, fifth.Magnitude)));
            if (!Finite(fifth) || !Finite(k7) || !double.IsFinite(relative)) { h *= 0.2; continue; }
            double factor = relative == 0 ? 5 : Math.Clamp(0.9 * Math.Pow(relative, -0.2), 0.2, 5);
            if (relative <= 1)
            {
                next = fifth; elapsed = h; nextStep = Math.Min(maxStep, h * factor);
                return true;
            }
            h *= Math.Min(0.9, factor);
        }
        return false;
    }

    /// <summary>Короткий шаг для уточнения пересечения секции внутри уже принятого шага.</summary>
    public static Complex Rk4(Func<Complex, Complex> field, Complex z, double h)
    {
        Complex k1 = field(z), k2 = field(z + h * k1 / 2), k3 = field(z + h * k2 / 2), k4 = field(z + h * k3);
        return z + h / 6 * (k1 + 2 * k2 + 2 * k3 + k4);
    }

    private static bool Finite(Complex z) => double.IsFinite(z.Real) && double.IsFinite(z.Imaginary);
}
