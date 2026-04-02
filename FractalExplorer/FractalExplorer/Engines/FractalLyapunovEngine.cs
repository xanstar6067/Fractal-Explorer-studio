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

        private static decimal Logistic(decimal x, decimal r) => r * x * (1m - x);

        private static double ComputeLyapunovExponent(decimal a, decimal b, int transient, int iterations, string pattern)
        {
            decimal x = 0.5m;
            int total = Math.Max(1, transient + iterations);
            double sum = 0.0;

            for (int i = 0; i < total; i++)
            {
                char token = pattern[i % pattern.Length];
                decimal r = token == 'A' ? a : b;
                x = Logistic(x, r);

                if (i >= transient)
                {
                    double derivative = Math.Abs((double)(r * (1m - 2m * x)));
                    derivative = Math.Max(derivative, 1e-12);
                    sum += Math.Log(derivative);
                }
            }

            return sum / Math.Max(1, iterations);
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
            int iterations = Math.Max(1, Iterations);
            int transient = Math.Max(0, TransientIterations);

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
            int iterations = Math.Max(1, Iterations);
            int transient = Math.Max(0, TransientIterations);
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
