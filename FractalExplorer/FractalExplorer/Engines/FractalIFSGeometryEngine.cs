namespace FractalExplorer.Engines
{
    public sealed class IfsAffineTransform
    {
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; }
        public double E { get; set; }
        public double F { get; set; }
        public double Probability { get; set; }

        public PointF Apply(double x, double y)
        {
            float nx = (float)(A * x + B * y + E);
            float ny = (float)(C * x + D * y + F);
            return new PointF(nx, ny);
        }

        public IfsAffineTransform Clone() => new()
        {
            A = A,
            B = B,
            C = C,
            D = D,
            E = E,
            F = F,
            Probability = Probability
        };
    }

    public sealed class IfsPointOfInterest
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public int Iterations { get; init; } = 250_000;
        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double Scale { get; init; } = 2.4;
        public required List<IfsAffineTransform> Transforms { get; init; }
    }

    public sealed class FractalIFSGeometryEngine
    {
        private const double MinScale = 0.05;
        private const double MaxScale = 40.0;

        public int Iterations { get; set; } = 220_000;
        public Color BackgroundColor { get; set; } = Color.Black;
        public Color FractalColor { get; set; } = Color.Lime;
        public double CenterX { get; set; } = 0;
        public double CenterY { get; set; } = 0;
        public double Scale { get; set; } = 2.4;
        public List<IfsAffineTransform> Transforms { get; private set; } = CreateDefaultPointsOfInterest()[0].Transforms.Select(t => t.Clone()).ToList();

        public void ApplyPointOfInterest(IfsPointOfInterest point)
        {
            Iterations = point.Iterations;
            CenterX = point.CenterX;
            CenterY = point.CenterY;
            Scale = point.Scale;
            SetTransforms(point.Transforms);
        }

        public void SetTransforms(IEnumerable<IfsAffineTransform> transforms)
        {
            Transforms = transforms.Select(t => t.Clone()).ToList();
        }

        public static List<IfsPointOfInterest> CreateDefaultPointsOfInterest()
        {
            List<IfsAffineTransform> barnsley =
            [
                new IfsAffineTransform { A = 0.0, B = 0.0, C = 0.0, D = 0.16, E = 0.0, F = 0.0, Probability = 0.01 },
                new IfsAffineTransform { A = 0.85, B = 0.04, C = -0.04, D = 0.85, E = 0.0, F = 1.6, Probability = 0.85 },
                new IfsAffineTransform { A = 0.2, B = -0.26, C = 0.23, D = 0.22, E = 0.0, F = 1.6, Probability = 0.07 },
                new IfsAffineTransform { A = -0.15, B = 0.28, C = 0.26, D = 0.24, E = 0.0, F = 0.44, Probability = 0.07 }
            ];

            List<IfsAffineTransform> dragon =
            [
                new IfsAffineTransform { A = 0.824074, B = 0.281428, C = -0.212346, D = 0.864198, E = -1.882290, F = -0.110607, Probability = 0.5 },
                new IfsAffineTransform { A = 0.088272, B = 0.520988, C = -0.463889, D = -0.377778, E = 0.785360, F = 8.095795, Probability = 0.5 }
            ];

            return
            [
                new IfsPointOfInterest
                {
                    Id = "barnsley_overview",
                    Name = "Barnsley Fern — общий вид",
                    Iterations = 220_000,
                    CenterX = 0,
                    CenterY = 0,
                    Scale = 2.45,
                    Transforms = barnsley.Select(t => t.Clone()).ToList()
                },
                new IfsPointOfInterest
                {
                    Id = "barnsley_stem",
                    Name = "Barnsley Fern — стебель",
                    Iterations = 320_000,
                    CenterX = -0.03,
                    CenterY = -0.2,
                    Scale = 1.1,
                    Transforms = barnsley.Select(t => t.Clone()).ToList()
                },
                new IfsPointOfInterest
                {
                    Id = "dragon_overview",
                    Name = "Heighway Dragon — общий вид",
                    Iterations = 200_000,
                    CenterX = 0,
                    CenterY = 0,
                    Scale = 2.3,
                    Transforms = dragon.Select(t => t.Clone()).ToList()
                },
                new IfsPointOfInterest
                {
                    Id = "dragon_wing",
                    Name = "Heighway Dragon — ребро",
                    Iterations = 300_000,
                    CenterX = -0.28,
                    CenterY = -0.08,
                    Scale = 0.95,
                    Transforms = dragon.Select(t => t.Clone()).ToList()
                }
            ];
        }

        public void RenderToBuffer(byte[] buffer, int width, int height, int stride, int bytesPerPixel, CancellationToken token, Action<int>? reportProgress = null)
        {
            FillBackground(buffer, width, height, stride, bytesPerPixel, BackgroundColor);

            if (Transforms.Count == 0)
            {
                reportProgress?.Invoke(100);
                return;
            }

            int iterations = Math.Max(1_000, Iterations);
            double x = 0;
            double y = 0;
            var random = new Random(12345);

            var points = new PointF[iterations];
            int burnIn = Math.Min(100, iterations / 10);

            for (int i = 0; i < iterations + burnIn; i++)
            {
                token.ThrowIfCancellationRequested();
                IfsAffineTransform transform = PickTransform(Transforms, random.NextDouble());
                PointF p = transform.Apply(x, y);
                x = p.X;
                y = p.Y;

                if (i >= burnIn)
                {
                    points[i - burnIn] = p;
                }

                if (i % Math.Max(1, iterations / 100) == 0)
                {
                    reportProgress?.Invoke((int)(Math.Min(i, iterations) * 100.0 / iterations));
                }
            }

            float minX = points.Min(p => p.X);
            float maxX = points.Max(p => p.X);
            float minY = points.Min(p => p.Y);
            float maxY = points.Max(p => p.Y);

            float dx = Math.Max(1e-6f, maxX - minX);
            float dy = Math.Max(1e-6f, maxY - minY);

            double viewportWidth = Math.Clamp(Math.Abs(Scale), MinScale, MaxScale);
            double viewportHeight = viewportWidth * height / (double)width;
            double left = CenterX - viewportWidth / 2.0;
            double top = CenterY + viewportHeight / 2.0;

            foreach (PointF point in points)
            {
                token.ThrowIfCancellationRequested();

                double nx = (point.X - minX) / dx;
                double ny = (point.Y - minY) / dy;
                double worldX = (nx - 0.5) * 2.0;
                double worldY = (ny - 0.5) * 2.0;

                int px = (int)(((worldX - left) / viewportWidth) * width);
                int py = (int)(((top - worldY) / viewportHeight) * height);

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
            double total = transforms.Sum(t => t.Probability);
            if (total <= 0)
            {
                return transforms[^1];
            }

            double sum = 0;
            foreach (IfsAffineTransform t in transforms)
            {
                sum += t.Probability / total;
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
