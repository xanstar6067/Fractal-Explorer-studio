namespace FractalExplorer.Engines
{
    public sealed class FractalLorenzEngine
    {
        public const decimal BaseScale = 80m;

        public enum ProjectionMode
        {
            XY,
            XZ,
            YZ
        }

        public sealed class RenderSettings
        {
            public decimal Sigma { get; init; }
            public decimal Rho { get; init; }
            public decimal Beta { get; init; }
            public decimal Dt { get; init; }
            public int Steps { get; init; }
            public decimal StartX { get; init; }
            public decimal StartY { get; init; }
            public decimal StartZ { get; init; }
            public ProjectionMode Projection { get; init; }
        }

        public static byte[] RenderBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, RenderSettings settings, CancellationToken ct, IProgress<int>? progress = null)
        {
            byte[] buffer = new byte[width * height * 4];

            int steps = Math.Max(100, settings.Steps);
            int warmup = Math.Max(50, Math.Min(4000, steps / 20));
            decimal dt = settings.Dt <= 0 ? 0.01m : settings.Dt;

            double sigma = (double)settings.Sigma;
            double rho = (double)settings.Rho;
            double beta = (double)settings.Beta;
            double dtD = (double)dt;

            double x = (double)settings.StartX;
            double y = (double)settings.StartY;
            double z = (double)settings.StartZ;

            decimal scale = BaseScale / Math.Max(0.000001m, zoom);
            decimal minU = centerX - scale / 2m;
            decimal maxU = centerX + scale / 2m;
            decimal minV = centerY - scale / 2m;
            decimal maxV = centerY + scale / 2m;

            (double u, double v) Project(double px, double py, double pz)
            {
                return settings.Projection switch
                {
                    ProjectionMode.XZ => (px, pz),
                    ProjectionMode.YZ => (py, pz),
                    _ => (px, py)
                };
            }

            for (int i = 0; i < warmup; i++)
            {
                ct.ThrowIfCancellationRequested();
                StepLorenz(ref x, ref y, ref z, sigma, rho, beta, dtD);
            }

            (double uPrev, double vPrev) = Project(x, y, z);
            int lastReported = -1;

            for (int i = 0; i < steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                StepLorenz(ref x, ref y, ref z, sigma, rho, beta, dtD);
                (double uCur, double vCur) = Project(x, y, z);

                decimal du1 = (decimal)uPrev;
                decimal dv1 = (decimal)vPrev;
                decimal du2 = (decimal)uCur;
                decimal dv2 = (decimal)vCur;

                DrawLineClamped(buffer, width, height, du1, dv1, du2, dv2, minU, maxU, minV, maxV, GetGradientColor(i, steps));

                uPrev = uCur;
                vPrev = vCur;

                int percent = (int)((i + 1L) * 100 / steps);
                if (percent > lastReported)
                {
                    lastReported = percent;
                    progress?.Report(percent);
                }
            }

            DrawAxes(buffer, width, height, minU, maxU, minV, maxV);
            progress?.Report(100);
            return buffer;
        }

        private static void StepLorenz(ref double x, ref double y, ref double z, double sigma, double rho, double beta, double dt)
        {
            double dx = sigma * (y - x);
            double dy = x * (rho - z) - y;
            double dz = x * y - beta * z;

            x += dx * dt;
            y += dy * dt;
            z += dz * dt;
        }

        private static Color GetGradientColor(int i, int total)
        {
            if (total <= 1) return Color.Cyan;
            double t = Math.Clamp((double)i / (total - 1), 0, 1);
            int r = (int)(70 + 160 * t);
            int g = (int)(220 - 120 * t);
            int b = (int)(255 - 30 * t);
            return Color.FromArgb(255, r, g, b);
        }

        private static void DrawLineClamped(byte[] buffer, int width, int height, decimal x0, decimal y0, decimal x1, decimal y1, decimal minX, decimal maxX, decimal minY, decimal maxY, Color color)
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
                SetPixel(buffer, width, height, px0, py0, color);
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

        private static void SetPixel(byte[] buffer, int width, int height, int x, int y, Color color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            int idx = (y * width + x) * 4;
            buffer[idx] = color.B;
            buffer[idx + 1] = color.G;
            buffer[idx + 2] = color.R;
            buffer[idx + 3] = color.A;
        }

        private static void DrawAxes(byte[] buffer, int width, int height, decimal minX, decimal maxX, decimal minY, decimal maxY)
        {
            Color axisColor = Color.FromArgb(110, 110, 110);
            if (minX <= 0m && maxX >= 0m)
            {
                int x0 = (int)Math.Round((-minX) / (maxX - minX) * (width - 1));
                for (int y = 0; y < height; y++) SetPixel(buffer, width, height, x0, y, axisColor);
            }

            if (minY <= 0m && maxY >= 0m)
            {
                int y0 = (int)Math.Round((maxY - 0m) / (maxY - minY) * (height - 1));
                for (int x = 0; x < width; x++) SetPixel(buffer, width, height, x, y0, axisColor);
            }
        }
    }
}
