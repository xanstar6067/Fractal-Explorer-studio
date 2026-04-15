using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FractalExplorer.Engines
{
    public enum ColoringModeType
    {
        Discrete = 0,
        Smooth = 1,
        Histogram = 2,
        OrbitTrap = 3,
        StripeAverage = 4,
        SmoothEscapePolynomial = 5
    }

    /// <summary>
    /// Абстрактный базовый класс для движков рендеринга фракталов семейства Мандельброта.
    /// Инкапсулирует общую логику, управление параметрами и поддерживает адаптивную точность вычислений,
    /// автоматически переключаясь между <see cref="double"/> и <see cref="decimal"/> в зависимости от масштаба.
    /// </summary>
    public abstract class FractalMandelbrotFamilyEngine
    {
        private delegate void IterationCalculatorDecimal(decimal re, decimal im, out int iter, out ComplexDecimal z);
        private delegate void IterationCalculatorDouble(double re, double im, out int iter, out ComplexDouble z);

        private enum SpecializedEngineKind
        {
            None,
            Mandelbrot,
            Julia,
            MandelbrotBurningShip,
            JuliaBurningShip,
            Tricorn
        }

        #region Constants

        /// <summary>
        /// Порог масштабирования для переключения на высокоточные вычисления (<see langword="decimal"/>).
        /// При значениях <see cref="Scale"/> МЕНЬШЕ этого порога будет использоваться <see langword="decimal"/>.
        /// </summary>
        private const decimal SCALE_THRESHOLD_FOR_DECIMAL = 4.0m / 2000000000.0m;

        #endregion

        #region Properties

        /// <summary>
        /// Получает или задает максимальное количество итераций для вычисления фрактала.
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Получает или задает квадрат порога (bailout value), используемый для определения, вышла ли точка за пределы множества.
        /// </summary>
        public decimal ThresholdSquared { get; set; }

        /// <summary>
        /// Получает или задает комплексную константу 'C' для фракталов семейства Жюлиа.
        /// </summary>
        public ComplexDecimal C { get; set; }

        /// <summary>
        /// Получает или задает координату X центра видимой области фрактала.
        /// </summary>
        public decimal CenterX { get; set; }

        /// <summary>
        /// Получает или задает координату Y центра видимой области фрактала.
        /// </summary>
        public decimal CenterY { get; set; }

        /// <summary>
        /// Получает или задает текущий масштаб рендеринга (ширина комплексной плоскости, отображаемая на экране).
        /// </summary>
        public decimal Scale { get; set; }

        /// <summary>
        /// Получает или задает флаг, указывающий, нужно ли использовать непрерывное (сглаженное) окрашивание.
        /// </summary>
        public bool UseSmoothColoring { get; set; } = false;

        /// <summary>
        /// Получает или задает функцию палитры для дискретного окрашивания.
        /// Принимает (текущая итерация, макс. итераций, макс. итераций для цвета) и возвращает цвет.
        /// </summary>
        public Func<int, int, int, Color> Palette { get; set; }

        /// <summary>
        /// Получает или задает функцию палитры для непрерывного (сглаженного) окрашивания.
        /// Принимает дробное значение итерации и возвращает цвет.
        /// </summary>
        public Func<double, Color> SmoothPalette { get; set; }

        /// <summary>
        /// Получает или задает максимальное количество итераций для нормализации цвета в палитре (для дискретного режима).
        /// </summary>
        public int MaxColorIterations { get; set; } = 1000;
        public ColoringModeType ActiveMode { get; set; } = ColoringModeType.Smooth;
        public bool HistogramEnabledEqualization { get; set; } = true;
        public double HistogramContrast { get; set; } = 1.0;
        public bool HistogramInputUseSmooth { get; set; } = true;
        public Color InteriorColor { get; set; } = Color.Black;
        public double OrbitTrapStrength { get; set; } = 1.0;
        public double OrbitTrapBias { get; set; } = 0.0;
        public double StripeFrequency { get; set; } = 3.0;
        public double StripeStrength { get; set; } = 0.5;
        public double StripeBias { get; set; } = 0.0;
        public double SmoothEscapePolyCoeffA { get; set; } = 9.0;
        public double SmoothEscapePolyCoeffB { get; set; } = 15.0;
        public double SmoothEscapePolyCoeffC { get; set; } = 8.5;
        public double SmoothEscapePolyGamma { get; set; } = 1.0;
        public double SmoothEscapePolyBlend { get; set; } = 1.0;
        public double SmoothEscapePolyBias { get; set; } = 0.0;

        #endregion

        #region Core Calculation Logic (Abstract Methods)

        /// <summary>
        /// Копирует специфичные для конкретного движка параметры из исходного экземпляра.
        /// </summary>
        /// <param name="source">Исходный движок, из которого копируются параметры.</param>
        public abstract void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source);

        /// <summary>
        /// Вычисляет количество итераций для точки с использованием высокой точности (<see cref="ComplexDecimal"/>).
        /// </summary>
        /// <param name="z">Начальное комплексное число (передается по ссылке и изменяется в процессе).</param>
        /// <param name="c">Комплексная константа.</param>
        /// <returns>Количество итераций до выхода за пределы порога или <see cref="MaxIterations"/>.</returns>
        public abstract int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c);

        /// <summary>
        /// Определяет начальные параметры для расчета точки с использованием высокой точности (<see cref="ComplexDecimal"/>).
        /// </summary>
        /// <param name="re">Действительная часть координаты точки.</param>
        /// <param name="im">Мнимая часть координаты точки.</param>
        /// <param name="initialZ">Выходной параметр: начальное значение Z.</param>
        /// <param name="constantC">Выходной параметр: константа C.</param>
        protected abstract void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC);

        /// <summary>
        /// Вычисляет количество итераций для точки с использованием стандартной точности (<see cref="ComplexDouble"/>).
        /// </summary>
        /// <param name="z">Начальное комплексное число (передается по ссылке и изменяется в процессе).</param>
        /// <param name="c">Комплексная константа.</param>
        /// <returns>Количество итераций до выхода за пределы порога или <see cref="MaxIterations"/>.</returns>
        public abstract int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c);

        /// <summary>
        /// Определяет начальные параметры для расчета точки с использованием стандартной точности (<see cref="ComplexDouble"/>).
        /// </summary>
        /// <param name="re">Действительная часть координаты точки.</param>
        /// <param name="im">Мнимая часть координаты точки.</param>
        /// <param name="initialZ">Выходной параметр: начальное значение Z.</param>
        /// <param name="constantC">Выходной параметр: константа C.</param>
        protected abstract void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC);

        #endregion

        #region Private Smoothing Logic

        /// <summary>
        /// Вычисляет "сглаженное" значение итерации для высокой точности (<see cref="ComplexDecimal"/>).
        /// </summary>
        /// <param name="iter">Количество итераций, после которых точка покинула множество.</param>
        /// <param name="finalZ">Конечное значение Z после итераций.</param>
        /// <returns>Дробное значение итерации для плавного окрашивания.</returns>
        private double CalculateSmoothValue(int iter, ComplexDecimal finalZ)
        {
            if (iter >= MaxIterations) return iter;
            double log_zn_sq = Math.Log((double)finalZ.MagnitudeSquared);
            double nu = Math.Log(log_zn_sq / (2 * Math.Log(2))) / Math.Log(2);
            return iter + 1 - nu;
        }

        /// <summary>
        /// Вычисляет "сглаженное" значение итерации для стандартной точности (<see cref="ComplexDouble"/>).
        /// </summary>
        /// <param name="iter">Количество итераций, после которых точка покинула множество.</param>
        /// <param name="finalZ">Конечное значение Z после итераций.</param>
        /// <returns>Дробное значение итерации для плавного окрашивания.</returns>
        private double CalculateSmoothValueDouble(int iter, ComplexDouble finalZ)
        {
            if (iter >= MaxIterations) return iter;
            double log_zn_sq = Math.Log(finalZ.MagnitudeSquared);
            double nu = Math.Log(log_zn_sq / (2 * Math.Log(2))) / Math.Log(2);
            return iter + 1 - nu;
        }

        private Color ComputePixelColorByMode(int iter, double smoothValue, double orbitTrapMetric, double stripeMetric)
        {
            return ActiveMode switch
            {
                ColoringModeType.Smooth when SmoothPalette != null => SmoothPalette(smoothValue),
                ColoringModeType.Histogram => iter >= MaxIterations
                    ? ResolveInteriorColor()
                    : GetHistogramMappedColor((HistogramInputUseSmooth ? smoothValue : iter) / Math.Max(1.0, MaxIterations)),
                ColoringModeType.OrbitTrap => GetOrbitTrapMappedColor(iter, orbitTrapMetric),
                ColoringModeType.StripeAverage => GetStripeAverageMappedColor(iter, smoothValue, stripeMetric),
                ColoringModeType.SmoothEscapePolynomial => GetSmoothEscapePolynomialColor(iter, smoothValue),
                _ => Palette(iter, MaxIterations, MaxColorIterations)
            };
        }

        private Color ResolveInteriorColor() => InteriorColor;

        private Color GetHistogramMappedColor(double normalized)
        {
            if (normalized < 0) normalized = 0;
            if (normalized > 1) normalized = 1;

            if (HistogramContrast > 0 && Math.Abs(HistogramContrast - 1.0) > 0.0001)
            {
                normalized = Math.Pow(normalized, 1.0 / HistogramContrast);
            }

            if (SmoothPalette != null && HistogramInputUseSmooth)
            {
                return SmoothPalette(normalized * MaxColorIterations);
            }

            int paletteIter = (int)Math.Round(normalized * MaxColorIterations);
            return Palette(paletteIter, MaxIterations, MaxColorIterations);
        }

        private Color GetOrbitTrapMappedColor(int iter, double orbitTrapMetric)
        {
            if (iter >= MaxIterations)
            {
                return ResolveInteriorColor();
            }

            double trapSignal = 1.0 / (1.0 + orbitTrapMetric);
            double normalized = Math.Max(0.0, Math.Min(1.0, trapSignal * OrbitTrapStrength + OrbitTrapBias));
            if (SmoothPalette != null)
            {
                return SmoothPalette(normalized * MaxColorIterations);
            }

            int paletteIter = (int)Math.Round(normalized * MaxColorIterations);
            return Palette(paletteIter, MaxIterations, MaxColorIterations);
        }

        private Color GetStripeAverageMappedColor(int iter, double smoothValue, double stripeMetric)
        {
            if (iter >= MaxIterations)
            {
                return ResolveInteriorColor();
            }

            double smoothNorm = Math.Max(0.0, Math.Min(1.0, smoothValue / Math.Max(1.0, MaxIterations)));
            double stripedNorm = Math.Max(0.0, Math.Min(1.0, stripeMetric + StripeBias));
            double blend = Math.Max(0.0, Math.Min(1.0, StripeStrength));
            double combined = smoothNorm * (1.0 - blend) + stripedNorm * blend;

            if (SmoothPalette != null)
            {
                return SmoothPalette(combined * MaxColorIterations);
            }

            int paletteIter = (int)Math.Round(combined * MaxColorIterations);
            return Palette(paletteIter, MaxIterations, MaxColorIterations);
        }

        private Color GetSmoothEscapePolynomialColor(int iter, double smoothValue)
        {
            if (iter >= MaxIterations)
            {
                return ResolveInteriorColor();
            }

            double t = smoothValue / Math.Max(1.0, MaxIterations);
            double smoothNorm = Math.Max(0.0, Math.Min(1.0, t));

            double polyMapped = SmoothEscapePolyCoeffA * (1.0 - smoothNorm) * smoothNorm * smoothNorm * smoothNorm
                + SmoothEscapePolyCoeffB * (1.0 - smoothNorm) * (1.0 - smoothNorm) * smoothNorm * smoothNorm
                + SmoothEscapePolyCoeffC * (1.0 - smoothNorm) * (1.0 - smoothNorm) * (1.0 - smoothNorm) * smoothNorm;
            polyMapped = Math.Max(0.0, Math.Min(1.0, polyMapped));

            double blend = Math.Max(0.0, Math.Min(1.0, SmoothEscapePolyBlend));
            double blended = smoothNorm * (1.0 - blend) + polyMapped * blend;
            blended = Math.Max(0.0, Math.Min(1.0, blended));

            double biased = blended + SmoothEscapePolyBias;
            biased = Math.Max(0.0, Math.Min(1.0, biased));

            double safeGamma = SmoothEscapePolyGamma <= 0.0 ? 1.0 : SmoothEscapePolyGamma;
            double gammaMapped = Math.Pow(biased, safeGamma);
            gammaMapped = Math.Max(0.0, Math.Min(1.0, gammaMapped));

            if (SmoothPalette != null)
            {
                return SmoothPalette(gammaMapped * MaxColorIterations);
            }

            int paletteIter = (int)Math.Round(gammaMapped * MaxColorIterations);
            return Palette(paletteIter, MaxIterations, MaxColorIterations);
        }

        private void ComputePixelMetricsDecimal(decimal re, decimal im, out int iter, out ComplexDecimal z, out double orbitTrapMetric, out double stripeMetric)
        {
            GetCalculationParameters(re, im, out z, out ComplexDecimal c);
            decimal thresholdSq = ThresholdSquared;
            SpecializedEngineKind kind = GetSpecializedEngineKind();
            iter = 0;
            decimal minTrapDistance = decimal.MaxValue;
            double stripeAccumulator = 0.0;
            int stripeSamples = 0;

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                decimal absRe = Math.Abs(z.Real);
                decimal absIm = Math.Abs(z.Imaginary);
                decimal trapDistance = Math.Min(absRe, absIm);
                if (trapDistance < minTrapDistance)
                {
                    minTrapDistance = trapDistance;
                }

                double angle = Math.Atan2((double)z.Imaginary, (double)z.Real);
                stripeAccumulator += 0.5 + 0.5 * Math.Sin(StripeFrequency * angle);
                stripeSamples++;

                z = IterateOneStepDecimal(kind, z, c);
                iter++;
            }

            orbitTrapMetric = minTrapDistance == decimal.MaxValue ? 0.0 : (double)minTrapDistance;
            stripeMetric = stripeSamples > 0 ? stripeAccumulator / stripeSamples : 0.0;
        }

        private void ComputePixelMetricsDouble(double re, double im, out int iter, out ComplexDouble z, out double orbitTrapMetric, out double stripeMetric)
        {
            GetCalculationParametersDouble(re, im, out z, out ComplexDouble c);
            double thresholdSq = (double)ThresholdSquared;
            SpecializedEngineKind kind = GetSpecializedEngineKind();
            iter = 0;
            double minTrapDistance = double.MaxValue;
            double stripeAccumulator = 0.0;
            int stripeSamples = 0;

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                double absRe = Math.Abs(z.Real);
                double absIm = Math.Abs(z.Imaginary);
                double trapDistance = Math.Min(absRe, absIm);
                if (trapDistance < minTrapDistance)
                {
                    minTrapDistance = trapDistance;
                }

                double angle = Math.Atan2(z.Imaginary, z.Real);
                stripeAccumulator += 0.5 + 0.5 * Math.Sin(StripeFrequency * angle);
                stripeSamples++;

                z = IterateOneStepDouble(kind, z, c);
                iter++;
            }

            orbitTrapMetric = minTrapDistance == double.MaxValue ? 0.0 : minTrapDistance;
            stripeMetric = stripeSamples > 0 ? stripeAccumulator / stripeSamples : 0.0;
        }

        private static ComplexDecimal IterateOneStepDecimal(SpecializedEngineKind kind, ComplexDecimal z, ComplexDecimal c)
        {
            if (kind == SpecializedEngineKind.MandelbrotBurningShip || kind == SpecializedEngineKind.JuliaBurningShip)
            {
                z = new ComplexDecimal(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
            }
            else if (kind == SpecializedEngineKind.Tricorn)
            {
                z = new ComplexDecimal(z.Real, -z.Imaginary);
            }
            return z * z + c;
        }

        private static ComplexDouble IterateOneStepDouble(SpecializedEngineKind kind, ComplexDouble z, ComplexDouble c)
        {
            if (kind == SpecializedEngineKind.MandelbrotBurningShip || kind == SpecializedEngineKind.JuliaBurningShip)
            {
                z = new ComplexDouble(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
            }
            else if (kind == SpecializedEngineKind.Tricorn)
            {
                z = new ComplexDouble(z.Real, -z.Imaginary);
            }
            return z * z + c;
        }

        private static double[] BuildHistogramCdf(int[] bins, int totalSamples)
        {
            var cdf = new double[bins.Length];
            if (totalSamples <= 0) return cdf;

            long cumulative = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                cumulative += bins[i];
                cdf[i] = (double)cumulative / totalSamples;
            }
            return cdf;
        }

        #endregion

        #region Public Rendering Methods

        /// <summary>
        /// Отрисовывает одну плитку (тайл), автоматически выбирая точность вычислений на основе текущего масштаба.
        /// </summary>
        /// <param name="tile">Информация о плитке для рендеринга.</param>
        /// <param name="canvasWidth">Общая ширина холста.</param>
        /// <param name="canvasHeight">Общая высота холста.</param>
        /// <param name="bytesPerPixel">Выходной параметр: количество байт на пиксель (BGRA).</param>
        /// <returns>Массив байт с пиксельными данными плитки в формате BGRA.</returns>
        public byte[] RenderSingleTile(TileInfo tile, int canvasWidth, int canvasHeight, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            byte[] buffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];
            if (canvasWidth <= 0 || canvasHeight <= 0) return buffer;

            if (Scale < SCALE_THRESHOLD_FOR_DECIMAL)
            {
                RenderTileDecimal(buffer, tile, canvasWidth, canvasHeight, bytesPerPixel);
            }
            else
            {
                RenderTileDouble(buffer, tile, canvasWidth, canvasHeight, bytesPerPixel);
            }

            return buffer;
        }

        /// <summary>
        /// Отрисовывает одну плитку с использованием суперсэмплинга (SSAA), автоматически выбирая точность вычислений.
        /// </summary>
        /// <param name="tile">Информация о плитке для рендеринга.</param>
        /// <param name="canvasWidth">Общая ширина холста.</param>
        /// <param name="canvasHeight">Общая высота холста.</param>
        /// <param name="supersamplingFactor">Фактор суперсэмплинга (например, 2 для 2x2 SSAA).</param>
        /// <param name="bytesPerPixel">Выходной параметр: количество байт на пиксель (BGRA).</param>
        /// <returns>Массив байт с пиксельными данными плитки в формате BGRA.</returns>
        public byte[] RenderSingleTileSSAA(TileInfo tile, int canvasWidth, int canvasHeight, int supersamplingFactor, int numThreads, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            numThreads = Math.Max(1, numThreads);
            if (supersamplingFactor <= 1)
            {
                return RenderSingleTile(tile, canvasWidth, canvasHeight, out bytesPerPixel);
            }

            byte[] finalTileBuffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];
            if (canvasWidth <= 0 || canvasHeight <= 0) return finalTileBuffer;

            if (Scale < SCALE_THRESHOLD_FOR_DECIMAL)
            {
                RenderTileSSAA_Decimal(finalTileBuffer, tile, canvasWidth, canvasHeight, supersamplingFactor, numThreads, bytesPerPixel);
            }
            else
            {
                RenderTileSSAA_Double(finalTileBuffer, tile, canvasWidth, canvasHeight, supersamplingFactor, numThreads, bytesPerPixel);
            }

            return finalTileBuffer;
        }

        /// <summary>
        /// Рендерит фрактал в новый объект <see cref="Bitmap"/>, автоматически выбирая точность вычислений.
        /// </summary>
        /// <param name="renderWidth">Ширина итогового изображения.</param>
        /// <param name="renderHeight">Высота итогового изображения.</param>
        /// <param name="numThreads">Количество потоков для параллельного рендеринга.</param>
        /// <param name="reportProgressCallback">Обратный вызов для сообщения о прогрессе (от 0 до 100).</param>
        /// <param name="cancellationToken">Токен для отмены операции.</param>
        /// <returns>Объект <see cref="Bitmap"/> с изображением фрактала.</returns>
        public Bitmap RenderToBitmap(int renderWidth, int renderHeight, int numThreads, Action<int> reportProgressCallback, CancellationToken cancellationToken = default)
        {
            if (renderWidth <= 0 || renderHeight <= 0) return new Bitmap(1, 1);
            if (ActiveMode == ColoringModeType.Histogram)
            {
                return Scale < SCALE_THRESHOLD_FOR_DECIMAL
                    ? RenderToBitmapHistogramDecimal(renderWidth, renderHeight, numThreads, reportProgressCallback, cancellationToken)
                    : RenderToBitmapHistogramDouble(renderWidth, renderHeight, numThreads, reportProgressCallback, cancellationToken);
            }

            Bitmap bmp = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(bmpData.Stride) * renderHeight];
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };
            long done = 0;

            try
            {
                if (Scale < SCALE_THRESHOLD_FOR_DECIMAL) // Высокая точность (decimal)
                {
                    decimal halfWidthPixels = renderWidth / 2.0m;
                    decimal halfHeightPixels = renderHeight / 2.0m;
                    decimal unitsPerPixel = Scale / renderWidth;
                    decimal centerX = CenterX;
                    decimal centerY = CenterY;
                    IterationCalculatorDecimal iterationCalculator = CreateDecimalIterationCalculator();

                    Parallel.For(0, renderHeight, po, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int rowOffset = y * bmpData.Stride;
                        for (int x = 0; x < renderWidth; x++)
                        {
                            decimal re = centerX + (x - halfWidthPixels) * unitsPerPixel;
                            decimal im = centerY - (y - halfHeightPixels) * unitsPerPixel;

                            Color pixelColor;
                            if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                            {
                                ComputePixelMetricsDecimal(re, im, out int iter, out ComplexDecimal z, out double orbitTrapMetric, out double stripeMetric);
                                double smoothValue = CalculateSmoothValue(iter, z);
                                pixelColor = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                            }
                            else
                            {
                                iterationCalculator(re, im, out int iter, out ComplexDecimal z);
                                double smoothValue = CalculateSmoothValue(iter, z);
                                pixelColor = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                            }

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
                else // Стандартная точность (double)
                {
                    double centerX_d = (double)CenterX;
                    double centerY_d = (double)CenterY;
                    double unitsPerPixel_d = (double)Scale / renderWidth;
                    double halfWidthPixels_d = renderWidth / 2.0;
                    double halfHeightPixels_d = renderHeight / 2.0;
                    IterationCalculatorDouble iterationCalculator = CreateDoubleIterationCalculator();

                    Parallel.For(0, renderHeight, po, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int rowOffset = y * bmpData.Stride;
                        for (int x = 0; x < renderWidth; x++)
                        {
                            double re = centerX_d + (x - halfWidthPixels_d) * unitsPerPixel_d;
                            double im = centerY_d - (y - halfHeightPixels_d) * unitsPerPixel_d;

                            Color pixelColor;
                            if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                            {
                                ComputePixelMetricsDouble(re, im, out int iter, out ComplexDouble z, out double orbitTrapMetric, out double stripeMetric);
                                double smoothValue = CalculateSmoothValueDouble(iter, z);
                                pixelColor = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                            }
                            else
                            {
                                iterationCalculator(re, im, out int iter, out ComplexDouble z);
                                double smoothValue = CalculateSmoothValueDouble(iter, z);
                                pixelColor = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                            }

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
            catch (OperationCanceledException)
            {
                // Позволяет выйти из метода без ошибок, если рендеринг был отменен.
            }
            finally
            {
                Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                bmp.UnlockBits(bmpData);
            }

            return bmp;
        }

        /// <summary>
        /// Асинхронно рендерит фрактал в новый <see cref="Bitmap"/> с использованием суперсэмплинга (SSAA).
        /// Этот метод инкапсулирует логику рендеринга в высоком разрешении и последующего уменьшения изображения.
        /// </summary>
        /// <param name="finalWidth">Итоговая ширина изображения.</param>
        /// <param name="finalHeight">Итоговая высота изображения.</param>
        /// <param name="numThreads">Количество потоков для параллельного рендеринга.</param>
        /// <param name="reportProgressCallback">Обратный вызов для сообщения о прогрессе (от 0 до 100).</param>
        /// <param name="supersamplingFactor">Фактор суперсэмплинга (например, 2 для 2x2 SSAA).</param>
        /// <param name="cancellationToken">Токен для отмены операции.</param>
        /// <returns>Задача, результатом которой является <see cref="Bitmap"/> с сглаженным изображением фрактала.</returns>
        public Task<Bitmap> RenderToBitmapSSAA(int finalWidth, int finalHeight, int numThreads, Action<int> reportProgressCallback, int supersamplingFactor, CancellationToken cancellationToken = default)
        {
            if (supersamplingFactor <= 1)
            {
                return Task.Run(() => RenderToBitmap(finalWidth, finalHeight, numThreads, reportProgressCallback, cancellationToken), cancellationToken);
            }

            return Task.Run(() =>
            {
                // Рендеринг с повышенным разрешением
                int highResWidth = finalWidth * supersamplingFactor;
                int highResHeight = finalHeight * supersamplingFactor;
                Action<int> highResProgressCallback = p => reportProgressCallback((int)(p * 0.98));
                Bitmap highResBitmap = RenderToBitmap(highResWidth, highResHeight, numThreads, highResProgressCallback, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                reportProgressCallback(98);

                // Уменьшение изображения до целевого размера
                Bitmap finalBitmap = new Bitmap(highResBitmap, finalWidth, finalHeight);
                highResBitmap.Dispose(); // Немедленное освобождение памяти
                reportProgressCallback(100);

                return finalBitmap;
            }, cancellationToken);
        }

        #endregion

        #region Private Rendering Helpers

        private SpecializedEngineKind GetSpecializedEngineKind()
        {
            if (this is MandelbrotEngine) return SpecializedEngineKind.Mandelbrot;
            if (this is JuliaEngine) return SpecializedEngineKind.Julia;
            if (this is MandelbrotBurningShipEngine) return SpecializedEngineKind.MandelbrotBurningShip;
            if (this is JuliaBurningShipEngine) return SpecializedEngineKind.JuliaBurningShip;
            if (this is TricornEngine) return SpecializedEngineKind.Tricorn;
            return SpecializedEngineKind.None;
        }

        private IterationCalculatorDecimal CreateDecimalIterationCalculator(bool allowSpecialized = true)
        {
            SpecializedEngineKind kind = allowSpecialized ? GetSpecializedEngineKind() : SpecializedEngineKind.None;
            int maxIterations = MaxIterations;
            decimal thresholdSq = ThresholdSquared;

            if (kind == SpecializedEngineKind.Julia || kind == SpecializedEngineKind.JuliaBurningShip)
            {
                ComplexDecimal juliaC = C;
                if (kind == SpecializedEngineKind.Julia)
                {
                    return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
                    {
                        z = new ComplexDecimal(re, im);
                        iter = 0;
                        while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                        {
                            z = z * z + juliaC;
                            iter++;
                        }
                    };
                }

                return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
                {
                    z = new ComplexDecimal(re, im);
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDecimal(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                        z = z * z + juliaC;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.Mandelbrot)
            {
                return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
                {
                    decimal xMinusQuarter = re - 0.25m;
                    decimal ySquared = im * im;
                    decimal q = xMinusQuarter * xMinusQuarter + ySquared;
                    if (q * (q + xMinusQuarter) <= 0.25m * ySquared)
                    {
                        z = ComplexDecimal.Zero;
                        iter = maxIterations;
                        return;
                    }

                    decimal xPlusOne = re + 1.0m;
                    if ((xPlusOne * xPlusOne + ySquared) <= 0.0625m)
                    {
                        z = ComplexDecimal.Zero;
                        iter = maxIterations;
                        return;
                    }

                    ComplexDecimal c = new ComplexDecimal(re, im);
                    z = ComplexDecimal.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.MandelbrotBurningShip)
            {
                return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
                {
                    ComplexDecimal c = new ComplexDecimal(re, im);
                    z = ComplexDecimal.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDecimal(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.Tricorn)
            {
                return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
                {
                    ComplexDecimal c = new ComplexDecimal(re, im);
                    z = ComplexDecimal.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDecimal(z.Real, -z.Imaginary);
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            return (decimal re, decimal im, out int iter, out ComplexDecimal z) =>
            {
                GetCalculationParameters(re, im, out z, out ComplexDecimal c);
                iter = CalculateIterations(ref z, c);
            };
        }

        private IterationCalculatorDouble CreateDoubleIterationCalculator(bool allowSpecialized = true)
        {
            SpecializedEngineKind kind = allowSpecialized ? GetSpecializedEngineKind() : SpecializedEngineKind.None;
            int maxIterations = MaxIterations;
            double thresholdSq = (double)ThresholdSquared;

            if (kind == SpecializedEngineKind.Julia || kind == SpecializedEngineKind.JuliaBurningShip)
            {
                ComplexDouble juliaC = new ComplexDouble((double)C.Real, (double)C.Imaginary);
                if (kind == SpecializedEngineKind.Julia)
                {
                    return (double re, double im, out int iter, out ComplexDouble z) =>
                    {
                        z = new ComplexDouble(re, im);
                        iter = 0;
                        while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                        {
                            z = z * z + juliaC;
                            iter++;
                        }
                    };
                }

                return (double re, double im, out int iter, out ComplexDouble z) =>
                {
                    z = new ComplexDouble(re, im);
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDouble(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                        z = z * z + juliaC;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.Mandelbrot)
            {
                return (double re, double im, out int iter, out ComplexDouble z) =>
                {
                    double xMinusQuarter = re - 0.25;
                    double ySquared = im * im;
                    double q = xMinusQuarter * xMinusQuarter + ySquared;
                    if (q * (q + xMinusQuarter) <= 0.25 * ySquared)
                    {
                        z = ComplexDouble.Zero;
                        iter = maxIterations;
                        return;
                    }

                    double xPlusOne = re + 1.0;
                    if ((xPlusOne * xPlusOne + ySquared) <= 0.0625)
                    {
                        z = ComplexDouble.Zero;
                        iter = maxIterations;
                        return;
                    }

                    ComplexDouble c = new ComplexDouble(re, im);
                    z = ComplexDouble.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.MandelbrotBurningShip)
            {
                return (double re, double im, out int iter, out ComplexDouble z) =>
                {
                    ComplexDouble c = new ComplexDouble(re, im);
                    z = ComplexDouble.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDouble(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            if (kind == SpecializedEngineKind.Tricorn)
            {
                return (double re, double im, out int iter, out ComplexDouble z) =>
                {
                    ComplexDouble c = new ComplexDouble(re, im);
                    z = ComplexDouble.Zero;
                    iter = 0;
                    while (iter < maxIterations && z.MagnitudeSquared <= thresholdSq)
                    {
                        z = new ComplexDouble(z.Real, -z.Imaginary);
                        z = z * z + c;
                        iter++;
                    }
                };
            }

            return (double re, double im, out int iter, out ComplexDouble z) =>
            {
                GetCalculationParametersDouble(re, im, out z, out ComplexDouble c);
                iter = CalculateIterationsDouble(ref z, c);
            };
        }

        private Bitmap RenderToBitmapHistogramDecimal(int renderWidth, int renderHeight, int numThreads, Action<int> reportProgressCallback, CancellationToken cancellationToken)
        {
            Bitmap bmp = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(bmpData.Stride) * renderHeight];

            int total = renderWidth * renderHeight;
            int[] iterData = new int[total];
            double[] smoothData = HistogramInputUseSmooth ? new double[total] : Array.Empty<double>();
            int[] bins = new int[MaxIterations + 1];
            object binsLock = new object();

            decimal halfWidthPixels = renderWidth / 2.0m;
            decimal halfHeightPixels = renderHeight / 2.0m;
            decimal unitsPerPixel = Scale / renderWidth;
            IterationCalculatorDecimal iterationCalculator = CreateDecimalIterationCalculator();
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };

            Parallel.For(0, renderHeight, po, y =>
            {
                int[] localBins = new int[bins.Length];
                for (int x = 0; x < renderWidth; x++)
                {
                    int idx = y * renderWidth + x;
                    decimal re = CenterX + (x - halfWidthPixels) * unitsPerPixel;
                    decimal im = CenterY - (y - halfHeightPixels) * unitsPerPixel;

                    iterationCalculator(re, im, out int iter, out ComplexDecimal z);
                    iterData[idx] = iter;
                    double smoothValue = CalculateSmoothValue(iter, z);
                    if (HistogramInputUseSmooth) smoothData[idx] = smoothValue;
                    int bin = Math.Max(0, Math.Min(MaxIterations, HistogramInputUseSmooth ? (int)Math.Floor(smoothValue) : iter));
                    localBins[bin]++;
                }
                lock (binsLock)
                {
                    for (int i = 0; i < bins.Length; i++) bins[i] += localBins[i];
                }
                reportProgressCallback((int)(50.0 * (y + 1) / renderHeight));
            });

            var cdf = BuildHistogramCdf(bins, total);
            for (int y = 0; y < renderHeight; y++)
            {
                int rowOffset = y * bmpData.Stride;
                for (int x = 0; x < renderWidth; x++)
                {
                    int idx = y * renderWidth + x;
                    int iter = iterData[idx];
                    int bin = Math.Max(0, Math.Min(MaxIterations, HistogramInputUseSmooth ? (int)Math.Floor(smoothData[idx]) : iter));
                    double normalized = HistogramEnabledEqualization ? cdf[bin] : bin / (double)Math.Max(1, MaxIterations);
                    Color color = iter >= MaxIterations ? ResolveInteriorColor() : GetHistogramMappedColor(normalized);
                    int p = rowOffset + x * 3;
                    buffer[p] = color.B; buffer[p + 1] = color.G; buffer[p + 2] = color.R;
                }
                reportProgressCallback(50 + (int)(50.0 * (y + 1) / renderHeight));
            }

            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        private Bitmap RenderToBitmapHistogramDouble(int renderWidth, int renderHeight, int numThreads, Action<int> reportProgressCallback, CancellationToken cancellationToken)
        {
            Bitmap bmp = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(bmpData.Stride) * renderHeight];

            int total = renderWidth * renderHeight;
            int[] iterData = new int[total];
            double[] smoothData = HistogramInputUseSmooth ? new double[total] : Array.Empty<double>();
            int[] bins = new int[MaxIterations + 1];
            object binsLock = new object();

            double halfWidthPixels = renderWidth / 2.0;
            double halfHeightPixels = renderHeight / 2.0;
            double unitsPerPixel = (double)Scale / renderWidth;
            IterationCalculatorDouble iterationCalculator = CreateDoubleIterationCalculator();
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };

            Parallel.For(0, renderHeight, po, y =>
            {
                int[] localBins = new int[bins.Length];
                for (int x = 0; x < renderWidth; x++)
                {
                    int idx = y * renderWidth + x;
                    double re = (double)CenterX + (x - halfWidthPixels) * unitsPerPixel;
                    double im = (double)CenterY - (y - halfHeightPixels) * unitsPerPixel;

                    iterationCalculator(re, im, out int iter, out ComplexDouble z);
                    iterData[idx] = iter;
                    double smoothValue = CalculateSmoothValueDouble(iter, z);
                    if (HistogramInputUseSmooth) smoothData[idx] = smoothValue;
                    int bin = Math.Max(0, Math.Min(MaxIterations, HistogramInputUseSmooth ? (int)Math.Floor(smoothValue) : iter));
                    localBins[bin]++;
                }
                lock (binsLock)
                {
                    for (int i = 0; i < bins.Length; i++) bins[i] += localBins[i];
                }
                reportProgressCallback((int)(50.0 * (y + 1) / renderHeight));
            });

            var cdf = BuildHistogramCdf(bins, total);
            for (int y = 0; y < renderHeight; y++)
            {
                int rowOffset = y * bmpData.Stride;
                for (int x = 0; x < renderWidth; x++)
                {
                    int idx = y * renderWidth + x;
                    int iter = iterData[idx];
                    int bin = Math.Max(0, Math.Min(MaxIterations, HistogramInputUseSmooth ? (int)Math.Floor(smoothData[idx]) : iter));
                    double normalized = HistogramEnabledEqualization ? cdf[bin] : bin / (double)Math.Max(1, MaxIterations);
                    Color color = iter >= MaxIterations ? ResolveInteriorColor() : GetHistogramMappedColor(normalized);
                    int p = rowOffset + x * 3;
                    buffer[p] = color.B; buffer[p + 1] = color.G; buffer[p + 2] = color.R;
                }
                reportProgressCallback(50 + (int)(50.0 * (y + 1) / renderHeight));
            }

            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        /// <summary>
        /// Вспомогательный метод для рендеринга тайла с высокой точностью (<see cref="decimal"/>).
        /// </summary>
        /// <param name="buffer">Буфер для записи пиксельных данных.</param>
        /// <param name="tile">Информация о плитке.</param>
        /// <param name="canvasWidth">Ширина холста.</param>
        /// <param name="canvasHeight">Высота холста.</param>
        /// <param name="bytesPerPixel">Количество байт на пиксель.</param>
        private void RenderTileDecimal(byte[] buffer, TileInfo tile, int canvasWidth, int canvasHeight, int bytesPerPixel)
        {
            decimal halfWidthPixels = canvasWidth / 2.0m;
            decimal halfHeightPixels = canvasHeight / 2.0m;
            decimal unitsPerPixel = Scale / canvasWidth;
            decimal centerX = CenterX;
            decimal centerY = CenterY;
            IterationCalculatorDecimal iterationCalculator = CreateDecimalIterationCalculator();

            for (int y = 0; y < tile.Bounds.Height; y++)
            {
                int canvasY = tile.Bounds.Y + y;
                if (canvasY >= canvasHeight) continue;

                for (int x = 0; x < tile.Bounds.Width; x++)
                {
                    int canvasX = tile.Bounds.X + x;
                    if (canvasX >= canvasWidth) continue;

                    decimal re = centerX + (canvasX - halfWidthPixels) * unitsPerPixel;
                    decimal im = centerY - (canvasY - halfHeightPixels) * unitsPerPixel;

                    Color pixelColor;
                    if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                    {
                        ComputePixelMetricsDecimal(re, im, out int iter, out ComplexDecimal z, out double orbitTrapMetric, out double stripeMetric);
                        double smoothValue = CalculateSmoothValue(iter, z);
                        pixelColor = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                    }
                    else
                    {
                        iterationCalculator(re, im, out int iter, out ComplexDecimal z);
                        double smoothValue = CalculateSmoothValue(iter, z);
                        pixelColor = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                    }

                    int bufferIndex = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[bufferIndex] = pixelColor.B;
                    buffer[bufferIndex + 1] = pixelColor.G;
                    buffer[bufferIndex + 2] = pixelColor.R;
                    buffer[bufferIndex + 3] = 255;
                }
            }
        }

        /// <summary>
        /// Вспомогательный метод для рендеринга тайла со стандартной точностью (<see cref="double"/>).
        /// </summary>
        /// <param name="buffer">Буфер для записи пиксельных данных.</param>
        /// <param name="tile">Информация о плитке.</param>
        /// <param name="canvasWidth">Ширина холста.</param>
        /// <param name="canvasHeight">Высота холста.</param>
        /// <param name="bytesPerPixel">Количество байт на пиксель.</param>
        private void RenderTileDouble(byte[] buffer, TileInfo tile, int canvasWidth, int canvasHeight, int bytesPerPixel)
        {
            double centerX_d = (double)CenterX;
            double centerY_d = (double)CenterY;
            double scale_d = (double)Scale;
            IterationCalculatorDouble iterationCalculator = CreateDoubleIterationCalculator();

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

                    Color pixelColor;
                    if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                    {
                        ComputePixelMetricsDouble(re, im, out int iter, out ComplexDouble z, out double orbitTrapMetric, out double stripeMetric);
                        double smoothValue = CalculateSmoothValueDouble(iter, z);
                        pixelColor = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                    }
                    else
                    {
                        iterationCalculator(re, im, out int iter, out ComplexDouble z);
                        double smoothValue = CalculateSmoothValueDouble(iter, z);
                        pixelColor = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                    }

                    int bufferIndex = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[bufferIndex] = pixelColor.B;
                    buffer[bufferIndex + 1] = pixelColor.G;
                    buffer[bufferIndex + 2] = pixelColor.R;
                    buffer[bufferIndex + 3] = 255;
                }
            }
        }

        /// <summary>
        /// Вспомогательный метод для SSAA рендеринга тайла с высокой точностью (<see cref="decimal"/>).
        /// </summary>
        /// <param name="finalTileBuffer">Буфер для итогового изображения тайла.</param>
        /// <param name="tile">Информация о плитке.</param>
        /// <param name="canvasWidth">Ширина холста.</param>
        /// <param name="canvasHeight">Высота холста.</param>
        /// <param name="supersamplingFactor">Фактор суперсэмплинга.</param>
        /// <param name="bytesPerPixel">Количество байт на пиксель.</param>
        private void RenderTileSSAA_Decimal(byte[] finalTileBuffer, TileInfo tile, int canvasWidth, int canvasHeight, int supersamplingFactor, int numThreads, int bytesPerPixel)
        {
            int highResTileWidth = tile.Bounds.Width * supersamplingFactor;
            int highResTileHeight = tile.Bounds.Height * supersamplingFactor;
            Color[,] highResColorBuffer = new Color[highResTileWidth, highResTileHeight];

            long highResCanvasWidth = (long)canvasWidth * supersamplingFactor;
            decimal unitsPerSubPixel = Scale / highResCanvasWidth;
            decimal highResHalfWidthPixels = highResCanvasWidth / 2.0m;
            decimal highResHalfHeightPixels = (long)canvasHeight * supersamplingFactor / 2.0m;
            decimal centerX = CenterX;
            decimal centerY = CenterY;
            IterationCalculatorDecimal iterationCalculator = CreateDecimalIterationCalculator();

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads };

            Parallel.For(0, highResTileHeight, po, y =>
            {
                for (int x = 0; x < highResTileWidth; x++)
                {
                    long globalHighResX = (long)tile.Bounds.X * supersamplingFactor + x;
                    long globalHighResY = (long)tile.Bounds.Y * supersamplingFactor + y;

                    decimal re = centerX + (globalHighResX - highResHalfWidthPixels) * unitsPerSubPixel;
                    decimal im = centerY - (globalHighResY - highResHalfHeightPixels) * unitsPerSubPixel;

                    if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                    {
                        ComputePixelMetricsDecimal(re, im, out int iter, out ComplexDecimal z, out double orbitTrapMetric, out double stripeMetric);
                        double smoothValue = CalculateSmoothValue(iter, z);
                        highResColorBuffer[x, y] = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                    }
                    else
                    {
                        iterationCalculator(re, im, out int iter, out ComplexDecimal z);
                        double smoothValue = CalculateSmoothValue(iter, z);
                        highResColorBuffer[x, y] = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                    }
                }
            });

            // Усреднение цветов субпикселей для получения итогового цвета пикселя
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

        /// <summary>
        /// Вспомогательный метод для SSAA рендеринга тайла со стандартной точностью (<see cref="double"/>).
        /// </summary>
        /// <param name="finalTileBuffer">Буфер для итогового изображения тайла.</param>
        /// <param name="tile">Информация о плитке.</param>
        /// <param name="canvasWidth">Ширина холста.</param>
        /// <param name="canvasHeight">Высота холста.</param>
        /// <param name="supersamplingFactor">Фактор суперсэмплинга.</param>
        /// <param name="bytesPerPixel">Количество байт на пиксель.</param>
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
            IterationCalculatorDouble iterationCalculator = CreateDoubleIterationCalculator();

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = numThreads };

            Parallel.For(0, highResTileHeight, po, y =>
            {
                for (int x = 0; x < highResTileWidth; x++)
                {
                    long globalHighResX = (long)tile.Bounds.X * supersamplingFactor + x;
                    long globalHighResY = (long)tile.Bounds.Y * supersamplingFactor + y;

                    double re = centerX_d + (globalHighResX - highResHalfWidthPixels) * unitsPerSubPixel;
                    double im = centerY_d - (globalHighResY - highResHalfHeightPixels) * unitsPerSubPixel;

                    if (ActiveMode == ColoringModeType.OrbitTrap || ActiveMode == ColoringModeType.StripeAverage)
                    {
                        ComputePixelMetricsDouble(re, im, out int iter, out ComplexDouble z, out double orbitTrapMetric, out double stripeMetric);
                        double smoothValue = CalculateSmoothValueDouble(iter, z);
                        highResColorBuffer[x, y] = ComputePixelColorByMode(iter, smoothValue, orbitTrapMetric, stripeMetric);
                    }
                    else
                    {
                        iterationCalculator(re, im, out int iter, out ComplexDouble z);
                        double smoothValue = CalculateSmoothValueDouble(iter, z);
                        highResColorBuffer[x, y] = ComputePixelColorByMode(iter, smoothValue, 0.0, 0.0);
                    }
                }
            });

            // Усреднение цветов субпикселей для получения итогового цвета пикселя
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
    /// Реализует движок для рендеринга фрактала "Буффало".
    /// Итерационная формула: z -> (|Re(z)| + i|Im(z)|)² + c.
    /// </summary>
    public class BuffaloEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                // Применяем модуль к каждой компоненте перед возведением в квадрат
                z = new ComplexDecimal(Math.Abs(z.Real), Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                // Применяем модуль к каждой компоненте перед возведением в квадрат
                z = new ComplexDouble(Math.Abs(z.Real), Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }


    /// <summary>
    /// Реализует движок для рендеринга фрактала "Кельтский Мандельброт".
    /// Итерационная формула: z -> (|Re(z²)| + i·Im(z²)) + c.
    /// </summary>
    public class CelticMandelbrotEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                ComplexDecimal squared = z * z;
                z = new ComplexDecimal(Math.Abs(squared.Real), squared.Imaginary) + c;
                iter++;
            }

            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                ComplexDouble squared = z * z;
                z = new ComplexDouble(Math.Abs(squared.Real), squared.Imaginary) + c;
                iter++;
            }

            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга фрактала Симоноброт (пользовательская версия).
    /// Итерационная формула: z -> (z^p * |z|^p) + c.
    /// </summary>
    public class SimonobrotEngine : FractalMandelbrotFamilyEngine
    {
        /// <summary>
        /// Получает или задает степень 'p', в которую возводится z и |z|.
        /// </summary>
        public decimal Power { get; set; } = 2m;

        /// <summary>
        /// Получает или задает флаг, определяющий зеркальное отражение фрактала относительно вертикальной оси.
        /// <see langword="false"/>: обычное отображение;
        /// <see langword="true"/>: зеркальное отражение (инверсия по горизонтали).
        /// </summary>
        public bool UseInversion { get; set; } = false;

        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            if (source is SimonobrotEngine sourceEngine)
            {
                this.Power = sourceEngine.Power;
                this.UseInversion = sourceEngine.UseInversion;
            }
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;

            // Применяем зеркальное отражение относительно вертикальной оси
            if (UseInversion)
            {
                constantC = new ComplexDecimal(-re, im);
            }
            else
            {
                constantC = new ComplexDecimal(re, im);
            }
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            decimal p = Power;

            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                // Обрабатываем сингулярность z=0 в первую очередь.
                if (z.MagnitudeSquared == 0)
                {
                    // Для первой итерации z_next всегда равно c, чтобы избежать 0^(-p).
                    z = c;
                }
                else
                {
                    // Теперь, когда z != 0, можно безопасно выполнять основные расчеты.
                    ComplexDecimal zPower = PowerComplex(z, p);

                    // Используем стандартную формулу Simonobrot: (z^p * |z|^p) + c
                    decimal magnitude = DecimalMath.Sqrt(z.MagnitudeSquared);
                    decimal magnitudePower = DecimalMath.Pow(magnitude, p);
                    z = new ComplexDecimal(zPower.Real * magnitudePower + c.Real,
                                         zPower.Imaginary * magnitudePower + c.Imaginary);
                }

                iter++;
            }

            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;

            // Применяем зеркальное отражение относительно вертикальной оси
            if (UseInversion)
            {
                constantC = new ComplexDouble(-re, im);
            }
            else
            {
                constantC = new ComplexDouble(re, im);
            }
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            double p = (double)Power;

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                // Обрабатываем сингулярность z=0 в первую очередь.
                if (z.MagnitudeSquared == 0)
                {
                    // Для первой итерации z_next всегда равно c, чтобы избежать 0^(-p).
                    z = c;
                }
                else
                {
                    // Теперь, когда z != 0, можно безопасно выполнять основные расчеты.
                    ComplexDouble zPower = PowerComplexDouble(z, p);

                    // Используем стандартную формулу Simonobrot: (z^p * |z|^p) + c
                    double magnitude = z.Magnitude;
                    double magnitudePower = Math.Pow(magnitude, p);
                    z = new ComplexDouble(zPower.Real * magnitudePower + c.Real,
                                        zPower.Imaginary * magnitudePower + c.Imaginary);
                }

                iter++;
            }

            return iter;
        }

        /// <summary>
        /// Возводит комплексное число в степень (<see langword="decimal"/> версия).
        /// </summary>
        /// <param name="z">Комплексное число.</param>
        /// <param name="power">Степень.</param>
        /// <returns>Результат возведения в степень.</returns>
        private ComplexDecimal PowerComplex(ComplexDecimal z, decimal power)
        {
            if (z == ComplexDecimal.Zero) return ComplexDecimal.Zero;

            decimal integerPart = decimal.Truncate(power);
            if (power == integerPart)
            {
                int intPower = (int)integerPart;
                if (intPower >= 0)
                {
                    return PowComplexDecimalInteger(z, intPower);
                }

                return ComplexDecimal.One / PowComplexDecimalInteger(z, -intPower);
            }

            return ComplexDecimal.Pow(z, new ComplexDecimal(power, 0m));
        }

        /// <summary>
        /// Возводит комплексное число в степень (<see langword="double"/> версия).
        /// </summary>
        /// <param name="z">Комплексное число.</param>
        /// <param name="power">Степень.</param>
        /// <returns>Результат возведения в степень.</returns>
        private static ComplexDecimal PowComplexDecimalInteger(ComplexDecimal z, int power)
        {
            if (power == 0) return ComplexDecimal.One;

            ComplexDecimal result = ComplexDecimal.One;
            ComplexDecimal current = z;
            int exponent = power;

            while (exponent > 0)
            {
                if ((exponent & 1) == 1)
                {
                    result *= current;
                }

                current *= current;
                exponent >>= 1;
            }

            return result;
        }

        private ComplexDouble PowerComplexDouble(ComplexDouble z, double power)
        {
            // Проверка на z=0 была вынесена выше, поэтому здесь она не нужна.
            // Используем стандартную быструю реализацию.
            System.Numerics.Complex result = System.Numerics.Complex.Pow(new System.Numerics.Complex(z.Real, z.Imaginary), power);
            return new ComplexDouble(result.Real, result.Imaginary);
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга классического множества Мандельброта (z = z^2 + c).
    /// </summary>
    public class MandelbrotEngine : FractalMandelbrotFamilyEngine
    {
        /// <summary>
        /// Проверяет быструю принадлежность точки внутренним областям классического множества Мандельброта.
        /// </summary>
        /// <remarks>
        /// Формулы отсечения:
        /// 1) Главная кардиоида: q = (x - 1/4)^2 + y^2, q * (q + (x - 1/4)) <= (1/4) * y^2.
        /// 2) Bulb периода 2: (x + 1)^2 + y^2 <= 1/16.
        /// </remarks>
        private static bool IsInsideMainCardioidOrPeriod2Bulb(decimal x, decimal y)
        {
            decimal xMinusQuarter = x - 0.25m;
            decimal ySquared = y * y;
            decimal q = xMinusQuarter * xMinusQuarter + ySquared;
            if (q * (q + xMinusQuarter) <= 0.25m * ySquared)
            {
                return true;
            }

            decimal xPlusOne = x + 1.0m;
            return (xPlusOne * xPlusOne + ySquared) <= 0.0625m;
        }

        /// <summary>
        /// Проверяет быструю принадлежность точки внутренним областям классического множества Мандельброта (double-версия).
        /// </summary>
        private static bool IsInsideMainCardioidOrPeriod2BulbDouble(double x, double y)
        {
            // Формулы отсечения:
            // 1) Главная кардиоида: q = (x - 1/4)^2 + y^2, q * (q + (x - 1/4)) <= (1/4) * y^2.
            // 2) Bulb периода 2: (x + 1)^2 + y^2 <= 1/16.
            double xMinusQuarter = x - 0.25;
            double ySquared = y * y;
            double q = xMinusQuarter * xMinusQuarter + ySquared;
            if (q * (q + xMinusQuarter) <= 0.25 * ySquared)
            {
                return true;
            }

            double xPlusOne = x + 1.0;
            return (xPlusOne * xPlusOne + ySquared) <= 0.0625;
        }

        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров для копирования.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            if (IsInsideMainCardioidOrPeriod2Bulb(c.Real, c.Imaginary))
            {
                return MaxIterations;
            }

            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            if (IsInsideMainCardioidOrPeriod2BulbDouble(c.Real, c.Imaginary))
            {
                return MaxIterations;
            }

            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }


    /// <summary>
    /// Реализует движок для рендеринга фрактала Трикорн (Mandelbar).
    /// Итерационная формула: z_next = conjugate(z)^2 + c.
    /// </summary>
    public class TricornEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров для копирования.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = new ComplexDecimal(z.Real, -z.Imaginary);
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                z = new ComplexDouble(z.Real, -z.Imaginary);
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга множества Жюлиа (z = z^2 + c), где 'c' - константа.
    /// </summary>
    public class JuliaEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров для копирования.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = new ComplexDecimal(re, im);
            constantC = C; // Используется заданная константа C
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = new ComplexDouble(re, im);
            constantC = new ComplexDouble((double)C.Real, (double)C.Imaginary);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга фрактала "Пылающий Корабль" (Mandelbrot-like).
    /// Итерационная формула: z' = |Re(z)| - i*|Im(z)|, z_next = (z')^2 + c.
    /// </summary>
    public class MandelbrotBurningShipEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров для копирования.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = new ComplexDecimal(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                z = new ComplexDouble(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга фрактала "Пылающий Корабль" (Julia-like).
    /// Итерационная формула: z' = |Re(z)| - i*|Im(z)|, z_next = (z')^2 + c.
    /// </summary>
    public class JuliaBurningShipEngine : FractalMandelbrotFamilyEngine
    {
        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            // Для этого движка нет специфичных параметров для копирования.
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = new ComplexDecimal(re, im);
            constantC = C;
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = new ComplexDecimal(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = new ComplexDouble(re, im);
            constantC = new ComplexDouble((double)C.Real, (double)C.Imaginary);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                z = new ComplexDouble(Math.Abs(z.Real), -Math.Abs(z.Imaginary));
                z = z * z + c;
                iter++;
            }
            return iter;
        }
    }

    /// <summary>
    /// Реализует движок для рендеринга Обобщенного множества Мандельброта (z -> z^p + c).
    /// </summary>
    public class GeneralizedMandelbrotEngine : FractalMandelbrotFamilyEngine
    {
        /// <summary>
        /// Получает или задает степень 'p', в которую возводится z.
        /// </summary>
        public decimal Power { get; set; } = 3m;

        /// <inheritdoc />
        public override void CopySpecificParametersFrom(FractalMandelbrotFamilyEngine source)
        {
            if (source is GeneralizedMandelbrotEngine sourceEngine)
            {
                this.Power = sourceEngine.Power;
            }
        }

        /// <inheritdoc />
        protected override void GetCalculationParameters(decimal re, decimal im, out ComplexDecimal initialZ, out ComplexDecimal constantC)
        {
            initialZ = ComplexDecimal.Zero;
            constantC = new ComplexDecimal(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterations(ref ComplexDecimal z, ComplexDecimal c)
        {
            int iter = 0;
            while (iter < MaxIterations && z.MagnitudeSquared <= ThresholdSquared)
            {
                z = ComplexDecimalPow(z, Power) + c;
                iter++;
            }
            return iter;
        }

        /// <inheritdoc />
        protected override void GetCalculationParametersDouble(double re, double im, out ComplexDouble initialZ, out ComplexDouble constantC)
        {
            initialZ = ComplexDouble.Zero;
            constantC = new ComplexDouble(re, im);
        }

        /// <inheritdoc />
        public override int CalculateIterationsDouble(ref ComplexDouble z, ComplexDouble c)
        {
            int iter = 0;
            double thresholdSq = (double)ThresholdSquared;
            decimal power = Power;
            int integerPower = decimal.Truncate(power) == power ? (int)power : -1;

            if (integerPower >= 2 && integerPower <= 8)
            {
                while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
                {
                    z = PowComplexDoubleInteger(z, integerPower) + c;
                    iter++;
                }

                return iter;
            }

            // Для дробных и экзотических степеней сохраняем прежнее поведение через System.Numerics.Complex.Pow.
            double powerDouble = (double)power;
            Complex cNumerics = new Complex(c.Real, c.Imaginary);

            while (iter < MaxIterations && z.MagnitudeSquared <= thresholdSq)
            {
                Complex zNumerics = Complex.Pow(new Complex(z.Real, z.Imaginary), powerDouble) + cNumerics;
                z = new ComplexDouble(zNumerics.Real, zNumerics.Imaginary);
                iter++;
            }

            return iter;
        }

        private static ComplexDouble PowComplexDoubleInteger(ComplexDouble z, int power)
        {
            switch (power)
            {
                case 2:
                    return z * z;
                case 3:
                    {
                        ComplexDouble z2 = z * z;
                        return z2 * z;
                    }
                case 4:
                    {
                        ComplexDouble z2 = z * z;
                        return z2 * z2;
                    }
                case 5:
                    {
                        ComplexDouble z2 = z * z;
                        ComplexDouble z4 = z2 * z2;
                        return z4 * z;
                    }
                case 6:
                    {
                        ComplexDouble z2 = z * z;
                        ComplexDouble z4 = z2 * z2;
                        return z4 * z2;
                    }
                case 7:
                    {
                        ComplexDouble z2 = z * z;
                        ComplexDouble z4 = z2 * z2;
                        ComplexDouble z6 = z4 * z2;
                        return z6 * z;
                    }
                case 8:
                    {
                        ComplexDouble z2 = z * z;
                        ComplexDouble z4 = z2 * z2;
                        return z4 * z4;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(power), "Поддерживаются только степени 2..8.");
            }
        }

        /// <summary>
        /// Возводит комплексное число высокой точности в указанную степень.
        /// </summary>
        /// <param name="z">Комплексное число для возведения в степень.</param>
        /// <param name="power">Степень.</param>
        /// <returns>Результат <paramref name="z"/> в степени <paramref name="power"/>.</returns>
        private ComplexDecimal ComplexDecimalPow(ComplexDecimal z, decimal power)
        {
            if (z == ComplexDecimal.Zero) return ComplexDecimal.Zero;

            decimal integerPart = decimal.Truncate(power);
            if (power == integerPart)
            {
                int intPower = (int)integerPart;
                if (intPower >= 0)
                {
                    return PowComplexDecimalInteger(z, intPower);
                }

                return ComplexDecimal.One / PowComplexDecimalInteger(z, -intPower);
            }

            return ComplexDecimal.Pow(z, new ComplexDecimal(power, 0m));
        }

        private static ComplexDecimal PowComplexDecimalInteger(ComplexDecimal z, int power)
        {
            if (power == 0) return ComplexDecimal.One;

            ComplexDecimal result = ComplexDecimal.One;
            ComplexDecimal current = z;
            int exponent = power;

            while (exponent > 0)
            {
                if ((exponent & 1) == 1)
                {
                    result *= current;
                }

                current *= current;
                exponent >>= 1;
            }

            return result;
        }
    }

    #endregion
}
