namespace FractalExplorer.Engines
{
    public sealed class FractalRosslerEngine
    {
        public const decimal BaseScale = 30m;
        private const double MinDt = 0.000001d;
        private const double MaxDt = 0.2d;

        public enum ProjectionMode
        {
            XY,
            XZ,
            YZ
        }

        public sealed class RenderSettings
        {
            public decimal A { get; init; }
            public decimal B { get; init; }
            public decimal C { get; init; }
            public decimal Dt { get; init; }
            public int Steps { get; init; }
            public decimal StartX { get; init; }
            public decimal StartY { get; init; }
            public decimal StartZ { get; init; }
            public ProjectionMode Projection { get; init; }
        }

        public static byte[] RenderBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            RenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            bool drawAxes = true)
        {
            byte[] buffer = new byte[width * height * 4];

            int steps = Math.Max(100, settings.Steps);
            int warmup = Math.Max(50, Math.Min(4000, steps / 20));
            decimal dt = settings.Dt <= 0 ? 0.01m : settings.Dt;

            double a = (double)settings.A;
            double b = (double)settings.B;
            double c = (double)settings.C;
            double dtD = Math.Clamp((double)dt, MinDt, MaxDt);

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
                StepRosslerRk4(ref x, ref y, ref z, a, b, c, dtD);
                if (!IsFiniteState(x, y, z))
                {
                    break;
                }
            }

            (double uPrev, double vPrev) = Project(x, y, z);
            int lastReported = -1;

            for (int i = 0; i < steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                StepRosslerRk4(ref x, ref y, ref z, a, b, c, dtD);
                if (!IsFiniteState(x, y, z))
                {
                    break;
                }

                (double uCur, double vCur) = Project(x, y, z);
                DrawLineClamped(buffer, width, height, uPrev, vPrev, uCur, vCur, (double)minU, (double)maxU, (double)minV, (double)maxV, GetGradientColor(i, steps));

                uPrev = uCur;
                vPrev = vCur;

                int percent = (int)((i + 1L) * 100 / steps);
                if (percent > lastReported)
                {
                    lastReported = percent;
                    progress?.Report(percent);
                }
            }

            if (drawAxes)
            {
                DrawAxes(buffer, width, height, minU, maxU, minV, maxV);
            }
            progress?.Report(100);
            return buffer;
        }

        private static void StepRosslerRk4(ref double x, ref double y, ref double z, double a, double b, double c, double dt)
        {
            (double k1x, double k1y, double k1z) = Derivatives(x, y, z, a, b, c);
            (double k2x, double k2y, double k2z) = Derivatives(
                x + 0.5 * dt * k1x,
                y + 0.5 * dt * k1y,
                z + 0.5 * dt * k1z,
                a, b, c);
            (double k3x, double k3y, double k3z) = Derivatives(
                x + 0.5 * dt * k2x,
                y + 0.5 * dt * k2y,
                z + 0.5 * dt * k2z,
                a, b, c);
            (double k4x, double k4y, double k4z) = Derivatives(
                x + dt * k3x,
                y + dt * k3y,
                z + dt * k3z,
                a, b, c);

            x += dt * (k1x + 2.0 * k2x + 2.0 * k3x + k4x) / 6.0;
            y += dt * (k1y + 2.0 * k2y + 2.0 * k3y + k4y) / 6.0;
            z += dt * (k1z + 2.0 * k2z + 2.0 * k3z + k4z) / 6.0;
        }

        private static (double dx, double dy, double dz) Derivatives(double x, double y, double z, double a, double b, double c)
        {
            double dx = -y - z;
            double dy = x + a * y;
            double dz = b + z * (x - c);
            return (dx, dy, dz);
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

        private static void DrawLineClamped(byte[] buffer, int width, int height, double x0, double y0, double x1, double y1, double minX, double maxX, double minY, double maxY, Color color)
        {
            if (!TryMapToPixel(x0, y0, minX, maxX, minY, maxY, width, height, out double px0d, out double py0d)) return;
            if (!TryMapToPixel(x1, y1, minX, maxX, minY, maxY, width, height, out double px1d, out double py1d)) return;
            if (!ClipLineToViewport(ref px0d, ref py0d, ref px1d, ref py1d, width, height)) return;

            int px0 = (int)Math.Round(px0d);
            int py0 = (int)Math.Round(py0d);
            int px1 = (int)Math.Round(px1d);
            int py1 = (int)Math.Round(py1d);

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

        private static bool TryMapToPixel(double x, double y, double minX, double maxX, double minY, double maxY, int width, int height, out double px, out double py)
        {
            px = 0;
            py = 0;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
            if (!double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY)) return false;

            double dx = maxX - minX;
            double dy = maxY - minY;
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || Math.Abs(dx) < 1e-12 || Math.Abs(dy) < 1e-12) return false;

            px = (x - minX) / dx * (width - 1);
            py = (maxY - y) / dy * (height - 1);
            return double.IsFinite(px) && double.IsFinite(py);
        }

        private static bool ClipLineToViewport(ref double x0, ref double y0, ref double x1, ref double y1, int width, int height)
        {
            double minX = 0;
            double minY = 0;
            double maxX = Math.Max(0, width - 1);
            double maxY = Math.Max(0, height - 1);

            int code0 = ComputeOutCode(x0, y0, minX, minY, maxX, maxY);
            int code1 = ComputeOutCode(x1, y1, minX, minY, maxX, maxY);

            while (true)
            {
                if ((code0 | code1) == 0) return true;
                if ((code0 & code1) != 0) return false;

                int outCode = code0 != 0 ? code0 : code1;
                double x = 0;
                double y = 0;

                if ((outCode & 8) != 0) // bottom
                {
                    if (Math.Abs(y1 - y0) < 1e-12) return false;
                    x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0);
                    y = maxY;
                }
                else if ((outCode & 4) != 0) // top
                {
                    if (Math.Abs(y1 - y0) < 1e-12) return false;
                    x = x0 + (x1 - x0) * (minY - y0) / (y1 - y0);
                    y = minY;
                }
                else if ((outCode & 2) != 0) // right
                {
                    if (Math.Abs(x1 - x0) < 1e-12) return false;
                    y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0);
                    x = maxX;
                }
                else if ((outCode & 1) != 0) // left
                {
                    if (Math.Abs(x1 - x0) < 1e-12) return false;
                    y = y0 + (y1 - y0) * (minX - x0) / (x1 - x0);
                    x = minX;
                }

                if (outCode == code0)
                {
                    x0 = x;
                    y0 = y;
                    code0 = ComputeOutCode(x0, y0, minX, minY, maxX, maxY);
                }
                else
                {
                    x1 = x;
                    y1 = y;
                    code1 = ComputeOutCode(x1, y1, minX, minY, maxX, maxY);
                }
            }
        }

        private static int ComputeOutCode(double x, double y, double minX, double minY, double maxX, double maxY)
        {
            int code = 0;
            if (x < minX) code |= 1;
            else if (x > maxX) code |= 2;
            if (y < minY) code |= 4;
            else if (y > maxY) code |= 8;
            return code;
        }

        private static bool IsFiniteState(double x, double y, double z) =>
            double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);

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
