using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Третья ступень точности Коллатца — прямая итерация в <see cref="BigFloat"/>.
///
/// Почему именно прямая итерация, а не пертурбация, как у семейства Мандельброта. Формула
/// Коллатца трансцендентна: <c>z ← a + b·z + (c + d·z)·cos(πz)</c>. Её производная
/// <c>|f′| ≈ π·|2+5z|·|sin πz|</c> — это десятки за шаг (у z² + c множитель всего |2z| ≈ 2),
/// поэтому отклонение δ от опорной орбиты вырастает до величины самой орбиты за пару десятков
/// итераций, и ребазирование пришлось бы делать почти на каждом шаге. При этом орбита
/// Коллатца не возвращается к началу опорной — ребазировать попросту не на что. Прямая
/// итерация даёт заведомо верный результат, а расплата за неё (арифметика произвольной
/// точности в каждом пикселе) сдерживается тем, что орбиты здесь короткие: за радиус выхода
/// они вылетают за десяток-другой шагов.
///
/// Прежний потолок был ≈1e10 и определялся вовсе не типом координат: и <c>Iterate</c>, и
/// <c>IterateDecimal</c> считали тригонометрию через <c>Math.Cos</c>/<c>Math.Sin</c>, то есть
/// в double. Ступень decimal поднимала точность координат и не поднимала точность самой
/// формулы — поэтому она оставлена как есть (её полоса 2e9…1e10 быстрая и честная), а
/// BigFloat включается ровно там, где она перестаёт быть честной.
/// </summary>
public static partial class CollatzRenderer
{
    /// <summary>
    /// Зум, выше которого включается ступень BigFloat. Ниже — ровно прежнее поведение:
    /// double до 2e9 и decimal до этого порога.
    /// </summary>
    private const double BigFloatZoomThreshold = 1e10;

    /// <summary>
    /// Базовый запас точности сверх «цифр зума»: 53 бита — столько значащих бит несёт double
    /// на зуме 1, то есть ровно то качество, которое даёт мелкий зум сегодня, плюс 32 бита
    /// запаса на округления внутри шага. Итог: на любой глубине ступень BigFloat считает
    /// орбиту не хуже, чем double считает её на зуме 1.
    /// </summary>
    private const int BaselinePrecisionBits = 53 + 32;

    // Буфер истории орбиты для режимов с поиском циклов: BigFloat содержит ссылочное поле,
    // поэтому stackalloc (как в double- и decimal-ступенях) здесь невозможен.
    [ThreadStatic] private static ComplexBigFloat[]? _deepHistory;

    /// <summary>
    /// Шов для проверок: позволяет включить или выключить ступень BigFloat независимо от
    /// зума и сравнить её с double-ступенью на одном и том же кадре. В приложении всегда null.
    /// </summary>
    internal static bool? ForceBigFloatForTests;

    /// <summary>
    /// Шов для проверок: подменяет рабочую точность кадра. Нужен, чтобы сравнить кадр,
    /// посчитанный по штатному плану точности, с кадром на заведомо избыточной точности —
    /// это единственная проверка самого плана, для которой не нужен внешний эталон.
    /// В приложении всегда null.
    /// </summary>
    internal static int? ForcePrecisionBitsForTests;

    internal static bool UsesBigFloat(CollatzState state) =>
        ForceBigFloatForTests ?? state.Zoom > BigFloatZoomThreshold;

    /// <summary>
    /// Рабочая точность мантиссы для кадра: цифры зума + запас
    /// <see cref="BaselinePrecisionBits"/> + поправка на длину орбиты. Округляется вверх до
    /// 32 бит и не опускается ниже 128 — иначе ступень не имела бы смысла против decimal.
    ///
    /// Верхний потолок 4096 — чисто защитный: при потолке зума 1e50 (<c>CollatzWindow.MaxZoom</c>)
    /// и любом практическом числе итераций формула никогда не поднимается выше нескольких
    /// сотен бит, так что потолок недостижим через интерфейс. Число подобрано не произвольно:
    /// оно совпадает с границей, на которую рассчитана ёмкость <see cref="BigMantissa"/> (см.
    /// её описание и <see cref="BigFloat"/>) — собственной мантиссы без выделений памяти в
    /// куче, пришедшей на смену <see cref="System.Numerics.BigInteger"/> ради устранения
    /// главного источника аллокаций на этой ступени (см. историю коммитов).
    /// </summary>
    internal static int PlanPrecisionBits(CollatzState state)
    {
        double zoom = Math.Max(1, state.Zoom);
        int zoomBits = (int)Math.Ceiling(Math.Log2(zoom));
        int iterations = Math.Max(1, state.Iterations);
        int iterationBits = 2 * (32 - System.Numerics.BitOperations.LeadingZeroCount((uint)iterations));
        int needed = zoomBits + iterationBits + BaselinePrecisionBits;
        return ForcePrecisionBitsForTests ?? Math.Clamp((needed + 31) / 32 * 32, 128, 4096);
    }

