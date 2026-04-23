namespace FractalExplorer.Engines
{
    public sealed class FractalLogisticMapEngine
    {
        public const decimal BaseScale = 1.0m;

        public sealed class RenderSettings
        {
            public int Iterations { get; init; }
            public int TransientIterations { get; init; }
            public decimal R { get; init; }
            public decimal X0 { get; init; }
            public decimal BifurcationRMin { get; init; }
            public decimal BifurcationRMax { get; init; }
            public int BifurcationSamples { get; init; }
            public int BifurcationTransient { get; init; }
            public int BifurcationPlottedPoints { get; init; }
            public int CobwebSteps { get; init; }
            public List<Color> PaletteColors { get; init; } = new();
        }

        public static byte[] RenderOrbitBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            RenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            byte[] buffer = new byte[width * height * 4];
            if (backgroundColor.A > 0)
            {
                FillBackground(buffer, backgroundColor);
            }

            int iterations = settings.Iterations;
            int transient = Math.Min(settings.TransientIterations, Math.Max(0, iterations - 1));
            double r = (double)settings.R;
            double x = (double)settings.X0;
            int threadCount = Math.Max(1, maxDegreeOfParallelism ?? Environment.ProcessorCount);

            decimal scale = BaseScale / zoom;
            decimal minX = centerX - scale / 2m;
            decimal maxX = centerX + scale / 2m;
            decimal minY = centerY - scale / 2m;
            decimal maxY = centerY + scale / 2m;

            var paletteColors = settings.PaletteColors;
            Color fallback = Color.Lime;

            if (iterations <= 0)
            {
                if (drawAxes)
                {
                    DrawFallbackAxes(buffer, width, height, minX, maxX, minY, maxY);
                }
                progress?.Report(100);
                return buffer;
            }

            int actualWorkers = Math.Min(threadCount, iterations);
            int chunkSize = (iterations + actualWorkers - 1) / actualWorkers;
            byte[][] localBuffers = new byte[actualWorkers][];
            int[] plottedPerChunk = new int[actualWorkers];
            int completedIterations = 0;
            int lastReportedPercent = -1;

            Parallel.For(0, actualWorkers, new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount,
                CancellationToken = ct
            }, chunkIndex =>
            {
                int start = chunkIndex * chunkSize;
                if (start >= iterations) return;
                int end = Math.Min(iterations, start + chunkSize);
                byte[] localBuffer = new byte[buffer.Length];
                localBuffers[chunkIndex] = localBuffer;

                double localX = x;
                for (int i = 0; i < start; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    localX = r * localX * (1.0 - localX);
                }

                int localPlotted = 0;
                for (int i = start; i < end; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    localX = r * localX * (1.0 - localX);
                    if (i >= transient)
                    {
                        decimal graphX = iterations > 1 ? (decimal)i / (iterations - 1) : 0m;
                        decimal graphY = (decimal)localX;
                        if (graphX >= minX && graphX <= maxX && graphY >= minY && graphY <= maxY)
                        {
                            int px = (int)Math.Round((graphX - minX) / (maxX - minX) * (width - 1));
                            int py = (int)Math.Round((maxY - graphY) / (maxY - minY) * (height - 1));
                            if (px >= 0 && px < width && py >= 0 && py < height)
                            {
                                Color c = paletteColors.Count > 0
                                    ? paletteColors[(i - transient) % paletteColors.Count]
                                    : fallback;

                                int idx = (py * width + px) * 4;
                                localBuffer[idx] = c.B;
                                localBuffer[idx + 1] = c.G;
                                localBuffer[idx + 2] = c.R;
                                localBuffer[idx + 3] = 255;
                                localPlotted++;
                            }
                        }
                    }

                    int processed = Interlocked.Increment(ref completedIterations);
                    int percent = (int)(processed * 100L / iterations);
                    int previous = Volatile.Read(ref lastReportedPercent);
                    if (percent > previous && Interlocked.CompareExchange(ref lastReportedPercent, percent, previous) == previous)
                    {
                        progress?.Report(percent);
                    }
                }

                plottedPerChunk[chunkIndex] = localPlotted;
            });

            int plotted = 0;
            for (int chunkIndex = 0; chunkIndex < actualWorkers; chunkIndex++)
            {
                byte[]? local = localBuffers[chunkIndex];
                if (local == null) continue;
                plotted += plottedPerChunk[chunkIndex];

                for (int idx = 0; idx < local.Length; idx += 4)
                {
                    if (local[idx + 3] == 0) continue;
                    buffer[idx] = local[idx];
                    buffer[idx + 1] = local[idx + 1];
                    buffer[idx + 2] = local[idx + 2];
                    buffer[idx + 3] = local[idx + 3];
                }
            }

            if (plotted == 0 && drawAxes)
            {
                DrawFallbackAxes(buffer, width, height, minX, maxX, minY, maxY);
            }

            progress?.Report(100);
            return buffer;
        }

        public static byte[] RenderBifurcationBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            RenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            byte[] buffer = new byte[width * height * 4];
            if (backgroundColor.A > 0)
            {
                FillBackground(buffer, backgroundColor);
            }

            decimal rMin = Math.Min(settings.BifurcationRMin, settings.BifurcationRMax);
            decimal rMax = Math.Max(settings.BifurcationRMin, settings.BifurcationRMax);
            int rSamples = settings.BifurcationSamples;
            int transient = settings.BifurcationTransient;
            int plottedPoints = settings.BifurcationPlottedPoints;
            double x0 = (double)settings.X0;
            int threadCount = Math.Max(1, maxDegreeOfParallelism ?? Environment.ProcessorCount);
            var paletteColors = settings.PaletteColors;
            Color fallback = Color.Lime;

            decimal scale = BaseScale / zoom;
            decimal viewRMin = centerX - scale / 2m;
            decimal viewRMax = centerX + scale / 2m;
            decimal viewYMin = centerY - scale / 2m;
            decimal viewYMax = centerY + scale / 2m;

            if (rSamples <= 0 || plottedPoints <= 0 || rMax <= rMin)
            {
                if (drawAxes)
                {
                    DrawFallbackAxes(buffer, width, height, viewRMin, viewRMax, viewYMin, viewYMax);
                }
                progress?.Report(100);
                return buffer;
            }

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

                for (int sample = start; sample < end; sample++)
                {
                    ct.ThrowIfCancellationRequested();
                    double t = rSamples > 1 ? (double)sample / (rSamples - 1) : 0.0;
                    double r = (double)rMin + ((double)rMax - (double)rMin) * t;
                    double x = x0;

                    for (int i = 0; i < transient; i++)
                    {
                        x = r * x * (1d - x);
                    }

                    for (int i = 0; i < plottedPoints; i++)
                    {
                        x = r * x * (1d - x);
                        decimal rd = (decimal)r;
                        decimal yd = (decimal)x;
                        if (rd < viewRMin || rd > viewRMax || yd < viewYMin || yd > viewYMax) continue;

                        int px = (int)Math.Round((rd - viewRMin) / (viewRMax - viewRMin) * (width - 1));
                        int py = (int)Math.Round((viewYMax - yd) / (viewYMax - viewYMin) * (height - 1));
                        if (px < 0 || px >= width || py < 0 || py >= height) continue;

                        Color c = paletteColors.Count > 0
                            ? paletteColors[i % paletteColors.Count]
                            : fallback;
                        int idx = (py * width + px) * 4;
                        localBuffer[idx] = c.B;
                        localBuffer[idx + 1] = c.G;
                        localBuffer[idx + 2] = c.R;
                        localBuffer[idx + 3] = 255;
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

            foreach (byte[]? local in localBuffers)
            {
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

            if (drawAxes)
            {
                DrawFallbackAxes(buffer, width, height, viewRMin, viewRMax, viewYMin, viewYMax);
            }
            progress?.Report(100);
            return buffer;
        }

        public static byte[] RenderCobwebBuffer(
            int width,
            int height,
            RenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            Color backgroundColor = default)
        {
            byte[] buffer = new byte[width * height * 4];
            if (backgroundColor.A > 0)
            {
                FillBackground(buffer, backgroundColor);
            }

            decimal min = 0m;
            decimal max = 1m;
            double r = (double)settings.R;
            double x = (double)settings.X0;
            int steps = settings.CobwebSteps;
            var paletteColors = settings.PaletteColors;
            Color curveColor = Color.FromArgb(220, 60, 180, 250);
            Color diagonalColor = Color.FromArgb(220, 230, 230, 230);

            PlotFunctionCurve(buffer, width, height, min, max, diagonalColor, t => t);
            PlotFunctionCurve(buffer, width, height, min, max, curveColor, t => (decimal)(r * (double)t * (1.0 - (double)t)));

            for (int i = 0; i < steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                double y = r * x * (1d - x);
                Color stepColor = paletteColors.Count > 0 ? paletteColors[i % paletteColors.Count] : Color.Lime;
                DrawLine(buffer, width, height, min, max, min, max, (decimal)x, (decimal)x, (decimal)x, (decimal)y, stepColor);
                DrawLine(buffer, width, height, min, max, min, max, (decimal)x, (decimal)y, (decimal)y, (decimal)y, stepColor);
                x = y;
                progress?.Report((int)((i + 1) * 100L / Math.Max(1, steps)));
            }

            progress?.Report(100);
            return buffer;
        }

        private static void PlotFunctionCurve(byte[] buffer, int width, int height, decimal minX, decimal maxX, Color color, Func<decimal, decimal> f)
        {
            decimal? prevX = null;
            decimal? prevY = null;
            int segments = Math.Max(128, width * 2);
            for (int i = 0; i <= segments; i++)
            {
                decimal t = (decimal)i / segments;
                decimal x = minX + (maxX - minX) * t;
                decimal y = f(x);
                if (prevX.HasValue && prevY.HasValue)
                {
                    DrawLine(buffer, width, height, minX, maxX, 0m, 1m, prevX.Value, prevY.Value, x, y, color);
                }

                prevX = x;
                prevY = y;
            }
        }

        private static void DrawLine(byte[] buffer, int width, int height, decimal minX, decimal maxX, decimal minY, decimal maxY, decimal x0, decimal y0, decimal x1, decimal y1, Color color)
        {
            int px0 = (int)Math.Round((x0 - minX) / (maxX - minX) * (width - 1));
            int py0 = (int)Math.Round((maxY - y0) / (maxY - minY) * (height - 1));
            int px1 = (int)Math.Round((x1 - minX) / (maxX - minX) * (width - 1));
            int py1 = (int)Math.Round((maxY - y1) / (maxY - minY) * (height - 1));

            int dx = Math.Abs(px1 - px0);
            int sx = px0 < px1 ? 1 : -1;
            int dy = -Math.Abs(py1 - py0);
            int sy = py0 < py1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                if (px0 >= 0 && px0 < width && py0 >= 0 && py0 < height)
                {
                    int idx = (py0 * width + px0) * 4;
                    buffer[idx] = color.B;
                    buffer[idx + 1] = color.G;
                    buffer[idx + 2] = color.R;
                    buffer[idx + 3] = color.A;
                }

                if (px0 == px1 && py0 == py1) break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    px0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    py0 += sy;
                }
            }
        }

        private static void FillBackground(byte[] buffer, Color color)
        {
            byte b = color.B;
            byte g = color.G;
            byte r = color.R;
            byte a = color.A;

            for (int i = 0; i < buffer.Length; i += 4)
            {
                buffer[i] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = a;
            }
        }

        private static void DrawFallbackAxes(byte[] buffer, int width, int height, decimal minX, decimal maxX, decimal minY, decimal maxY)
        {
            void SetPixel(int px, int py, Color color)
            {
                if (px < 0 || py < 0 || px >= width || py >= height) return;
                int idx = (py * width + px) * 4;
                buffer[idx] = color.B;
                buffer[idx + 1] = color.G;
                buffer[idx + 2] = color.R;
                buffer[idx + 3] = color.A;
            }

            if (minX <= 0m && maxX >= 0m)
            {
                int x0 = (int)Math.Round((-minX) / (maxX - minX) * (width - 1));
                for (int y = 0; y < height; y++) SetPixel(x0, y, Color.FromArgb(128, 80, 80, 80));
            }

            if (minY <= 0m && maxY >= 0m)
            {
                int y0 = (int)Math.Round((maxY - 0m) / (maxY - minY) * (height - 1));
                for (int x = 0; x < width; x++) SetPixel(x, y0, Color.FromArgb(128, 80, 80, 80));
            }
        }
    }
}
