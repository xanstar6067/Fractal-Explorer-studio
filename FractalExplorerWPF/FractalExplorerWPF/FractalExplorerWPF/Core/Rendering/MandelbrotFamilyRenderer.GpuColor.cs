using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class MandelbrotFamilyRenderer
{
    // Keep palette/gamma/wrapping and distance lighting identical across both backends.
    internal static void ColorGpuMetrics(MandelbrotState state, float[] samples, byte[] pixels,
        int width, int height, CancellationToken token)
    {
        bool relief = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        int sampleWidth = width + (relief ? 2 : 0);
        PixelMetrics Read(int x, int y)
        {
            int i = (y * sampleWidth + x) * 5;
            return new((int)samples[i], samples[i + 1], samples[i + 2], samples[i + 3], samples[i + 4]);
        }
        int Bin(PixelMetrics m) => Math.Clamp(state.HistogramInputUseSmooth
            ? (int)Math.Floor(m.Smooth) : m.Iterations, 0, state.Iterations);
        double[]? cdf = null;
        if (state.ColoringMode == MandelbrotColoringMode.Histogram && state.HistogramEnabledEqualization)
        {
            var bins = new int[state.Iterations + 1];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++) bins[Bin(Read(x, y))]++;
            }
            cdf = new double[bins.Length];
            long total = (long)width * height, cumulative = 0;
            for (int i = 0; i < bins.Length; i++) { cumulative += bins[i]; cdf[i] = (double)cumulative / total; }
        }
        float[]? distances = relief ? new float[(width + 2) * (height + 2)] : null;
        if (distances is not null)
            for (int i = 0; i < distances.Length; i++) distances[i] = StoreDistance(samples[i * 5 + 4]);
        var options = new ParallelOptions { CancellationToken = token,
            MaxDegreeOfParallelism = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads };
        Parallel.For(0, height, options, y =>
        {
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0) token.ThrowIfCancellationRequested();
                PixelMetrics m = Read(x + (relief ? 1 : 0), y + (relief ? 1 : 0));
                double histogram = state.ColoringMode == MandelbrotColoringMode.Histogram && m.Iterations < state.Iterations
                    ? cdf is null ? Bin(m) / (double)state.Iterations : cdf[Bin(m)] : 0;
                WriteColor(pixels, (y * width + x) * 4, relief ? ResolveDistanceBaseColor(state, m) : ResolveColor(state, m, histogram));
            }
        });
        if (distances is not null)
            ShadeDistanceField(state, pixels, width, height, width * 4, distances,
                3 / state.Zoom.ToDouble() / width, options, token, null);
    }
}
