using System.Collections.Concurrent;

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
        private const int DefaultBatchSize = 50_000;
        private readonly ConcurrentBag<List<(double re, double im)>> _orbitBufferPool = new();
        private int[]? _densityBuffer;
        private int _densityWidth;
        private int _densityHeight;
        private int _processedSamples;

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

        public int ProcessedSamples => Volatile.Read(ref _processedSamples);

        public int[] InitializeOrResetDensityBuffer(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

            if (_densityBuffer == null || _densityWidth != width || _densityHeight != height)
            {
                _densityBuffer = new int[width * height];
                _densityWidth = width;
                _densityHeight = height;
            }
            else
            {
                Array.Clear(_densityBuffer, 0, _densityBuffer.Length);
            }

            Volatile.Write(ref _processedSamples, 0);
            return _densityBuffer;
        }

        public int AccumulateSamplesBatch(CancellationToken token, int batchSize = DefaultBatchSize)
        {
            EnsureDensityBufferInitialized();
            int[] density = _densityBuffer!;

            int startSample = ProcessedSamples;
            if (startSample >= SampleCount)
            {
                return 0;
            }

            int safeBatchSize = Math.Max(1, batchSize);
            int endSample = Math.Min(SampleCount, startSample + safeBatchSize);
            BuildDensityBufferRange(density, _densityWidth, _densityHeight, startSample, endSample, token);
            Volatile.Write(ref _processedSamples, endSample);
            return endSample - startSample;
        }

        public void ConvertCurrentDensityToColor(byte[] targetBuffer, int width, int height, int stride, int bytesPerPixel)
        {
            EnsureDensityBufferInitialized();
            if (width != _densityWidth || height != _densityHeight)
            {
                throw new ArgumentException("Размеры целевого буфера должны совпадать с размерами density-буфера.");
            }

            ConvertDensityBufferToColor(targetBuffer, _densityBuffer!, width, height, stride, bytesPerPixel);
        }

        public void RenderToBuffer(byte[] buffer, int width, int height, int stride, int bytesPerPixel, CancellationToken token, Action<int>? reportProgress = null)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (bytesPerPixel < 4) throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));

            InitializeOrResetDensityBuffer(width, height);
            int progressStep = Math.Max(1, SampleCount / 100);
            while (ProcessedSamples < SampleCount)
            {
                token.ThrowIfCancellationRequested();
                AccumulateSamplesBatch(token, DefaultBatchSize);
                int processed = ProcessedSamples;
                if (processed % progressStep == 0 || processed >= SampleCount)
                {
                    reportProgress?.Invoke((int)(processed * 100.0 / Math.Max(1, SampleCount)));
                }
            }

            ConvertCurrentDensityToColor(buffer, width, height, stride, bytesPerPixel);
            reportProgress?.Invoke(100);
        }

        private void BuildDensityBufferRange(int[] density, int width, int height, int startSample, int endSample, CancellationToken token)
        {
            double minRe = (double)SampleMinRe;
            double maxRe = (double)SampleMaxRe;
            double minIm = (double)SampleMinIm;
            double maxIm = (double)SampleMaxIm;
            double escapeSq = (double)EscapeRadiusSquared;

            double viewScale = (double)Scale;
            double viewScaleY = viewScale * (height / (double)width);

            int resolvedThreadCount = ThreadCount <= 0 ? Environment.ProcessorCount : ThreadCount;
            int orbitWarmupIterations = Math.Max(0, OrbitWarmupIterations);

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, resolvedThreadCount),
                CancellationToken = token
            };

            Parallel.For(startSample, endSample, options,
                () => RentOrbitBuffer(Math.Max(8, MaxIterations)),
                (s, _, local) =>
                {
                    token.ThrowIfCancellationRequested();
                    local.Clear();

                    ulong rngState = MixSeed((ulong)s + 0x9E3779B97F4A7C15UL);
                    double cre = minRe + NextUnitDouble(ref rngState) * (maxRe - minRe);
                    double cim = minIm + NextUnitDouble(ref rngState) * (maxIm - minIm);

                    double zre = 0.0;
                    double zim = 0.0;
                    bool escaped = false;

                    for (int i = 0; i < MaxIterations; i++)
                    {
                        if ((i & 63) == 0)
                        {
                            token.ThrowIfCancellationRequested();
                        }

                        double nextRe = zre * zre - zim * zim + cre;
                        double nextIm = 2.0 * zre * zim + cim;
                        zre = nextRe;
                        zim = nextIm;

                        if (i >= orbitWarmupIterations)
                        {
                            local.Add((zre, zim));
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
                        token.ThrowIfCancellationRequested();
                        bool mirrorAcrossRealAxis = RenderMode == BuddhabrotRenderMode.SymmetricBuddhabrot;
                        foreach ((double ore, double oim) in local)
                        {
                            // Поворот итогового изображения на 90° по часовой стрелке:
                            // экранная X-ось теперь соответствует мнимой оси,
                            // экранная Y-ось — действительной.
                            int px = (int)(((oim - (double)CenterY) / viewScale + 0.5) * width);
                            int py = (int)(((ore - (double)CenterX) / viewScaleY + 0.5) * height);

                            if ((uint)px < (uint)width && (uint)py < (uint)height)
                            {
                                Interlocked.Increment(ref density[py * width + px]);
                            }

                            if (mirrorAcrossRealAxis)
                            {
                                int mirroredPx = (int)((((-oim) - (double)CenterY) / viewScale + 0.5) * width);
                                if ((uint)mirroredPx < (uint)width && (uint)py < (uint)height)
                                {
                                    Interlocked.Increment(ref density[py * width + mirroredPx]);
                                }
                            }
                        }
                    }

                    return local;
                },
                ReturnOrbitBuffer);
        }

        private List<(double re, double im)> RentOrbitBuffer(int minCapacity)
        {
            if (!_orbitBufferPool.TryTake(out List<(double re, double im)>? local))
            {
                return new List<(double re, double im)>(minCapacity);
            }

            if (local.Capacity < minCapacity)
            {
                local.Capacity = minCapacity;
            }

            return local;
        }

        private void ReturnOrbitBuffer(List<(double re, double im)> local)
        {
            local.Clear();
            _orbitBufferPool.Add(local);
        }

        private void EnsureDensityBufferInitialized()
        {
            if (_densityBuffer == null || _densityWidth <= 0 || _densityHeight <= 0)
            {
                throw new InvalidOperationException("Density-буфер не инициализирован. Вызовите InitializeOrResetDensityBuffer().");
            }
        }

        private static ulong MixSeed(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static double NextUnitDouble(ref ulong state)
        {
            state = MixSeed(state);
            return (state >> 11) * (1.0 / (1UL << 53));
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
