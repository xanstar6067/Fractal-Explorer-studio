namespace FractalExplorer.Engines
{
    public sealed class FractalBifurcationEngine
    {
        public const decimal BaseScale = 1.0m;

        public sealed class RenderSettings
        {
            public decimal RMin { get; init; }
            public decimal RMax { get; init; }
            public decimal XMin { get; init; }
            public decimal XMax { get; init; }
            public int TransientIterations { get; init; }
            public int SamplesPerR { get; init; }
            public int Iterations { get; init; }
        }

        public static byte[] RenderBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, RenderSettings settings, CancellationToken ct, IProgress<int>? progress = null, int? maxDegreeOfParallelism = null)
        {
            byte[] buffer = new byte[width * height * 4];

            decimal rMin = Math.Min(settings.RMin, settings.RMax);
            decimal rMax = Math.Max(settings.RMin, settings.RMax);
            decimal xMin = Math.Min(settings.XMin, settings.XMax);
            decimal xMax = Math.Max(settings.XMin, settings.XMax);
            int transient = settings.TransientIterations;
            int samplesPerR = settings.SamplesPerR;
            int iterations = settings.Iterations;

            decimal scale = BaseScale / zoom;
            decimal viewRMin = centerX - scale / 2m;
            decimal viewRMax = centerX + scale / 2m;
            decimal viewXMin = centerY - scale / 2m;
            decimal viewXMax = centerY + scale / 2m;

            if (iterations <= 0 || samplesPerR <= 0 || rMax <= rMin || xMax <= xMin)
            {
                DrawAxes(buffer, width, height, viewRMin, viewRMax, viewXMin, viewXMax);
                progress?.Report(100);
                return buffer;
            }

            int rSamples = Math.Max(16, width * 3);
            int threadCount = Math.Max(1, maxDegreeOfParallelism ?? Environment.ProcessorCount);
            int actualWorkers = Math.Min(threadCount, rSamples);
            int chunkSize = (rSamples + actualWorkers - 1) / actualWorkers;

            byte[][] localBuffers = new byte[actualWorkers][];
            int completedSamples = 0;
            int lastReportedPercent = -1;

            Parallel.For(0, actualWorkers, new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount,
                CancellationToken = ct
            }, chunkIndex =>
            {
                int start = chunkIndex * chunkSize;
                if (start >= rSamples) return;
                int end = Math.Min(rSamples, start + chunkSize);

                byte[] localBuffer = new byte[buffer.Length];
                localBuffers[chunkIndex] = localBuffer;

                for (int i = start; i < end; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    decimal t = rSamples <= 1 ? 0m : (decimal)i / (rSamples - 1);
                    decimal r = rMin + (rMax - rMin) * t;
                    double rValue = (double)r;

                    for (int seed = 0; seed < samplesPerR; seed++)
                    {
                        double x = (double)(xMin + (xMax - xMin) * (samplesPerR <= 1 ? 0m : (decimal)seed / (samplesPerR - 1)));

                        for (int k = 0; k < transient; k++)
                        {
                            x = rValue * x * (1.0 - x);
                        }

                        for (int k = 0; k < iterations; k++)
                        {
                            x = rValue * x * (1.0 - x);
                            decimal graphX = r;
                            decimal graphY = (decimal)x;

                            if (graphX < viewRMin || graphX > viewRMax || graphY < viewXMin || graphY > viewXMax)
                            {
                                continue;
                            }

                            int px = (int)Math.Round((graphX - viewRMin) / (viewRMax - viewRMin) * (width - 1));
                            int py = (int)Math.Round((viewXMax - graphY) / (viewXMax - viewXMin) * (height - 1));
                            if (px < 0 || px >= width || py < 0 || py >= height) continue;

                            int idx = (py * width + px) * 4;
                            localBuffer[idx] = 255;
                            localBuffer[idx + 1] = 255;
                            localBuffer[idx + 2] = 255;
                            localBuffer[idx + 3] = 255;
                        }
                    }

                    int processed = Interlocked.Increment(ref completedSamples);
                    int percent = (int)(processed * 100L / rSamples);
                    int previous = Volatile.Read(ref lastReportedPercent);
                    if (percent > previous && Interlocked.CompareExchange(ref lastReportedPercent, percent, previous) == previous)
                    {
                        progress?.Report(percent);
                    }
                }
            });

            for (int chunkIndex = 0; chunkIndex < actualWorkers; chunkIndex++)
            {
                byte[]? local = localBuffers[chunkIndex];
                if (local == null) continue;

                for (int idx = 0; idx < local.Length; idx += 4)
                {
                    if (local[idx + 3] == 0) continue;
                    buffer[idx] = local[idx];
                    buffer[idx + 1] = local[idx + 1];
                    buffer[idx + 2] = local[idx + 2];
                    buffer[idx + 3] = local[idx + 3];
                }
            }

            DrawAxes(buffer, width, height, viewRMin, viewRMax, viewXMin, viewXMax);
            progress?.Report(100);
            return buffer;
        }

        private static void DrawAxes(byte[] buffer, int width, int height, decimal minX, decimal maxX, decimal minY, decimal maxY)
        {
            void SetPixel(int x, int y, Color color)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                int idx = (y * width + x) * 4;
                buffer[idx] = color.B;
                buffer[idx + 1] = color.G;
                buffer[idx + 2] = color.R;
                buffer[idx + 3] = color.A;
            }

            if (minX <= 0m && maxX >= 0m)
            {
                int x0 = (int)Math.Round((-minX) / (maxX - minX) * (width - 1));
                for (int y = 0; y < height; y++) SetPixel(x0, y, Color.FromArgb(140, 120, 120, 120));
            }

            if (minY <= 0m && maxY >= 0m)
            {
                int y0 = (int)Math.Round((maxY - 0m) / (maxY - minY) * (height - 1));
                for (int x = 0; x < width; x++) SetPixel(x, y0, Color.FromArgb(140, 120, 120, 120));
            }
        }
    }
}