    /// <summary>
    /// Геометрия кадра в произвольной точности. Центр берётся из точных строк состояния,
    /// когда они есть (глубокий зум ведёт центр в BigFloat), иначе — из decimal-полей.
    /// Шаг между пикселями считается один раз, поэтому на пиксель приходится одно умножение
    /// и одно сложение.
    /// </summary>
    private readonly struct DeepGeometry
    {
        private readonly BigFloat _centerX;
        private readonly BigFloat _centerY;
        private readonly BigFloat _step;
        private readonly BigFloat _inverseStep;
        private readonly double _halfWidth;
        private readonly double _halfHeight;

        public DeepGeometry(CollatzState state, int width, int height)
        {
            _centerX = ParseCenter(state.CenterXExact, state.CenterX);
            _centerY = ParseCenter(state.CenterYExact, state.CenterY);
            BigFloat scale = BigFloat.FromInt(4) / BigFloat.FromDouble(Math.Max(1e-15, state.Zoom));
            _step = scale / Math.Max(1, width);
            _inverseStep = BigFloat.One / _step;
            _halfWidth = width / 2.0;
            _halfHeight = height / 2.0;
        }

        public BigFloat Real(int x) => _centerX + _step * BigFloat.FromDouble(x - _halfWidth);

        public BigFloat Imaginary(int y) => _centerY - _step * BigFloat.FromDouble(y - _halfHeight);

        public bool TryMapToPixel(ComplexBigFloat z, int width, int height, out int pixelIndex)
        {
            double canvasX = ((z.Real - _centerX) * _inverseStep).ToDouble() + _halfWidth;
            double canvasY = _halfHeight - ((z.Imaginary - _centerY) * _inverseStep).ToDouble();
            if (!double.IsFinite(canvasX) || !double.IsFinite(canvasY) ||
                canvasX < 0 || canvasX >= width || canvasY < 0 || canvasY >= height)
            {
                pixelIndex = 0;
                return false;
            }
            pixelIndex = (int)canvasY * width + (int)canvasX;
            return true;
        }

        private static BigFloat ParseCenter(string? exact, decimal fallback) =>
            exact is { Length: > 0 } text ? BigFloat.Parse(text) : BigFloat.FromDecimal(fallback);
    }

    /// <summary>Не зависящие от пикселя величины формулы и критериев выхода.</summary>
    private readonly struct DeepParameters
    {
        public readonly BigFloat ThresholdSquared;
        public readonly BigFloat ImaginaryLimit;
        public readonly BigFloat PParameter;
        public readonly ComplexBigFloat QParameter;
        public readonly BigFloat CycleTolerance;
        public readonly int MaximumPeriod;

        public DeepParameters(CollatzState state)
        {
            BigFloat threshold = BigFloat.FromDecimal(state.Threshold);
            ThresholdSquared = threshold * threshold;
            // Double-ступень обрывает орбиту по |Im z · π| > 700 (защита от переполнения
            // ch/sh). Тот же порог, только поделённый на π заранее: сравнение на шаге
            // обходится без умножения.
            ImaginaryLimit = BigFloat.FromInt(700) / BigFloatMath.Pi;
            PParameter = BigFloat.FromDecimal(state.PParameter);
            QParameter = ComplexBigFloat.FromDecimal(state.QRealParameter, state.QImaginaryParameter);
            CycleTolerance = BigFloat.FromDouble(Math.Clamp(state.CycleTolerance, 1e-12, 0.1));
            MaximumPeriod = Math.Clamp(state.MaximumDetectedPeriod, 1, MaximumSupportedPeriod);
        }

        public bool IsInside(ComplexBigFloat z) =>
            z.MagnitudeSquared <= ThresholdSquared && BigFloat.Abs(z.Imaginary) <= ImaginaryLimit;
    }

