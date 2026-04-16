namespace FractalExplorer.Engines
{
    public sealed class FractalHenonEngine
    {
        public const decimal BaseScale = 6.0m;

        public sealed class RenderSettings
        {
            public decimal A { get; init; }
            public decimal B { get; init; }
            public decimal X0 { get; init; }
            public decimal Y0 { get; init; }
            public int Iterations { get; init; }
            public int DiscardIterations { get; init; }
            public int Threads { get; init; }
        }

        public static byte[] RenderBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, RenderSettings settings, CancellationToken token, IProgress<int>? progress = null)
        {
            int[] hits = new int[width * height];
            int seeds = Math.Max(1, settings.Threads * 2);
            int pointsPerSeed = Math.Max(1000, settings.Iterations / seeds);
            int discardPerSeed = settings.DiscardIterations;

            double cx = (double)centerX;
            double cy = (double)centerY;
            double scaleX = (double)(BaseScale / (zoom == 0 ? 1m : zoom));
            double scaleY = scaleX * Math.Max(1, height) / Math.Max(1, width);
            double minX = cx - scaleX * 0.5;
            double maxX = cx + scaleX * 0.5;
            double minY = cy - scaleY * 0.5;
            double maxY = cy + scaleY * 0.5;

            double a = (double)settings.A;
            double b = (double)settings.B;

            Parallel.For(0, seeds, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, settings.Threads),
                CancellationToken = token
            }, seedIndex =>
            {
                double x = (double)settings.X0 + seedIndex * 0.000137;
                double y = (double)settings.Y0 - seedIndex * 0.000073;
                int localSteps = seedIndex == seeds - 1 ? settings.Iterations - pointsPerSeed * (seeds - 1) : pointsPerSeed;
                localSteps = Math.Max(1, localSteps);

                for (int i = 0; i < discardPerSeed; i++)
                {
                    double xNext = 1.0 - a * x * x + y;
                    double yNext = b * x;
                    x = xNext;
                    y = yNext;
                }

                for (int i = 0; i < localSteps; i++)
                {
                    token.ThrowIfCancellationRequested();

                    double xNext = 1.0 - a * x * x + y;
                    double yNext = b * x;
                    x = xNext;
                    y = yNext;

                    if (x < minX || x > maxX || y < minY || y > maxY) continue;

                    int px = (int)((x - minX) / (maxX - minX) * (width - 1));
                    int py = (int)((maxY - y) / (maxY - minY) * (height - 1));

                    if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                    Interlocked.Increment(ref hits[py * width + px]);
                }

                progress?.Report(Math.Min(99, (int)((seedIndex + 1) * 100.0 / seeds)));
            });

            int maxHit = hits.Max();
            if (maxHit <= 0)
            {
                return new byte[width * height * 4];
            }

            byte[] buffer = new byte[width * height * 4];
            double norm = Math.Log(1.0 + maxHit);
            for (int i = 0; i < hits.Length; i++)
            {
                int hit = hits[i];
                if (hit <= 0) continue;

                double v = Math.Log(1.0 + hit) / norm;
                byte c = (byte)Math.Clamp((int)(Math.Pow(v, 0.65) * 255.0), 0, 255);
                int offset = i * 4;
                buffer[offset] = c;
                buffer[offset + 1] = c;
                buffer[offset + 2] = c;
                buffer[offset + 3] = 255;
            }

            progress?.Report(100);
            return buffer;
        }
    }
}
