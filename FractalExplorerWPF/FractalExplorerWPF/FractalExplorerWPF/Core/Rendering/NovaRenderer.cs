using System.Numerics;
using System.Windows.Media;
using FractalExplorer.Utilities;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class NovaRenderer
{
    private const decimal BaseScale = 4m;
    private const decimal DecimalScaleThreshold = 4m / 2_000_000_000m;

    /// <summary>
    /// Масштаб кадра для плоских (double и decimal) ступеней. Деление осталось в
    /// <see cref="decimal"/>, хотя <see cref="NovaState.Zoom"/> стал double: так округление
    /// совпадает с прежним бит-в-бит. Верхний кламп защищает приведение к decimal — выше
    /// <see cref="DeepZoomThreshold"/> кадр считает пертурбационный движок, а сюда попадают
    /// только вырожденная опорная орбита и нулевая степень, то есть однородные кадры, где
    /// масштаб уже ни на что не влияет.
    /// </summary>
    private static decimal PlainScale(NovaState state) =>
        BaseScale / Math.Max(0.000000000000001m, (decimal)Math.Clamp(state.Zoom, 1e-15, 1e25));

    public static byte[]? RenderTile(NovaState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token, bool selectorPalette = false)
    {
        if (ShouldUseDeepZoom(state))
            return RenderDeepZoomTile(state, canvasWidth, canvasHeight, tile, token, selectorPalette);

        return RenderPlainTile(state, canvasWidth, canvasHeight, tile, token, selectorPalette);
    }

    /// <summary>
    /// Плоские ступени целиком. Выделены из <see cref="RenderTile"/>, чтобы глубокий путь мог
    /// откатиться на них при вырожденной опорной орбите, не проходя проверку зума заново.
    /// </summary>
    private static byte[]? RenderPlainTile(NovaState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token, bool selectorPalette)
    {
        byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];
        decimal scale = PlainScale(state);
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int canvasY = tile.Y + localY;
            decimal imaginary = state.CenterY - (canvasY - canvasHeight / 2m) * scale / canvasWidth;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                int canvasX = tile.X + localX;
                decimal real = state.CenterX + (canvasX - canvasWidth / 2m) * scale / canvasWidth;
                Color color = CalculateColor(state, real, imaginary, scale, selectorPalette);
                int offset = (localY * tile.Width + localX) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = color.A;
            }
        }
        return pixels;
    }

    public static void Render(NovaState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress = null)
    {
        if (ShouldUseDeepZoom(state))
        {
            RenderDeepZoom(state, pixels, width, height, stride, threadCount, token, progress);
            return;
        }

        RenderPlain(state, pixels, width, height, stride, threadCount, token, progress);
    }

    /// <inheritdoc cref="RenderPlainTile"/>
    private static void RenderPlain(NovaState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress)
    {
        decimal scale = PlainScale(state);
        long completed = 0;
        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, threadCount)
        }, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            decimal imaginary = state.CenterY - (y - height / 2m) * scale / width;
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                decimal real = state.CenterX + (x - width / 2m) * scale / width;
                Color color = CalculateColor(state, real, imaginary, scale, false);
                int offset = row + x * 4;
                pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = color.A;
            }
            int rows = (int)Interlocked.Increment(ref completed);
            if (rows == height || rows % Math.Max(1, height / 100) == 0) progress?.Invoke(rows * 100 / height);
        });
    }

    private static Color CalculateColor(NovaState state, decimal real, decimal imaginary, decimal scale, bool selectorPalette)
    {
        int iteration;
        double smooth;
        if (scale < DecimalScaleThreshold)
        {
            ComplexDecimal z = state.Variant == NovaVariant.Julia
                ? new ComplexDecimal(real, imaginary)
                : new ComplexDecimal(state.Z0Real, state.Z0Imaginary);
            ComplexDecimal c = state.Variant == NovaVariant.Julia
                ? new ComplexDecimal(state.CReal, state.CImaginary)
                : new ComplexDecimal(real, imaginary);
            iteration = IterateDecimal(ref z, c, state);
            smooth = Smooth(iteration, state, (double)z.MagnitudeSquared);
        }
        else
        {
            Complex z = state.Variant == NovaVariant.Julia
                ? new Complex((double)real, (double)imaginary)
                : new Complex((double)state.Z0Real, (double)state.Z0Imaginary);
            Complex c = state.Variant == NovaVariant.Julia
                ? new Complex((double)state.CReal, (double)state.CImaginary)
                : new Complex((double)real, (double)imaginary);
            iteration = IterateDouble(ref z, c, state);
            smooth = Smooth(iteration, state, z.Real * z.Real + z.Imaginary * z.Imaginary);
        }
        return selectorPalette ? FireColor(iteration, state.Iterations) : ResolveColor(state, iteration, smooth);
    }

    private static int IterateDouble(ref Complex z, Complex c, NovaState state)
    {
        int iteration = 0;
        double thresholdSquared = (double)(state.Threshold * state.Threshold);
        var p = new Complex((double)state.PReal, (double)state.PImaginary);
        Complex pMinusOne = p - Complex.One;
        double m = (double)state.M;
        while (iteration < state.Iterations && z.Real * z.Real + z.Imaginary * z.Imaginary <= thresholdSquared)
        {
            if (z.Real * z.Real + z.Imaginary * z.Imaginary < 1e-12) break;
            Complex zPowP = Complex.Pow(z, p);
            Complex denominator = p * Complex.Pow(z, pMinusOne);
            if (!IsFinite(denominator) || denominator.Magnitude < 1e-12) break;
            z = z - m * (zPowP - Complex.One) / denominator + c;
            if (!IsFinite(z)) break;
            iteration++;
        }
        return iteration;
    }

    private static int IterateDecimal(ref ComplexDecimal z, ComplexDecimal c, NovaState state)
    {
        int iteration = 0;
        ComplexDecimal p = new(state.PReal, state.PImaginary);
        ComplexDecimal pMinusOne = new(p.Real - 1, p.Imaginary);
        decimal thresholdSquared = state.Threshold * state.Threshold;
        while (iteration < state.Iterations && z.MagnitudeSquared <= thresholdSquared)
        {
            if (z.MagnitudeSquared < 1e-28m) break;
            try
            {
                ComplexDecimal zPowP = ComplexDecimal.Pow(z, p);
                ComplexDecimal denominator = p * ComplexDecimal.Pow(z, pMinusOne);
                if (denominator.MagnitudeSquared < 1e-28m) break;
                z = z - state.M * (zPowP - 1) / denominator + c;
                iteration++;
            }
            catch (OverflowException)
            {
                iteration = state.Iterations;
                break;
            }
        }
        return iteration;
    }

    private static double Smooth(int iteration, NovaState state, double magnitudeSquared)
    {
        if (iteration >= state.Iterations || magnitudeSquared <= 0 || !double.IsFinite(magnitudeSquared)) return iteration;
        double pMagnitude = Complex.Abs(new Complex((double)state.PReal, (double)state.PImaginary));
        double logMagnitude = Math.Log(magnitudeSquared);
        double logP = Math.Log(pMagnitude);
        double inner = logMagnitude / (2 * Math.Log(2));
        if (logMagnitude <= 0 || logP == 0 || inner <= 0 || !double.IsFinite(logP) || !double.IsFinite(inner)) return iteration;
        double nu = Math.Log(inner) / logP;
        return double.IsFinite(nu) ? iteration + 1 - nu : iteration;
    }

    private static Color ResolveColor(NovaState state, int iteration, double smooth)
    {
        MandelbrotPalette palette = state.Palette;
        double value = state.UseSmoothColoring ? Math.Max(0, smooth) : iteration;
        if (palette.UsesAlgorithmicGrayscale)
        {
            if (iteration >= state.Iterations) return Colors.White;
            double logarithmic = Math.Log(value + 1) / Math.Log(state.Iterations + 1);
            byte gray = (byte)Math.Clamp((int)Math.Round(255 * logarithmic), 0, 255);
            return ApplyGamma(Color.FromRgb(gray, gray, gray), palette.Gamma);
        }
        if (iteration >= state.Iterations) return palette.InteriorColor;
        double period = palette.AlignWithRenderIterations ? state.Iterations : Math.Max(1, palette.ColorPeriod);
        return SamplePalette(palette, (value % period + period) % period / period);
    }

    private static Color FireColor(int iteration, int maximum)
    {
        if (iteration >= maximum) return Colors.Black;
        double t = iteration % 20 / 20d;
        return Color.FromRgb((byte)Math.Min(255, t * 3 * 255),
            (byte)Math.Min(255, Math.Max(0, (t - 0.33) * 3 * 255)),
            (byte)Math.Min(255, Math.Max(0, (t - 0.66) * 3 * 255)));
    }

    private static Color SamplePalette(MandelbrotPalette palette, double normalized)
    {
        if (palette.Colors.Count == 0) return Colors.White;
        if (palette.Colors.Count == 1) return ApplyGamma(palette.Colors[0], palette.Gamma);
        normalized = Math.Clamp(normalized, 0, 1);
        Color result;
        if (!palette.IsGradient) result = palette.Colors[Math.Min((int)(normalized * palette.Colors.Count), palette.Colors.Count - 1)];
        else
        {
            double position = normalized * (palette.Colors.Count - 1);
            int left = Math.Min((int)position, palette.Colors.Count - 1);
            if (left == palette.Colors.Count - 1) result = palette.Colors[left];
            else
            {
                Color a = palette.Colors[left], b = palette.Colors[left + 1];
                double amount = position - left;
                result = Color.FromArgb(Lerp(a.A, b.A, amount), Lerp(a.R, b.R, amount), Lerp(a.G, b.G, amount), Lerp(a.B, b.B, amount));
            }
        }
        return ApplyGamma(result, palette.Gamma);
    }

    // Таблица гаммы на 256 значений, кэш на поток (гамма постоянна в пределах рендера).
    // Бит-в-бит совпадает с прямым Math.Pow.
    [ThreadStatic] private static double _gammaLutKey;
    [ThreadStatic] private static byte[]? _gammaLut;

    private static Color ApplyGamma(Color color, double gamma)
    {
        byte[]? lut = _gammaLut;
        if (lut is null || _gammaLutKey != gamma)
        {
            lut = new byte[256];
            double correction = 1 / Math.Max(0.01, gamma);
            for (int value = 0; value < 256; value++)
                lut[value] = (byte)(255 * Math.Pow(value / 255d, correction));
            _gammaLut = lut;
            _gammaLutKey = gamma;
        }
        return Color.FromArgb(color.A, lut[color.R], lut[color.G], lut[color.B]);
    }

    private static bool IsFinite(Complex value) => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
    private static byte Lerp(byte start, byte end, double amount) => (byte)Math.Round(start + (end - start) * amount);
}
