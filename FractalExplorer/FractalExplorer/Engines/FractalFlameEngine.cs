using FractalExplorer.Utilities;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FractalExplorer.Engines
{
    public enum FlameVariation
    {
        Linear = 0,
        Sinusoidal = 1,
        Spherical = 2
    }

    public sealed class FlameTransform
    {
        public double Weight { get; set; } = 1.0;
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; }
        public double E { get; set; }
        public double F { get; set; }
        public FlameVariation Variation { get; set; } = FlameVariation.Linear;
        public Color Color { get; set; } = Color.White;

        public FlameTransform Clone()
        {
            return (FlameTransform)MemberwiseClone();
        }
    }

    public sealed class FractalFlameEngine
    {
        public double CenterX { get; set; } = 0.0;
        public double CenterY { get; set; } = 0.0;
        public double Scale { get; set; } = 4.0;
        public int Samples { get; set; } = 1_000_000;
        public int IterationsPerSample { get; set; } = 20;
        public int WarmupIterations { get; set; } = 20;
        public int ThreadCount { get; set; } = 0;
        public double Exposure { get; set; } = 1.35;
        public double Gamma { get; set; } = 2.2;

        public List<FlameTransform> Transforms { get; } = new();

        public void RenderToBuffer(
            byte[] buffer,
            int width,
            int height,
            int stride,
            int bytesPerPixel,
            CancellationToken token,
            Action<int>? reportProgress = null,
            int viewportOffsetX = 0,
            int viewportOffsetY = 0,
            int viewportTotalWidth = 0,
            int viewportTotalHeight = 0,
            Action<byte[]>? reportCoverageHeatmap = null,
            int coverageUpdateIntervalMs = 200)
        {
            if (bytesPerPixel < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));
            }

            int projectionWidth = viewportTotalWidth > 0 ? viewportTotalWidth : width;
            int projectionHeight = viewportTotalHeight > 0 ? viewportTotalHeight : height;
            int projectionOffsetX = viewportOffsetX;
            int projectionOffsetY = viewportOffsetY;

            double worldWidth = Scale;
            double worldHeight = Scale * projectionHeight / (double)projectionWidth;
            double worldLeft = CenterX - worldWidth * 0.5;
            double worldTop = CenterY + worldHeight * 0.5;

            double[] hdrHit = new double[width * height];
            double[] hdrR = new double[width * height];
            double[] hdrG = new double[width * height];
            double[] hdrB = new double[width * height];

            List<FlameTransform> activeTransforms = Transforms.Where(t => t.Weight > 0).Select(t => t.Clone()).ToList();
            if (activeTransforms.Count == 0)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return;
            }

            double weightSum = activeTransforms.Sum(t => t.Weight);
            double[] cumulativeWeights = new double[activeTransforms.Count];
            double cumulative = 0;
            for (int i = 0; i < activeTransforms.Count; i++)
            {
                cumulative += activeTransforms[i].Weight / weightSum;
                cumulativeWeights[i] = cumulative;
            }
            cumulativeWeights[^1] = 1.0;

            int totalSamples = Math.Max(1, Samples);
            int iterations = Math.Max(1, IterationsPerSample);
            int warmup = Math.Max(0, WarmupIterations);
            int totalIterations = (int)Math.Min(int.MaxValue, (long)warmup + iterations);
            int threadCount = ThreadCount <= 0 ? Environment.ProcessorCount : ThreadCount;
            int processed = 0;
            object mergeLock = new();
            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Max(1, threadCount)
            };

            if (reportCoverageHeatmap == null)
            {
                Parallel.For(0, totalSamples, options,
                    () => new LocalDenseAccumulator(width * height),
                    (sampleIndex, _, local) =>
                    {
                        token.ThrowIfCancellationRequested();
                        ulong seed = MixSeed((ulong)(sampleIndex + 1) * 0x9E3779B97F4A7C15UL);
                        double x = NextUnitSigned(ref seed);
                        double y = NextUnitSigned(ref seed);
                        double cr = 0.5;
                        double cg = 0.5;
                        double cb = 0.5;

                        for (int i = 0; i < totalIterations; i++)
                        {
                            FlameTransform transform = SelectTransform(activeTransforms, cumulativeWeights, NextUnit(ref seed));
                            ApplyTransform(transform, ref x, ref y);

                            cr = (cr + transform.Color.R / 255.0) * 0.5;
                            cg = (cg + transform.Color.G / 255.0) * 0.5;
                            cb = (cb + transform.Color.B / 255.0) * 0.5;

                            if (i < warmup)
                            {
                                continue;
                            }

                            int px = (int)((x - worldLeft) / worldWidth * projectionWidth) - projectionOffsetX;
                            int py = (int)((worldTop - y) / worldHeight * projectionHeight) - projectionOffsetY;
                            if ((uint)px >= (uint)width || (uint)py >= (uint)height)
                            {
                                continue;
                            }

                            int idx = py * width + px;
                            local.Hit[idx] += 1.0;
                            local.R[idx] += cr;
                            local.G[idx] += cg;
                            local.B[idx] += cb;
                        }

                        int done = Interlocked.Increment(ref processed);
                        if (done % Math.Max(1, totalSamples / 100) == 0)
                        {
                            reportProgress?.Invoke((int)(done * 100.0 / totalSamples));
                        }

                        return local;
                    },
                    local =>
                    {
                        lock (mergeLock)
                        {
                            for (int i = 0; i < hdrHit.Length; i++)
                            {
                                hdrHit[i] += local.Hit[i];
                                hdrR[i] += local.R[i];
                                hdrG[i] += local.G[i];
                                hdrB[i] += local.B[i];
                            }
                        }
                    });
            }
            else
            {
                int batchSize = Math.Max(1_000, totalSamples / 24);
                int coverageIntervalMs = Math.Clamp(coverageUpdateIntervalMs, 50, 2_000);
                var coverageTimer = Stopwatch.StartNew();

                for (int batchStart = 0; batchStart < totalSamples; batchStart += batchSize)
                {
                    int batchEnd = Math.Min(totalSamples, batchStart + batchSize);
                    Parallel.For(batchStart, batchEnd, options,
                        () => new LocalAccumulator(),
                        (sampleIndex, _, local) =>
                        {
                            token.ThrowIfCancellationRequested();
                            ulong seed = MixSeed((ulong)(sampleIndex + 1) * 0x9E3779B97F4A7C15UL);
                            double x = NextUnitSigned(ref seed);
                            double y = NextUnitSigned(ref seed);
                            double cr = 0.5;
                            double cg = 0.5;
                            double cb = 0.5;

                            for (int i = 0; i < totalIterations; i++)
                            {
                                FlameTransform transform = SelectTransform(activeTransforms, cumulativeWeights, NextUnit(ref seed));
                                ApplyTransform(transform, ref x, ref y);

                                cr = (cr + transform.Color.R / 255.0) * 0.5;
                                cg = (cg + transform.Color.G / 255.0) * 0.5;
                                cb = (cb + transform.Color.B / 255.0) * 0.5;

                                if (i < warmup)
                                {
                                    continue;
                                }

                                int px = (int)((x - worldLeft) / worldWidth * projectionWidth) - projectionOffsetX;
                                int py = (int)((worldTop - y) / worldHeight * projectionHeight) - projectionOffsetY;
                                if ((uint)px >= (uint)width || (uint)py >= (uint)height)
                                {
                                    continue;
                                }

                                int idx = py * width + px;
                                ref PixelAccumulation pixel = ref local.GetOrAddPixel(idx);
                                pixel.Hit += 1.0;
                                pixel.R += cr;
                                pixel.G += cg;
                                pixel.B += cb;
                            }

                            int done = Interlocked.Increment(ref processed);
                            if (done % Math.Max(1, totalSamples / 100) == 0)
                            {
                                reportProgress?.Invoke((int)(done * 100.0 / totalSamples));
                            }

                            return local;
                        },
                        local =>
                        {
                            lock (mergeLock)
                            {
                                foreach ((int idx, PixelAccumulation pixel) in local.Pixels)
                                {
                                    hdrHit[idx] += pixel.Hit;
                                    hdrR[idx] += pixel.R;
                                    hdrG[idx] += pixel.G;
                                    hdrB[idx] += pixel.B;
                                }
                            }
                        });

                    if (coverageTimer.ElapsedMilliseconds >= coverageIntervalMs)
                    {
                        reportCoverageHeatmap(ConvertHitToHeatmapBuffer(width, height, stride, bytesPerPixel, hdrHit));
                        coverageTimer.Restart();
                    }
                }
            }

            ConvertHdrToBitmap(buffer, width, height, stride, bytesPerPixel, hdrHit, hdrR, hdrG, hdrB);
            reportProgress?.Invoke(100);
        }

        private static byte[] ConvertHitToHeatmapBuffer(int width, int height, int stride, int bytesPerPixel, double[] hit)
        {
            byte[] heatmap = new byte[Math.Abs(stride) * height];
            double maxHit = hit.Max();
            if (maxHit <= 0)
            {
                return heatmap;
            }

            double denom = Math.Log(1.0 + maxHit);
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    int px = row + x * bytesPerPixel;
                    double h = hit[idx];
                    if (h <= 0)
                    {
                        heatmap[px + 3] = 0;
                        continue;
                    }

                    double mapped = Math.Log(1.0 + h) / Math.Max(1e-12, denom);
                    Color color = MapHeatColor(mapped);
                    heatmap[px + 0] = color.B;
                    heatmap[px + 1] = color.G;
                    heatmap[px + 2] = color.R;
                    heatmap[px + 3] = 255;
                }
            }

            return heatmap;
        }

        private static Color MapHeatColor(double intensity)
        {
            double t = Math.Clamp(intensity, 0.0, 1.0);
            if (t < 0.33)
            {
                double k = t / 0.33;
                return Color.FromArgb(255, (int)(k * 90), (int)(k * 255), 255);
            }

            if (t < 0.66)
            {
                double k = (t - 0.33) / 0.33;
                return Color.FromArgb(255, (int)(90 + k * 165), 255, (int)(255 - k * 255));
            }

            {
                double k = (t - 0.66) / 0.34;
                return Color.FromArgb(255, 255, (int)(255 - k * 255), (int)(k * 120));
            }
        }

        private void ConvertHdrToBitmap(byte[] buffer, int width, int height, int stride, int bytesPerPixel, double[] hit, double[] r, double[] g, double[] b)
        {
            double maxHit = hit.Max();
            if (maxHit <= 0)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return;
            }

            double denom = Math.Log(1.0 + maxHit);
            double exposure = Math.Max(0.0001, Exposure);

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    double h = hit[idx];
                    int px = row + x * bytesPerPixel;
                    if (h <= 0)
                    {
                        buffer[px + 0] = 0;
                        buffer[px + 1] = 0;
                        buffer[px + 2] = 0;
                        buffer[px + 3] = 255;
                        continue;
                    }

                    double mapped = Math.Log(1.0 + h) / Math.Max(1e-12, denom);
                    double avgR = r[idx] / h;
                    double avgG = g[idx] / h;
                    double avgB = b[idx] / h;

                    double tonedR = 1.0 - Math.Exp(-avgR * mapped * exposure);
                    double tonedG = 1.0 - Math.Exp(-avgG * mapped * exposure);
                    double tonedB = 1.0 - Math.Exp(-avgB * mapped * exposure);

                    Color color = Color.FromArgb(
                        255,
                        (int)Math.Clamp(tonedR * 255.0, 0, 255),
                        (int)Math.Clamp(tonedG * 255.0, 0, 255),
                        (int)Math.Clamp(tonedB * 255.0, 0, 255));

                    color = ColorCorrection.ApplyGamma(color, Math.Max(0.1, Gamma));
                    buffer[px + 0] = color.B;
                    buffer[px + 1] = color.G;
                    buffer[px + 2] = color.R;
                    buffer[px + 3] = 255;
                }
            }
        }

        private static void ApplyTransform(FlameTransform transform, ref double x, ref double y)
        {
            double ax = transform.A * x + transform.B * y + transform.C;
            double ay = transform.D * x + transform.E * y + transform.F;
            (x, y) = transform.Variation switch
            {
                FlameVariation.Linear => (ax, ay),
                FlameVariation.Sinusoidal => (Math.Sin(ax), Math.Sin(ay)),
                FlameVariation.Spherical => ApplySpherical(ax, ay),
                _ => (ax, ay)
            };
        }

        private static (double x, double y) ApplySpherical(double x, double y)
        {
            double r2 = x * x + y * y;
            if (r2 < 1e-12)
            {
                return (0, 0);
            }

            return (x / r2, y / r2);
        }

        private static FlameTransform SelectTransform(List<FlameTransform> transforms, double[] cumulativeWeights, double t)
        {
            int index = Array.BinarySearch(cumulativeWeights, t);
            if (index < 0)
            {
                index = ~index;
            }
            if (index >= transforms.Count)
            {
                index = transforms.Count - 1;
            }

            return transforms[index];
        }

        private static ulong MixSeed(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static double NextUnit(ref ulong state)
        {
            state = MixSeed(state);
            return (state >> 11) * (1.0 / (1UL << 53));
        }

        private static double NextUnitSigned(ref ulong state)
        {
            return NextUnit(ref state) * 2.0 - 1.0;
        }

        private sealed class LocalAccumulator
        {
            public Dictionary<int, PixelAccumulation> Pixels { get; } = new();

            public ref PixelAccumulation GetOrAddPixel(int pixelIndex)
            {
                return ref CollectionsMarshal.GetValueRefOrAddDefault(Pixels, pixelIndex, out _);
            }
        }

        private sealed class LocalDenseAccumulator
        {
            public LocalDenseAccumulator(int size)
            {
                Hit = new double[size];
                R = new double[size];
                G = new double[size];
                B = new double[size];
            }

            public double[] Hit { get; }
            public double[] R { get; }
            public double[] G { get; }
            public double[] B { get; }
        }

        private struct PixelAccumulation
        {
            public double Hit;
            public double R;
            public double G;
            public double B;
        }
    }
}
