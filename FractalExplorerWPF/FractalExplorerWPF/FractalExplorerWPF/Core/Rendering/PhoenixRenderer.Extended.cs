using System.Globalization;
using System.Runtime.CompilerServices;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Сверхглубокая ступень Феникса: зум за пределами того, что держит δ в double. Само ядро —
/// общее <see cref="DeepZoomPixel{TMode}"/> в гибридном режиме; здесь — план, переходы между
/// представлениями δ и линейный пропуск.
///
/// <para><b>Почему потолок был не в методе, а в представлении.</b> Прежний потолок окна 1e24
/// был измерен на кадре, где весь кадр вылетает за радиус на одном шаге: там разброс |z|² по
/// кадру на шаге решения меньше 2⁻⁵² относительно, и такой кадр не различит никакой рендер,
/// ведущий z в double, — точный эталон различает его только потому, что итерирует z в
/// BigFloat. На кадрах со структурой расхождение с эталоном не растёт с глубиной: у
/// пертурбации точность относительная, и перенос пары <c>(δ, η)</c> в
/// <see cref="TryRebase"/> принимается лишь тогда, когда информация уже усилена до порядка
/// самих значений. Настоящие стены — диапазон double: зум и сетка кадра за 1.8e308, а δᴾ — за
/// <c>2^(−1022/P)</c>, где старшие члены бинома уходят в денормалы и в ноль ровно тогда, когда
/// опорная орбита проходит у нуля ближе, чем на δ.</para>
///
/// <para><b>Гибридное ядро.</b> δ и η ведутся в <see cref="FloatExp"/>, пока малы, и
/// переходят в double, как только double их представляет без потерь (см.
/// <see cref="SafeInDouble"/>). Обратный переход — если δ снова стало мало и опорная точка
/// близка к нулю. Шаг в обоих режимах — один и тот же обобщённый
/// <see cref="PerturbStep{TDelta}"/>.</para>
///
/// <para><b>Линейный пропуск.</b> Пока <c>|δₙ| ≲ 2⁻⁵⁶·|Zₙ|</c>, шаг возмущения линеен в
/// пределах разрядности double, а восстановленное <c>z = Z + δ</c> совпадает с опорным
/// значением. Значит, начало орбиты у всех пикселей описывается одной матрицей
/// переноса <c>δₙ = Dₙ·p</c> (p — смещение пикселя), а метрики окраски за это время — метриками
/// опорной орбиты. На глубине 1e1000 это почти вся орбита: без пропуска первые ~10⁴ итераций
/// шли бы в медленном FloatExp. Матрицы и префиксы метрик считаются один раз на кадр
/// (<see cref="GetLinearSkipTable"/>). Вещественные 2×2 вместо комплексного множителя —
/// потому что свёртки знака и сопряжение линейны только над ℝ.</para>
/// </summary>
public static partial class PhoenixRenderer
{
    private readonly record struct DeepZoomPlan(int ReferenceBits, bool UseExtendedDelta);

    /// <summary>
    /// Шов для проверок: включает гибридное ядро независимо от зума — чтобы сравнить его с
    /// double-ядром в полосе, где верны оба. В приложении всегда null.
    /// </summary>
    internal static bool? ForceExtendedDeltaForTests { get; set; }

    /// <summary>
    /// Шов для проверок: false отключает линейный пропуск, и гибридное ядро шагает всю орбиту
    /// само. Сравнение «с пропуском / без» — единственная проверка пропуска, не требующая
    /// внешнего эталона. В приложении всегда null.
    /// </summary>
    internal static bool? ForceLinearSkipForTests { get; set; }

    /// <summary>
    /// Диагностика для проверок: сколько итераций всего пропущено линейным пропуском. Нужна,
    /// чтобы проверка «с пропуском совпадает с пошаговым» не прошла вхолостую на кадре, где
    /// пропуск не сработал ни разу.
    /// </summary>
    internal static long SkippedIterationsForTests;

    /// <summary>
    /// Двоичный порядок зума, начиная с которого кадр считает гибридное ядро:
    /// <c>900/P</c>, P = <c>max(2, a, b)</c>. Смещение пикселя порядка <c>2^(−порог)</c>, и
    /// ниже порога δᴾ ≥ 2⁻⁹⁰⁰ — с запасом выше денормалов. Для квадратичной формулы это
    /// 2⁴⁵⁰ ≈ 3e135, для a = 12 — 2⁷⁵ ≈ 4e22.
    /// </summary>
    internal static int ExtendedDeltaBits(int deltaPower) => 900 / Math.Max(2, deltaPower);

