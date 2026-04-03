namespace FractalExplorer.Engines
{
    public enum BuddhabrotRenderMode
    {
        Buddhabrot = 0,
        AntiBuddhabrot = 1
    }

    /// <summary>
    /// Рендер-движок Buddhabrot/Anti-Buddhabrot.
    /// Вместо прямой окраски пикселя по escape-time накапливает плотность посещений орбит.
    /// </summary>
    public sealed class FractalBuddhabrotEngine
    {
        public decimal CenterX { get; set; } = 0m;
        public decimal CenterY { get; set; } = 0m;
        public decimal Scale { get; set; } = 4.0m;

        public int MaxIterations { get; set; } = 500;
        public int SampleCount { get; set; } = 250_000;
        public decimal EscapeRadiusSquared { get; set; } = 16m;
        public BuddhabrotRenderMode RenderMode { get; set; } = BuddhabrotRenderMode.Buddhabrot;

        /// <summary>Ограничение области случайной выборки стартовых точек c.</summary>
        public decimal SampleMinRe { get; set; } = -2.0m;
        public decimal SampleMaxRe { get; set; } = 1.0m;
        public decimal SampleMinIm { get; set; } = -1.5m;
        public decimal SampleMaxIm { get; set; } = 1.5m;

        /// <summary>Функция отображения нормализованной плотности [0..1] в цвет.</summary>
        public Func<double, Color>? DensityPalette { get; set; }

        public void RenderToBuffer(byte[] buffer, int width, int height, int stride, int bytesPerPixel, CancellationToken token, Action<int>? reportProgress = null)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (bytesPerPixel < 4) throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));

            double[] density = new double[width * height];
            BuildDensityBuffer(density, width, height, token, reportProgress);
            ConvertDensityBufferToColor(buffer, density, width, height, stride, bytesPerPixel);
        }

        private void BuildDensityBuffer(double[] density, int width, int height, CancellationToken token, Action<int>? reportProgress)
        {
            double minRe = (double)SampleMinRe;
            double maxRe = (double)SampleMaxRe;
            double minIm = (double)SampleMinIm;
            double maxIm = (double)SampleMaxIm;
            double escapeSq = (double)EscapeRadiusSquared;

            double viewScale = (double)Scale;
            double viewLeft = (double)CenterX - viewScale * 0.5;
            double viewTop = (double)CenterY + viewScale * (height / (double)width) * 0.5;
            double viewScaleY = viewScale * (height / (double)width);

            int progressStep = Math.Max(1, SampleCount / 100);
            var orbit = new List<(double re, double im)>(Math.Max(8, MaxIterations));
            Random random = new(42);

            for (int s = 0; s < SampleCount; s++)
            {
                token.ThrowIfCancellationRequested();
                orbit.Clear();

                double cre = minRe + random.NextDouble() * (maxRe - minRe);
                double cim = minIm + random.NextDouble() * (maxIm - minIm);

                double zre = 0.0;
                double zim = 0.0;
                bool escaped = false;

                for (int i = 0; i < MaxIterations; i++)
                {
                    double nextRe = zre * zre - zim * zim + cre;
                    double nextIm = 2.0 * zre * zim + cim;
                    zre = nextRe;
                    zim = nextIm;

                    orbit.Add((zre, zim));
                    if (zre * zre + zim * zim > escapeSq)
                    {
                        escaped = true;
                        break;
                    }
                }

                bool takeOrbit = RenderMode == BuddhabrotRenderMode.Buddhabrot ? escaped : !escaped;
                if (takeOrbit)
                {
                    foreach ((double ore, double oim) in orbit)
                    {
                        int px = (int)((ore - viewLeft) / viewScale * width);
                        int py = (int)((viewTop - oim) / viewScaleY * height);

                        if ((uint)px < (uint)width && (uint)py < (uint)height)
                        {
                            density[py * width + px] += 1.0;
                        }
                    }
                }

                if (s % progressStep == 0)
                {
                    reportProgress?.Invoke((int)(s * 100.0 / Math.Max(1, SampleCount)));
                }
            }

            reportProgress?.Invoke(100);
        }

        /// <summary>
        /// Отдельный этап преобразования буфера плотности в итоговые пиксели.
        /// </summary>
        public void ConvertDensityBufferToColor(byte[] targetBuffer, double[] density, int width, int height, int stride, int bytesPerPixel)
        {
            double maxDensity = density.Max();
            if (maxDensity <= 0)
            {
                Array.Clear(targetBuffer, 0, targetBuffer.Length);
                return;
            }

            const double eps = 1e-12;
            double denom = Math.Log(1.0 + maxDensity);

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    double value = density[idx];
                    double normalized = value <= 0 ? 0 : Math.Log(1.0 + value) / Math.Max(eps, denom);

                    Color color = MapDensityToColor(normalized);

                    int o = row + x * bytesPerPixel;
                    targetBuffer[o + 0] = color.B;
                    targetBuffer[o + 1] = color.G;
                    targetBuffer[o + 2] = color.R;
                    targetBuffer[o + 3] = color.A;
                }
            }
        }

        private Color MapDensityToColor(double normalized)
        {
            normalized = Math.Clamp(normalized, 0.0, 1.0);
            if (DensityPalette != null)
            {
                return DensityPalette(normalized);
            }

            int c = (int)Math.Round(normalized * 255.0);
            return Color.FromArgb(255, c, c, c);
        }
    }
}
