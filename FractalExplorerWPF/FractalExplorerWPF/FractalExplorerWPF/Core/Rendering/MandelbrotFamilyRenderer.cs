using System.Numerics;
using System.Windows.Media;
using FractalExplorer.Utilities;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class MandelbrotFamilyRenderer
{
    // Порог зума, за которым обычный double перестаёт быть надёжным (сетка координат и
    // накопление ошибки в z→z²+c). Ниже — плоский double-рендер (см. <see cref="Iterate"/>).
    // Выше ступень decimal (<see cref="IterateDecimal"/>) по-прежнему обслуживает: варианты
    // без глубокого движка (Simonobrot/Generalized вне целой поддерживаемой степени) и
    // вырожденную опорную орбиту в Histogram/DistanceEstimation. Во всех остальных случаях
    // (включая Histogram — см. RenderDeepZoomHistogram — и DistanceEstimation — см.
    // RenderDeepZoomDistanceEstimation) ступень decimal пропускается.
    private const double DecimalIterationZoomThreshold = 1_500_000_000d;

    // Mandelbrot/Julia: «второй двигатель» (пертурбация + опорная орбита в BigFloat)
    // включается там же, где кончается надёжный double. Фаза 2 опустила порог с 1e25:
    // ступень decimal для этих двух вариантов больше не используется, лестница точности
    // схлопнута до double → пертурбация.
    private const double PerturbationZoomThreshold = DecimalIterationZoomThreshold;

    // Ширина области в decimal для «плоских» ступеней: double-/decimal-итерация вне
    // глубокого зума и запасные пути Histogram/DistanceEstimation при вырожденной опорной
    // орбите. Зум зажимается по верхней границе decimal; выше него всё, что умеет,
    // обслуживает пертурбационный движок, а этот путь и раньше упирался в старый MaxZoom,
    // так что ничего не теряется.
    private const decimal DecimalViewWidthMinimum = 0.000000000000001m;

    // Верхняя граница зума для decimal-сетки. Зум расширенного диапазона (FloatExp) сюда
    // приходит только на запасных путях, где сама ступень всё равно не тянет такую глубину,
    // поэтому он просто зажимается потолком decimal.
    private const double DecimalViewWidthZoomCeiling = 7.9e28;

    private static decimal DecimalViewWidth(FloatExp zoom)
    {
        double asDouble = zoom.ToDouble();
        double clamped = double.IsNaN(asDouble)
            ? DecimalViewWidthZoomCeiling
            : Math.Min(asDouble, DecimalViewWidthZoomCeiling);
        return 3m / Math.Max((decimal)clamped, DecimalViewWidthMinimum);
    }

    private readonly record struct PixelMetrics(
        int Iterations,
        double Smooth,
        double OrbitTrap,
        double Stripe,
        double Distance = 0);

    public static byte[]? RenderTile(
        MandelbrotState state,
        int canvasWidth,
        int canvasHeight,
        MandelbrotRenderTile tile,
        CancellationToken token)
    {
        if (ShouldUseDeepZoom(state))
            return RenderDeepZoomTile(state, canvasWidth, canvasHeight, tile, token);

        if (state.ColoringMode == MandelbrotColoringMode.DistanceEstimation)
            return RenderDistanceEstimationTile(state, canvasWidth, canvasHeight, tile, token);

        int stride = checked(tile.Width * 4);
        var buffer = new byte[checked(stride * tile.Height)];
        decimal viewWidth = DecimalViewWidth(state.Zoom);
        decimal viewHeight = viewWidth * canvasHeight / canvasWidth;

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int y = tile.Y + localY;
            decimal im = state.CenterY + (0.5m - (decimal)y / canvasHeight) * viewHeight;
            int row = localY * stride;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                int x = tile.X + localX;
                decimal re = state.CenterX + ((decimal)x / canvasWidth - 0.5m) * viewWidth;
                PixelMetrics metrics = IterateAt(state, re, im, token);
                double histogramValue = state.ColoringMode == MandelbrotColoringMode.Histogram
                    ? Math.Clamp((state.HistogramInputUseSmooth ? metrics.Smooth : metrics.Iterations) /
                                 Math.Max(1, state.Iterations), 0, 1)
                    : 0;
                Color color = ResolveColor(state, metrics, histogramValue);
                int offset = row + localX * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
        }
        return buffer;
    }

    public static void Render(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        CancellationToken token,
        Action<int>? reportProgress = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (buffer.Length < stride * height) throw new ArgumentException("Буфер изображения слишком мал.", nameof(buffer));

        if (ShouldUseDeepZoom(state))
        {
            RenderDeepZoom(state, buffer, width, height, stride, token, reportProgress);
            return;
        }

        int threads = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(threads, 1, Environment.ProcessorCount)
        };
        decimal viewWidth = DecimalViewWidth(state.Zoom);
        decimal viewHeight = viewWidth * height / width;
        int completedRows = 0;

        if (state.ColoringMode == MandelbrotColoringMode.DistanceEstimation)
        {
            RenderDistanceEstimation(state, buffer, width, height, stride, viewWidth, viewHeight,
                options, token, reportProgress);
            return;
        }

        if (state.ColoringMode == MandelbrotColoringMode.Histogram)
        {
            RenderHistogram(state, buffer, width, height, stride, viewWidth, viewHeight,
                options, token, ref completedRows, reportProgress);
            return;
        }

        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int row = y * stride;
            decimal im = state.CenterY + (0.5m - (decimal)y / height) * viewHeight;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                decimal re = state.CenterX + ((decimal)x / width - 0.5m) * viewWidth;
                PixelMetrics metrics = IterateAt(state, re, im, token);
                Color color = ResolveColor(state, metrics, 0);
                int offset = row + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
            int done = Interlocked.Increment(ref completedRows);
            if (done == height || done % Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(done * 100 / height);
        });
    }

    private static byte[]? RenderDistanceEstimationTile(
        MandelbrotState state,
        int canvasWidth,
        int canvasHeight,
        MandelbrotRenderTile tile,
        CancellationToken token)
    {
        int stride = checked(tile.Width * 4);
        var buffer = new byte[checked(stride * tile.Height)];
        int sampleWidth = checked(tile.Width + 2);
        int sampleHeight = checked(tile.Height + 2);
        var distances = new float[checked(sampleWidth * sampleHeight)];
        decimal viewWidth = DecimalViewWidth(state.Zoom);
        decimal viewHeight = viewWidth * canvasHeight / canvasWidth;
        double pixelSize = (double)(viewWidth / canvasWidth);

        for (int sampleY = 0; sampleY < sampleHeight; sampleY++)
        {
            if (token.IsCancellationRequested) return null;
            int y = tile.Y + sampleY - 1;
            decimal im = state.CenterY + (0.5m - (decimal)y / canvasHeight) * viewHeight;
            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                int x = tile.X + sampleX - 1;
                decimal re = state.CenterX + ((decimal)x / canvasWidth - 0.5m) * viewWidth;
                PixelMetrics metrics = IterateAt(state, re, im, token);
                distances[sampleY * sampleWidth + sampleX] = StoreDistance(metrics.Distance);

                if (sampleX is > 0 && sampleX <= tile.Width &&
                    sampleY is > 0 && sampleY <= tile.Height)
                {
                    Color baseColor = ResolveDistanceBaseColor(state, metrics);
                    WriteColor(buffer, (sampleY - 1) * stride + (sampleX - 1) * 4, baseColor);
                }
            }
        }

        ShadeDistanceFieldTile(state, buffer, tile.Width, tile.Height, stride, distances, pixelSize);
        return buffer;
    }

    // Второй проход Distance Estimation для тайла: по полю расстояний (tileWidth+2)×(tileHeight+2)
    // затеняет уже записанные в buffer базовые цвета. Общий для decimal-ступени и глубокого
    // движка — тот отличается только тем, что подаёт нормированные расстояния и pixelSize = 1.
    private static void ShadeDistanceFieldTile(
        MandelbrotState state,
        byte[] buffer,
        int tileWidth,
        int tileHeight,
        int stride,
        float[] distances,
        double pixelSize)
    {
        int sampleWidth = tileWidth + 2;
        for (int localY = 0; localY < tileHeight; localY++)
        {
            int sampleRow = (localY + 1) * sampleWidth;
            int outputRow = localY * stride;
            for (int localX = 0; localX < tileWidth; localX++)
            {
                int sampleIndex = sampleRow + localX + 1;
                int outputOffset = outputRow + localX * 4;
                Color shaded = ApplyDistanceLighting(
                    state,
                    ReadColor(buffer, outputOffset),
                    distances[sampleIndex],
                    distances[sampleIndex - 1],
                    distances[sampleIndex + 1],
                    distances[sampleIndex - sampleWidth],
                    distances[sampleIndex + sampleWidth],
                    pixelSize);
                WriteColor(buffer, outputOffset, shaded);
            }
        }
    }

    // То же самое для полного кадра, параллельно и с прогрессом (70..100 %).
    private static void ShadeDistanceField(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        float[] distances,
        double pixelSize,
        ParallelOptions options,
        CancellationToken token,
        Action<int>? progress)
    {
        int sampleWidth = width + 2;
        int shadedRows = 0;
        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int sampleRow = (y + 1) * sampleWidth;
            int outputRow = y * stride;
            for (int x = 0; x < width; x++)
            {
                int sampleIndex = sampleRow + x + 1;
                int outputOffset = outputRow + x * 4;
                Color shaded = ApplyDistanceLighting(
                    state,
                    ReadColor(buffer, outputOffset),
                    distances[sampleIndex],
                    distances[sampleIndex - 1],
                    distances[sampleIndex + 1],
                    distances[sampleIndex - sampleWidth],
                    distances[sampleIndex + sampleWidth],
                    pixelSize);
                WriteColor(buffer, outputOffset, shaded);
            }

            int done = Interlocked.Increment(ref shadedRows);
            progress?.Invoke(70 + done * 30 / height);
        });
    }

    private static void RenderDistanceEstimation(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        decimal viewWidth,
        decimal viewHeight,
        ParallelOptions options,
        CancellationToken token,
        Action<int>? progress)
    {
        int sampleWidth = checked(width + 2);
        int sampleHeight = checked(height + 2);
        var distances = new float[checked(sampleWidth * sampleHeight)];
        int sampledRows = 0;

        Parallel.For(0, sampleHeight, options, (sampleY, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int y = sampleY - 1;
            decimal im = state.CenterY + (0.5m - (decimal)y / height) * viewHeight;
            int distanceRow = sampleY * sampleWidth;
            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                if ((sampleX & 63) == 0 && token.IsCancellationRequested)
                {
                    loopState.Stop();
                    return;
                }

                int x = sampleX - 1;
                decimal re = state.CenterX + ((decimal)x / width - 0.5m) * viewWidth;
                PixelMetrics metrics = IterateAt(state, re, im, token);
                distances[distanceRow + sampleX] = StoreDistance(metrics.Distance);
                if (sampleX is > 0 && sampleX <= width && sampleY is > 0 && sampleY <= height)
                {
                    Color baseColor = ResolveDistanceBaseColor(state, metrics);
                    WriteColor(buffer, (sampleY - 1) * stride + (sampleX - 1) * 4, baseColor);
                }
            }

            int done = Interlocked.Increment(ref sampledRows);
            progress?.Invoke(done * 70 / sampleHeight);
        });

        if (token.IsCancellationRequested) return;

        ShadeDistanceField(state, buffer, width, height, stride, distances,
            (double)(viewWidth / width), options, token, progress);
    }

    private static Color ResolveDistanceBaseColor(MandelbrotState state, PixelMetrics metrics)
    {
        if (metrics.Iterations >= state.Iterations) return ResolveInteriorColor(state);
        double colorPeriod = state.Palette.AlignWithRenderIterations
            ? Math.Max(1, state.Iterations)
            : Math.Max(1, state.Palette.ColorPeriod);
        return SampleSmoothPalette(state, Math.Min(metrics.Smooth, state.Iterations - 1e-9), colorPeriod);
    }

    private static Color ApplyDistanceLighting(
        MandelbrotState state,
        Color baseColor,
        double distance,
        double left,
        double right,
        double up,
        double down,
        double pixelSize)
    {
        if (!(distance > 0) || !double.IsFinite(distance) || !(pixelSize > 0)) return baseColor;

        left = SanitizeNeighborDistance(left, distance);
        right = SanitizeNeighborDistance(right, distance);
        up = SanitizeNeighborDistance(up, distance);
        down = SanitizeNeighborDistance(down, distance);
        double gradientX = (right - left) / (2 * pixelSize);
        double gradientY = (up - down) / (2 * pixelSize);

        // The surface height is -distance: the set remains a raised plateau while the
        // exterior falls away from its boundary.
        double relief = Math.Max(0, state.DistanceReliefStrength);
        double nx = gradientX * relief;
        double ny = gradientY * relief;
        double nz = 1;
        double normalLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        nx /= normalLength;
        ny /= normalLength;
        nz /= normalLength;

        double azimuth = state.DistanceLightAzimuth * Math.PI / 180;
        double elevation = Math.Clamp(state.DistanceLightElevation, 0, 90) * Math.PI / 180;
        double horizontal = Math.Cos(elevation);
        double lx = horizontal * Math.Cos(azimuth);
        double ly = horizontal * Math.Sin(azimuth);
        double lz = Math.Sin(elevation);
        double diffuse = Math.Max(0, nx * lx + ny * ly + nz * lz);

        double halfX = lx;
        double halfY = ly;
        double halfZ = lz + 1;
        double halfLength = Math.Sqrt(halfX * halfX + halfY * halfY + halfZ * halfZ);
        halfX /= halfLength;
        halfY /= halfLength;
        halfZ /= halfLength;
        double specular = diffuse <= 0
            ? 0
            : Math.Pow(Math.Max(0, nx * halfX + ny * halfY + nz * halfZ),
                Math.Max(1, state.DistanceShininess)) * Math.Max(0, state.DistanceSpecular);

        double illumination = Math.Max(0, state.DistanceAmbient) +
                              Math.Max(0, state.DistanceDiffuse) * diffuse;
        double contourFactor = 1;
        if (state.DistanceContoursEnabled && state.DistanceContourStrength > 0)
        {
            double spacing = Math.Max(2, state.DistanceContourSpacing) * pixelSize;
            double coordinate = distance / spacing;
            double fromLine = Math.Abs(coordinate - Math.Round(coordinate));
            double line = 1 - SmoothStep(0.025, 0.11, fromLine);
            contourFactor = 1 - Math.Clamp(state.DistanceContourStrength, 0, 1) * line;
        }

        double red = (SrgbToLinear(baseColor.R / 255.0) * illumination + specular) * contourFactor;
        double green = (SrgbToLinear(baseColor.G / 255.0) * illumination + specular) * contourFactor;
        double blue = (SrgbToLinear(baseColor.B / 255.0) * illumination + specular) * contourFactor;
        return Color.FromRgb(LinearToByte(red), LinearToByte(green), LinearToByte(blue));
    }

    private static double SanitizeNeighborDistance(double value, double fallback) =>
        value >= 0 && double.IsFinite(value) ? value : fallback;

    private static float StoreDistance(double distance) =>
        !(distance > 0) || !double.IsFinite(distance)
            ? 0
            : (float)Math.Min(distance, float.MaxValue);

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static byte LinearToByte(double value)
    {
        value = Math.Clamp(value, 0, 1);
        double srgb = value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(srgb * 255), 0, 255);
    }

    private static Color ReadColor(byte[] buffer, int offset) =>
        Color.FromRgb(buffer[offset + 2], buffer[offset + 1], buffer[offset]);

    private static void WriteColor(byte[] buffer, int offset, Color color)
    {
        buffer[offset] = color.B;
        buffer[offset + 1] = color.G;
        buffer[offset + 2] = color.R;
        buffer[offset + 3] = 255;
    }

    private static void RenderHistogram(
        MandelbrotState state, byte[] buffer, int width, int height, int stride,
        decimal viewWidth, decimal viewHeight, ParallelOptions options,
        CancellationToken token, ref int completedRows, Action<int>? progress)
    {
        completedRows = 0;
        int scanRows = 0;
        // Между проходами храним только два поля, которые реально нужны второму проходу
        // (сглаженное значение в double — точность бинирования обязана совпадать —
        // и счётчик итераций), а не весь PixelMetrics: это втрое меньше памяти.
        var smoothValues = new double[checked(width * height)];
        var iterationValues = new int[smoothValues.Length];
        var bins = new int[state.Iterations + 1];
        object histogramLock = new();

        // Счётчики бинов — по одному массиву на рабочий поток (localInit/localFinally), а не
        // на строку: при потолке итераций массив бинов занимает единицы мегабайт, и аллокация
        // на каждую строку давала бы гигабайты мусора за кадр.
        Parallel.For(0, height, options, () => new int[bins.Length], (y, loopState, localBins) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return localBins; }
            decimal im = state.CenterY + (0.5m - (decimal)y / height) * viewHeight;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return localBins; }
                decimal re = state.CenterX + ((decimal)x / width - 0.5m) * viewWidth;
                PixelMetrics value = IterateAt(state, re, im, token);
                smoothValues[row + x] = value.Smooth;
                iterationValues[row + x] = value.Iterations;
                int bin = state.HistogramInputUseSmooth
                    ? Math.Clamp((int)Math.Floor(value.Smooth), 0, state.Iterations)
                    : Math.Clamp(value.Iterations, 0, state.Iterations);
                localBins[bin]++;
            }
            int done = Interlocked.Increment(ref scanRows);
            progress?.Invoke(done * 65 / height);
            return localBins;
        },
        localBins =>
        {
            lock (histogramLock)
            {
                for (int i = 0; i < bins.Length; i++) bins[i] += localBins[i];
            }
        });

        if (token.IsCancellationRequested) return;

        long total = (long)width * height;
        var cdf = new double[bins.Length];
        long cumulative = 0;
        for (int i = 0; i <= state.Iterations; i++)
        {
            cumulative += bins[i];
            cdf[i] = total == 0 ? 0 : (double)cumulative / total;
        }

        int coloredRows = 0;
        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int metricRow = y * width;
            int outputRow = y * stride;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                PixelMetrics value = new(iterationValues[metricRow + x], smoothValues[metricRow + x], 0, 0);
                int bin = state.HistogramInputUseSmooth
                    ? Math.Clamp((int)Math.Floor(value.Smooth), 0, state.Iterations)
                    : Math.Clamp(value.Iterations, 0, state.Iterations);
                double normalized = value.Iterations >= state.Iterations
                    ? 0
                    : state.HistogramEnabledEqualization
                        ? cdf[bin]
                        : bin / (double)Math.Max(1, state.Iterations);
                Color color = ResolveColor(state, value, normalized);
                int offset = outputRow + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
            int done = Interlocked.Increment(ref coloredRows);
            progress?.Invoke(65 + done * 35 / height);
        });
    }

    private static PixelMetrics IterateAt(MandelbrotState state, decimal re, decimal im, CancellationToken token) =>
        state.Zoom > DecimalIterationZoomThreshold
            ? IterateDecimal(state, re, im, token)
            : Iterate(state, (double)re, (double)im, token);

    private static PixelMetrics Iterate(MandelbrotState state, double re, double im, CancellationToken token)
    {
        if (state.Variant == MandelbrotVariant.Mandelbrot && IsInsideMandelbrot(re, im))
            return new PixelMetrics(state.Iterations, state.Iterations, 0, 0);
        bool isJulia = state.Variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip;
        double cr = isJulia ? (double)state.JuliaCReal
            : state.UseInversion && state.Variant == MandelbrotVariant.Simonobrot ? -re : re;
        double ci = isJulia ? (double)state.JuliaCImaginary : im;
        double zr = isJulia ? re : 0;
        double zi = isJulia ? im : 0;
        double thresholdSquared = (double)(state.Threshold * state.Threshold);
        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        if (estimateDistance) thresholdSquared = Math.Max(4, thresholdSquared);
        Jacobian2 derivative = isJulia ? Jacobian2.Identity : Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);
        double minTrap = double.MaxValue;
        double stripe = 0;
        int iterations = 0;
        // Орбитальную ловушку и полосовую сумму держим только для их режимов окраски —
        // в остальных режимах эти поля PixelMetrics не читаются, а Atan2/Sin на каждой
        // итерации стоят дорого. Результат для прочих режимов бит-в-бит идентичен.
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        while (iterations < state.Iterations && zr * zr + zi * zi <= thresholdSquared)
        {
            if ((iterations & 63) == 0 && token.IsCancellationRequested) return default;
            if (trackTrap)
                minTrap = Math.Min(minTrap, Math.Min(Math.Abs(zr), Math.Abs(zi)));
            if (trackStripe)
                stripe += 0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2(zi, zr));
            if (estimateDistance)
                derivative = Jacobian2.Multiply(GetIterationJacobian(state, zr, zi), derivative) +
                             parameterDerivative;
            IterateOnce(state, ref zr, ref zi, cr, ci);
            iterations++;
        }

        double magnitudeSquared = zr * zr + zi * zi;
        double smooth = iterations;
        if (iterations < state.Iterations && magnitudeSquared > 1)
        {
            double logZn = Math.Log(magnitudeSquared) / 2;
            const double smoothingPower = 2;
            double nu = Math.Log(Math.Max(logZn, 1e-300) / Math.Log(smoothingPower)) /
                        Math.Log(smoothingPower);
            if (double.IsFinite(nu)) smooth = iterations + 1 - nu;
        }

        double distance = estimateDistance && iterations < state.Iterations
            ? EstimateDistance(zr, zi, derivative)
            : 0;
        return new PixelMetrics(
            iterations,
            smooth,
            minTrap == double.MaxValue ? 0 : minTrap,
            iterations == 0 ? 0 : stripe / iterations,
            distance);
    }

    private static PixelMetrics IterateDecimal(MandelbrotState state, decimal re, decimal im, CancellationToken token)
    {
        if (state.Variant == MandelbrotVariant.Mandelbrot && IsInsideMandelbrot(re, im))
            return new PixelMetrics(state.Iterations, state.Iterations, 0, 0);
        bool isJulia = state.Variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip;
        decimal cr = isJulia ? state.JuliaCReal
            : state.UseInversion && state.Variant == MandelbrotVariant.Simonobrot ? -re : re;
        decimal ci = isJulia ? state.JuliaCImaginary : im;
        decimal zr = isJulia ? re : 0;
        decimal zi = isJulia ? im : 0;
        decimal thresholdSquared = state.Threshold * state.Threshold;
        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        if (estimateDistance) thresholdSquared = Math.Max(4m, thresholdSquared);
        Jacobian2 derivative = isJulia ? Jacobian2.Identity : Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);
        decimal minTrap = decimal.MaxValue;
        double stripe = 0;
        int iterations = 0;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        while (iterations < state.Iterations && zr * zr + zi * zi <= thresholdSquared)
        {
            if ((iterations & 63) == 0 && token.IsCancellationRequested) return default;
            if (trackTrap)
                minTrap = Math.Min(minTrap, Math.Min(Math.Abs(zr), Math.Abs(zi)));
            if (trackStripe)
                stripe += 0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2((double)zi, (double)zr));
            if (estimateDistance)
                derivative = Jacobian2.Multiply(
                                 GetIterationJacobian(state, (double)zr, (double)zi), derivative) +
                             parameterDerivative;
            try
            {
                IterateOnceDecimal(state, ref zr, ref zi, cr, ci);
            }
            catch (OverflowException)
            {
                zr = state.Threshold + 1;
                zi = 0;
            }
            iterations++;
        }

        decimal magnitudeSquared = zr * zr + zi * zi;
        double smooth = iterations;
        if (iterations < state.Iterations && magnitudeSquared > 1)
        {
            double magnitudeAsDouble = (double)magnitudeSquared;
            double logZn = Math.Log(magnitudeAsDouble) / 2;
            const double smoothingPower = 2;
            double nu = Math.Log(Math.Max(logZn, 1e-300) / Math.Log(smoothingPower)) /
                        Math.Log(smoothingPower);
            if (double.IsFinite(nu)) smooth = iterations + 1 - nu;
        }

        double distance = estimateDistance && iterations < state.Iterations
            ? EstimateDistance((double)zr, (double)zi, derivative)
            : 0;
        return new PixelMetrics(iterations, smooth,
            minTrap == decimal.MaxValue ? 0 : (double)minTrap,
            iterations == 0 ? 0 : stripe / iterations,
            distance);
    }

    // ∂f/∂c — постоянная часть рекуррентности производной D ← J(z)·D + ∂f/∂c.
    // Жюлиа: c фиксирована, производная ведётся по z₀, поэтому добавки нет (а D₀ = I).
    // Остальные: c = точка пикселя, добавка единичная; у Симоноброта с инверсией в формулу
    // подставляется −re, поэтому у вещественной компоненты знак минус.
    private static Jacobian2 ParameterDerivativeOf(MandelbrotState state, bool isJulia) =>
        isJulia
            ? Jacobian2.Zero
            : new Jacobian2(
                state.UseInversion && state.Variant == MandelbrotVariant.Simonobrot ? -1 : 1,
                0,
                0,
                1);

    private static Jacobian2 GetIterationJacobian(MandelbrotState state, double zr, double zi)
    {
        Jacobian2 result;
        switch (state.Variant)
        {
            case MandelbrotVariant.Mandelbrot:
            case MandelbrotVariant.Julia:
                result = ComplexJacobian(new Complex(2 * zr, 2 * zi));
                break;
            case MandelbrotVariant.BurningShip:
            case MandelbrotVariant.JuliaBurningShip:
            {
                double transformedReal = Math.Abs(zr);
                double transformedImaginary = -Math.Abs(zi);
                var absoluteJacobian = new Jacobian2(Math.Sign(zr), 0, 0, -Math.Sign(zi));
                result = Jacobian2.Multiply(
                    ComplexJacobian(new Complex(2 * transformedReal, 2 * transformedImaginary)),
                    absoluteJacobian);
                break;
            }
            case MandelbrotVariant.Tricorn:
                result = Jacobian2.Multiply(
                    ComplexJacobian(new Complex(2 * zr, -2 * zi)),
                    new Jacobian2(1, 0, 0, -1));
                break;
            case MandelbrotVariant.Buffalo:
            {
                double transformedReal = Math.Abs(zr);
                double transformedImaginary = Math.Abs(zi);
                var absoluteJacobian = new Jacobian2(Math.Sign(zr), 0, 0, Math.Sign(zi));
                result = Jacobian2.Multiply(
                    ComplexJacobian(new Complex(2 * transformedReal, 2 * transformedImaginary)),
                    absoluteJacobian);
                break;
            }
            case MandelbrotVariant.Celtic:
            {
                double realPart = zr * zr - zi * zi;
                double sign = Math.Sign(realPart);
                result = new Jacobian2(
                    sign * 2 * zr,
                    -sign * 2 * zi,
                    2 * zi,
                    2 * zr);
                break;
            }
            case MandelbrotVariant.Generalized:
            {
                double power = (double)state.Power;
                Complex complexDerivative = power * Complex.Pow(new Complex(zr, zi), power - 1);
                result = ComplexJacobian(complexDerivative);
                break;
            }
            case MandelbrotVariant.Simonobrot:
            {
                double radiusSquared = zr * zr + zi * zi;
                if (!(radiusSquared > 0)) return Jacobian2.Zero;
                double power = (double)state.Power;
                double radius = Math.Sqrt(radiusSquared);
                Complex z = new(zr, zi);
                Complex powered = Complex.Pow(z, power);
                double radialScale = Math.Pow(radius, power);
                Complex complexDerivative = power * Complex.Pow(z, power - 1);
                double gradientScale = power * Math.Pow(radius, power - 2);
                double scaleX = gradientScale * zr;
                double scaleY = gradientScale * zi;
                Jacobian2 analyticPart = ComplexJacobian(complexDerivative) * radialScale;
                Jacobian2 radialPart = new(
                    powered.Real * scaleX,
                    powered.Real * scaleY,
                    powered.Imaginary * scaleX,
                    powered.Imaginary * scaleY);
                result = analyticPart + radialPart;
                break;
            }
            default:
                return Jacobian2.Zero;
        }

        return result.IsFinite ? result : Jacobian2.Zero;
    }

    private static Jacobian2 ComplexJacobian(Complex value) =>
        new(value.Real, -value.Imaginary, value.Imaginary, value.Real);

    private static double EstimateDistance(double zr, double zi, Jacobian2 derivative)
    {
        if (!derivative.IsFinite) return 0;
        double radiusSquared = zr * zr + zi * zi;
        if (!(radiusSquared > 1) || !double.IsFinite(radiusSquared)) return 0;

        double gradientX = (zr * derivative.M11 + zi * derivative.M21) / radiusSquared;
        double gradientY = (zr * derivative.M12 + zi * derivative.M22) / radiusSquared;
        double gradientLength = Math.Sqrt(gradientX * gradientX + gradientY * gradientY);
        if (!(gradientLength > 0) || !double.IsFinite(gradientLength)) return 0;

        double distance = 0.5 * Math.Log(Math.Sqrt(radiusSquared)) / gradientLength;
        return distance > 0 && double.IsFinite(distance) ? distance : 0;
    }

    private readonly record struct Jacobian2(double M11, double M12, double M21, double M22)
    {
        public static Jacobian2 Zero => new(0, 0, 0, 0);
        public static Jacobian2 Identity => new(1, 0, 0, 1);
        public bool IsFinite =>
            double.IsFinite(M11) && double.IsFinite(M12) &&
            double.IsFinite(M21) && double.IsFinite(M22);

        public static Jacobian2 Multiply(Jacobian2 left, Jacobian2 right) => new(
            left.M11 * right.M11 + left.M12 * right.M21,
            left.M11 * right.M12 + left.M12 * right.M22,
            left.M21 * right.M11 + left.M22 * right.M21,
            left.M21 * right.M12 + left.M22 * right.M22);

        public static Jacobian2 operator +(Jacobian2 left, Jacobian2 right) => new(
            left.M11 + right.M11,
            left.M12 + right.M12,
            left.M21 + right.M21,
            left.M22 + right.M22);

        public static Jacobian2 operator *(Jacobian2 value, double scale) => new(
            value.M11 * scale,
            value.M12 * scale,
            value.M21 * scale,
            value.M22 * scale);
    }

    /// <summary>
    /// Тот же якобиан 2×2, но с элементами в <see cref="FloatExp"/>. Нужен Distance
    /// Estimation на сверхглубоком зуме: |D| растёт примерно как сам зум, и за 1.8e308
    /// double-версия переполняется, обращая расстояние в ноль (рельеф исчезал целиком).
    /// Сам якобиан шага <see cref="GetIterationJacobian"/> остаётся double — он зависит
    /// только от ограниченного радиусом бейлаута <c>z</c>.
    /// </summary>
    private readonly record struct Jacobian2Exp(FloatExp M11, FloatExp M12, FloatExp M21, FloatExp M22)
    {
        public static Jacobian2Exp Zero => new(FloatExp.Zero, FloatExp.Zero, FloatExp.Zero, FloatExp.Zero);
        public static Jacobian2Exp Identity => new(FloatExp.One, FloatExp.Zero, FloatExp.Zero, FloatExp.One);

        public static Jacobian2Exp FromDouble(Jacobian2 value) => new(
            FloatExp.FromDouble(value.M11),
            FloatExp.FromDouble(value.M12),
            FloatExp.FromDouble(value.M21),
            FloatExp.FromDouble(value.M22));

        public bool IsFinite => M11.IsFinite && M12.IsFinite && M21.IsFinite && M22.IsFinite;

        /// <summary>Произведение double-якобиана шага на накопленную расширенную производную.</summary>
        public static Jacobian2Exp Multiply(Jacobian2 left, Jacobian2Exp right) => new(
            right.M11 * left.M11 + right.M21 * left.M12,
            right.M12 * left.M11 + right.M22 * left.M12,
            right.M11 * left.M21 + right.M21 * left.M22,
            right.M12 * left.M21 + right.M22 * left.M22);

        public static Jacobian2Exp operator +(Jacobian2Exp left, Jacobian2 right) => new(
            left.M11 + FloatExp.FromDouble(right.M11),
            left.M12 + FloatExp.FromDouble(right.M12),
            left.M21 + FloatExp.FromDouble(right.M21),
            left.M22 + FloatExp.FromDouble(right.M22));
    }

    /// <summary>
    /// Расстояние до границы в единицах пикселя: та же формула, что и
    /// <see cref="EstimateDistance"/>, но производная — в расширенном диапазоне, а результат
    /// сразу умножается на <paramref name="distanceScale"/> (величина, обратная размеру
    /// пикселя). Именно произведение и остаётся в диапазоне double: абсолютное расстояние на
    /// зуме 1e1000 само по себе непредставимо.
    /// </summary>
    private static double EstimateDistanceExp(
        double zr, double zi, Jacobian2Exp derivative, FloatExp distanceScale)
    {
        if (!derivative.IsFinite) return 0;
        double radiusSquared = zr * zr + zi * zi;
        if (!(radiusSquared > 1) || !double.IsFinite(radiusSquared)) return 0;

        FloatExp gradientX = (derivative.M11 * zr + derivative.M21 * zi) / radiusSquared;
        FloatExp gradientY = (derivative.M12 * zr + derivative.M22 * zi) / radiusSquared;
        FloatExp gradientLength = FloatExp.Sqrt(FloatExp.MagnitudeSquared(gradientX, gradientY));
        if (gradientLength.IsZero || !gradientLength.IsFinite) return 0;

        FloatExp distance = 0.5 * Math.Log(Math.Sqrt(radiusSquared)) / gradientLength * distanceScale;
        double result = distance.ToDouble();
        return result > 0 && double.IsFinite(result) ? result : 0;
    }

    private static void IterateOnceDecimal(
        MandelbrotState state, ref decimal zr, ref decimal zi, decimal cr, decimal ci)
    {
        switch (state.Variant)
        {
            case MandelbrotVariant.Mandelbrot:
            case MandelbrotVariant.Julia:
                SquareAddDecimal(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.BurningShip:
            case MandelbrotVariant.JuliaBurningShip:
                zr = Math.Abs(zr);
                zi = -Math.Abs(zi);
                SquareAddDecimal(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Tricorn:
                zi = -zi;
                SquareAddDecimal(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Buffalo:
                zr = Math.Abs(zr);
                zi = Math.Abs(zi);
                SquareAddDecimal(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Celtic:
            {
                decimal real = Math.Abs(zr * zr - zi * zi) + cr;
                zi = 2 * zr * zi + ci;
                zr = real;
                break;
            }
            case MandelbrotVariant.Simonobrot:
            {
                decimal magnitudeSquared = zr * zr + zi * zi;
                if (magnitudeSquared == 0)
                {
                    zr = cr;
                    zi = ci;
                    break;
                }
                ComplexDecimal powered = ComplexDecimal.Pow(
                    new ComplexDecimal(zr, zi), new ComplexDecimal(state.Power, 0));
                decimal magnitudePower = DecimalMath.Pow(DecimalMath.Sqrt(magnitudeSquared), state.Power);
                zr = powered.Real * magnitudePower + cr;
                zi = powered.Imaginary * magnitudePower + ci;
                break;
            }
            case MandelbrotVariant.Generalized:
            {
                ComplexDecimal powered = ComplexDecimal.Pow(
                    new ComplexDecimal(zr, zi), new ComplexDecimal(state.Power, 0));
                zr = powered.Real + cr;
                zi = powered.Imaginary + ci;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void SquareAddDecimal(ref decimal zr, ref decimal zi, decimal cr, decimal ci)
    {
        decimal real = zr * zr - zi * zi + cr;
        zi = 2 * zr * zi + ci;
        zr = real;
    }

    private static void IterateOnce(MandelbrotState state, ref double zr, ref double zi, double cr, double ci)
    {
        switch (state.Variant)
        {
            case MandelbrotVariant.Mandelbrot:
            case MandelbrotVariant.Julia:
                SquareAdd(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.BurningShip:
            case MandelbrotVariant.JuliaBurningShip:
                zr = Math.Abs(zr);
                zi = -Math.Abs(zi);
                SquareAdd(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Tricorn:
                zi = -zi;
                SquareAdd(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Buffalo:
                zr = Math.Abs(zr);
                zi = Math.Abs(zi);
                SquareAdd(ref zr, ref zi, cr, ci);
                break;
            case MandelbrotVariant.Celtic:
            {
                double real = Math.Abs(zr * zr - zi * zi) + cr;
                zi = 2 * zr * zi + ci;
                zr = real;
                break;
            }
            case MandelbrotVariant.Simonobrot:
            {
                double magnitudeSquared = zr * zr + zi * zi;
                if (magnitudeSquared == 0)
                {
                    zr = cr;
                    zi = ci;
                    break;
                }
                Complex powered = Complex.Pow(new Complex(zr, zi), (double)state.Power);
                double magnitudePower = Math.Pow(Math.Sqrt(magnitudeSquared), (double)state.Power);
                zr = powered.Real * magnitudePower + cr;
                zi = powered.Imaginary * magnitudePower + ci;
                break;
            }
            case MandelbrotVariant.Generalized:
            {
                Complex powered = Complex.Pow(new Complex(zr, zi), (double)state.Power);
                zr = powered.Real + cr;
                zi = powered.Imaginary + ci;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void SquareAdd(ref double zr, ref double zi, double cr, double ci)
    {
        double real = zr * zr - zi * zi + cr;
        zi = 2 * zr * zi + ci;
        zr = real;
    }

    private static bool IsInsideMandelbrot(double x, double y)
    {
        double shiftedX = x - 0.25;
        double ySquared = y * y;
        double q = shiftedX * shiftedX + ySquared;
        if (q * (q + shiftedX) <= 0.25 * ySquared) return true;
        double bulbX = x + 1;
        return bulbX * bulbX + ySquared <= 0.0625;
    }

    private static bool IsInsideMandelbrot(decimal x, decimal y)
    {
        decimal shiftedX = x - 0.25m;
        decimal ySquared = y * y;
        decimal q = shiftedX * shiftedX + ySquared;
        if (q * (q + shiftedX) <= 0.25m * ySquared) return true;
        decimal bulbX = x + 1;
        return bulbX * bulbX + ySquared <= 0.0625m;
    }

    private static Color ResolveColor(MandelbrotState state, PixelMetrics value, double histogramValue)
    {
        MandelbrotPalette palette = state.Palette;
        if (value.Iterations >= state.Iterations) return ResolveInteriorColor(state);
        double colorPeriod = palette.AlignWithRenderIterations
            ? Math.Max(1, state.Iterations)
            : Math.Max(1, palette.ColorPeriod);

        if (state.ColoringMode == MandelbrotColoringMode.Discrete)
            return SampleDiscretePalette(state, value.Iterations, colorPeriod);
        if (state.ColoringMode == MandelbrotColoringMode.Smooth)
            return SampleSmoothPalette(state, value.Smooth, colorPeriod);

        double normalized = state.ColoringMode switch
        {
            MandelbrotColoringMode.Histogram => Math.Pow(Math.Clamp(histogramValue, 0, 1), 1 / Math.Max(0.01, state.HistogramContrast)),
            MandelbrotColoringMode.OrbitTrap => Math.Clamp(1 / (1 + value.OrbitTrap) * state.OrbitTrapStrength + state.OrbitTrapBias, 0, 1),
            MandelbrotColoringMode.StripeAverage => Math.Clamp(
                Math.Clamp(value.Smooth / Math.Max(1, state.Iterations), 0, 1) * (1 - Math.Clamp(state.StripeStrength, 0, 1)) +
                Math.Clamp(value.Stripe + state.StripeBias, 0, 1) * Math.Clamp(state.StripeStrength, 0, 1), 0, 1),
            MandelbrotColoringMode.SmoothEscapePolynomial => PolynomialMap(state, value.Smooth / Math.Max(1, state.Iterations)),
            _ => 0
        };

        bool useSmoothPalette = state.ColoringMode != MandelbrotColoringMode.Histogram ||
                                state.HistogramInputUseSmooth;
        return useSmoothPalette
            ? SampleSmoothPalette(state, normalized * colorPeriod, colorPeriod)
            : SampleDiscretePalette(state, (int)Math.Round(normalized * colorPeriod), colorPeriod);
    }

    private static Color SampleDiscretePalette(MandelbrotState state, int iteration, double colorPeriod)
    {
        MandelbrotPalette palette = state.Palette;
        if (palette.UsesAlgorithmicGrayscale)
        {
            double grayNormalized = Math.Log(Math.Min(iteration, colorPeriod) + 1) /
                                    Math.Log(colorPeriod + 1);
            grayNormalized = TransformPaletteIndex(grayNormalized, state);
            byte gray = (byte)Math.Clamp((int)(255 * (1 - grayNormalized)), 0, 255);
            return ApplyGamma(Color.FromRgb(gray, gray, gray), palette.Gamma);
        }
        double normalized = Math.Min(iteration, colorPeriod) / colorPeriod;
        normalized = TransformPaletteIndex(normalized, state);
        return SamplePalette(palette, normalized);
    }

    private static Color SampleSmoothPalette(MandelbrotState state, double smoothIteration, double colorPeriod)
    {
        MandelbrotPalette palette = state.Palette;
        smoothIteration += state.SmoothIterationOffset;
        if (smoothIteration >= state.Iterations) return ResolveInteriorColor(state);
        smoothIteration = Math.Max(0, smoothIteration);

        double normalized;
        if (palette.UsesAlgorithmicGrayscale)
        {
            normalized = Math.Log(smoothIteration + 1) / Math.Log(Math.Max(1, state.Iterations) + 1);
            normalized = Math.Pow(Math.Clamp(normalized, 0, 1), Math.Max(0.01, state.SmoothBlendPower));
            normalized = TransformPaletteIndex(normalized, state);
            byte gray = (byte)Math.Clamp((int)(255 * (1 - normalized)), 0, 255);
            return ApplyGamma(Color.FromRgb(gray, gray, gray), palette.Gamma);
        }

        normalized = PositiveModulo(smoothIteration, colorPeriod) / colorPeriod;
        normalized = Math.Pow(normalized, Math.Max(0.01, state.SmoothBlendPower));
        normalized = TransformPaletteIndex(normalized, state);
        return SamplePalette(palette, normalized);
    }

    private static Color ResolveInteriorColor(MandelbrotState state) =>
        state.UseCustomInteriorColor ? state.InteriorColor : state.Palette.InteriorColor;

    private static double PolynomialMap(MandelbrotState state, double t)
    {
        t = Math.Clamp(t, 0, 1);
        double inverse = 1 - t;
        double polynomial = state.PolynomialA * inverse * t * t * t +
                            state.PolynomialB * inverse * inverse * t * t +
                            state.PolynomialC * inverse * inverse * inverse * t;
        polynomial = Math.Clamp(polynomial, 0, 1);
        double blended = t * (1 - Math.Clamp(state.PolynomialBlend, 0, 1)) +
                         polynomial * Math.Clamp(state.PolynomialBlend, 0, 1);
        return Math.Pow(Math.Clamp(blended + state.PolynomialBias, 0, 1), Math.Max(0.01, state.PolynomialGamma));
    }

    private static Color SamplePalette(MandelbrotPalette palette, double normalized)
    {
        if (palette.Colors.Count == 0) return Colors.White;
        if (palette.Colors.Count == 1) return ApplyGamma(palette.Colors[0], palette.Gamma);
        normalized = Math.Clamp(normalized, 0, 1);
        Color result;
        if (!palette.IsGradient)
        {
            int index = Math.Min((int)(normalized * palette.Colors.Count), palette.Colors.Count - 1);
            result = palette.Colors[index];
        }
        else
        {
            double position = normalized * (palette.Colors.Count - 1);
            int left = Math.Min((int)position, palette.Colors.Count - 1);
            if (left == palette.Colors.Count - 1) result = palette.Colors[left];
            else
            {
                Color a = palette.Colors[left];
                Color b = palette.Colors[left + 1];
                double t = position - left;
                result = Color.FromArgb(
                    Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
            }
        }
        return ApplyGamma(result, palette.Gamma);
    }

    // Гамма-коррекция всегда получает цвет с 8-битными каналами и применяет одну и ту
    // же формулу к значениям 0..255, поэтому кэшируем таблицу из 256 значений на поток
    // (гамма постоянна в пределах рендера). Результат бит-в-бит совпадает с прямым
    // вызовом Math.Pow, но вместо миллионов вызовов их всего 256 на поток.
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
                lut[value] = (byte)(255 * Math.Pow(value / 255.0, correction));
            _gammaLut = lut;
            _gammaLutKey = gamma;
        }
        return Color.FromArgb(color.A, lut[color.R], lut[color.G], lut[color.B]);
    }

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
    private static double PositiveModulo(double value, double period) => (value % period + period) % period;

    private static double TransformPaletteIndex(double value, MandelbrotState state)
    {
        double scale = Math.Abs(state.PaletteScale) < 1e-9 ? 1 : state.PaletteScale;
        double transformed = value * scale + state.PalettePhaseOffset;
        return state.PaletteWrapMode switch
        {
            MandelbrotPaletteWrapMode.Clamp => Math.Clamp(transformed, 0, 1),
            MandelbrotPaletteWrapMode.Mirror => Mirror01(transformed),
            _ => transformed - Math.Floor(transformed)
        };
    }

    private static double Mirror01(double value)
    {
        double period = value % 2;
        if (period < 0) period += 2;
        return period <= 1 ? period : 2 - period;
    }
}
