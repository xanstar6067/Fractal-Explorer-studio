using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class PhoenixRenderer
{
    private const decimal BaseScale = 4m;
    private const int MaximumSupportedPeriod = 64;

    /// <summary>
    /// Масштаб кадра для плоской (double) ступени. Деление осталось в <see cref="decimal"/>,
    /// хотя <see cref="PhoenixState.Zoom"/> давно не decimal: так округление совпадает с прежним
    /// бит-в-бит. Верхний кламп 1e15 защищает приведение к decimal — выше этого зума кадр
    /// считает пертурбационный движок, а сюда попадает только вырожденная опорная орбита
    /// (однородный кадр, где масштаб уже не важен). За пределами double зум обращается в ∞ и
    /// тоже упирается в кламп.
    /// </summary>
    private static double PlainScale(PhoenixState state) =>
        (double)(BaseScale / Math.Max(0.000000000001m, (decimal)Math.Clamp(state.Zoom.ToDouble(), 1e-12, 1e15)));

    private readonly record struct ComplexValue(double Real, double Imaginary)
    {
        public double MagnitudeSquared => Real * Real + Imaginary * Imaginary;
        public double Magnitude => Math.Sqrt(MagnitudeSquared);

        public static ComplexValue operator +(ComplexValue left, ComplexValue right) =>
            new(left.Real + right.Real, left.Imaginary + right.Imaginary);

        public static ComplexValue operator *(ComplexValue left, ComplexValue right) =>
            new(left.Real * right.Real - left.Imaginary * right.Imaginary,
                left.Real * right.Imaginary + left.Imaginary * right.Real);
    }

    private readonly record struct PixelMetrics(
        int Iterations,
        double Smooth,
        double OrbitTrap,
        double Stripe,
        double Triangle,
        double FinalArgument,
        int DetectedPeriod,
        bool IsInterior);

    public static void Render(PhoenixState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress = null)
    {
        if (ShouldUseDeepZoom(state))
        {
            RenderDeepZoom(state, pixels, width, height, stride, threadCount, token, progress);
            return;
        }

        RenderPlain(state, pixels, width, height, stride, threadCount, token, progress);
    }

    /// <summary>
    /// Плоская (double) ступень целиком. Выделена из <see cref="Render"/>, чтобы глубокий путь
    /// мог откатиться на неё при вырожденной опорной орбите, не проходя проверку зума заново.
    /// </summary>
    private static void RenderPlain(PhoenixState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress)
    {
        double centerX = (double)state.CenterX;
        double centerY = (double)state.CenterY;
        double scale = PlainScale(state);
        long completed = 0;
        Parallel.For(0, height,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threadCount), CancellationToken = token },
            y =>
            {
                int row = y * stride;
                double imaginary = centerY + (height / 2.0 - y) * scale / width;
                for (int x = 0; x < width; x++)
                {
                    if ((x & 63) == 0) token.ThrowIfCancellationRequested();
                    double real = centerX + (x - width / 2.0) * scale / width;
                    PixelMetrics metrics = IterateMain(state, real, imaginary);
                    WritePixel(pixels, row + x * 4, ResolveColor(state, metrics));
                }

                int rows = (int)Interlocked.Increment(ref completed);
                if (rows == height || rows % Math.Max(1, height / 100) == 0)
                    progress?.Invoke(rows * 100 / height);
            });
    }

    public static byte[]? RenderTile(PhoenixState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token)
    {
        if (ShouldUseDeepZoom(state))
            return RenderDeepZoomTile(state, canvasWidth, canvasHeight, tile, token);

        return RenderPlainTile(state, canvasWidth, canvasHeight, tile, token);
    }

    /// <inheritdoc cref="RenderPlain"/>
    private static byte[]? RenderPlainTile(PhoenixState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token)
    {
        byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];
        double centerX = (double)state.CenterX;
        double centerY = (double)state.CenterY;
        double scale = PlainScale(state);
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int canvasY = tile.Y + localY;
            double imaginary = centerY + (canvasHeight / 2.0 - canvasY) * scale / canvasWidth;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                int canvasX = tile.X + localX;
                double real = centerX + (canvasX - canvasWidth / 2.0) * scale / canvasWidth;
                PixelMetrics metrics = IterateMain(state, real, imaginary);
                WritePixel(pixels, (localY * tile.Width + localX) * 4, ResolveColor(state, metrics));
            }
        }
        return pixels;
    }

    public static void RenderParameterPlane(PhoenixState state, byte[] pixels, int width, int height,
        int stride, PhoenixSliceRange range, PhoenixParameterPlane plane, int threadCount,
        CancellationToken token, Action<int>? progress = null)
    {
        (ComplexValue initial, ComplexValue initialPrevious, _) = ResolveParameterInitialValues(state);
        long completed = 0;
        Parallel.For(0, height,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threadCount) },
            (y, loopState) =>
            {
                if (token.IsCancellationRequested) { loopState.Stop(); return; }
                double imaginary = range.MaxY - (y + 0.5) * (range.MaxY - range.MinY) / height;
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                    double real = range.MinX + (x + 0.5) * (range.MaxX - range.MinX) / width;
                    ComplexValue c1 = plane == PhoenixParameterPlane.C1
                        ? new ComplexValue(real, imaginary)
                        : new ComplexValue((double)state.C1Real, (double)state.C1Imaginary);
                    ComplexValue c2 = plane == PhoenixParameterPlane.C2
                        ? new ComplexValue(real, imaginary)
                        : new ComplexValue((double)state.C2Real, (double)state.C2Imaginary);
                    PixelMetrics metrics = Iterate(state, initial, initialPrevious, c1, c2);
                    WritePixel(pixels, row + x * 4, ResolveColor(state, metrics));
                }

                int rows = (int)Interlocked.Increment(ref completed);
                if (rows == height || rows % Math.Max(1, height / 100) == 0)
                    progress?.Invoke(rows * 100 / height);
            });
    }

    private static PixelMetrics IterateMain(PhoenixState state, double pixelReal, double pixelImaginary)
    {
        ComplexValue pixel = new(pixelReal, pixelImaginary);
        (ComplexValue parameterInitial, ComplexValue parameterPrevious, _) = ResolveParameterInitialValues(state);
        ComplexValue current = state.PlaneMode == PhoenixPlaneMode.Julia ? pixel : parameterInitial;
        ComplexValue previous = state.PlaneMode == PhoenixPlaneMode.Julia
            ? new ComplexValue((double)state.InitialPreviousReal, (double)state.InitialPreviousImaginary)
            : parameterPrevious;
        ComplexValue c1 = state.PlaneMode == PhoenixPlaneMode.ParameterC1
            ? pixel
            : new ComplexValue((double)state.C1Real, (double)state.C1Imaginary);
        ComplexValue c2 = new((double)state.C2Real, (double)state.C2Imaginary);
        return Iterate(state, current, previous, c1, c2);
    }

    private static (ComplexValue Current, ComplexValue Previous, bool UsedAutomaticStart)
        ResolveParameterInitialValues(PhoenixState state)
    {
        ComplexValue current = new((double)state.InitialZReal, (double)state.InitialZImaginary);
        ComplexValue previous = new((double)state.InitialPreviousReal, (double)state.InitialPreviousImaginary);
        bool useAutomaticStart = state.SecondaryPower > 0 &&
                                 current.MagnitudeSquared == 0 && previous.MagnitudeSquared == 0;
        return useAutomaticStart ? (new ComplexValue(1, 0), previous, true) : (current, previous, false);
    }

    private static PixelMetrics Iterate(PhoenixState state, ComplexValue current, ComplexValue previous,
        ComplexValue c1, ComplexValue c2)
    {
        int maximum = state.Iterations;
        double thresholdSquared = (double)(state.Threshold * state.Threshold);
        bool detectPeriods = state.ColoringMode == PhoenixColoringMode.Period;
        int maximumPeriod = Math.Clamp(state.MaximumDetectedPeriod, 1, MaximumSupportedPeriod);
        int historyCapacity = maximumPeriod + 1;
        Span<ComplexValue> currentHistory = detectPeriods
            ? stackalloc ComplexValue[MaximumSupportedPeriod + 1]
            : Span<ComplexValue>.Empty;
        Span<ComplexValue> previousHistory = detectPeriods
            ? stackalloc ComplexValue[MaximumSupportedPeriod + 1]
            : Span<ComplexValue>.Empty;

        double minimumTrap = double.MaxValue;
        double stripeSum = 0;
        double triangleSum = 0;
        int iteration = 0;
        int detectedPeriod = 0;
        // Ловушку, полосовую и треугольную суммы считаем только в их режимах окраски.
        // В прочих режимах эти поля PixelMetrics не читаются, а Atan2/Sin/Sqrt на
        // каждой итерации дороги. Результат для прочих режимов бит-в-бит идентичен.
        bool trackTrap = state.ColoringMode == PhoenixColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == PhoenixColoringMode.StripeAverage;
        bool trackTriangle = state.ColoringMode == PhoenixColoringMode.TriangleInequalityAverage;

        while (iteration < maximum && current.MagnitudeSquared <= thresholdSquared)
        {
            if (trackTrap)
                minimumTrap = Math.Min(minimumTrap, OrbitTrapDistance(state, current));
            if (trackStripe)
                stripeSum += 0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2(current.Imaginary, current.Real));

            if (detectPeriods)
            {
                int historyIndex = iteration % historyCapacity;
                currentHistory[historyIndex] = current;
                previousHistory[historyIndex] = previous;
            }

            ComplexValue primary = VariantPower(current, state.PrimaryPower, state.Variant);
            ComplexValue secondary = VariantPower(current, state.SecondaryPower, state.Variant);
            ComplexValue next = primary + c1 * secondary + c2 * previous;

            if (trackTriangle)
            {
                double edgeLength = Distance(next, current);
                if (double.IsFinite(edgeLength) && edgeLength > 1e-300)
                {
                    double triangleRatio = (next.Magnitude - current.Magnitude) / edgeLength;
                    triangleSum += 0.5 + 0.5 * Math.Clamp(triangleRatio, -1, 1);
                }
            }

            previous = current;
            current = next;
            iteration++;

            if (detectPeriods && iteration >= 4)
            {
                int available = Math.Min(maximumPeriod, iteration - 1);
                double toleranceSquared = Math.Pow(Math.Max(1e-14, state.CycleTolerance), 2);
                for (int period = 1; period <= available; period++)
                {
                    if (iteration < period * 2) continue;
                    int pastIndex = (iteration - period) % historyCapacity;
                    if (!Close(current, currentHistory[pastIndex], toleranceSquared) ||
                        !Close(previous, previousHistory[pastIndex], toleranceSquared)) continue;
                    detectedPeriod = period;
                    break;
                }
                if (detectedPeriod > 0) break;
            }
        }

        bool isInterior = detectedPeriod > 0 || iteration >= maximum;
        double smooth = Smooth(iteration, maximum, current.MagnitudeSquared,
            Math.Max(state.PrimaryPower, state.SecondaryPower));
        double argument = double.IsFinite(current.Real) && double.IsFinite(current.Imaginary)
            ? PositiveModulo(Math.Atan2(current.Imaginary, current.Real) / (2 * Math.PI) + 0.5, 1)
            : 0;
        return new PixelMetrics(
            iteration,
            smooth,
            minimumTrap == double.MaxValue ? 0 : minimumTrap,
            iteration == 0 ? 0 : stripeSum / iteration,
            iteration == 0 ? 0 : triangleSum / iteration,
            argument,
            detectedPeriod,
            isInterior);
    }

    private static ComplexValue VariantPower(ComplexValue value, int power, PhoenixVariant variant)
    {
        ComplexValue transformed = variant switch
        {
            PhoenixVariant.Tricorn => new ComplexValue(value.Real, -value.Imaginary),
            PhoenixVariant.BurningShip => new ComplexValue(Math.Abs(value.Real), -Math.Abs(value.Imaginary)),
            PhoenixVariant.Buffalo => new ComplexValue(Math.Abs(value.Real), Math.Abs(value.Imaginary)),
            _ => value
        };
        ComplexValue result = IntegerPower(transformed, power);
        return variant == PhoenixVariant.Celtic ? new ComplexValue(Math.Abs(result.Real), result.Imaginary) : result;
    }

    private static ComplexValue IntegerPower(ComplexValue value, int power)
    {
        if (power == 0) return new ComplexValue(1, 0);
        ComplexValue result = new(1, 0);
        ComplexValue factor = value;
        int exponent = power;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result *= factor;
            exponent >>= 1;
            if (exponent > 0) factor *= factor;
        }
        return result;
    }

    private static bool Close(ComplexValue left, ComplexValue right, double toleranceSquared)
    {
        double dr = left.Real - right.Real;
        double di = left.Imaginary - right.Imaginary;
        double scale = 1 + left.MagnitudeSquared + right.MagnitudeSquared;
        return dr * dr + di * di <= toleranceSquared * scale;
    }

    private static double OrbitTrapDistance(PhoenixState state, ComplexValue value) => state.OrbitTrapMode switch
    {
        PhoenixOrbitTrapMode.Circle => Math.Abs(value.Magnitude - Math.Max(0, state.OrbitTrapRadius)),
        PhoenixOrbitTrapMode.Point => value.Magnitude,
        _ => Math.Min(Math.Abs(value.Real), Math.Abs(value.Imaginary))
    };

    private static double Distance(ComplexValue left, ComplexValue right)
    {
        double dr = left.Real - right.Real;
        double di = left.Imaginary - right.Imaginary;
        return Math.Sqrt(dr * dr + di * di);
    }

    private static double Smooth(int iteration, int maximum, double magnitudeSquared, int dominantPower)
    {
        if (iteration >= maximum || !double.IsFinite(magnitudeSquared) || magnitudeSquared <= 1)
            return iteration;
        double magnitude = Math.Sqrt(magnitudeSquared);
        double degree = Math.Max(2, dominantPower);
        double nu = Math.Log(Math.Max(Math.Log(magnitude), 1e-300)) / Math.Log(degree);
        double result = iteration + 1 - nu;
        return double.IsFinite(result) ? result : iteration;
    }

    private static Color ResolveColor(PhoenixState state, PixelMetrics metrics)
    {
        MandelbrotPalette palette = state.Palette;
        if (metrics.IsInterior)
        {
            if (state.ColoringMode == PhoenixColoringMode.Period && metrics.DetectedPeriod > 0)
                return SamplePalette(palette, PositiveModulo(metrics.DetectedPeriod * 0.6180339887498949, 1));
            if (state.ColoringMode == PhoenixColoringMode.FinalArgument)
                return SamplePalette(palette, metrics.FinalArgument);
            return palette.InteriorColor;
        }

        double period = palette.AlignWithRenderIterations
            ? Math.Max(1, state.Iterations)
            : Math.Max(1, palette.ColorPeriod);
        if (state.ColoringMode == PhoenixColoringMode.Discrete)
            return SampleIterationColor(palette, metrics.Iterations, state.Iterations, period, false);
        if (state.ColoringMode == PhoenixColoringMode.Smooth)
            return SampleIterationColor(palette, metrics.Smooth, state.Iterations, period, true);

        double normalized = state.ColoringMode switch
        {
            PhoenixColoringMode.OrbitTrap => Math.Clamp(
                1 / (1 + metrics.OrbitTrap * Math.Max(0.01, state.OrbitTrapStrength)), 0, 1),
            PhoenixColoringMode.StripeAverage => Math.Clamp(
                metrics.Smooth / Math.Max(1, state.Iterations) * (1 - Math.Clamp(state.StripeStrength, 0, 1)) +
                metrics.Stripe * Math.Clamp(state.StripeStrength, 0, 1), 0, 1),
            PhoenixColoringMode.TriangleInequalityAverage => Math.Clamp(metrics.Triangle, 0, 1),
            PhoenixColoringMode.FinalArgument => metrics.FinalArgument,
            PhoenixColoringMode.Period => PositiveModulo(metrics.Smooth, period) / period,
            _ => 0
        };
        return SamplePalette(palette, normalized);
    }

    private static Color SampleIterationColor(MandelbrotPalette palette, double value, int iterations,
        double period, bool smooth)
    {
        value = Math.Max(0, value);
        if (palette.UsesAlgorithmicGrayscale)
        {
            double normalized = Math.Log(Math.Min(value, iterations) + 1) / Math.Log(iterations + 1);
            byte gray = (byte)Math.Clamp((int)(255 * (1 - normalized)), 0, 255);
            return ApplyGamma(Color.FromRgb(gray, gray, gray), palette.Gamma);
        }
        double normalizedValue = smooth
            ? PositiveModulo(value, period) / period
            : Math.Min(value, period) / period;
        return SamplePalette(palette, normalizedValue);
    }

    private static Color SamplePalette(MandelbrotPalette palette, double normalized)
    {
        if (palette.Colors.Count == 0) return Colors.White;
        if (palette.Colors.Count == 1) return ApplyGamma(palette.Colors[0], palette.Gamma);
        normalized = Math.Clamp(normalized, 0, 1);
        Color result;
        if (!palette.IsGradient)
        {
            result = palette.Colors[Math.Min((int)(normalized * palette.Colors.Count), palette.Colors.Count - 1)];
        }
        else
        {
            double position = normalized * (palette.Colors.Count - 1);
            int left = Math.Min((int)position, palette.Colors.Count - 1);
            if (left == palette.Colors.Count - 1)
            {
                result = palette.Colors[left];
            }
            else
            {
                Color a = palette.Colors[left];
                Color b = palette.Colors[left + 1];
                double amount = position - left;
                result = Color.FromArgb(Lerp(a.A, b.A, amount), Lerp(a.R, b.R, amount),
                    Lerp(a.G, b.G, amount), Lerp(a.B, b.B, amount));
            }
        }
        return ApplyGamma(result, palette.Gamma);
    }

    private static void WritePixel(byte[] pixels, int offset, Color color)
    {
        pixels[offset] = color.B;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.R;
        pixels[offset + 3] = color.A;
    }

    // Таблица гаммы на 256 значений, кэшируется на поток (гамма постоянна в пределах
    // рендера). Бит-в-бит совпадает с прямым Math.Pow, но вызовов Math.Pow — 256 на
    // поток вместо трёх на пиксель.
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

    private static double PositiveModulo(double value, double modulus)
    {
        double result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static byte Lerp(byte start, byte end, double amount) =>
        (byte)Math.Round(start + (end - start) * amount);
}
