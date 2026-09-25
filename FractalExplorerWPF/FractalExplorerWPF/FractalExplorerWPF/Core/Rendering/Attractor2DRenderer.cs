using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Renders two-dimensional strange attractors in two independent stages:
/// orbit-density accumulation and density colorization.  Keeping the histogram
/// intact until the last stage lets a future palette editor change coloring
/// without changing any attractor formulas.
/// </summary>
public static class Attractor2DRenderer
{
    private const double EscapeLimit = 1e12;

    public static byte[] RenderBuffer(
        DynamicSystemState state,
        int width,
        int height,
        DynamicPalette? palette,
        CancellationToken token,
        IProgress<int>? progress = null)
    {
        Attractor2DKind kind = ParseKind(state.Attractor2DMode);
        if (kind == Attractor2DKind.SprottQuadratic &&
            (state.QuadraticCoefficients is not { Length: SprottQuadraticMap.CoefficientCount } ||
             state.QuadraticCoefficients.Any(v => !double.IsFinite(v))))
            throw new InvalidOperationException("Квадратичная карта требует 12 конечных коэффициентов.");
        int[] density = AccumulateDensity(state, kind, width, height, token, progress);
        token.ThrowIfCancellationRequested();
        byte[] pixels = AttractorDensityColorizer.Colorize(
            density, state.BackgroundColor, state.FractalColor, palette, state.DensityGamma, token);
        progress?.Report(100);
        return pixels;
    }

    public static double GetBaseSpan(Attractor2DKind kind, DynamicSystemState? state = null) => kind switch
    {
        Attractor2DKind.Clifford => 5,
        Attractor2DKind.PeterDeJong => 4.5,
        Attractor2DKind.Tinkerbell => 3.2,
        Attractor2DKind.GumowskiMira => 32,
        Attractor2DKind.SprottQuadratic => Math.Max(.01, state?.QuadraticSpan ?? 5),
        _ => 5
    };

    public static Attractor2DKind ParseKind(string? value) =>
        Enum.TryParse(value, true, out Attractor2DKind kind) ? kind : Attractor2DKind.Clifford;

    private static int[] AccumulateDensity(
        DynamicSystemState state,
        Attractor2DKind kind,
        int width,
        int height,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int[] density = new int[checked(width * height)];
        int pointCount = Math.Max(1, state.Iterations);
        int workerCount = Math.Min(Math.Max(1, state.Threads), Math.Max(1, pointCount / 16_384));
        int completedWorkers = 0;
        object mergeLock = new();

        double spanX = GetBaseSpan(kind, state) / Math.Max(1e-9, state.Zoom);
        double spanY = spanX * Math.Max(1, height) / Math.Max(1, width);
        double minX = state.CenterX - spanX * .5;
        double maxX = state.CenterX + spanX * .5;
        double minY = state.CenterY - spanY * .5;
        double maxY = state.CenterY + spanY * .5;

        // Каждый воркер копит плотность в собственный буфер и сливает его в общий один
        // раз в конце. Раньше на каждую точку орбиты был Interlocked по общему массиву —
        // конкуренция кэш-линий не давала линейного ускорения. Сумма тех же целых
        // приращений не зависит от порядка, поле плотности идентично.
        Parallel.For(0, workerCount, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, state.Threads),
            CancellationToken = token
        },
        () => new int[density.Length],
        (workerIndex, _, localDensity) =>
        {
            int localPointCount = pointCount / workerCount + (workerIndex < pointCount % workerCount ? 1 : 0);
            double jitter = workerIndex == 0 ? 0 : (workerIndex + 1) * 1e-9;
            double x = state.X0 + jitter;
            double y = state.Y0 - jitter * .731;

            for (int i = 0; i < state.DiscardIterations; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                Iterate(kind, state, ref x, ref y);
                if (!IsFiniteOrbit(x, y)) ResetOrbit(state, workerIndex, ref x, ref y);
            }

            for (int i = 0; i < localPointCount; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                Iterate(kind, state, ref x, ref y);
                if (!IsFiniteOrbit(x, y))
                {
                    ResetOrbit(state, workerIndex + i, ref x, ref y);
                    continue;
                }

                if (x < minX || x > maxX || y < minY || y > maxY) continue;
                int px = (int)((x - minX) / spanX * (width - 1));
                int py = (int)((maxY - y) / spanY * (height - 1));
                if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                localDensity[py * width + px]++;
            }

            int done = Interlocked.Increment(ref completedWorkers);
            progress?.Report(Math.Min(94, done * 94 / workerCount));
            return localDensity;
        },
        localDensity =>
        {
            lock (mergeLock)
            {
                for (int i = 0; i < density.Length; i++) density[i] += localDensity[i];
            }
        });

        return density;
    }

    private static void Iterate(Attractor2DKind kind, DynamicSystemState state, ref double x, ref double y)
    {
        double nextX;
        double nextY;

        switch (kind)
        {
            case Attractor2DKind.Clifford:
                nextX = Math.Sin(state.A * y) + state.C * Math.Cos(state.A * x);
                nextY = Math.Sin(state.B * x) + state.D * Math.Cos(state.B * y);
                break;
            case Attractor2DKind.PeterDeJong:
                nextX = Math.Sin(state.A * y) - Math.Cos(state.B * x);
                nextY = Math.Sin(state.C * x) - Math.Cos(state.D * y);
                break;
            case Attractor2DKind.Tinkerbell:
                nextX = x * x - y * y + state.A * x + state.B * y;
                nextY = 2 * x * y + state.C * x + state.D * y;
                break;
            case Attractor2DKind.GumowskiMira:
                nextX = y + state.A * (1 - state.B * y * y) * y + GumowskiMiraFunction(x, state.C);
                nextY = -x + GumowskiMiraFunction(nextX, state.C);
                break;
            case Attractor2DKind.SprottQuadratic:
                SprottQuadraticMap.Iterate(state.QuadraticCoefficients, ref x, ref y);
                return;
            default:
                nextX = x;
                nextY = y;
                break;
        }

        x = nextX;
        y = nextY;
    }

    private static double GumowskiMiraFunction(double value, double mu)
    {
        double square = value * value;
        return mu * value + 2 * (1 - mu) * square / (1 + square);
    }

    private static bool IsFiniteOrbit(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= EscapeLimit && Math.Abs(y) <= EscapeLimit;

    private static void ResetOrbit(DynamicSystemState state, int seed, ref double x, ref double y)
    {
        double jitter = (seed + 1) * 1e-7;
        x = state.X0 + jitter;
        y = state.Y0 - jitter * .731;
    }
}