    private static DeepZoomPlan PlanDeepZoom(PhoenixState state)
    {
        int power = Math.Max(2, Math.Max(state.PrimaryPower, state.SecondaryPower));
        bool extended = ForceExtendedDeltaForTests ?? PerturbationReference.ZoomBits(state.Zoom) >= ExtendedDeltaBits(power);
        return new DeepZoomPlan(PlanReferenceBits(state), extended);
    }

    // ------------------------------------------------------------------ linear skip

    /// <summary>
    /// Допуск линейного пропуска: отброшенный член второго порядка относительно линейного —
    /// не больше этой доли (с поправкой на биномиальный коэффициент старшей степени).
    /// </summary>
    private static readonly double LinearSkipTolerance = Math.ScaleB(1.0, -56);

    private sealed class LinearSkipTable
    {
        /// <summary>Матрица переноса: <c>δₙ = Dₙ·p</c> для n = 0..<see cref="Limit"/>.</summary>
        public required FloatExp[] D00, D01, D10, D11;

        /// <summary>
        /// <c>|p| ≤ Radius[n]</c> ⇒ шаги 0..n линейны для этого пикселя. Префиксный минимум,
        /// поэтому массив не возрастает и допускает двоичный поиск.
        /// </summary>
        public required FloatExp[] Radius;

        /// <summary>Метрики окраски, накопленные за итерации 0..N−1 опорной орбиты (индекс N).</summary>
        public required double[] Trap, Stripe, Triangle;

        /// <summary>
        /// Наибольшая длина пропуска: не дальше выхода опорной точки к радиусу, найденного на
        /// ней периода, исчерпания орбиты и числа итераций.
        /// </summary>
        public required int Limit;

        /// <summary>Наибольшее N ≤ <see cref="Limit"/>, для которого шаги 0..N−1 линейны.</summary>
        public int FindSkip(FloatExp pixelMagnitude)
        {
            int low = 0, high = Limit;
            while (low < high)
            {
                int middle = (low + high + 1) >> 1;
                if (Radius[middle - 1] >= pixelMagnitude) low = middle;
                else high = middle - 1;
            }
            return low;
        }
    }

    private static readonly object _skipLock = new();
    private static ReferenceOrbit? _skipOrbit;
    private static string? _skipKey;
    private static LinearSkipTable? _skipCache;

    /// <summary>
    /// Таблица пропуска кадра. Кэш привязан к объекту опорной орбиты (её ключ уже несёт центр,
    /// зум, формулу и плоскость) и к параметрам, которые орбита не учитывает, но которые
    /// входят в метрики пропуска: радиус выхода и настройки окраски.
    /// </summary>
    private static LinearSkipTable? GetLinearSkipTable(
        PhoenixState state, ReferenceOrbit orbit, in DeepParameters parameters)
    {
        if (ForceLinearSkipForTests == false) return null;

        string key = string.Join('|',
            state.Iterations.ToString(CultureInfo.InvariantCulture),
            state.Threshold.ToString(CultureInfo.InvariantCulture),
            ((int)state.ColoringMode).ToString(CultureInfo.InvariantCulture),
            ((int)state.OrbitTrapMode).ToString(CultureInfo.InvariantCulture),
            state.OrbitTrapRadius.ToString("R", CultureInfo.InvariantCulture),
            state.StripeFrequency.ToString("R", CultureInfo.InvariantCulture),
            state.CycleTolerance.ToString("R", CultureInfo.InvariantCulture),
            state.MaximumDetectedPeriod.ToString(CultureInfo.InvariantCulture));

        lock (_skipLock)
        {
            if (ReferenceEquals(_skipOrbit, orbit) && _skipKey == key && _skipCache is not null)
                return _skipCache;

            LinearSkipTable table = BuildLinearSkipTable(state, orbit, parameters);
            _skipOrbit = orbit;
            _skipKey = key;
            _skipCache = table;
            return table;
        }
    }

