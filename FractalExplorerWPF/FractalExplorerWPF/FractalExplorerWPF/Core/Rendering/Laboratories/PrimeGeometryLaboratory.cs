using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Геометрия простых чисел: спирали Улама, Сакса, шестиугольная, простые Гаусса и Эйзенштейна.</summary>
internal static class PrimeGeometryLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int radius = Math.Clamp(state.PrimaryValue, 8, 260);
        int colorModulus = Math.Clamp(state.SecondaryValue, 2, 64);
        int side = radius * 2 + 1;
        int maximum = state.Mode switch
        {
            0 => side * side + Math.Abs(state.TertiaryValue) + 10,
            1 or 2 => side * side + 10,
            3 => 2 * radius * radius + 10,
            _ => 3 * radius * radius + 10
        };
        bool[] primes = BuildPrimeSieve(Math.Max(32, maximum), token);
        double pointRadius = Math.Clamp(state.Parameter / Math.Min(canvas.Width, canvas.Height), 0.0012, 0.018);

        if (state.Mode == 0)
        {
            int x = 0, y = 0, dx = 1, dy = 0, segmentLength = 1, segmentProgress = 0, turns = 0;
            int count = side * side;
            var polynomialValues = new HashSet<int>();
            if (state.ShowGuides)
            {
                for (int n = -radius; n <= radius; n++)
                {
                    long value = (long)n * n + n + state.TertiaryValue;
                    if (value is >= 0 and <= int.MaxValue) polynomialValues.Add((int)value);
                }
            }
            for (int n = 1; n <= count; n++)
            {
                if (n < primes.Length && primes[n])
                {
                    Color color = polynomialValues.Contains(n) ? state.AccentColor : Palette(state, (double)(n % colorModulus) / colorModulus);
                    canvas.Circle(0.88 * x / radius, 0.88 * y / radius, pointRadius, 1, color, true);
                }
                x += dx; y += dy; segmentProgress++;
                if (segmentProgress == segmentLength)
                {
                    segmentProgress = 0; (dx, dy) = (-dy, dx); turns++;
                    if ((turns & 1) == 0) segmentLength++;
                }
                if ((n & 2047) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(12 + n * 80 / count);
                }
            }
            return;
        }

        if (state.Mode is 1 or 2)
        {
            int count = side * side;
            for (int n = 2; n <= count; n++)
            {
                if (!primes[n]) continue;
                double radial = 0.9 * Math.Sqrt((double)n / count);
                double angle = state.Mode == 1
                    ? Math.Tau * Math.Sqrt(n)
                    : Math.Tau * n / 6 + Math.Sin(n * Math.PI / 3) * 0.16;
                canvas.Circle(radial * Math.Cos(angle), radial * Math.Sin(angle), pointRadius,
                    1, Palette(state, (double)(n % colorModulus) / colorModulus), true);
                if ((n & 2047) == 0) token.ThrowIfCancellationRequested();
            }
            return;
        }

        for (int a = -radius; a <= radius; a++)
        {
            for (int b = -radius; b <= radius; b++)
            {
                if (a == 0 && b == 0) continue;
                long norm = state.Mode == 3
                    ? (long)a * a + (long)b * b
                    : (long)a * a - (long)a * b + (long)b * b;
                bool isPrime = state.Mode == 3
                    ? a == 0 || b == 0
                        ? IsPrime(Math.Abs(a == 0 ? b : a)) && Math.Abs(a == 0 ? b : a) % 4 == 3
                        : norm < primes.Length && primes[(int)norm]
                    : norm < primes.Length && primes[(int)norm];
                if (!isPrime) continue;
                double x = 0.88 * a / radius;
                double y = state.Mode == 4
                    ? 0.88 * (b - a * 0.5) / radius
                    : 0.88 * b / radius;
                canvas.Circle(x, y, pointRadius, 1,
                    Palette(state, (double)(norm % colorModulus) / colorModulus), true);
            }
            if ((a & 15) == 0) token.ThrowIfCancellationRequested();
        }
        if (state.ShowGuides)
        {
            canvas.Line(-0.92, 0, 0.92, 0, 1, state.PrimaryColor, 0.2);
            canvas.Line(0, -0.92, 0, 0.92, 1, state.PrimaryColor, 0.2);
        }
    }

    private static bool[] BuildPrimeSieve(int maximum, CancellationToken token)
    {
        var prime = Enumerable.Repeat(true, maximum + 1).ToArray();
        prime[0] = prime[1] = false;
        for (int value = 2; value * value <= maximum; value++)
        {
            if (!prime[value]) continue;
            for (int composite = value * value; composite <= maximum; composite += value)
                prime[composite] = false;
            if ((value & 31) == 0) token.ThrowIfCancellationRequested();
        }
        return prime;
    }

    private static bool IsPrime(int value)
    {
        value = Math.Abs(value);
        if (value < 2) return false;
        if (value % 2 == 0) return value == 2;
        for (int divisor = 3; divisor * divisor <= value; divisor += 2)
            if (value % divisor == 0) return false;
        return true;
    }
}
