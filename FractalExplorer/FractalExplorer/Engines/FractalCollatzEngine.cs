using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Engines
{
    /// <summary>
    /// Перечисление для различных вариаций итерационной формулы фрактала Коллатца.
    /// </summary>
    public enum CollatzVariation
    {
        /// <summary>
        /// Стандартная формула: 0.25 * (2 + 7z - (2 + 5z) * cos(pi*z))
        /// </summary>
        Standard,

        /// <summary>
        /// Вариация с использованием синуса: 0.25 * (2 + 7z - (2 + 5z) * sin(pi*z))
        /// </summary>
        SineVariation,

        /// <summary>
        /// Обобщенная формула для "px + 1": 0.5 * ((p-1)z + 1 - ((p-1)z - 1) * cos(pi*z))
        /// </summary>
        GeneralizedP
    }

    /// <summary>
    /// Реализует движок для рендеринга фрактала Коллатца с поддержкой нескольких итерационных формул.
    /// </summary>
    public class FractalCollatzEngine
    {
        #region Constants
        /// <summary>
        /// Порог масштабирования для переключения на высокоточные вычисления (<see langword="decimal"/>).
        /// </summary>
        private const decimal SCALE_THRESHOLD_FOR_DECIMAL = 4.0m / 2000000000.0m;
        #endregion

        #region Properties
        public int MaxIterations { get; set; }
        public decimal ThresholdSquared { get; set; }
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Scale { get; set; }
        public bool UseSmoothColoring { get; set; } = false;
        public Func<int, int, int, Color> Palette { get; set; }
        public Func<double, Color> SmoothPalette { get; set; }
        public int MaxColorIterations { get; set; } = 1000;
        #endregion

        #region Variation Properties
        /// <summary>
        /// Выбранная вариация итерационной формулы Коллатца.
        /// </summary>
        public CollatzVariation Variation { get; set; } = CollatzVariation.Standard;

        /// <summary>
        /// Параметр 'p' для обобщенной вариации <see cref="CollatzVariation.GeneralizedP"/>.
        /// Определяет правило "px + 1". Для стандартной гипотезы Коллатца p=3.
        /// </summary>
        public decimal P_Parameter { get; set; } = 3.0m;
        #endregion

        #region Private Calculation Logic

        /// <summary>
        /// Вычисляет косинус комплексного числа с высокой точностью (decimal).
        /// </summary>
        private ComplexDecimal ComplexCos(ComplexDecimal z)
        {
            double x = (double)z.Real;
            double y = (double)z.Imaginary;
            decimal cos_x = (decimal)Math.Cos(x);
            decimal sin_x = (decimal)Math.Sin(x);
            decimal cosh_y = (decimal)Math.Cosh(y);
            decimal sinh_y = (decimal)Math.Sinh(y);
            return new ComplexDecimal(cos_x * cosh_y, -sin_x * sinh_y);
        }

        /// <summary>
        /// Вычисляет синус комплексного числа с высокой точностью (decimal).
        /// </summary>
        private ComplexDecimal ComplexSin(ComplexDecimal z)
        {
            double x = (double)z.Real;
            double y = (double)z.Imaginary;
            decimal sin_x = (decimal)Math.Sin(x);
            decimal cos_x = (decimal)Math.Cos(x);
            decimal sinh_y = (decimal)Math.Sinh(y);
            decimal cosh_y = (decimal)Math.Cosh(y);
            return new ComplexDecimal(sin_x * cosh_y, cos_x * sinh_y);
        }

        /// <summary>
        /// Вычисляет количество итераций для точки с использованием высокой точности (<see cref="ComplexDecimal"/>).
        /// </summary>
        private int CalculateIterations(ref ComplexDecimal z)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                // === ГЛАВНОЕ ИЗМЕНЕНИЕ: УПРЕЖДАЮЩАЯ ПРОВЕРКА ===
                // Аргумент для Cos/Sin - это z * PI. Проверяем его мнимую часть.
                // Если |Im(z*PI)| > 60, то Cosh/Sinh почти наверняка вызовут переполнение.
                // 60 - это безопасный предел.
                if (Math.Abs(z.Imaginary * (decimal)Math.PI) > 60.0m)
                {
                    // Точка гарантированно "улетает". Считаем, что она вышла за пределы.
                    // Просто выходим из цикла. Текущее значение 'iter' будет использовано для окраски.
                    break;
                }

                try
                {
                    // Теперь можно безопасно вычислять.
                    switch (Variation)
                    {
                        case CollatzVariation.Standard:
                            {
                                ComplexDecimal cos_pi_z = ComplexCos(z * (decimal)Math.PI);
                                z = (new ComplexDecimal(2, 0) + z * 7 - (new ComplexDecimal(2, 0) + z * 5) * cos_pi_z) / 4;
                                break;
                            }
                        case CollatzVariation.SineVariation:
                            {
                                ComplexDecimal sin_pi_z = ComplexSin(z * (decimal)Math.PI);
                                z = 0.25m * (new ComplexDecimal(2, 0) + z * 7 - (new ComplexDecimal(2, 0) + z * 5) * sin_pi_z);
                                break;
                            }
                        case CollatzVariation.GeneralizedP:
                            {
                                decimal p = P_Parameter;
                                ComplexDecimal cos_pi_z = ComplexCos(z * (decimal)Math.PI);
                                z = 0.5m * ((p - 1) * z + 1 - ((p - 1) * z - 1) * cos_pi_z);
                                break;
                            }
                    }
                    iter++;
                }
                catch (OverflowException)
                {
                    // === ВТОРОЕ ВАЖНОЕ ИЗМЕНЕНИЕ ===
                    // Если переполнение все же случилось (например, при умножении),
                    // это значит, что точка улетела в бесконечность.
                    // Мы НЕ присваиваем iter = MaxIterations. Мы просто выходим из цикла.
                    // Это исправит ошибку с "черным цветом" на границе.
                    break;
                }
            }
            return iter;
        }

        /// <summary>
        /// Вычисляет количество итераций для точки с использованием стандартной точности (<see cref="ComplexDouble"/>).
        /// </summary>
        private int CalculateIterationsDouble(ref ComplexDouble z)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            System.Numerics.Complex z_numerics = new System.Numerics.Complex(z.Real, z.Imaginary);

            while (iter < MaxIterations && z_numerics.Magnitude * z_numerics.Magnitude <= thresholdSq)
            {
                // Выбор итерационной формулы на основе выбранной вариации
                switch (Variation)
                {
                    case CollatzVariation.Standard:
                        {
                            System.Numerics.Complex cos_pi_z = System.Numerics.Complex.Cos(z_numerics * Math.PI);
                            z_numerics = 0.25 * (2 + 7 * z_numerics - (2 + 5 * z_numerics) * cos_pi_z);
                            break;
                        }
                    case CollatzVariation.SineVariation:
                        {
                            System.Numerics.Complex sin_pi_z = System.Numerics.Complex.Sin(z_numerics * Math.PI);
                            z_numerics = 0.25 * (2 + 7 * z_numerics - (2 + 5 * z_numerics) * sin_pi_z);
                            break;
                        }
                    case CollatzVariation.GeneralizedP:
                        {
                            double p = (double)P_Parameter;
                            System.Numerics.Complex cos_pi_z = System.Numerics.Complex.Cos(z_numerics * Math.PI);
                            z_numerics = 0.5 * ((p - 1) * z_numerics + 1 - ((p - 1) * z_numerics - 1) * cos_pi_z);
                            break;
                        }
                }
                iter++;
            }
            z = new ComplexDouble(z_numerics.Real, z_numerics.Imaginary);
            return iter;
        }

        #endregion

        #region Private Smoothing Logic

        private double CalculateSmoothValue(int iter, ComplexDecimal finalZ)
        {
            if (iter >= MaxIterations) return iter;
            double log_zn_sq = Math.Log((double)finalZ.MagnitudeSquared);
            double nu = Math.Log(log_zn_sq / (2 * Math.Log(2))) / Math.Log(2);
            return iter + 1 - nu;
        }

        private double CalculateSmoothValueDouble(int iter, ComplexDouble finalZ)
        {
            if (iter >= MaxIterations) return iter;
            double log_zn_sq = Math.Log(finalZ.MagnitudeSquared);
            double nu = Math.Log(log_zn_sq / (2 * Math.Log(2))) / Math.Log(2);
            return iter + 1 - nu;
        }

        #endregion

        #region Public Rendering Methods

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
                if (Scale < SCALE_THRESHOLD_FOR_DECIMAL) // High precision (decimal)
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

                            ComplexDecimal z = new ComplexDecimal(re, im);
                            int iter = CalculateIterations(ref z);

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
                else // Standard precision (double)
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

                            ComplexDouble z = new ComplexDouble(re, im);
                            int iter = CalculateIterationsDouble(ref z);

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
                return Task.Run(() => RenderToBitmap(finalWidth, finalHeight, numThreads, reportProgressCallback, cancellationToken), cancellationToken);

            return Task.Run(() =>
            {
                int highResWidth = finalWidth * supersamplingFactor;
                int highResHeight = finalHeight * supersamplingFactor;
                Action<int> highResProgressCallback = p => reportProgressCallback((int)(p * 0.98));
                Bitmap highResBitmap = RenderToBitmap(highResWidth, highResHeight, numThreads, highResProgressCallback, cancellationToken);
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

                    ComplexDecimal z = new ComplexDecimal(re, im);
                    int iter = CalculateIterations(ref z);

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

                    ComplexDouble z = new ComplexDouble(re, im);
                    int iter = CalculateIterationsDouble(ref z);

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

                    ComplexDecimal z = new ComplexDecimal(re, im);
                    int iter = CalculateIterations(ref z);

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

                    ComplexDouble z = new ComplexDouble(re, im);
                    int iter = CalculateIterationsDouble(ref z);

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
}