    private static LinearSkipTable BuildLinearSkipTable(
        PhoenixState state, ReferenceOrbit orbit, in DeepParameters parameters)
    {
        // Шаг n читает Orbit[n+1] и Orbit[n+2]; кроме того, после пропуска ядро не должно
        // оказаться у самого конца орбиты, где TryRebase переносит пару безусловно.
        int capacity = Math.Max(0, Math.Min(state.Iterations - 1, orbit.Length - 3));
        var d00 = new FloatExp[capacity + 1];
        var d01 = new FloatExp[capacity + 1];
        var d10 = new FloatExp[capacity + 1];
        var d11 = new FloatExp[capacity + 1];
        var radius = new FloatExp[Math.Max(1, capacity)];
        var trap = new double[capacity + 1];
        var stripe = new double[capacity + 1];
        var triangle = new double[capacity + 1];

        bool detectPeriods = state.ColoringMode == PhoenixColoringMode.Period;
        bool trackTrap = state.ColoringMode == PhoenixColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == PhoenixColoringMode.StripeAverage;
        bool trackTriangle = state.ColoringMode == PhoenixColoringMode.TriangleInequalityAverage;
        int maximumPeriod = Math.Clamp(state.MaximumDetectedPeriod, 1, MaximumSupportedPeriod);
        double toleranceSquared = Math.Pow(Math.Max(1e-14, state.CycleTolerance), 2);

        // Запас на радиусе выхода: на пропущенных шагах |z|² отличается от |Z|² не больше чем
        // в (1 + 2⁻⁵⁵) раз, поэтому решение «вышел / не вышел» у самого радиуса оставляем ядру.
        double escapeSquared = parameters.ThresholdSquared * (1 - 1e-9);
        double tolerance = LinearSkipTolerance / ((parameters.DeltaPower - 1) / 2.0 + 1);

        double c1Real = parameters.C1Real, c1Imaginary = parameters.C1Imaginary;
        double c2Real = parameters.C2Real, c2Imaginary = parameters.C2Imaginary;
        bool parameterPlane = parameters.ParameterPlane;

        // Dₙ (a) и Dₙ₋₁ (b). В динамической плоскости пиксель — это δ₀, в параметрической —
        // δc1, а δ₀ = 0.
        FloatExp a00 = parameterPlane ? FloatExp.Zero : FloatExp.One, a01 = FloatExp.Zero;
        FloatExp a10 = FloatExp.Zero, a11 = a00;
        FloatExp b00 = FloatExp.Zero, b01 = FloatExp.Zero, b10 = FloatExp.Zero, b11 = FloatExp.Zero;
        d00[0] = a00; d01[0] = a01; d10[0] = a10; d11[0] = a11;
        trap[0] = double.MaxValue;

        FloatExp running = FloatExp.FromDouble(double.PositiveInfinity);
        int limit = 0;

        for (int n = 0; n < capacity; n++)
        {
            double zReal = orbit.Re[n + 1], zImaginary = orbit.Im[n + 1];
            double magnitudeSquared = zReal * zReal + zImaginary * zImaginary;
            if (!(magnitudeSquared <= escapeSquared)) break;

            FloatExp norm = FloatExp.Sqrt(a00 * a00 + a01 * a01 + a10 * a10 + a11 * a11);
            FloatExp admissible = norm.IsZero
                ? FloatExp.FromDouble(double.PositiveInfinity)
                : FloatExp.FromDouble(tolerance * LinearMargin(zReal, zImaginary, parameters)) / norm;
            if (admissible < running) running = admissible;
            radius[n] = running;
            if (running.IsZero) break;

            // Метрики итерации n — на опорной точке, в том же порядке, что в ядре.
            var current = new ComplexValue(zReal, zImaginary);
            trap[n + 1] = trackTrap ? Math.Min(trap[n], OrbitTrapDistance(state, current)) : trap[n];
            stripe[n + 1] = trackStripe
                ? stripe[n] + (0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2(zImaginary, zReal)))
                : stripe[n];
            double nextReal = orbit.Re[n + 2], nextImaginary = orbit.Im[n + 2];
            triangle[n + 1] = triangle[n];
            if (trackTriangle)
            {
                double edgeLength = Distance(new ComplexValue(nextReal, nextImaginary), current);
                if (double.IsFinite(edgeLength) && edgeLength > 1e-300)
                {
                    double triangleRatio =
                        (Math.Sqrt(nextReal * nextReal + nextImaginary * nextImaginary) - current.Magnitude) / edgeLength;
                    triangle[n + 1] = triangle[n] + (0.5 + 0.5 * Math.Clamp(triangleRatio, -1, 1));
                }
            }

            // Dₙ₊₁ = Lₙ·Dₙ + C2·Dₙ₋₁ (+ G(Zₙ) в параметрической плоскости), Lₙ = F′ + C1·G′.
            LinearizeVariantPower(zReal, zImaginary, parameters.PrimaryPower, parameters.Variant,
                out double f00, out double f01, out double f10, out double f11, out _, out _);
            LinearizeVariantPower(zReal, zImaginary, parameters.SecondaryPower, parameters.Variant,
                out double g00, out double g01, out double g10, out double g11,
                out double gReal, out double gImaginary);
            double l00 = f00 + (c1Real * g00 - c1Imaginary * g10);
            double l01 = f01 + (c1Real * g01 - c1Imaginary * g11);
            double l10 = f10 + (c1Imaginary * g00 + c1Real * g10);
            double l11 = f11 + (c1Imaginary * g01 + c1Real * g11);

            FloatExp n00 = l00 * a00 + l01 * a10 + (c2Real * b00 - c2Imaginary * b10);
            FloatExp n01 = l00 * a01 + l01 * a11 + (c2Real * b01 - c2Imaginary * b11);
            FloatExp n10 = l10 * a00 + l11 * a10 + (c2Imaginary * b00 + c2Real * b10);
            FloatExp n11 = l10 * a01 + l11 * a11 + (c2Imaginary * b01 + c2Real * b11);
            if (parameterPlane)
            {
                n00 += gReal;
                n01 += -gImaginary;
                n10 += gImaginary;
                n11 += gReal;
            }

            b00 = a00; b01 = a01; b10 = a10; b11 = a11;
            a00 = n00; a01 = n01; a10 = n10; a11 = n11;
            d00[n + 1] = a00; d01[n + 1] = a01; d10[n + 1] = a10; d11[n + 1] = a11;

            // Период, найденный на опорной точке на итерации n+1, пиксель обязан найти сам:
            // пропуск останавливается до этой проверки.
            if (detectPeriods && n + 1 >= 4 &&
                ReferenceDetectsPeriod(orbit, n + 1, maximumPeriod, toleranceSquared))
                break;

            limit = n + 1;
        }

