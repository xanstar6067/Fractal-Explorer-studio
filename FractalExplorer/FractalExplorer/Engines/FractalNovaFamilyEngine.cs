using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Numerics; // Для Complex.Pow в double-версии
using System.Runtime.InteropServices;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Engines
{
    /// <summary>
    /// Абстрактный базовый класс для движков рендеринга фракталов семейства Нова (Nova).
    /// Инкапсулирует общую логику, управление параметрами (P, M, Z0) и поддерживает адаптивную точность вычислений.
    /// </summary>
    public abstract class FractalNovaFamilyEngine
    {
        #region Constants

        /// <summary>
        /// Порог масштабирования для переключения на высокоточные вычисления (<see langword="decimal"/>).
        /// </summary>
        private const decimal SCALE_THRESHOLD_FOR_DECIMAL = 4.0m / 2000000000.0m;

        #endregion

        #region Properties

        // --- Общие параметры рендеринга ---

        /// <summary>
        /// Получает или задает максимальное количество итераций.
        /// </summary>
        public int MaxIterations { get; set; } = 100;

        /// <summary>
        /// Получает или задает квадрат порога (bailout value). Для Nova обычно не используется для выхода, 
        /// но используется для проверки сходимости или расходимости.
        /// </summary>
        public decimal ThresholdSquared { get; set; } = 100.0m;

        /// <summary>
        /// Получает или задает координату X центра видимой области.
        /// </summary>
        public decimal CenterX { get; set; } = 0.0m;

        /// <summary>
        /// Получает или задает координату Y центра видимой области.
        /// </summary>
        public decimal CenterY { get; set; } = 0.0m;

        /// <summary>
        /// Получает или задает текущий масштаб рендеринга.
        /// </summary>
        public decimal Scale { get; set; } = 4.0m;

        // --- Специфичные параметры Nova ---

        /// <summary>
        /// Комплексная степень 'p' в формуле (z^p - 1).
        /// </summary>
        public ComplexDecimal P { get; set; } = new ComplexDecimal(3.0m, 0.0m);

        /// <summary>
        /// Начальное значение Z₀ для итераций (стартовая точка).
        /// </summary>
        public ComplexDecimal Z0 { get; set; } = new ComplexDecimal(1.0m, 0.0m);

        /// <summary>
        /// Параметр релаксации 'm' (Generalized Newton).
        /// </summary>
        public decimal M { get; set; } = 1.0m;

        /// <summary>
        /// Константа C для режима Жюлиа (игнорируется в режиме Мандельброта).
        /// </summary>
        public ComplexDecimal JuliaC { get; set; } = new ComplexDecimal(0, 0);

        // --- Параметры окрашивания ---

        public bool UseSmoothColoring { get; set; } = false;
        public Func<int, int, int, Color> Palette { get; set; }
        public Func<double, Color> SmoothPalette { get; set; }
        public int MaxColorIterations { get; set; } = 1000;

        #endregion

        #region Core Calculation Logic (Abstract Methods)

        /// <summary>
        /// Копирует специфичные для конкретного движка параметры из исходного экземпляра.
        /// </summary>
        public abstract void CopySpecificParametersFrom(FractalNovaFamilyEngine source);

        /// <summary>
        /// Вычисляет количество итераций (Decimal).
        /// </summary>
        public abstract int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c);

        /// <summary>
        /// Определяет начальные Z и C (Decimal) на основе координат пикселя.
        /// </summary>
        protected abstract void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC);

        /// <summary>
        /// Вычисляет количество итераций (Double).
        /// </summary>
        public abstract int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c);

        /// <summary>
        /// Определяет начальные Z и C (Double) на основе координат пикселя.
        /// </summary>
        protected abstract void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC);

        #endregion

        #region Private Smoothing Logic

        /// <summary>
        /// Вычисляет сглаженное значение итерации для Nova по формуле
        /// <c>iter + 1 - log(log(|z|²) / (2 * log(2))) / log(|P|)</c>,
        /// где <c>|P|</c> — модуль комплексной степени.
        /// При некорректных аргументах логарифма (≤ 0, NaN, Infinity) возвращает безопасный fallback: <paramref name="iter"/>.
        /// </summary>
        private double CalculateSmoothValue(int iter, ComplexDecimal finalZ)
        {
            if (iter >= MaxIterations) return iter;

            double magnitudeSquared = (double)finalZ.MagnitudeSquared;
            double pMagnitude = (double)P.Magnitude;
            double denominator = 2.0 * Math.Log(2.0);

            if (magnitudeSquared <= 0 || pMagnitude <= 0 || double.IsNaN(magnitudeSquared) || double.IsInfinity(magnitudeSquared) || double.IsNaN(pMagnitude) || double.IsInfinity(pMagnitude))
            {
                return iter;
            }

            double logZnSq = Math.Log(magnitudeSquared);
            double logP = Math.Log(pMagnitude);
            if (logZnSq <= 0 || logP == 0 || double.IsNaN(logZnSq) || double.IsInfinity(logZnSq) || double.IsNaN(logP) || double.IsInfinity(logP))
            {
                return iter;
            }

            double innerArgument = logZnSq / denominator;
            if (innerArgument <= 0 || double.IsNaN(innerArgument) || double.IsInfinity(innerArgument))
            {
                return iter;
            }

            double nu = Math.Log(innerArgument) / logP;
            return double.IsNaN(nu) || double.IsInfinity(nu) ? iter : iter + 1 - nu;
        }

        /// <summary>
        /// Вычисляет сглаженное значение итерации для Nova по формуле
        /// <c>iter + 1 - log(log(|z|²) / (2 * log(2))) / log(|P|)</c>,
        /// где <c>|P|</c> — модуль комплексной степени.
        /// При некорректных аргументах логарифма (≤ 0, NaN, Infinity) возвращает безопасный fallback: <paramref name="iter"/>.
        /// </summary>
        private double CalculateSmoothValueDouble(int iter, ComplexDouble finalZ)
        {
            if (iter >= MaxIterations)
            {
                return iter;
            }

            double magnitudeSquared = finalZ.MagnitudeSquared;
            double pMagnitude = (double)P.Magnitude;
            double denominator = 2.0 * Math.Log(2.0);

            if (magnitudeSquared <= 0 || pMagnitude <= 0 || double.IsNaN(magnitudeSquared) || double.IsInfinity(magnitudeSquared) || double.IsNaN(pMagnitude) || double.IsInfinity(pMagnitude))
            {
                return iter;
            }

            double logZnSq = Math.Log(magnitudeSquared);
            double logP = Math.Log(pMagnitude);
            if (logZnSq <= 0 || logP == 0 || double.IsNaN(logZnSq) || double.IsInfinity(logZnSq) || double.IsNaN(logP) || double.IsInfinity(logP))
            {
                return iter;
            }

            double innerArgument = logZnSq / denominator;
            if (innerArgument <= 0 || double.IsNaN(innerArgument) || double.IsInfinity(innerArgument))
            {
                return iter;
            }

            double nu = Math.Log(innerArgument) / logP;
            return double.IsNaN(nu) || double.IsInfinity(nu) ? iter : iter + 1 - nu;
        }

        #endregion

        #region Public Rendering Methods

        public void CopyParametersFrom(FractalNovaFamilyEngine source)
        {
            this.MaxIterations = source.MaxIterations;
            this.ThresholdSquared = source.ThresholdSquared;
            this.CenterX = source.CenterX;
            this.CenterY = source.CenterY;
            this.Scale = source.Scale;
            this.UseSmoothColoring = source.UseSmoothColoring;
            this.Palette = source.Palette;
            this.SmoothPalette = source.SmoothPalette;
            this.MaxColorIterations = source.MaxColorIterations;

            // Этот метод уже был в твоем коде, он скопирует специфичные параметры (P, Z0, M)
            this.CopySpecificParametersFrom(source);
        }

        public byte[] RenderSingleTile(TileInfo tile, int canvasWidth, int canvasHeight, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            byte[] buffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];
            if (canvasWidth <= 0 || canvasHeight <= 0) return buffer;

            if (Scale < SCALE_THRESHOLD_FOR_DECIMAL)
                RenderTileDecimal(buffer, tile, canvasWidth, canvasHeight, bytesPerPixel);
            else
                RenderTileDouble(buffer, tile, canvasWidth, canvasHeight, bytesPerPixel);

            return buffer;
        }

        public byte[] RenderSingleTileSSAA(TileInfo tile, int canvasWidth, int canvasHeight, int supersamplingFactor, int numThreads, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            numThreads = Math.Max(1, numThreads);
            if (supersamplingFactor <= 1)
                return RenderSingleTile(tile, canvasWidth, canvasHeight, out bytesPerPixel);

            byte[] finalTileBuffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];
            if (canvasWidth <= 0 || canvasHeight <= 0) return finalTileBuffer;

            if (Scale < SCALE_THRESHOLD_FOR_DECIMAL)
                RenderTileSSAA_Decimal(finalTileBuffer, tile, canvasWidth, canvasHeight, supersamplingFactor, numThreads, bytesPerPixel);
            else
                RenderTileSSAA_Double(finalTileBuffer, tile, canvasWidth, canvasHeight, supersamplingFactor, numThreads, bytesPerPixel);

            return finalTileBuffer;
        }

        public Bitmap RenderToBitmap(int renderWidth, int renderHeight, int numThreads, Action<int> reportProgressCallback, CancellationToken cancellationToken = default)
        {
            if (renderWidth <= 0 || renderHeight <= 0) return new Bitmap(1, 1);

            Bitmap bmp = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(bmpData.Stride) * renderHeight];
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };
            long done = 0;

            try
            {
                if (Scale < SCALE_THRESHOLD_FOR_DECIMAL)
                {
                    decimal halfWidthPixels = renderWidth / 2.0m;
                    decimal halfHeightPixels = renderHeight / 2.0m;
                    decimal unitsPerPixel = Scale / renderWidth;

                    Parallel.For(0, renderHeight, po, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int rowOffset = y * bmpData.Stride;
                        for (int x = 0; x < renderWidth; x++)
                        {
                            decimal re = CenterX + (x - halfWidthPixels) * unitsPerPixel;
                            decimal im = CenterY - (y - halfHeightPixels) * unitsPerPixel;

                            GetCalculationParameters(re, im, out ComplexDecimal z, out ComplexDecimal c);
                            int iter = CalculateIterations(ref z, c);

                            Color pixelColor = UseSmoothColoring && SmoothPalette != null
                                ? SmoothPalette(CalculateSmoothValue(iter, z))
                                : Palette(iter, MaxIterations, MaxColorIterations);

                            int index = rowOffset + x * 3;
                            if (index + 2 < buffer.Length)
                            {
                                buffer[index] = pixelColor.B;
                                buffer[index + 1] = pixelColor.G;
                                buffer[index + 2] = pixelColor.R;
                            }
                        }
                        long currentDone = Interlocked.Increment(ref done);
                        if (renderHeight > 0) reportProgressCallback((int)(100.0 * currentDone / renderHeight));
                    });
                }
                else
                {
                    double centerX_d = (double)CenterX;
                    double centerY_d = (double)CenterY;
                    double unitsPerPixel_d = (double)Scale / renderWidth;
                    double halfWidthPixels_d = renderWidth / 2.0;
                    double halfHeightPixels_d = renderHeight / 2.0;

                    Parallel.For(0, renderHeight, po, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int rowOffset = y * bmpData.Stride;
                        for (int x = 0; x < renderWidth; x++)
                        {
                            double re = centerX_d + (x - halfWidthPixels_d) * unitsPerPixel_d;
                            double im = centerY_d - (y - halfHeightPixels_d) * unitsPerPixel_d;

                            GetCalculationParametersDouble(re, im, out ComplexDouble z, out ComplexDouble c);
                            int iter = CalculateIterationsDouble(ref z, c);

                            Color pixelColor = UseSmoothColoring && SmoothPalette != null
                                ? SmoothPalette(CalculateSmoothValueDouble(iter, z))
                                : Palette(iter, MaxIterations, MaxColorIterations);

                            int index = rowOffset + x * 3;
                            if (index + 2 < buffer.Length)
                            {
                                buffer[index] = pixelColor.B;
                                buffer[index + 1] = pixelColor.G;
                                buffer[index + 2] = pixelColor.R;
                            }
                        }
                        long currentDone = Interlocked.Increment(ref done);
                        if (renderHeight > 0) reportProgressCallback((int)(100.0 * currentDone / renderHeight));
                    });
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                bmp.UnlockBits(bmpData);
            }
            return bmp;
        }

        public Task<Bitmap> RenderToBitmapSSAA(int finalWidth, int finalHeight, int numThreads, Action<int> reportProgressCallback, int supersamplingFactor, CancellationToken cancellationToken = default)
        {
            if (supersamplingFactor <= 1)
            {
                return Task.Run(() => RenderToBitmap(finalWidth, finalHeight, numThreads, reportProgressCallback, cancellationToken), cancellationToken);
            }

            return Task.Run(() =>
            {
                int highResWidth = finalWidth * supersamplingFactor;
                int highResHeight = finalHeight * supersamplingFactor;
                Action<int> highResProgress = p => reportProgressCallback((int)(p * 0.98));
                Bitmap highResBitmap = RenderToBitmap(highResWidth, highResHeight, numThreads, highResProgress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                reportProgressCallback(98);

                Bitmap finalBitmap = new Bitmap(highResBitmap, finalWidth, finalHeight);
                highResBitmap.Dispose();
                reportProgressCallback(100);

                return finalBitmap;
            }, cancellationToken);
        }

        #endregion

        #region Private Rendering Helpers

        private void RenderTileDecimal(byte[] buffer, TileInfo tile, int canvasWidth, int canvasHeight, int bytesPerPixel)
        {
            decimal halfWidthPixels = canvasWidth / 2.0m;
            decimal halfHeightPixels = canvasHeight / 2.0m;
            decimal unitsPerPixel = Scale / canvasWidth;

            for (int y = 0; y < tile.Bounds.Height; y++)
            {
                int canvasY = tile.Bounds.Y + y;
                if (canvasY >= canvasHeight) continue;

                for (int x = 0; x < tile.Bounds.Width; x++)
                {
                    int canvasX = tile.Bounds.X + x;
                    if (canvasX >= canvasWidth) continue;

                    decimal re = CenterX + (canvasX - halfWidthPixels) * unitsPerPixel;
                    decimal im = CenterY - (canvasY - halfHeightPixels) * unitsPerPixel;

                    GetCalculationParameters(re, im, out ComplexDecimal z, out ComplexDecimal c);
                    int iter = CalculateIterations(ref z, c);

                    Color pixelColor = UseSmoothColoring && SmoothPalette != null
                        ? SmoothPalette(CalculateSmoothValue(iter, z))
                        : Palette(iter, MaxIterations, MaxColorIterations);

                    int bufferIndex = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[bufferIndex] = pixelColor.B;
                    buffer[bufferIndex + 1] = pixelColor.G;
                    buffer[bufferIndex + 2] = pixelColor.R;
                    buffer[bufferIndex + 3] = 255;
                }
            }
        }

        private void RenderTileDouble(byte[] buffer, TileInfo tile, int canvasWidth, int canvasHeight, int bytesPerPixel)
        {
            double centerX_d = (double)CenterX;
            double centerY_d = (double)CenterY;
            double scale_d = (double)Scale;
            double halfWidthPixels = canvasWidth / 2.0;
            double halfHeightPixels = canvasHeight / 2.0;
            double unitsPerPixel = scale_d / canvasWidth;

            for (int y = 0; y < tile.Bounds.Height; y++)
            {
                int canvasY = tile.Bounds.Y + y;
                if (canvasY >= canvasHeight) continue;

                for (int x = 0; x < tile.Bounds.Width; x++)
                {
                    int canvasX = tile.Bounds.X + x;
                    if (canvasX >= canvasWidth) continue;

                    double re = centerX_d + (canvasX - halfWidthPixels) * unitsPerPixel;
                    double im = centerY_d - (canvasY - halfHeightPixels) * unitsPerPixel;

                    GetCalculationParametersDouble(re, im, out ComplexDouble z, out ComplexDouble c);
                    int iter = CalculateIterationsDouble(ref z, c);

                    Color pixelColor = UseSmoothColoring && SmoothPalette != null
                        ? SmoothPalette(CalculateSmoothValueDouble(iter, z))
                        : Palette(iter, MaxIterations, MaxColorIterations);

                    int bufferIndex = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[bufferIndex] = pixelColor.B;
                    buffer[bufferIndex + 1] = pixelColor.G;
                    buffer[bufferIndex + 2] = pixelColor.R;
                    buffer[bufferIndex + 3] = 255;
                }
            }
        }

        private void RenderTileSSAA_Decimal(byte[] finalTileBuffer, TileInfo tile, int canvasWidth, int canvasHeight, int supersamplingFactor, int numThreads, int bytesPerPixel)
        {
            int highResTileWidth = tile.Bounds.Width * supersamplingFactor;
            int highResTileHeight = tile.Bounds.Height * supersamplingFactor;
            Color[,] highResColorBuffer = new Color[highResTileWidth, highResTileHeight];

            long highResCanvasWidth = (long)canvasWidth * supersamplingFactor;
            decimal unitsPerSubPixel = Scale / highResCanvasWidth;
            decimal highResHalfWidthPixels = highResCanvasWidth / 2.0m;
            decimal highResHalfHeightPixels = (long)canvasHeight * supersamplingFactor / 2.0m;

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads };

            Parallel.For(0, highResTileHeight, po, y =>
            {
                for (int x = 0; x < highResTileWidth; x++)
                {
                    long globalHighResX = (long)tile.Bounds.X * supersamplingFactor + x;
                    long globalHighResY = (long)tile.Bounds.Y * supersamplingFactor + y;

                    decimal re = CenterX + (globalHighResX - highResHalfWidthPixels) * unitsPerSubPixel;
                    decimal im = CenterY - (globalHighResY - highResHalfHeightPixels) * unitsPerSubPixel;

                    GetCalculationParameters(re, im, out ComplexDecimal z, out ComplexDecimal c);
                    int iter = CalculateIterations(ref z, c);

                    highResColorBuffer[x, y] = UseSmoothColoring && SmoothPalette != null
                        ? SmoothPalette(CalculateSmoothValue(iter, z))
                        : Palette(iter, MaxIterations, MaxColorIterations);
                }
            });

            int sampleCount = supersamplingFactor * supersamplingFactor;
            for (int finalY = 0; finalY < tile.Bounds.Height; finalY++)
            {
                for (int finalX = 0; finalX < tile.Bounds.Width; finalX++)
                {
                    long totalR = 0, totalG = 0, totalB = 0;
                    int startSubX = finalX * supersamplingFactor;
                    int startSubY = finalY * supersamplingFactor;
                    for (int subY = 0; subY < supersamplingFactor; subY++)
                    {
                        for (int subX = 0; subX < supersamplingFactor; subX++)
                        {
                            Color pixelColor = highResColorBuffer[startSubX + subX, startSubY + subY];
                            totalR += pixelColor.R;
                            totalG += pixelColor.G;
                            totalB += pixelColor.B;
                        }
                    }
                    int bufferIndex = (finalY * tile.Bounds.Width + finalX) * bytesPerPixel;
                    finalTileBuffer[bufferIndex] = (byte)(totalB / sampleCount);
                    finalTileBuffer[bufferIndex + 1] = (byte)(totalG / sampleCount);
                    finalTileBuffer[bufferIndex + 2] = (byte)(totalR / sampleCount);
                    finalTileBuffer[bufferIndex + 3] = 255;
                }
            }
        }

        private void RenderTileSSAA_Double(byte[] finalTileBuffer, TileInfo tile, int canvasWidth, int canvasHeight, int supersamplingFactor, int numThreads, int bytesPerPixel)
        {
            int highResTileWidth = tile.Bounds.Width * supersamplingFactor;
            int highResTileHeight = tile.Bounds.Height * supersamplingFactor;
            Color[,] highResColorBuffer = new Color[highResTileWidth, highResTileHeight];

            long highResCanvasWidth = (long)canvasWidth * supersamplingFactor;
            double unitsPerSubPixel = (double)Scale / highResCanvasWidth;
            double highResHalfWidthPixels = highResCanvasWidth / 2.0;
            double highResHalfHeightPixels = (long)canvasHeight * supersamplingFactor / 2.0;
            double centerX_d = (double)CenterX;
            double centerY_d = (double)CenterY;

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads };

            Parallel.For(0, highResTileHeight, po, y =>
            {
                for (int x = 0; x < highResTileWidth; x++)
                {
                    long globalHighResX = (long)tile.Bounds.X * supersamplingFactor + x;
                    long globalHighResY = (long)tile.Bounds.Y * supersamplingFactor + y;

                    double re = centerX_d + (globalHighResX - highResHalfWidthPixels) * unitsPerSubPixel;
                    double im = centerY_d - (globalHighResY - highResHalfHeightPixels) * unitsPerSubPixel;

                    GetCalculationParametersDouble(re, im, out ComplexDouble z, out ComplexDouble c);
                    int iter = CalculateIterationsDouble(ref z, c);

                    highResColorBuffer[x, y] = UseSmoothColoring && SmoothPalette != null
                        ? SmoothPalette(CalculateSmoothValueDouble(iter, z))
                        : Palette(iter, MaxIterations, MaxColorIterations);
                }
            });

            int sampleCount = supersamplingFactor * supersamplingFactor;
            for (int finalY = 0; finalY < tile.Bounds.Height; finalY++)
            {
                for (int finalX = 0; finalX < tile.Bounds.Width; finalX++)
                {
                    long totalR = 0, totalG = 0, totalB = 0;
                    int startSubX = finalX * supersamplingFactor;
                    int startSubY = finalY * supersamplingFactor;
                    for (int subY = 0; subY < supersamplingFactor; subY++)
                    {
                        for (int subX = 0; subX < supersamplingFactor; subX++)
                        {
                            Color pixelColor = highResColorBuffer[startSubX + subX, startSubY + subY];
                            totalR += pixelColor.R;
                            totalG += pixelColor.G;
                            totalB += pixelColor.B;
                        }
                    }
                    int bufferIndex = (finalY * tile.Bounds.Width + finalX) * bytesPerPixel;
                    finalTileBuffer[bufferIndex] = (byte)(totalB / sampleCount);
                    finalTileBuffer[bufferIndex + 1] = (byte)(totalG / sampleCount);
                    finalTileBuffer[bufferIndex + 2] = (byte)(totalR / sampleCount);
                    finalTileBuffer[bufferIndex + 3] = 255;
                }
            }
        }

        #endregion
    }

    #region Concrete Engines Implementations

    /// <summary>
    /// Реализует движок для рендеринга Nova Mandelbrot.
    /// C изменяется по координатам пикселя, Z начинается с Z0.
    /// Формула: z_next = z - m * (z^p - 1) / (p * z^(p-1)) + c
    /// </summary>
    public class NovaMandelbrotEngine : FractalNovaFamilyEngine
    {
        public override void CopySpecificParametersFrom(FractalNovaFamilyEngine source)
        {
            this.P = source.P;
            this.M = source.M;
            this.Z0 = source.Z0;
            this.JuliaC = source.JuliaC;
        }

        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            // Мандельброт: C зависит от координат, Z фиксировано (Z0)
            initialZ = this.Z0;
            constantC = new ComplexDecimal(re, im);
        }

        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            ComplexDecimal p = this.P;
            decimal m = this.M;

            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                if (z.MagnitudeSquared < 1e-28m) break; // Защита от деления на ноль

                try
                {
                    ComplexDecimal z_pow_p = ComplexDecimal.Pow(z, p);
                    // Производная: p * z^(p-1). Можно оптимизировать как p * z^p / z, но прямой расчет точнее.
                    ComplexDecimal z_pow_p_minus_1 = ComplexDecimal.Pow(z, new ComplexDecimal(p.Real - 1, p.Imaginary));

                    ComplexDecimal numerator = m * (z_pow_p - 1);
                    ComplexDecimal denominator = p * z_pow_p_minus_1;

                    if (denominator.MagnitudeSquared < 1e-28m) break;

                    z = z - numerator / denominator + c;
                    iter++;
                }
                catch (OverflowException)
                {
                    iter = MaxIterations;
                    break;
                }
            }
            return iter;
        }

        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = new ComplexDouble((double)Z0.Real, (double)Z0.Imaginary);
            constantC = new ComplexDouble(re, im);
        }

        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            Complex p_complex = new Complex((double)P.Real, (double)P.Imaginary);
            Complex one = Complex.One;
            double m_val = (double)M;

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                if (z.MagnitudeSquared < 1e-12) break;

                Complex z_val = new Complex(z.Real, z.Imaginary);
                Complex z_pow_p = Complex.Pow(z_val, p_complex);
                Complex z_pow_p_minus_1 = Complex.Pow(z_val, p_complex - one);

                Complex numerator = m_val * (z_pow_p - one);
                Complex denominator = p_complex * z_pow_p_minus_1;

                if (denominator.Magnitude < 1e-12) break;

                Complex result = z_val - numerator / denominator + new Complex(c.Real, c.Imaginary);
                z = new ComplexDouble(result.Real, result.Imaginary);
                iter++;
            }
            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга Nova Julia.
    /// Z изменяется по координатам пикселя, C фиксировано (JuliaC).
    /// Формула аналогична Mandelbrot версии.
    /// </summary>
    public class NovaJuliaEngine : FractalNovaFamilyEngine
    {
        public override void CopySpecificParametersFrom(FractalNovaFamilyEngine source)
        {
            this.P = source.P;
            this.M = source.M;
            this.Z0 = source.Z0;
            this.JuliaC = source.JuliaC;
        }

        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            // Жюлиа: Z зависит от координат, C фиксировано
            initialZ = new ComplexDecimal(re, im);
            constantC = this.JuliaC;
        }

        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            // Логика формулы идентична Nova Mandelbrot
            int iter = 0;
            ComplexDecimal p = this.P;
            decimal m = this.M;

            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                if (z.MagnitudeSquared < 1e-28m) break;

                try
                {
                    ComplexDecimal z_pow_p = ComplexDecimal.Pow(z, p);
                    ComplexDecimal z_pow_p_minus_1 = ComplexDecimal.Pow(z, new ComplexDecimal(p.Real - 1, p.Imaginary));

                    ComplexDecimal numerator = m * (z_pow_p - 1);
                    ComplexDecimal denominator = p * z_pow_p_minus_1;

                    if (denominator.MagnitudeSquared < 1e-28m) break;

                    z = z - numerator / denominator + c;
                    iter++;
                }
                catch (OverflowException)
                {
                    iter = MaxIterations;
                    break;
                }
            }
            return iter;
        }

        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = new ComplexDouble(re, im);
            constantC = new ComplexDouble((double)JuliaC.Real, (double)JuliaC.Imaginary);
        }

        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            Complex p_complex = new Complex((double)P.Real, (double)P.Imaginary);
            Complex one = Complex.One;
            double m_val = (double)M;

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                if (z.MagnitudeSquared < 1e-12) break;

                Complex z_val = new Complex(z.Real, z.Imaginary);
                Complex z_pow_p = Complex.Pow(z_val, p_complex);
                Complex z_pow_p_minus_1 = Complex.Pow(z_val, p_complex - one);

                Complex numerator = m_val * (z_pow_p - one);
                Complex denominator = p_complex * z_pow_p_minus_1;

                if (denominator.Magnitude < 1e-12) break;

                Complex result = z_val - numerator / denominator + new Complex(c.Real, c.Imaginary);
                z = new ComplexDouble(result.Real, result.Imaginary);
                iter++;
            }
            return iter;
        }
    }

    #endregion
}
