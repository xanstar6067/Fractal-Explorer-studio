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
            decimal viewRangeR = viewRMax - viewRMin;
            decimal viewRangeX = viewXMax - viewXMin;
            double invRangeR = 1.0 / (double)viewRangeR;
            double invRangeX = 1.0 / (double)viewRangeX;
            double pxScale = (width - 1) * invRangeR;
            double pyScale = (height - 1) * invRangeX;
            double viewRMinDouble = (double)viewRMin;
            double viewXMaxDouble = (double)viewXMax;

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

            bool[] hit = new bool[width * height];
            int[] sampleColumns = new int[rSamples];
            int[] sampleOwners = new int[rSamples];
            int[] columnOwner = new int[width];
            Array.Fill(columnOwner, -1);
            bool hasSingleRSample = rSamples <= 1;
            decimal rRange = rMax - rMin;
            decimal rSampleFactor = hasSingleRSample ? 0m : 1m / (rSamples - 1);

            for (int i = 0; i < rSamples; i++)
            {
                decimal t = hasSingleRSample ? 0m : i * rSampleFactor;
                decimal r = rMin + rRange * t;

                if (r < viewRMin || r > viewRMax)
                {
                    sampleColumns[i] = -1;
                    sampleOwners[i] = -1;
                    continue;
                }

                double graphX = (double)r;
                int px = (int)((graphX - viewRMinDouble) * pxScale + 0.5);
                if (px < 0 || px >= width)
                {
                    sampleColumns[i] = -1;
                    sampleOwners[i] = -1;
                    continue;
                }

                sampleColumns[i] = px;
                int owner = Math.Min(actualWorkers - 1, i / chunkSize);
                sampleOwners[i] = owner;
                if (columnOwner[px] == -1)
                {
                    columnOwner[px] = owner;
                }
            }

            int completedSamples = 0;
            int lastReportedPercent = -1;
            bool hasSingleSeed = samplesPerR <= 1;
            decimal xRange = xMax - xMin;
            decimal seedFactor = hasSingleSeed ? 0m : 1m / (samplesPerR - 1);

            Parallel.For(0, actualWorkers, new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount,
                CancellationToken = ct
            }, chunkIndex =>
            {
                int start = chunkIndex * chunkSize;
                if (start >= rSamples) return;
                int end = Math.Min(rSamples, start + chunkSize);

                for (int i = start; i < end; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    int px = sampleColumns[i];
                    if (px < 0 || sampleOwners[i] != chunkIndex || columnOwner[px] != chunkIndex)
                    {
                        int skippedProcessed = Interlocked.Increment(ref completedSamples);
                        int skippedPercent = (int)(skippedProcessed * 100L / rSamples);
                        int skippedPrevious = Volatile.Read(ref lastReportedPercent);
                        if (skippedPercent > skippedPrevious && Interlocked.CompareExchange(ref lastReportedPercent, skippedPercent, skippedPrevious) == skippedPrevious)
                        {
                            progress?.Report(skippedPercent);
                        }
                        continue;
                    }

                    decimal t = hasSingleRSample ? 0m : i * rSampleFactor;
                    decimal r = rMin + rRange * t;
                    double rValue = (double)r;

                    for (int seed = 0; seed < samplesPerR; seed++)
                    {
                        decimal seedT = hasSingleSeed ? 0m : seed * seedFactor;
                        double x = (double)(xMin + xRange * seedT);

                        for (int k = 0; k < transient; k++)
                        {
                            x = rValue * x * (1.0 - x);
                        }

                        for (int k = 0; k < iterations; k++)
                        {
                            x = rValue * x * (1.0 - x);
                            decimal graphY = (decimal)x;

                            if (graphY < viewXMin || graphY > viewXMax)
                            {
                                continue;
                            }

                            int py = (int)((viewXMaxDouble - (double)graphY) * pyScale + 0.5);
                            if (py < 0 || py >= height) continue;

                            int hitIndex = py * width + px;
                            hit[hitIndex] = true;
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

            for (int pixel = 0; pixel < hit.Length; pixel++)
            {
                if (!hit[pixel]) continue;
                int idx = pixel * 4;
                buffer[idx] = 255;
                buffer[idx + 1] = 255;
                buffer[idx + 2] = 255;
                buffer[idx + 3] = 255;
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
