using FractalExplorer.Resources;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Engines
{
    /// <summary>
    /// Движок рендера карты показателей Ляпунова для логистического отображения.
    /// </summary>
    public class FractalLyapunovEngine
    {
        /// <summary>
        /// Порог переключения между базовым и высокостабильным вычислителем.
        /// Требование: 200,0.
        /// </summary>
        public const decimal HighDepthSwitchThreshold = 200.0m;

        /// <summary>
        /// Верхний предел "глубины" (iterations/transient), который движок принимает для
        /// гарантированно стабильных по числам вычислений и адекватной практической выполнимости.
        /// </summary>
        public const int MaxStableDepth = 2_000_000;

        private const double MinLogArgument = 1e-300;
        private const double LogisticStateClamp = 1e-15;

        public decimal AMin { get; set; } = 2.5m;
        public decimal AMax { get; set; } = 4.0m;
        public decimal BMin { get; set; } = 2.5m;
        public decimal BMax { get; set; } = 4.0m;
        public int Iterations { get; set; } = 120;
        public int TransientIterations { get; set; } = 60;
        public string Pattern { get; set; } = "AB";

        private static string NormalizePattern(string? pattern)
        {
            string raw = string.IsNullOrWhiteSpace(pattern) ? "AB" : pattern.Trim().ToUpperInvariant();
            string sanitized = new string(raw.Where(c => c == 'A' || c == 'B').ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "AB" : sanitized;
        }

        private static double Logistic(double x, double r) => r * x * (1d - x);

        private static int ClampDepth(int value) => Math.Clamp(value, 1, MaxStableDepth);

        private static double ComputeLyapunovExponentBasic(decimal a, decimal b, int transient, int iterations, string pattern)
        {
            double x = 0.5d;
            double aValue = (double)a;
            double bValue = (double)b;
            int total = Math.Max(1, transient + iterations);
            double sum = 0.0;

            for (int i = 0; i < total; i++)
            {
                char token = pattern[i % pattern.Length];
                double r = token == 'A' ? aValue : bValue;
                x = Logistic(x, r);

                if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 1e12)
                {
                    return double.NaN;
                }

                if (i >= transient)
                {
                    double derivative = Math.Abs(r * (1d - 2d * x));
                    derivative = Math.Max(derivative, 1e-12);
                    sum += Math.Log(derivative);
                }
            }

            return sum / Math.Max(1, iterations);
        }

        private static double ComputeLyapunovExponentHighDepth(decimal a, decimal b, int transient, int iterations, string pattern)
        {
            double x = 0.5d;
            double aValue = (double)a;
            double bValue = (double)b;
            int total = Math.Max(1, transient + iterations);
            double sum = 0.0d;
            double compensation = 0.0d; // Kahan compensation

            for (int i = 0; i < total; i++)
            {
                char token = pattern[i % pattern.Length];
                double r = token == 'A' ? aValue : bValue;
                x = Logistic(x, r);

                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    return double.NaN;
                }

                // Для глубинных серий держим x в рабочем интервале.
                x = Math.Clamp(x, LogisticStateClamp, 1d - LogisticStateClamp);

                if (i >= transient)
                {
                    // Устойчивый вариант ln|r(1-2x)| = ln|r| + ln|1-2x|
                    // Позволяет избежать переполнения/денормалов при прямом умножении.
                    double logAbsR = Math.Log(Math.Max(Math.Abs(r), MinLogArgument));
                    double logAbsTerm = Math.Log(Math.Max(Math.Abs(1d - (2d * x)), MinLogArgument));
                    double value = logAbsR + logAbsTerm;

                    // Kahan-summation для уменьшения накопления ошибок на большой глубине.
                    double corrected = value - compensation;
                    double next = sum + corrected;
                    compensation = (next - sum) - corrected;
                    sum = next;
                }
            }

            return sum / Math.Max(1, iterations);
        }

        private static double ComputeLyapunovExponent(decimal a, decimal b, int transient, int iterations, string pattern)
        {
            decimal depth = iterations;
            return depth <= HighDepthSwitchThreshold
                ? ComputeLyapunovExponentBasic(a, b, transient, iterations, pattern)
                : ComputeLyapunovExponentHighDepth(a, b, transient, iterations, pattern);
        }

        private static Color MapExponentToColor(double exponent)
        {
            if (double.IsNaN(exponent) || double.IsInfinity(exponent))
            {
                return Color.Black;
            }

            if (exponent < 0)
            {
                double t = Math.Max(0, Math.Min(1, (-exponent) / 2.0));
                int r = (int)(20 + 70 * t);
                int g = (int)(30 + 170 * t);
                int b = (int)(80 + 175 * t);
                return Color.FromArgb(r, g, b);
            }
            else
            {
                double t = Math.Max(0, Math.Min(1, exponent / 2.0));
                int r = (int)(120 + 135 * t);
                int g = (int)(50 + 90 * (1 - t));
                int b = (int)(30 + 40 * (1 - t));
                return Color.FromArgb(r, g, b);
            }
        }

        public byte[] RenderSingleTile(TileInfo tile, int canvasWidth, int canvasHeight, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            byte[] buffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                return buffer;
            }

            string pattern = NormalizePattern(Pattern);
            int iterations = ClampDepth(Iterations);
            int transient = Math.Clamp(TransientIterations, 0, MaxStableDepth);

            for (int y = 0; y < tile.Bounds.Height; y++)
            {
                int globalY = tile.Bounds.Top + y;
                decimal b = BMax - (BMax - BMin) * globalY / Math.Max(1, canvasHeight - 1);

                for (int x = 0; x < tile.Bounds.Width; x++)
                {
                    int globalX = tile.Bounds.Left + x;
                    decimal a = AMin + (AMax - AMin) * globalX / Math.Max(1, canvasWidth - 1);

                    double exponent = ComputeLyapunovExponent(a, b, transient, iterations, pattern);
                    Color color = MapExponentToColor(exponent);

                    int idx = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[idx] = color.B;
                    buffer[idx + 1] = color.G;
                    buffer[idx + 2] = color.R;
                    buffer[idx + 3] = 255;
                }
            }

            return buffer;
        }

        public Bitmap RenderToBitmap(int width, int height, int threads, Action<int>? progressCallback = null, CancellationToken cancellationToken = default)
        {
            if (width <= 0 || height <= 0)
            {
                return new Bitmap(1, 1);
            }

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * height];

            string pattern = NormalizePattern(Pattern);
            int iterations = ClampDepth(Iterations);
            int transient = Math.Clamp(TransientIterations, 0, MaxStableDepth);
            int doneRows = 0;
            object lockObj = new object();

            var po = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, threads),
                CancellationToken = cancellationToken
            };

            Parallel.For(0, height, po, y =>
            {
                decimal b = BMax - (BMax - BMin) * y / Math.Max(1, height - 1);
                int rowBase = y * data.Stride;

                for (int x = 0; x < width; x++)
                {
                    decimal a = AMin + (AMax - AMin) * x / Math.Max(1, width - 1);
                    double exponent = ComputeLyapunovExponent(a, b, transient, iterations, pattern);
                    Color color = MapExponentToColor(exponent);

                    int idx = rowBase + x * 3;
                    buffer[idx] = color.B;
                    buffer[idx + 1] = color.G;
                    buffer[idx + 2] = color.R;
                }

                if (progressCallback != null)
                {
                    lock (lockObj)
                    {
                        doneRows++;
                        progressCallback((int)Math.Round(doneRows * 100.0 / height));
                    }
                }
            });

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }
    }
}