    private static void RenderDeep(CollatzState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress)
    {
        int precisionBits = PlanPrecisionBits(state);
        long completed = 0;

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threadCount },
            (y, loopState) =>
            {
                if (token.IsCancellationRequested) { loopState.Stop(); return; }
                using var precision = new BigFloat.PrecisionScope(precisionBits);
                var geometry = new DeepGeometry(state, width, height);
                var parameters = new DeepParameters(state);
                int row = y * stride;
                BigFloat imaginary = geometry.Imaginary(y);
                for (int x = 0; x < width; x++)
                {
                    // Пиксель этой ступени стоит миллисекунды, поэтому флаг отмены читается
                    // на каждом — задержка отклика перестаёт зависеть от глубины.
                    if (token.IsCancellationRequested) { loopState.Stop(); return; }
                    var z = new ComplexBigFloat(geometry.Real(x), imaginary);
                    Color color = ResolveColor(state, IterateDeep(z, state, parameters));
                    int offset = row + x * 4;
                    pixels[offset] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = color.A;
                }
                int rows = (int)Interlocked.Increment(ref completed);
                if (rows == height || rows % Math.Max(1, height / 100) == 0)
                    progress?.Invoke(rows * 100 / height);
            });
    }

    private static bool RenderTileDeep(CollatzState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, byte[] pixels, CancellationToken token)
    {
        using var precision = new BigFloat.PrecisionScope(PlanPrecisionBits(state));
        var geometry = new DeepGeometry(state, canvasWidth, canvasHeight);
        var parameters = new DeepParameters(state);

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return false;
            BigFloat imaginary = geometry.Imaginary(tile.Y + localY);
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if (token.IsCancellationRequested) return false;
                var z = new ComplexBigFloat(geometry.Real(tile.X + localX), imaginary);
                Color color = ResolveColor(state, IterateDeep(z, state, parameters));
                int offset = (localY * tile.Width + localX) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = color.A;
            }
        }
        return true;
    }

    /// <summary>
    /// Итерация орбиты в произвольной точности. Повторяет <c>Iterate</c> шаг в шаг, включая
    /// порядок проверок выхода и набор накапливаемых метрик.
    /// </summary>
    private static OrbitMetrics IterateDeep(ComplexBigFloat z, CollatzState state, in DeepParameters parameters)
    {
        int maximum = state.Iterations;
        bool trackIntegerTrap = state.ColoringMode == CollatzColoringMode.IntegerTrap;
        bool trackRealAxisTrap = state.ColoringMode == CollatzColoringMode.RealAxisTrap;
        bool detectCycles = state.ColoringMode is CollatzColoringMode.CycleBasins or
            CollatzColoringMode.PeriodDetection;
        ComplexBigFloat[] history = detectCycles
            ? _deepHistory ??= new ComplexBigFloat[MaximumSupportedPeriod + 1]
            : [];
        int historyCount = 0;
        int candidatePeriod = 0;
        int candidateHits = 0;
        int detectedPeriod = 0;
        double cycleKey = 0;
        double integerTrap = trackIntegerTrap ? DistanceToInteger(z) : double.PositiveInfinity;
        double realAxisTrap = trackRealAxisTrap ? Math.Abs(z.Imaginary.ToDouble()) : double.PositiveInfinity;
        int iteration = 0;

        if (detectCycles)
        {
            history[0] = z;
            historyCount = 1;
        }

        while (iteration < maximum)
        {
            if (!parameters.IsInside(z)) break;

            ComplexBigFloat previous = z;
            z = ApplyFormula(z, state.Variation, parameters.PParameter, parameters.QParameter);
            iteration++;
            if (trackIntegerTrap) integerTrap = Math.Min(integerTrap, DistanceToInteger(z));
            if (trackRealAxisTrap) realAxisTrap = Math.Min(realAxisTrap, Math.Abs(z.Imaginary.ToDouble()));

            if (!detectCycles)
            {
                // Орбита села в неподвижную точку в точности: следующий шаг вернёт то же
                // самое число, и так до конца отпущенных итераций. Досчитывать их незачем —
                // результат от досчёта не зависит ни в одной метрике. Внутренние области
                // (а именно они и стоят полной длины орбиты) так обходятся в единицы шагов
                // вместо сотни. Если же точка притяжения лежит за радиусом выхода, счётчик
                // не трогаем: цикл всё равно оборвётся на ближайшей проверке.
                if (z.Equals(previous))
                {
                    if (parameters.IsInside(z)) iteration = maximum;
                    break;
                }
                continue;
            }

            int matchedPeriod = FindPeriod(z, history, historyCount, parameters.MaximumPeriod,
                parameters.CycleTolerance);
            if (matchedPeriod == candidatePeriod && matchedPeriod > 0)
                candidateHits++;
            else
            {
                candidatePeriod = matchedPeriod;
                candidateHits = matchedPeriod > 0 ? 1 : 0;
            }

            if (candidateHits >= 2)
            {
                detectedPeriod = candidatePeriod;
                cycleKey = CalculateCycleKey(z, history, historyCount, detectedPeriod);
                break;
            }

            history[historyCount % history.Length] = z;
            historyCount++;
        }

        bool isInterior = detectedPeriod > 0 || iteration >= maximum && parameters.IsInside(z);
        Complex final = z.ToComplex();
        return new OrbitMetrics(iteration, Smooth(iteration, maximum, final), final,
            integerTrap, realAxisTrap, detectedPeriod, cycleKey, isInterior);
    }

    internal static ComplexBigFloat ApplyFormula(ComplexBigFloat z, CollatzVariation variation,
        BigFloat p, ComplexBigFloat q)
    {
        switch (variation)
        {
            case CollatzVariation.SineVariation:
                return ComplexBigFloat.ScaleByPowerOfTwo(
                    2 + z * 7 - (2 + z * 5) * ComplexBigFloat.SinPi(z), -2);

            case CollatzVariation.ParityBranchVariation:
            {
                ComplexBigFloat scaled = z * (p - BigFloat.One);
                return ComplexBigFloat.ScaleByPowerOfTwo(
                    scaled + 1 - (scaled - 1) * ComplexBigFloat.CosPi(z), -1);
            }

            case CollatzVariation.GeneralizedP:
                return GeneralizedCollatz(z, p, ComplexBigFloat.CosPi(z));

            case CollatzVariation.GeneralizedPQ:
            {
                ComplexBigFloat.SinCosPi(z, out ComplexBigFloat sine, out ComplexBigFloat cosine);
                return GeneralizedCollatz(z, p, cosine) + q * sine;
            }

            default:
                return ComplexBigFloat.ScaleByPowerOfTwo(
                    2 + z * 7 - (2 + z * 5) * ComplexBigFloat.CosPi(z), -2);
        }
    }

    private static ComplexBigFloat GeneralizedCollatz(ComplexBigFloat z, BigFloat p, ComplexBigFloat cosine)
    {
        BigFloat doubled = BigFloat.ScaleByPowerOfTwo(p, 1);
        ComplexBigFloat odd = 2 + z * (doubled + BigFloat.One);
        ComplexBigFloat even = 2 + z * (doubled - BigFloat.One);
        return ComplexBigFloat.ScaleByPowerOfTwo(odd - even * cosine, -2);
    }

    private static int FindPeriod(ComplexBigFloat z, ComplexBigFloat[] history, int historyCount,
        int maximumPeriod, BigFloat tolerance)
    {
        int available = Math.Min(maximumPeriod, historyCount);
        BigFloat magnitude = Larger(BigFloat.One,
            Larger(BigFloat.Abs(z.Real), BigFloat.Abs(z.Imaginary)));
        BigFloat limit = tolerance * magnitude;
        for (int period = 1; period <= available; period++)
        {
            ComplexBigFloat previous = history[(historyCount - period) % history.Length];
            BigFloat difference = Larger(BigFloat.Abs(z.Real - previous.Real),
                BigFloat.Abs(z.Imaginary - previous.Imaginary));
            if (difference <= limit) return period;
        }
        return 0;
    }

    private static double CalculateCycleKey(ComplexBigFloat z, ComplexBigFloat[] history,
        int historyCount, int period)
    {
        ComplexBigFloat centroid = z;
        for (int offset = 1; offset < period; offset++)
            centroid += history[(historyCount - offset) % history.Length];
        return CalculateCycleKey((centroid / period).ToComplex(), period);
    }

    private static double DistanceToInteger(ComplexBigFloat z)
    {
        BigFloat fraction = z.Real - BigFloatMath.Round(z.Real);
        return Complex.Abs(new Complex(fraction.ToDouble(), z.Imaginary.ToDouble()));
    }

    private static BigFloat Larger(BigFloat left, BigFloat right) => left >= right ? left : right;

    private static int TraceDensityOrbitDeep(CollatzState state, in DeepGeometry geometry,
        in DeepParameters parameters, int x, int y, int width, int height, int[] path,
        CancellationToken token, out bool escaped)
    {
        var z = new ComplexBigFloat(geometry.Real(x), geometry.Imaginary(y));
        int iteration = 0;
        int pathLength = 0;
        while (iteration < state.Iterations)
        {
            if (token.IsCancellationRequested) break;
            if (!parameters.IsInside(z)) break;
            ComplexBigFloat previous = z;
            z = ApplyFormula(z, state.Variation, parameters.PParameter, parameters.QParameter);
            iteration++;
            bool mapped = geometry.TryMapToPixel(z, width, height, out int pixelIndex);
            if (mapped) path[pathLength++] = pixelIndex;

            // Неподвижная точка: каждый оставшийся шаг добавил бы в плотность ровно тот же
            // пиксель. Добавляем их разом — вклад в поле плотности совпадает с честным
            // досчётом, а сотня шагов арифметики произвольной точности не тратится.
            if (!z.Equals(previous)) continue;
            if (!parameters.IsInside(z)) break;
            while (iteration < state.Iterations)
            {
                iteration++;
                if (mapped) path[pathLength++] = pixelIndex;
            }
            break;
        }
        escaped = iteration < state.Iterations && !token.IsCancellationRequested;
        return pathLength;
    }
}
