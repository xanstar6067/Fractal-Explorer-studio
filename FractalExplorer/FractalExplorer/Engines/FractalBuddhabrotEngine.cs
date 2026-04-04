namespace FractalExplorer.Engines
{
    public enum BuddhabrotRenderMode
    {
        Buddhabrot = 0,
        AntiBuddhabrot = 1,
        SymmetricBuddhabrot = 2
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
        /// <summary>
        /// Количество начальных итераций орбиты, которые пропускаются при накоплении плотности.
        /// Уменьшает квадратный артефакт от равномерной выборки c в первых шагах.
        /// </summary>
        public int OrbitWarmupIterations { get; set; } = 2;
        /// <summary>
        /// Количество рабочих потоков. 0 или меньше = Auto (все логические ядра).
        /// </summary>
        public int ThreadCount { get; set; } = 0;
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

            int[] density = new int[width * height];
            BuildDensityBuffer(density, width, height, token, reportProgress);
            ConvertDensityBufferToColor(buffer, density, width, height, stride, bytesPerPixel);
        }

        private void BuildDensityBuffer(int[] density, int width, int height, CancellationToken token, Action<int>? reportProgress)
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
            int resolvedThreadCount = ThreadCount <= 0 ? Environment.ProcessorCount : ThreadCount;
            int orbitWarmupIterations = Math.Max(0, OrbitWarmupIterations);
            int completedSamples = 0;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, resolvedThreadCount),
                CancellationToken = token
            };

            Parallel.For(0, SampleCount, options,
                () => (random: new Random(42 + Environment.CurrentManagedThreadId * 7919), orbit: new List<(double re, double im)>(Math.Max(8, MaxIterations))),
                (s, _, local) =>
                {
                    token.ThrowIfCancellationRequested();
                    local.orbit.Clear();

                    double cre = minRe + local.random.NextDouble() * (maxRe - minRe);
                    double cim = minIm + local.random.NextDouble() * (maxIm - minIm);

                    double zre = 0.0;
                    double zim = 0.0;
                    bool escaped = false;

                    for (int i = 0; i < MaxIterations; i++)
                    {
                        double nextRe = zre * zre - zim * zim + cre;
                        double nextIm = 2.0 * zre * zim + cim;
                        zre = nextRe;
                        zim = nextIm;

                        if (i >= orbitWarmupIterations)
                        {
                            local.orbit.Add((zre, zim));
                        }

                        if (zre * zre + zim * zim > escapeSq)
                        {
                            escaped = true;
                            break;
                        }
                    }

                    bool takeOrbit = RenderMode switch
                    {
                        BuddhabrotRenderMode.Buddhabrot => escaped,
                        BuddhabrotRenderMode.AntiBuddhabrot => !escaped,
                        BuddhabrotRenderMode.SymmetricBuddhabrot => escaped,
                        _ => escaped
                    };

                    if (takeOrbit)
                    {
                        bool mirrorAcrossRealAxis = RenderMode == BuddhabrotRenderMode.SymmetricBuddhabrot;
                        foreach ((double ore, double oim) in local.orbit)
                        {
                            int px = (int)((ore - viewLeft) / viewScale * width);
                            int py = (int)((viewTop - oim) / viewScaleY * height);

                            if ((uint)px < (uint)width && (uint)py < (uint)height)
                            {
                                Interlocked.Increment(ref density[py * width + px]);
                            }

                            if (mirrorAcrossRealAxis)
                            {
                                int mirroredPy = (int)((viewTop + oim) / viewScaleY * height);
                                if ((uint)px < (uint)width && (uint)mirroredPy < (uint)height)
                                {
                                    Interlocked.Increment(ref density[mirroredPy * width + px]);
                                }
                            }
                        }
                    }

                    int processed = Interlocked.Increment(ref completedSamples);
                    if (processed % progressStep == 0 || processed == SampleCount)
                    {
                        reportProgress?.Invoke((int)(processed * 100.0 / Math.Max(1, SampleCount)));
                    }

                    return local;
                },
                _ => { });

            reportProgress?.Invoke(100);
        }

        /// <summary>
        /// Отдельный этап преобразования буфера плотности в итоговые пиксели.
        /// </summary>
        public void ConvertDensityBufferToColor(byte[] targetBuffer, int[] density, int width, int height, int stride, int bytesPerPixel)
        {
            int maxDensity = density.Max();
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
                    int value = density[idx];
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
