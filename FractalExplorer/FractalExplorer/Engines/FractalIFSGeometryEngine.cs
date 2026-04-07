namespace FractalExplorer.Engines
{
    public enum IfsPreset
    {
        BarnsleyFern,
        HeighwayDragon
    }

    public sealed class IfsAffineTransform
    {
        public double A { get; init; }
        public double B { get; init; }
        public double C { get; init; }
        public double D { get; init; }
        public double E { get; init; }
        public double F { get; init; }
        public double Probability { get; init; }

        public PointF Apply(double x, double y)
        {
            float nx = (float)(A * x + B * y + E);
            float ny = (float)(C * x + D * y + F);
            return new PointF(nx, ny);
        }
    }

    public sealed class IfsPresetDefinition
    {
        public required string Name { get; init; }
        public required List<IfsAffineTransform> Transforms { get; init; }
    }

    public sealed class FractalIFSGeometryEngine
    {
        public int Iterations { get; set; } = 200_000;
        public Color BackgroundColor { get; set; } = Color.Black;
        public Color FractalColor { get; set; } = Color.Lime;
        public IfsPreset Preset { get; private set; } = IfsPreset.BarnsleyFern;
        public List<IfsAffineTransform> Transforms { get; private set; } = CreatePreset(IfsPreset.BarnsleyFern).Transforms;

        public void ApplyPreset(IfsPreset preset)
        {
            Preset = preset;
            Transforms = CreatePreset(preset).Transforms;
        }

        public static IfsPresetDefinition CreatePreset(IfsPreset preset)
        {
            return preset switch
            {
                IfsPreset.HeighwayDragon => new IfsPresetDefinition
                {
                    Name = "HeighwayDragon",
                    Transforms =
                    [
                        new IfsAffineTransform { A = 0.824074, B = 0.281428, C = -0.212346, D = 0.864198, E = -1.882290, F = -0.110607, Probability = 0.5 },
                        new IfsAffineTransform { A = 0.088272, B = 0.520988, C = -0.463889, D = -0.377778, E = 0.785360, F = 8.095795, Probability = 0.5 }
                    ]
                },
                _ => new IfsPresetDefinition
                {
                    Name = "BarnsleyFern",
                    Transforms =
                    [
                        new IfsAffineTransform { A = 0.0, B = 0.0, C = 0.0, D = 0.16, E = 0.0, F = 0.0, Probability = 0.01 },
                        new IfsAffineTransform { A = 0.85, B = 0.04, C = -0.04, D = 0.85, E = 0.0, F = 1.6, Probability = 0.85 },
                        new IfsAffineTransform { A = 0.2, B = -0.26, C = 0.23, D = 0.22, E = 0.0, F = 1.6, Probability = 0.07 },
                        new IfsAffineTransform { A = -0.15, B = 0.28, C = 0.26, D = 0.24, E = 0.0, F = 0.44, Probability = 0.07 }
                    ]
                }
            };
        }

        public void RenderToBuffer(byte[] buffer, int width, int height, int stride, int bytesPerPixel, CancellationToken token, Action<int>? reportProgress = null)
        {
            FillBackground(buffer, width, height, stride, bytesPerPixel, BackgroundColor);

            if (Transforms.Count == 0)
            {
                reportProgress?.Invoke(100);
                return;
            }

            int iterations = Math.Max(1000, Iterations);
            double x = 0;
            double y = 0;
            var random = new Random(12345);

            var points = new PointF[iterations];
            for (int i = 0; i < iterations; i++)
            {
                token.ThrowIfCancellationRequested();
                IfsAffineTransform transform = PickTransform(Transforms, random.NextDouble());
                PointF p = transform.Apply(x, y);
                x = p.X;
                y = p.Y;
                points[i] = p;

                if (i % Math.Max(1, iterations / 100) == 0)
                {
                    reportProgress?.Invoke((int)(i * 100.0 / iterations));
                }
            }

            float minX = points.Min(p => p.X);
            float maxX = points.Max(p => p.X);
            float minY = points.Min(p => p.Y);
            float maxY = points.Max(p => p.Y);

            float dx = Math.Max(1e-6f, maxX - minX);
            float dy = Math.Max(1e-6f, maxY - minY);
            float scale = 0.9f * Math.Min(width / dx, height / dy);

            float xOffset = (width - dx * scale) * 0.5f;
            float yOffset = (height - dy * scale) * 0.5f;

            foreach (PointF point in points)
            {
                token.ThrowIfCancellationRequested();
                int px = (int)((point.X - minX) * scale + xOffset);
                int py = height - 1 - (int)((point.Y - minY) * scale + yOffset);

                if ((uint)px >= (uint)width || (uint)py >= (uint)height)
                {
                    continue;
                }

                int index = py * stride + px * bytesPerPixel;
                buffer[index] = FractalColor.B;
                buffer[index + 1] = FractalColor.G;
                buffer[index + 2] = FractalColor.R;
                if (bytesPerPixel >= 4)
                {
                    buffer[index + 3] = 255;
                }
            }

            reportProgress?.Invoke(100);
        }

        private static IfsAffineTransform PickTransform(List<IfsAffineTransform> transforms, double randomValue)
        {
            double sum = 0;
            foreach (IfsAffineTransform t in transforms)
            {
                sum += t.Probability;
                if (randomValue <= sum)
                {
                    return t;
                }
            }

            return transforms[^1];
        }

        private static void FillBackground(byte[] buffer, int width, int height, int stride, int bytesPerPixel, Color color)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * bytesPerPixel;
                    buffer[index] = color.B;
                    buffer[index + 1] = color.G;
                    buffer[index + 2] = color.R;
                    if (bytesPerPixel >= 4)
                    {
                        buffer[index + 3] = color.A;
                    }
                }
            }
        }
    }
}