        return new LinearSkipTable
        {
            D00 = d00, D01 = d01, D10 = d10, D11 = d11,
            Radius = radius, Trap = trap, Stripe = stripe, Triangle = triangle,
            Limit = limit
        };
    }

    /// <summary>
    /// Проверка периода ядра, выполненная на самой опорной орбите после итерации
    /// <paramref name="iteration"/>: <c>zₖ = Orbit[k+1]</c>, <c>zₖ₋₁ = Orbit[k]</c>.
    /// </summary>
    private static bool ReferenceDetectsPeriod(ReferenceOrbit orbit, int iteration, int maximumPeriod,
        double toleranceSquared)
    {
        var current = new ComplexValue(orbit.Re[iteration + 1], orbit.Im[iteration + 1]);
        var previous = new ComplexValue(orbit.Re[iteration], orbit.Im[iteration]);
        int available = Math.Min(maximumPeriod, iteration - 1);
        for (int period = 1; period <= available; period++)
        {
            if (iteration < period * 2) continue;
            int past = iteration - period;
            if (Close(current, new ComplexValue(orbit.Re[past + 1], orbit.Im[past + 1]), toleranceSquared) &&
                Close(previous, new ComplexValue(orbit.Re[past], orbit.Im[past]), toleranceSquared))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Насколько велико может быть |δ| относительно опорной точки, чтобы шаг оставался
    /// линейным: |Z| для аналитических вариантов; меньшая из |Re Z|, |Im Z| — у свёрток знака
    /// компонент (иначе δ перевернёт знак); у Celtic дополнительно — запас до смены знака
    /// <c>Re F(Z)</c>, который сворачивается уже на результате степени.
    /// </summary>
    private static double LinearMargin(double zReal, double zImaginary, in DeepParameters parameters)
    {
        double magnitude = Math.Sqrt(zReal * zReal + zImaginary * zImaginary);
        double margin = magnitude;
        switch (parameters.Variant)
        {
            case PhoenixVariant.BurningShip:
            case PhoenixVariant.Buffalo:
                margin = Math.Min(Math.Abs(zReal), Math.Abs(zImaginary));
                break;
            case PhoenixVariant.Celtic:
                margin = Math.Min(margin, CelticMargin(zReal, zImaginary, magnitude, parameters.PrimaryPower));
                if (parameters.SecondaryPower >= 1)
                    margin = Math.Min(margin, CelticMargin(zReal, zImaginary, magnitude, parameters.SecondaryPower));
                break;
        }
        return double.IsFinite(margin) && margin > 0 ? margin : 0;
    }

    private static double CelticMargin(double zReal, double zImaginary, double magnitude, int power)
    {
        if (power <= 0) return double.PositiveInfinity;
        double powerReal = 1, powerImaginary = 0;
        for (int k = 0; k < power; k++)
            (powerReal, powerImaginary) = (powerReal * zReal - powerImaginary * zImaginary,
                powerReal * zImaginary + powerImaginary * zReal);
        double derivative = power * Math.Pow(magnitude, power - 1);
        return derivative > 0 ? Math.Abs(powerReal) / derivative : 0;
    }

    /// <summary>
    /// Линейная часть <c>VariantPower</c> в точке Z как вещественная матрица 2×2 над
    /// <c>(Re δ, Im δ)</c> и само значение степени. Порядок свёрток — как в
    /// <see cref="PerturbVariantPower"/>: <c>M = T·K·S</c>, где S — свёртка аргумента
    /// (сопряжение или знаки компонент), K — умножение на <c>p·W^(p−1)</c>, T — знак
    /// вещественной части результата у Celtic.
    /// </summary>
    private static void LinearizeVariantPower(double zReal, double zImaginary, int power, PhoenixVariant variant,
        out double m00, out double m01, out double m10, out double m11,
        out double valueReal, out double valueImaginary)
    {
        if (power == 0)
        {
            m00 = m01 = m10 = m11 = 0;
            valueReal = 1;
            valueImaginary = 0;
            return;
        }

        double baseReal, baseImaginary, sign1, sign2;
        switch (variant)
        {
            case PhoenixVariant.Tricorn:
                baseReal = zReal; baseImaginary = -zImaginary; sign1 = 1; sign2 = -1;
                break;
            case PhoenixVariant.BurningShip:
                baseReal = Math.Abs(zReal); baseImaginary = -Math.Abs(zImaginary);
                sign1 = Math.Sign(zReal); sign2 = -Math.Sign(zImaginary);
                break;
            case PhoenixVariant.Buffalo:
                baseReal = Math.Abs(zReal); baseImaginary = Math.Abs(zImaginary);
                sign1 = Math.Sign(zReal); sign2 = Math.Sign(zImaginary);
                break;
            default:
                baseReal = zReal; baseImaginary = zImaginary; sign1 = 1; sign2 = 1;
                break;
        }

        double powerReal = 1, powerImaginary = 0; // W^(p−1)
        for (int k = 1; k < power; k++)
            (powerReal, powerImaginary) = (powerReal * baseReal - powerImaginary * baseImaginary,
                powerReal * baseImaginary + powerImaginary * baseReal);
        valueReal = powerReal * baseReal - powerImaginary * baseImaginary;
        valueImaginary = powerReal * baseImaginary + powerImaginary * baseReal;

        double kReal = power * powerReal, kImaginary = power * powerImaginary;
        double outer = 1;
        if (variant == PhoenixVariant.Celtic)
        {
            outer = Math.Sign(valueReal);
            valueReal = Math.Abs(valueReal);
        }

        m00 = outer * kReal * sign1;
        m01 = -outer * kImaginary * sign2;
        m10 = kImaginary * sign1;
        m11 = kReal * sign2;
    }

    // ------------------------------------------------------------------ hybrid kernel

    // Порог «δ снова мало» ниже порога перехода в double: гистерезис против переключения на
    // каждом шаге у самой границы.
    private const int ModeHysteresisBits = 32;

    // Опорная точка во столько раз больше δ, что члены бинома старше первого пренебрежимы
    // и их уход в ноль ничего не меняет.
    private static readonly double NegligibleNonlinearRatio = Math.ScaleB(1.0, 56);

    // Ниже этого δ в double теряет значащие разряды на подходе к денормалам.
    private static readonly double DoubleDeltaFloor = Math.ScaleB(1.0, -900);

    /// <summary>
    /// double представляет шаг без потерь: либо δ выше порога плана (δᴾ далеко от
    /// денормалов при любой опорной точке), либо опорная точка настолько больше δ, что
    /// нелинейные члены пренебрежимы, а сама δ ещё далеко от денормалов.
    /// </summary>
    private static bool SafeInDouble(FloatExp deltaReal, FloatExp deltaImaginary, int switchExponent,
        double referenceReal, double referenceImaginary)
    {
        int exponent = Math.Max(
            deltaReal.IsZero ? int.MinValue : deltaReal.Exponent,
            deltaImaginary.IsZero ? int.MinValue : deltaImaginary.Exponent);
        if (exponent >= switchExponent) return true;
        if (exponent < -900) return false;
        double deltaMax = Math.Max(Math.Abs(deltaReal.ToDouble()), Math.Abs(deltaImaginary.ToDouble()));
        double referenceMax = Math.Max(Math.Abs(referenceReal), Math.Abs(referenceImaginary));
        return deltaMax >= DoubleDeltaFloor && referenceMax >= deltaMax * NegligibleNonlinearRatio;
    }
}
