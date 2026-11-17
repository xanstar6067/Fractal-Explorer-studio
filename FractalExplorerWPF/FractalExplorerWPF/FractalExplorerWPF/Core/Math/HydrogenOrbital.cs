using System.Collections.Concurrent;
using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.QuantumMath;

/// <summary>Normalized Coulomb bound states, Z=1, a₀=1. Condon–Shortley convention.</summary>
internal static class HydrogenOrbital
{
    private static readonly ConcurrentDictionary<(int N, int L, int M, QuantumOrbitalBasis Basis), double> Peaks = new();
    internal static double Factorial(int n) { double v = 1; for (int i = 2; i <= n; i++) v *= i; return v; }
    internal static double Radial(int n, int l, double r)
    {
        double rho = 2 * r / n, a = 1, b = 1 + 2 * l + 1 - rho;
        int degree = n - l - 1;
        for (int k = 2; k <= degree; k++) { double c = ((2 * k + 2 * l - rho) * b - (k + 2 * l) * a) / k; a = b; b = c; }
        double laguerre = degree == 0 ? 1 : b;
        double norm = System.Math.Sqrt(System.Math.Pow(2.0 / n, 3) * Factorial(degree) / (2 * n * Factorial(n + l)));
        return norm * System.Math.Exp(-rho / 2) * System.Math.Pow(rho, l) * laguerre;
    }
    internal static double Angular(int l, int m, double cosine)
    {
        m = System.Math.Abs(m);
        double p = 1, s = System.Math.Sqrt(System.Math.Max(0, 1 - cosine * cosine));
        for (int i = 1; i <= m; i++) p *= -(2 * i - 1) * s;
        if (l > m)
        {
            double prev = p; p *= (2 * m + 1) * cosine;
            for (int k = m + 2; k <= l; k++) { double next = ((2 * k - 1) * cosine * p - (k + m - 1) * prev) / (k - m); prev = p; p = next; }
        }
        return p * System.Math.Sqrt((2 * l + 1) / (4 * System.Math.PI) * Factorial(l - m) / Factorial(l + m));
    }
    internal static Complex Evaluate(int n, int l, int m, QuantumOrbitalBasis basis, double x, double y, double z)
    {
        double r = System.Math.Sqrt(x * x + y * y + z * z);
        double a = Radial(n, l, r) * Angular(l, m, r > 1e-15 ? z / r : 1);
        double phi = System.Math.Atan2(y, x);
        if (basis == QuantumOrbitalBasis.Real)
            return new(a * (m == 0 ? 1 : System.Math.Sqrt(2) * ((System.Math.Abs(m) % 2 == 0) ? 1 : -1) *
                (m > 0 ? System.Math.Cos(m * phi) : System.Math.Sin(-m * phi))), 0);
        if (m < 0 && (-m) % 2 != 0) a = -a;
        return Complex.FromPolarCoordinates(System.Math.Abs(a), m * phi + (a < 0 ? System.Math.PI : 0));
    }
    internal static (double A, Complex B) Weights(QuantumOrbitalSettings s)
    {
        if (!s.Superposition) return (1, Complex.Zero);
        double a = System.Math.Sqrt(1 - s.Mix);
        Complex b = Complex.FromPolarCoordinates(System.Math.Sqrt(s.Mix), System.Math.IEEERemainder(s.Phase, 360) * System.Math.PI / 180);
        double norm = s.N == s.N2 && s.L == s.L2 && s.M == s.M2 ? (a + b).Magnitude : 1;
        return (a / System.Math.Max(norm, 1e-12), b / System.Math.Max(norm, 1e-12));
    }
    internal static Complex Evaluate(QuantumOrbitalSettings s, double x, double y, double z)
    {
        var (a, b) = Weights(s);
        return a * Evaluate(s.N, s.L, s.M, s.Basis, x, y, z) +
            (s.Superposition ? b * Evaluate(s.N2, s.L2, s.M2, s.Basis, x, y, z) : Complex.Zero);
    }
    // Separable numerical peak estimate used only for display exposure, never to define ψ.
    internal static double Peak(int n, int l, int m, QuantumOrbitalBasis basis) => Peaks.GetOrAdd((n, l, m, basis), key =>
    {
        double radial = 0, angular = 0;
        for (int i = 0; i <= 4096; i++) radial = System.Math.Max(radial, System.Math.Abs(Radial(n, l, 4.0 * n * n * i / 4096)));
        for (int i = 0; i <= 2048; i++) angular = System.Math.Max(angular, System.Math.Abs(Angular(l, m, -1.0 + 2.0 * i / 2048)));
        return radial * angular * (basis == QuantumOrbitalBasis.Real && m != 0 ? System.Math.Sqrt(2) : 1);
    });
    internal static double DisplayScale(QuantumOrbitalSettings s)
    {
        var (a, b) = Weights(s);
        return System.Math.Max(1e-12, a * Peak(s.N, s.L, s.M, s.Basis) +
            (s.Superposition ? b.Magnitude * Peak(s.N2, s.L2, s.M2, s.Basis) : 0));
    }
}