/// <summary>
/// Converts an attractor density field to BGRA pixels.  A multi-stop palette is
/// already supported here; the current UI intentionally exposes only the
/// background and density colors until the dedicated palette stage is added.
/// </summary>
public static class AttractorDensityColorizer
{
    public static byte[] Colorize(
        IReadOnlyList<int> density,
        Color background,
        Color densityColor,
        DynamicPalette? palette,
        double gamma,
        CancellationToken token)
    {
        byte[] pixels = new byte[checked(density.Count * 4)];
        IReadOnlyList<Color> colors = palette is { Colors.Count: >= 2 }
            ? palette.Colors
            : new[] { background, densityColor };

        int maxHit = 0;
        for (int i = 0; i < density.Count; i++)
        {
            if ((i & 65_535) == 0) token.ThrowIfCancellationRequested();
            if (density[i] > maxHit) maxHit = density[i];
        }

        double logarithmicMaximum = Math.Log(1 + Math.Max(1, maxHit));
        double safeGamma = Math.Clamp(gamma, .05, 8);
        for (int i = 0; i < density.Count; i++)
        {
            if ((i & 65_535) == 0) token.ThrowIfCancellationRequested();
            Color color = background;
            if (density[i] > 0)
            {
                double normalized = Math.Log(1 + density[i]) / logarithmicMaximum;
                color = Sample(colors, Math.Pow(normalized, safeGamma));
            }

            int offset = i * 4;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        return pixels;
    }

    private static Color Sample(IReadOnlyList<Color> colors, double position)
    {
        double scaled = Math.Clamp(position, 0, 1) * (colors.Count - 1);
        int leftIndex = Math.Min(colors.Count - 2, (int)scaled);
        double amount = scaled - leftIndex;
        Color left = colors[leftIndex];
        Color right = colors[leftIndex + 1];
        return Color.FromArgb(
            Lerp(left.A, right.A, amount),
            Lerp(left.R, right.R, amount),
            Lerp(left.G, right.G, amount),
            Lerp(left.B, right.B, amount));
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Clamp((int)Math.Round(from + (to - from) * amount), 0, 255);
}
