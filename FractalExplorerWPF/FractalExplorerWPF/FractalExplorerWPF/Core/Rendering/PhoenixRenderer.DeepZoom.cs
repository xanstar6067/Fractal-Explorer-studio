using System.Globalization;
using System.Runtime.CompilerServices;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Вторая ступень точности Феникса — пертурбационный движок, как у семейства Мандельброта.
///
/// Прежде у Феникса ступени точности не было вовсе: и <see cref="Render"/>, и
/// <see cref="RenderTile"/> считали координаты в double, поэтому картинка распадалась уже
/// около 1e12, хотя поле зума пускало до <c>decimal.MaxValue/2</c>. Теперь выше
/// <see cref="DeepZoomThreshold"/> кадр считает этот движок, а ниже остаётся ровно прежний
/// double-путь — бит-в-бит.
///
/// Почему пертурбация, а не прямая итерация в <see cref="BigFloat"/> (как у Коллатца):
/// формула Феникса полиномиальна,
/// <c>z_{n+1} = F(z_n) + c1·G(z_n) + c2·z_{n-1}</c>, где <c>F</c> и <c>G</c> — целые степени
/// (со свёрткой знака у отражённых вариантов). Возмущение здесь раскладывается точно, а
/// арифметика произвольной точности нужна только один раз на кадр — для опорной орбиты.
///
/// Отличие от Мандельброта — <b>память</b>: рекуррентность второго порядка. Отсюда две
/// особенности, которых нет в <c>MandelbrotFamilyRenderer.DeepZoom</c>:
/// <list type="number">
/// <item>Состояние пикселя — пара <c>(δₙ, δₙ₋₁)</c>, а опорная орбита хранится со сдвигом на
/// единицу: <c>Orbit[i] = Z_{i-1}</c>, то есть <c>Orbit[0] = z₋₁</c>. Тогда паре
/// <c>(Zₙ, Zₙ₋₁)</c> отвечают соседние элементы <c>Orbit[r]</c> и <c>Orbit[r-1]</c>, и
/// ребазирование в начало орбиты не требует отдельно хранить <c>z₋₁</c>.</item>
/// <item>Ребазирование переносит обе компоненты пары сразу. Оно остаётся точным тождеством
/// при любом выборе момента, но полезно лишь когда уменьшает <b>обе</b> величины, — поэтому,
/// в отличие от одномерного случая, сначала проверяется, что новая пара не хуже старой
/// (см. <see cref="TryRebase"/>).</item>
/// </list>
///
/// Все семь режимов окраски обслуживаются одним проходом: каждый читает восстановленное
/// <c>z = Z + δ</c>, а двухпроходных режимов (гистограмма, оценка расстояния) у Феникса нет.
///
/// Здесь — ядро <see cref="DeepZoomPixel{TMode}"/> и общая инфраструктура. До порога
/// <see cref="ExtendedDeltaBits"/> ядро ведёт δ в double, за ним — в гибридном режиме
/// сверхглубокой ступени (<c>PhoenixRenderer.Extended</c>): зум и сетка кадра в
/// <see cref="FloatExp"/>, δ в FloatExp пока мало, и линейный пропуск начала орбиты.
/// </summary>
public static partial class PhoenixRenderer
{
    /// <summary>Зум, выше которого кадр считает пертурбационный движок, — общий порог.</summary>
    internal const double DeepZoomThreshold = PerturbationReference.ZoomThreshold;

    /// <inheritdoc cref="PerturbationReference.EscapeSquared"/>
    private const double ReferenceEscapeSquared = PerturbationReference.EscapeSquared;

    /// <summary>Критерий Pauldelbrot: |z|² ниже этой доли от |Zref|² — опорная точка ненадёжна.</summary>
    private const double GlitchToleranceSquared = PerturbationReference.GlitchToleranceSquared;

    /// <summary>Максимальная степень, которую принимает окно (a ∈ [2,12], b ∈ [0,12]).</summary>
    private const int MaximumPower = 12;

    /// <summary>
    /// Шов для проверок: включает или выключает движок независимо от зума, чтобы сравнить его
    /// с плоской ступенью на одном и том же кадре. В приложении всегда null.
    /// </summary>
    internal static bool? ForceDeepZoomForTests { get; set; }

    /// <summary>
    /// Шов для проверок: подменяет разрядность опорной орбиты. Нужен, чтобы сравнить кадр по
    /// штатному плану точности с кадром на заведомо избыточной точности — единственная
    /// проверка самого плана, не требующая внешнего эталона. В приложении всегда null.
    /// </summary>
    internal static int? ForceReferenceBitsForTests { get; set; }

    /// <summary>
    /// Диагностика для проверок: сколько раз сработало ребазирование. Величина мала не
    /// случайно — см. <see cref="TryRebase"/>; проверка следит, чтобы это осталось так.
    /// </summary>
    internal static long RebaseCountForTests;

    /// <summary>
    /// Степени вне диапазона, который принимает окно, движок не обслуживает: буферы под
    /// <c>Wᵏ</c> и биномиальные коэффициенты рассчитаны на <see cref="MaximumPower"/>.
    /// Такое состояние может прийти только из подправленного вручную файла сохранений —
    /// оно уходит на плоскую ступень, которая любую степень считает напрямую.
    /// </summary>
    private static bool SupportsDeepZoom(PhoenixState state) =>
        state.PrimaryPower is >= 0 and <= MaximumPower &&
        state.SecondaryPower is >= 0 and <= MaximumPower;

    internal static bool ShouldUseDeepZoom(PhoenixState state) =>
        SupportsDeepZoom(state) && (ForceDeepZoomForTests ?? (state.Zoom > DeepZoomThreshold));

    /// <summary>Разрядность мантиссы опорной орбиты — общий план <see cref="PerturbationReference.PlanBits"/>.</summary>
    internal static int PlanReferenceBits(PhoenixState state) =>
        ForceReferenceBitsForTests ??
        PerturbationReference.PlanBits(PerturbationReference.ZoomBits(state.Zoom), state.Iterations);

    // ------------------------------------------------------------------ reference orbit

    /// <summary>
    /// Опорная орбита центра кадра в double. Индексация сдвинута на единицу:
    /// <c>Re[i]</c> — это <c>Z_{i-1}</c>, поэтому <c>Re[0]</c> хранит начальное <c>z₋₁</c>, а
    /// <c>Re[1]</c> — начальное <c>z₀</c>. Пара, нужная шагу с памятью, всегда лежит рядом.
    /// </summary>
    private sealed class ReferenceOrbit
    {
        public required double[] Re;
        public required double[] Im;

        /// <summary>Количество заполненных точек (индексы 0..<see cref="Length"/>-1).</summary>
        public required int Length;

        /// <summary>Опорная орбита вышла за радиус раньше, чем достигла числа итераций.</summary>
        public required bool Escaped;

        /// <summary>
        /// Константа c1 опорной точки в double. В динамической плоскости это просто параметр
        /// формулы, в параметрической — центр кадра: ядру она нужна в члене <c>C1·ΔG</c>, а
        /// разбирать точную строку центра заново на каждый тайл было бы дорого.
        /// </summary>
        public required double C1Real;

        /// <inheritdoc cref="C1Real"/>
        public required double C1Imaginary;
    }

    private static readonly ReferenceOrbitCache<ReferenceOrbit> _orbits = new();

    /// <summary>
    /// Слишком короткая опорная орбита (центр вылетел почти сразу) — единственный случай, когда
    /// пертурбации не на что опереться: ребазировать некуда. Рано, но не мгновенно вышедшая
    /// орбита обслуживается ребазированием и вырожденной не считается.
    /// </summary>
    private static bool IsDegenerateOrbit(ReferenceOrbit orbit) => orbit.Length < PerturbationReference.MinimumOrbitLength;

    private static ReferenceOrbit GetReferenceOrbit(PhoenixState state, int referenceBits)
    {
        string centerXRaw = PerturbationReference.CenterText(state.CenterXExact, state.CenterX);
        string centerYRaw = PerturbationReference.CenterText(state.CenterYExact, state.CenterY);

        // В ключе — всё, что участвует в построении орбиты (см. ReferenceOrbitCache).
        string key = string.Join('|',
            centerXRaw,
            centerYRaw,
            state.Zoom.ToInvariantString(),
            state.Iterations.ToString(CultureInfo.InvariantCulture),
            ((int)state.PlaneMode).ToString(CultureInfo.InvariantCulture),
            ((int)state.Variant).ToString(CultureInfo.InvariantCulture),
            state.PrimaryPower.ToString(CultureInfo.InvariantCulture),
            state.SecondaryPower.ToString(CultureInfo.InvariantCulture),
            state.C1Real.ToString(CultureInfo.InvariantCulture),
            state.C1Imaginary.ToString(CultureInfo.InvariantCulture),
            state.C2Real.ToString(CultureInfo.InvariantCulture),
            state.C2Imaginary.ToString(CultureInfo.InvariantCulture),
            state.InitialZReal.ToString(CultureInfo.InvariantCulture),
            state.InitialZImaginary.ToString(CultureInfo.InvariantCulture),
            state.InitialPreviousReal.ToString(CultureInfo.InvariantCulture),
            state.InitialPreviousImaginary.ToString(CultureInfo.InvariantCulture),
            referenceBits.ToString(CultureInfo.InvariantCulture));

        return _orbits.GetOrCompute(key, () => ComputeReferenceOrbit(state, centerXRaw, centerYRaw, referenceBits));
    }

    private static ReferenceOrbit ComputeReferenceOrbit(
        PhoenixState state, string centerXRaw, string centerYRaw, int referenceBits)
    {
        // Парсинг центра тоже внутри области: Parse округляет до рабочей точности.
        using var precision = new BigFloat.PrecisionScope(referenceBits);

        BigFloat centerX = BigFloat.Parse(centerXRaw);
        BigFloat centerY = BigFloat.Parse(centerYRaw);
        bool parameterPlane = state.PlaneMode == PhoenixPlaneMode.ParameterC1;

        // Автоматический старт z₀ = 1 при b > 0 и нулевых z₀/z₋₁ — та же оговорка, что в
        // ResolveParameterInitialValues: иначе параметрическая карта вырождается.
        bool automaticStart = state.SecondaryPower > 0 &&
                              state.InitialZReal == 0 && state.InitialZImaginary == 0 &&
                              state.InitialPreviousReal == 0 && state.InitialPreviousImaginary == 0;

        // Динамическая плоскость: пиксель — начальная точка z₀, значит центр задаёт её.
        // Параметрическая: пиксель — константа c1, а z₀ берётся из параметров.
        BigFloat currentReal, currentImaginary, c1Real, c1Imaginary;
        if (parameterPlane)
        {
            currentReal = automaticStart ? BigFloat.One : BigFloat.FromDecimal(state.InitialZReal);
            currentImaginary = automaticStart ? BigFloat.Zero : BigFloat.FromDecimal(state.InitialZImaginary);
            c1Real = centerX;
            c1Imaginary = centerY;
        }
        else
        {
            currentReal = centerX;
            currentImaginary = centerY;
            c1Real = BigFloat.FromDecimal(state.C1Real);
            c1Imaginary = BigFloat.FromDecimal(state.C1Imaginary);
        }

        BigFloat previousReal = BigFloat.FromDecimal(state.InitialPreviousReal);
        BigFloat previousImaginary = BigFloat.FromDecimal(state.InitialPreviousImaginary);
        BigFloat c2Real = BigFloat.FromDecimal(state.C2Real);
        BigFloat c2Imaginary = BigFloat.FromDecimal(state.C2Imaginary);

        // +2: элемент под z₋₁ и элемент под z₀; дальше по одному на итерацию.
        int capacity = state.Iterations + 2;
        var re = new double[capacity];
        var im = new double[capacity];

        re[0] = previousReal.ToDouble();
        im[0] = previousImaginary.ToDouble();
        int length = 1;
        bool escaped = false;

        for (int index = 1; index < capacity; index++)
        {
            double realDouble = currentReal.ToDouble();
            double imaginaryDouble = currentImaginary.ToDouble();
            re[index] = realDouble;
            im[index] = imaginaryDouble;
            length = index + 1;

            double magnitudeSquared = realDouble * realDouble + imaginaryDouble * imaginaryDouble;
            if (!double.IsFinite(magnitudeSquared) || magnitudeSquared > ReferenceEscapeSquared)
            {
                escaped = true;
                break;
            }

            // z ← F(z) + c1·G(z) + c2·z₋₁
            (BigFloat primaryReal, BigFloat primaryImaginary) = VariantPowerBig(
                currentReal, currentImaginary, state.PrimaryPower, state.Variant);
            (BigFloat secondaryReal, BigFloat secondaryImaginary) = VariantPowerBig(
                currentReal, currentImaginary, state.SecondaryPower, state.Variant);

            BigFloat nextReal = primaryReal
                + (c1Real * secondaryReal - c1Imaginary * secondaryImaginary)
                + (c2Real * previousReal - c2Imaginary * previousImaginary);
            BigFloat nextImaginary = primaryImaginary
                + (c1Real * secondaryImaginary + c1Imaginary * secondaryReal)
                + (c2Real * previousImaginary + c2Imaginary * previousReal);

            previousReal = currentReal;
            previousImaginary = currentImaginary;
            currentReal = nextReal;
            currentImaginary = nextImaginary;
        }

        return new ReferenceOrbit
        {
            Re = re, Im = im, Length = length, Escaped = escaped,
            // В динамической плоскости — то же приведение decimal → double, что у плоской
            // ступени: иначе константа могла бы разойтись с ней на ulp.
            C1Real = parameterPlane ? c1Real.ToDouble() : (double)state.C1Real,
            C1Imaginary = parameterPlane ? c1Imaginary.ToDouble() : (double)state.C1Imaginary
        };
    }

    /// <summary>
    /// <c>VariantPower</c> в произвольной точности — повторяет double-версию шаг в шаг, включая
    /// бинарное возведение в степень и порядок свёрток: внутренняя действует на аргумент
    /// (сопряжение и модули компонент), внешняя — только у Celtic, на вещественную часть
    /// результата.
    /// </summary>
    private static (BigFloat Real, BigFloat Imaginary) VariantPowerBig(
        BigFloat zReal, BigFloat zImaginary, int power, PhoenixVariant variant)
    {
        BigFloat baseReal, baseImaginary;
        switch (variant)
        {
            case PhoenixVariant.Tricorn:
                baseReal = zReal;
                baseImaginary = -zImaginary;
                break;
            case PhoenixVariant.BurningShip:
                baseReal = BigFloat.Abs(zReal);
                baseImaginary = -BigFloat.Abs(zImaginary);
                break;
            case PhoenixVariant.Buffalo:
                baseReal = BigFloat.Abs(zReal);
                baseImaginary = BigFloat.Abs(zImaginary);
                break;
            default: // Classic, Celtic — внутренней свёртки нет
                baseReal = zReal;
                baseImaginary = zImaginary;
                break;
        }

        BigFloat resultReal = BigFloat.One;
        BigFloat resultImaginary = BigFloat.Zero;
        BigFloat factorReal = baseReal;
        BigFloat factorImaginary = baseImaginary;
        for (int exponent = power; exponent > 0; exponent >>= 1)
        {
            if ((exponent & 1) != 0)
            {
                BigFloat productReal = resultReal * factorReal - resultImaginary * factorImaginary;
                resultImaginary = resultReal * factorImaginary + resultImaginary * factorReal;
                resultReal = productReal;
            }
            if (exponent > 1)
            {
                BigFloat squareReal = factorReal * factorReal - factorImaginary * factorImaginary;
                factorImaginary = BigFloat.ScaleByPowerOfTwo(factorReal * factorImaginary, 1);
                factorReal = squareReal;
            }
        }

        if (variant == PhoenixVariant.Celtic) resultReal = BigFloat.Abs(resultReal);
        return (resultReal, resultImaginary);
    }

    // ------------------------------------------------------------------ per-pixel perturbation

    // Шаг возмущения обобщён по представлению δ (IDeltaNumber): DoubleDelta — обычный double,
    // FloatExp — расширенная экспонента сверхглубокой ступени. Степени опорной точки остаются
    // double: они зависят только от опорной орбиты, ограниченной радиусом выхода.

    /// <summary>
    /// Значение <c>W^p</c> и его точное возмущение <c>(W+δw)^p − W^p</c> биномиальным
    /// разложением <c>Σₖ C(p,k)·W^{p−k}·δwᵏ</c>: сумма, а не разность близких величин,
    /// поэтому сокращения нет ни при какой глубине.
    /// </summary>
    private static void PerturbPower<TDelta>(
        double baseReal, double baseImaginary,
        TDelta deltaReal, TDelta deltaImaginary,
        int power,
        ReadOnlySpan<long> binomial,
        Span<double> powersReal, Span<double> powersImaginary,
        out double valueReal, out double valueImaginary,
        out TDelta deltaValueReal, out TDelta deltaValueImaginary)
        where TDelta : struct, IDeltaNumber<TDelta>
    {
        if (power == 0)
        {
            // W⁰ ≡ 1: константа, возмущения нет.
            valueReal = 1.0;
            valueImaginary = 0.0;
            deltaValueReal = TDelta.Zero;
            deltaValueImaginary = TDelta.Zero;
            return;
        }

        // Wᵏ для k = 0..p−1.
        powersReal[0] = 1.0;
        powersImaginary[0] = 0.0;
        for (int k = 1; k < power; k++)
        {
            powersReal[k] = powersReal[k - 1] * baseReal - powersImaginary[k - 1] * baseImaginary;
            powersImaginary[k] = powersReal[k - 1] * baseImaginary + powersImaginary[k - 1] * baseReal;
        }

        valueReal = powersReal[power - 1] * baseReal - powersImaginary[power - 1] * baseImaginary;
        valueImaginary = powersReal[power - 1] * baseImaginary + powersImaginary[power - 1] * baseReal;

        TDelta accumulatorReal = TDelta.Zero, accumulatorImaginary = TDelta.Zero;
        TDelta deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary; // δw¹
        for (int k = 1; k <= power; k++)
        {
            double termBaseReal = powersReal[power - k];
            double termBaseImaginary = powersImaginary[power - k];
            double coefficient = binomial[k];
            accumulatorReal += (deltaPowerReal * termBaseReal - deltaPowerImaginary * termBaseImaginary) * coefficient;
            accumulatorImaginary += (deltaPowerImaginary * termBaseReal + deltaPowerReal * termBaseImaginary) * coefficient;

            TDelta nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
            deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
            deltaPowerReal = nextDeltaPowerReal;
        }

        deltaValueReal = accumulatorReal;
        deltaValueImaginary = accumulatorImaginary;
    }

    /// <summary>
    /// Значение <c>VariantPower(Z)</c> и его точное возмущение. Свёртки знака проходят через
    /// <see cref="PerturbationReference.FoldedDelta{TDelta}"/>, степень — через
    /// <see cref="PerturbPower{TDelta}"/>; у Celtic внешний модуль вещественной части — снова
    /// свёртка, уже на результате.
    /// </summary>
    private static void PerturbVariantPower<TDelta>(
        double referenceReal, double referenceImaginary,
        TDelta deltaReal, TDelta deltaImaginary,
        int power, PhoenixVariant variant,
        ReadOnlySpan<long> binomial,
        Span<double> powersReal, Span<double> powersImaginary,
        out double valueReal, out double valueImaginary,
        out TDelta deltaValueReal, out TDelta deltaValueImaginary)
        where TDelta : struct, IDeltaNumber<TDelta>
    {
        double baseReal, baseImaginary;
        TDelta foldedDeltaReal, foldedDeltaImaginary;
        switch (variant)
        {
            case PhoenixVariant.Tricorn:
                baseReal = referenceReal;
                baseImaginary = -referenceImaginary;
                foldedDeltaReal = deltaReal;
                foldedDeltaImaginary = -deltaImaginary;
                break;
            case PhoenixVariant.BurningShip:
                baseReal = Math.Abs(referenceReal);
                baseImaginary = -Math.Abs(referenceImaginary);
                foldedDeltaReal = PerturbationReference.FoldedDelta(referenceReal, deltaReal);
                foldedDeltaImaginary = -PerturbationReference.FoldedDelta(referenceImaginary, deltaImaginary);
                break;
            case PhoenixVariant.Buffalo:
                baseReal = Math.Abs(referenceReal);
                baseImaginary = Math.Abs(referenceImaginary);
                foldedDeltaReal = PerturbationReference.FoldedDelta(referenceReal, deltaReal);
                foldedDeltaImaginary = PerturbationReference.FoldedDelta(referenceImaginary, deltaImaginary);
                break;
            default: // Classic, Celtic
                baseReal = referenceReal;
                baseImaginary = referenceImaginary;
                foldedDeltaReal = deltaReal;
                foldedDeltaImaginary = deltaImaginary;
                break;
        }

        PerturbPower(baseReal, baseImaginary, foldedDeltaReal, foldedDeltaImaginary, power,
            binomial, powersReal, powersImaginary,
            out valueReal, out valueImaginary, out deltaValueReal, out deltaValueImaginary);

        if (variant != PhoenixVariant.Celtic) return;

        deltaValueReal = PerturbationReference.FoldedDelta(valueReal, deltaValueReal);
        valueReal = Math.Abs(valueReal);
    }

    /// <summary>
    /// Не зависящие от пикселя величины кадра: константы формулы, радиус выхода, план точности.
    /// </summary>
    private readonly struct DeepParameters
    {
        public readonly double C1Real, C1Imaginary, C2Real, C2Imaginary;
        public readonly double ThresholdSquared;
        public readonly int PrimaryPower, SecondaryPower, DominantPower;

        /// <summary>Старшая степень возмущения <c>max(2, a, b)</c> — задаёт порог FloatExp-δ.</summary>
        public readonly int DeltaPower;
        public readonly PhoenixVariant Variant;
        public readonly bool ParameterPlane;

        public DeepParameters(PhoenixState state, ReferenceOrbit orbit)
        {
            ParameterPlane = state.PlaneMode == PhoenixPlaneMode.ParameterC1;
            // Опорное c1. В параметрической плоскости это центр кадра, и он обязателен: член
            // C1·ΔG возмущения — это вклад опорной константы при сдвиге z. Прежде здесь стоял
            // ноль («опорное значение приходит через орбиту»), и при b > 0 кадр параметрической
            // плоскости расходился с плоской ступенью почти целиком; при b = 0 ΔG ≡ 0, и ошибка
            // была не видна.
            C1Real = orbit.C1Real;
            C1Imaginary = orbit.C1Imaginary;
            C2Real = (double)state.C2Real;
            C2Imaginary = (double)state.C2Imaginary;
            // Умножение в decimal и лишь потом приведение — ровно как в плоской ступени.
            ThresholdSquared = (double)(state.Threshold * state.Threshold);
            PrimaryPower = state.PrimaryPower;
            SecondaryPower = state.SecondaryPower;
            DominantPower = Math.Max(state.PrimaryPower, state.SecondaryPower);
            DeltaPower = Math.Max(2, DominantPower);
            Variant = state.Variant;
        }
    }

    /// <summary>
    /// Шаг возмущения Феникса. Возмущение выводится из формулы вычитанием опорной
    /// рекуррентности и не содержит разностей близких величин:
    /// <code>
    ///   δ' = ΔF + C1·ΔG + δc1·G(z) + C2·δ₋₁
    /// </code>
    /// где <c>ΔF = F(Z+δ) − F(Z)</c> и <c>ΔG = G(Z+δ) − G(Z)</c> раскрываются биномиально, а
    /// <c>G(z) = G(Z) + ΔG</c> — значение в самой точке. Пара <c>C1·ΔG + δc1·G(z)</c> — это в
    /// точности <c>(C1+δc1)·G(z) − C1·G(Z)</c>, то есть вклад второго слагаемого формулы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (TDelta Real, TDelta Imaginary) PerturbStep<TDelta>(
        double referenceReal, double referenceImaginary, in DeepParameters parameters,
        ReadOnlySpan<long> primaryBinomial, ReadOnlySpan<long> secondaryBinomial,
        Span<double> powersReal, Span<double> powersImaginary,
        TDelta deltaC1Real, TDelta deltaC1Imaginary,
        TDelta deltaCurrentReal, TDelta deltaCurrentImaginary,
        TDelta deltaPreviousReal, TDelta deltaPreviousImaginary)
        where TDelta : struct, IDeltaNumber<TDelta>
    {
        PerturbVariantPower(referenceReal, referenceImaginary,
            deltaCurrentReal, deltaCurrentImaginary,
            parameters.PrimaryPower, parameters.Variant,
            primaryBinomial, powersReal, powersImaginary,
            out _, out _, out TDelta deltaPrimaryReal, out TDelta deltaPrimaryImaginary);

        PerturbVariantPower(referenceReal, referenceImaginary,
            deltaCurrentReal, deltaCurrentImaginary,
            parameters.SecondaryPower, parameters.Variant,
            secondaryBinomial, powersReal, powersImaginary,
            out double secondaryReal, out double secondaryImaginary,
            out TDelta deltaSecondaryReal, out TDelta deltaSecondaryImaginary);

        // G(z) = G(Z) + ΔG — значение в самой точке, множитель при δc1.
        TDelta secondaryAtPointReal = deltaSecondaryReal + secondaryReal;
        TDelta secondaryAtPointImaginary = deltaSecondaryImaginary + secondaryImaginary;

        TDelta nextDeltaReal = deltaPrimaryReal
            + (deltaSecondaryReal * parameters.C1Real - deltaSecondaryImaginary * parameters.C1Imaginary)
            + (deltaC1Real * secondaryAtPointReal - deltaC1Imaginary * secondaryAtPointImaginary)
            + (deltaPreviousReal * parameters.C2Real - deltaPreviousImaginary * parameters.C2Imaginary);
        TDelta nextDeltaImaginary = deltaPrimaryImaginary
            + (deltaSecondaryImaginary * parameters.C1Real + deltaSecondaryReal * parameters.C1Imaginary)
            + (deltaC1Real * secondaryAtPointImaginary + deltaC1Imaginary * secondaryAtPointReal)
            + (deltaPreviousImaginary * parameters.C2Real + deltaPreviousReal * parameters.C2Imaginary);
        return (nextDeltaReal, nextDeltaImaginary);
    }

    /// <summary>Режим ядра: только double или гибридный (см. <see cref="DeepZoomPixel{TMode}"/>).</summary>
    private interface IKernelMode
    {
        static abstract bool AllowExtended { get; }
    }

    private readonly struct DoubleOnlyMode : IKernelMode
    {
        public static bool AllowExtended => false;
    }

    private readonly struct HybridMode : IKernelMode
    {
        public static bool AllowExtended => true;
    }

    /// <summary>
    /// Пертурбационное ядро Феникса. Шаг — <see cref="PerturbStep{TDelta}"/>; в динамической
    /// плоскости пиксель задаёт <c>δ₀</c>, а <c>δc1 = 0</c>; в параметрической наоборот —
    /// <c>δ₀ = 0</c>, а пиксель задаёт <c>δc1</c>. В обоих случаях <c>δ₋₁ = 0</c>.
    ///
    /// До порога <see cref="ExtendedDeltaBits"/> (<see cref="DoubleOnlyMode"/>) ядро ведёт δ в
    /// double от начала до конца; режим — параметр-тип, чтобы в этом, самом частом, случае
    /// JIT выбросил гибридные ветки целиком. За порогом оно гибридное (см. описание
    /// <c>PhoenixRenderer.Extended</c>): δ и η ведутся в <see cref="FloatExp"/>, пока малы, и
    /// переходят в double, как только double их представляет без потерь; начало орбиты
    /// пропускается таблицей <paramref name="skip"/>. В double-режиме арифметика одна и та же,
    /// поэтому в полосе, где верны оба пути, кадры совпадают бит-в-бит.
    /// </summary>
    private static PixelMetrics DeepZoomPixel<TMode>(
        PhoenixState state,
        ReferenceOrbit orbit,
        in DeepParameters parameters,
        LinearSkipTable? skip,
        FloatExp pixelReal,
        FloatExp pixelImaginary,
        CancellationToken token)
        where TMode : struct, IKernelMode
    {
        int maximum = state.Iterations;
        bool detectPeriods = state.ColoringMode == PhoenixColoringMode.Period;
        bool trackTrap = state.ColoringMode == PhoenixColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == PhoenixColoringMode.StripeAverage;
        bool trackTriangle = state.ColoringMode == PhoenixColoringMode.TriangleInequalityAverage;
        int maximumPeriod = Math.Clamp(state.MaximumDetectedPeriod, 1, MaximumSupportedPeriod);
        int historyCapacity = maximumPeriod + 1;

        Span<ComplexValue> currentHistory = detectPeriods
            ? stackalloc ComplexValue[MaximumSupportedPeriod + 1]
            : Span<ComplexValue>.Empty;
        Span<ComplexValue> previousHistory = detectPeriods
            ? stackalloc ComplexValue[MaximumSupportedPeriod + 1]
            : Span<ComplexValue>.Empty;

        // Биномиальные коэффициенты обеих степеней и общий буфер под Wᵏ — считаются один раз
        // на пиксель, а не на итерацию (stackalloc внутри цикла переполнил бы стек).
        Span<long> primaryBinomial = stackalloc long[MaximumPower + 1];
        Span<long> secondaryBinomial = stackalloc long[MaximumPower + 1];
        FillBinomial(primaryBinomial, parameters.PrimaryPower);
        FillBinomial(secondaryBinomial, parameters.SecondaryPower);
        Span<double> powersReal = stackalloc double[MaximumPower];
        Span<double> powersImaginary = stackalloc double[MaximumPower];

        int switchExponent = -ExtendedDeltaBits(parameters.DeltaPower);
        double switchBack = Math.ScaleB(1.0, switchExponent - ModeHysteresisBits);

        FloatExp extendedC1Real = parameters.ParameterPlane ? pixelReal : FloatExp.Zero;
        FloatExp extendedC1Imaginary = parameters.ParameterPlane ? pixelImaginary : FloatExp.Zero;
        // В double-режиме δc1 может обратиться в ноль — это допустимо: туда гибридное ядро
        // переходит лишь при δ, рядом с которым вклад δc1·G(z) пренебрежим (см. SafeInDouble).
        double deltaC1Real = extendedC1Real.ToDouble();
        double deltaC1Imaginary = extendedC1Imaginary.ToDouble();

        FloatExp extendedCurrentReal = parameters.ParameterPlane ? FloatExp.Zero : pixelReal;
        FloatExp extendedCurrentImaginary = parameters.ParameterPlane ? FloatExp.Zero : pixelImaginary;
        FloatExp extendedPreviousReal = FloatExp.Zero, extendedPreviousImaginary = FloatExp.Zero;

        // Orbit[1] — это z₀; Orbit[0] под ним хранит z₋₁.
        int referenceIndex = 1;
        int iteration = 0;
        int detectedPeriod = 0;
        double minimumTrap = double.MaxValue;
        double stripeSum = 0, triangleSum = 0;

        if (TMode.AllowExtended && skip is not null)
        {
            int skipped = skip.FindSkip(FloatExp.Sqrt(FloatExp.MagnitudeSquared(pixelReal, pixelImaginary)));
            if (skipped > 0)
            {
                Interlocked.Add(ref SkippedIterationsForTests, skipped);
                extendedCurrentReal = skip.D00[skipped] * pixelReal + skip.D01[skipped] * pixelImaginary;
                extendedCurrentImaginary = skip.D10[skipped] * pixelReal + skip.D11[skipped] * pixelImaginary;
                extendedPreviousReal = skip.D00[skipped - 1] * pixelReal + skip.D01[skipped - 1] * pixelImaginary;
                extendedPreviousImaginary = skip.D10[skipped - 1] * pixelReal + skip.D11[skipped - 1] * pixelImaginary;
                iteration = skipped;
                referenceIndex = skipped + 1;
                minimumTrap = skip.Trap[skipped];
                stripeSum = skip.Stripe[skipped];
                triangleSum = skip.Triangle[skipped];
                if (detectPeriods)
                {
                    for (int past = Math.Max(0, skipped - historyCapacity); past < skipped; past++)
                    {
                        currentHistory[past % historyCapacity] = new ComplexValue(orbit.Re[past + 1], orbit.Im[past + 1]);
                        previousHistory[past % historyCapacity] = new ComplexValue(orbit.Re[past], orbit.Im[past]);
                    }
                }
            }
        }

        bool extended = TMode.AllowExtended && !SafeInDouble(extendedCurrentReal, extendedCurrentImaginary,
            switchExponent, orbit.Re[referenceIndex], orbit.Im[referenceIndex]);
        // В FloatExp-режиме double-копии δ — лишь приближение для восстановления z и метрик.
        double deltaCurrentReal = extendedCurrentReal.ToDouble();
        double deltaCurrentImaginary = extendedCurrentImaginary.ToDouble();
        double deltaPreviousReal = extendedPreviousReal.ToDouble();
        double deltaPreviousImaginary = extendedPreviousImaginary.ToDouble();

        double currentReal = orbit.Re[referenceIndex] + deltaCurrentReal;
        double currentImaginary = orbit.Im[referenceIndex] + deltaCurrentImaginary;
        double currentMagnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;

        while (iteration < maximum && currentMagnitudeSquared <= parameters.ThresholdSquared)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            var current = new ComplexValue(currentReal, currentImaginary);
            if (trackTrap)
                minimumTrap = Math.Min(minimumTrap, OrbitTrapDistance(state, current));
            if (trackStripe)
                stripeSum += 0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2(currentImaginary, currentReal));

            if (detectPeriods)
            {
                int historyIndex = iteration % historyCapacity;
                currentHistory[historyIndex] = current;
                previousHistory[historyIndex] = new ComplexValue(
                    orbit.Re[referenceIndex - 1] + deltaPreviousReal,
                    orbit.Im[referenceIndex - 1] + deltaPreviousImaginary);
            }

            double referenceReal = orbit.Re[referenceIndex];
            double referenceImaginary = orbit.Im[referenceIndex];

            if (TMode.AllowExtended && extended)
            {
                (FloatExp stepReal, FloatExp stepImaginary) = PerturbStep(referenceReal, referenceImaginary, parameters,
                    primaryBinomial, secondaryBinomial, powersReal, powersImaginary,
                    extendedC1Real, extendedC1Imaginary,
                    extendedCurrentReal, extendedCurrentImaginary, extendedPreviousReal, extendedPreviousImaginary);
                extendedPreviousReal = extendedCurrentReal;
                extendedPreviousImaginary = extendedCurrentImaginary;
                extendedCurrentReal = stepReal;
                extendedCurrentImaginary = stepImaginary;
                deltaCurrentReal = extendedCurrentReal.ToDouble();
                deltaCurrentImaginary = extendedCurrentImaginary.ToDouble();
                deltaPreviousReal = extendedPreviousReal.ToDouble();
                deltaPreviousImaginary = extendedPreviousImaginary.ToDouble();
            }
            else
            {
                (DoubleDelta stepReal, DoubleDelta stepImaginary) = PerturbStep(referenceReal, referenceImaginary, parameters,
                    primaryBinomial, secondaryBinomial, powersReal, powersImaginary,
                    new DoubleDelta(deltaC1Real), new DoubleDelta(deltaC1Imaginary),
                    new DoubleDelta(deltaCurrentReal), new DoubleDelta(deltaCurrentImaginary),
                    new DoubleDelta(deltaPreviousReal), new DoubleDelta(deltaPreviousImaginary));
                deltaPreviousReal = deltaCurrentReal;
                deltaPreviousImaginary = deltaCurrentImaginary;
                deltaCurrentReal = stepReal.Value;
                deltaCurrentImaginary = stepImaginary.Value;
            }
            referenceIndex++;
            iteration++;

            // Ребазирование — точное тождество, поэтому выполняется до восстановления z: само z
            // от него не меняется, меняется только опорная точка, от которой оно отсчитано.
            // Перенос работает с double-копиями. В FloatExp-режиме он случается только на
            // исчерпании орбиты (для крошечного δ критерии потери значимости не срабатывают),
            // и перенесённая пара — порядка самих z, так что дальше ей хватает double.
            int indexBeforeRebase = referenceIndex;
            TryRebase(orbit, ref referenceIndex,
                ref deltaCurrentReal, ref deltaCurrentImaginary,
                ref deltaPreviousReal, ref deltaPreviousImaginary);
            if (TMode.AllowExtended && extended && referenceIndex != indexBeforeRebase) extended = false;

            double nextReal = orbit.Re[referenceIndex] + deltaCurrentReal;
            double nextImaginary = orbit.Im[referenceIndex] + deltaCurrentImaginary;

            if (trackTriangle)
            {
                double edgeLength = Distance(new ComplexValue(nextReal, nextImaginary), current);
                if (double.IsFinite(edgeLength) && edgeLength > 1e-300)
                {
                    double triangleRatio =
                        (Math.Sqrt(nextReal * nextReal + nextImaginary * nextImaginary) - current.Magnitude) / edgeLength;
                    triangleSum += 0.5 + 0.5 * Math.Clamp(triangleRatio, -1, 1);
                }
            }

            currentReal = nextReal;
            currentImaginary = nextImaginary;
            currentMagnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;

            if (detectPeriods && iteration >= 4)
            {
                var currentValue = new ComplexValue(currentReal, currentImaginary);
                var previousValue = new ComplexValue(
                    orbit.Re[referenceIndex - 1] + deltaPreviousReal,
                    orbit.Im[referenceIndex - 1] + deltaPreviousImaginary);
                int available = Math.Min(maximumPeriod, iteration - 1);
                double toleranceSquared = Math.Pow(Math.Max(1e-14, state.CycleTolerance), 2);
                for (int period = 1; period <= available; period++)
                {
                    if (iteration < period * 2) continue;
                    int pastIndex = (iteration - period) % historyCapacity;
                    if (!Close(currentValue, currentHistory[pastIndex], toleranceSquared) ||
                        !Close(previousValue, previousHistory[pastIndex], toleranceSquared)) continue;
                    detectedPeriod = period;
                    break;
                }
                if (detectedPeriod > 0) break;
            }

            if (!TMode.AllowExtended) continue;
            double nextReferenceReal = orbit.Re[referenceIndex];
            double nextReferenceImaginary = orbit.Im[referenceIndex];
            if (extended)
            {
                if (SafeInDouble(extendedCurrentReal, extendedCurrentImaginary, switchExponent,
                        nextReferenceReal, nextReferenceImaginary))
                    extended = false;
            }
            else
            {
                double deltaMax = Math.Max(Math.Abs(deltaCurrentReal), Math.Abs(deltaCurrentImaginary));
                double referenceMax = Math.Max(Math.Abs(nextReferenceReal), Math.Abs(nextReferenceImaginary));
                // δ ровно 0 точен и в double; сжатие δ у общего с опорной точкой аттрактора
                // (referenceMax ≫ δ) тоже не повод замедляться — нелинейных членов там нет.
                if (deltaMax != 0 && deltaMax < switchBack &&
                    referenceMax < deltaMax * (NegligibleNonlinearRatio / 16))
                {
                    extendedCurrentReal = FloatExp.FromDouble(deltaCurrentReal);
                    extendedCurrentImaginary = FloatExp.FromDouble(deltaCurrentImaginary);
                    extendedPreviousReal = FloatExp.FromDouble(deltaPreviousReal);
                    extendedPreviousImaginary = FloatExp.FromDouble(deltaPreviousImaginary);
                    extended = true;
                }
            }
        }

        bool isInterior = detectedPeriod > 0 || iteration >= maximum;
        double smooth = Smooth(iteration, maximum, currentMagnitudeSquared, parameters.DominantPower);
        double argument = double.IsFinite(currentReal) && double.IsFinite(currentImaginary)
            ? PositiveModulo(Math.Atan2(currentImaginary, currentReal) / (2 * Math.PI) + 0.5, 1)
            : 0;
        return new PixelMetrics(
            iteration,
            smooth,
            minimumTrap == double.MaxValue ? 0 : minimumTrap,
            iteration == 0 ? 0 : stripeSum / iteration,
            iteration == 0 ? 0 : triangleSum / iteration,
            argument,
            detectedPeriod,
            isInterior);
    }

    private static void FillBinomial(Span<long> binomial, int power)
    {
        binomial[0] = 1;
        for (int k = 1; k <= power; k++) binomial[k] = binomial[k - 1] * (power - k + 1) / k;
    }

    /// <summary>
    /// Перенос пары <c>(δₙ, δₙ₋₁)</c> в начало опорной орбиты. Само <c>z</c> при этом не
    /// меняется — меняется лишь точка отсчёта, поэтому операция точна при любом выборе момента
    /// и её единственный смысл в том, чтобы уменьшить δ, восстановив значащие разряды.
    ///
    /// В отличие от одномерного случая уменьшение не гарантировано: перенос одновременно
    /// затрагивает обе компоненты пары, и вторая может вырасти сильнее, чем убыла первая.
    /// Поэтому по критериям Zhuoran/Pauldelbrot перенос лишь <b>предлагается</b> и принимается
    /// только если он не увеличил худшую из двух величин. Исчерпание орбиты — иное дело: там
    /// выбора нет, продолжать не на чем, и перенос выполняется безусловно.
    ///
    /// Насколько часто перенос проходит — зависит от кадра. Там, где орбиты пикселей почти
    /// повторяют опорную (вся область на грани выхода), условие «не хуже» отвергает почти
    /// каждое предложение: в начале орбиты <c>z₋₁ = 0</c>, тогда как <c>zₙ₋₁</c> к этому
    /// моменту порядка единицы, — перенос обменял бы малое <c>δₙ₋₁</c> на большое, и остаётся
    /// только вынужденный перенос по исчерпанию, один раз на пиксель. Замер на таком кадре:
    /// с ребазированием и без него кадр совпадает пиксель в пиксель. На кадрах со структурой
    /// перенос проходит заметно чаще — порядка нескольких раз на пиксель, и там он работает
    /// по существу.
    ///
    /// Прежде считалось, что первый случай и задаёт потолок зума: ошибка δ копится без сброса.
    /// Разбор кадра «на грани выхода» показал другое — там разброс |z| по кадру на шаге решения
    /// ниже разрядности double, и кадр не различим ни для какого double-рендера. Условие «не
    /// хуже» к тому же гарантирует главное: пара переносится, лишь когда её значения уже порядка
    /// самих z, то есть когда информация кадра усилена выше ошибки округления перенесённых
    /// значений. Поэтому точность пертурбации относительная и от глубины не зависит (см.
    /// <c>PhoenixRenderer.Extended</c>).
    /// </summary>
    private static void TryRebase(
        ReferenceOrbit orbit,
        ref int referenceIndex,
        ref double deltaCurrentReal, ref double deltaCurrentImaginary,
        ref double deltaPreviousReal, ref double deltaPreviousImaginary)
    {
        bool exhausted = referenceIndex >= orbit.Length - 1;

        double currentReal = orbit.Re[referenceIndex] + deltaCurrentReal;
        double currentImaginary = orbit.Im[referenceIndex] + deltaCurrentImaginary;
        double currentMagnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;
        double deltaMagnitudeSquared =
            deltaCurrentReal * deltaCurrentReal + deltaCurrentImaginary * deltaCurrentImaginary;
        double referenceMagnitudeSquared =
            orbit.Re[referenceIndex] * orbit.Re[referenceIndex] +
            orbit.Im[referenceIndex] * orbit.Im[referenceIndex];

        bool lostSignificance = currentMagnitudeSquared < deltaMagnitudeSquared ||
                                currentMagnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared;
        if (!exhausted && !lostSignificance) return;

        double previousReal = orbit.Re[referenceIndex - 1] + deltaPreviousReal;
        double previousImaginary = orbit.Im[referenceIndex - 1] + deltaPreviousImaginary;

        double rebasedCurrentReal = currentReal - orbit.Re[1];
        double rebasedCurrentImaginary = currentImaginary - orbit.Im[1];
        double rebasedPreviousReal = previousReal - orbit.Re[0];
        double rebasedPreviousImaginary = previousImaginary - orbit.Im[0];

        if (!exhausted)
        {
            double before = Math.Max(deltaMagnitudeSquared,
                deltaPreviousReal * deltaPreviousReal + deltaPreviousImaginary * deltaPreviousImaginary);
            double after = Math.Max(
                rebasedCurrentReal * rebasedCurrentReal + rebasedCurrentImaginary * rebasedCurrentImaginary,
                rebasedPreviousReal * rebasedPreviousReal + rebasedPreviousImaginary * rebasedPreviousImaginary);
            if (!(after <= before)) return;
        }

        Interlocked.Increment(ref RebaseCountForTests);

        deltaCurrentReal = rebasedCurrentReal;
        deltaCurrentImaginary = rebasedCurrentImaginary;
        deltaPreviousReal = rebasedPreviousReal;
        deltaPreviousImaginary = rebasedPreviousImaginary;
        referenceIndex = 1;
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>
    /// Ширина видимой области. Раскладка пикселей дальше повторяет плоскую ступень дословно —
    /// <c>(x − width/2)·scale/width</c>, включая порядок умножения и деления: обе оси делятся
    /// на <b>ширину</b> полотна (пиксели квадратные), а координата берётся по краю пикселя, без
    /// сдвига на полпикселя. Иначе на самом пороге глубокий кадр разъезжался бы с плоским.
    ///
    /// Ширина и смещения пикселей ведутся в <see cref="FloatExp"/>: за 1.8e308 зум в double не
    /// представим, а шаг сетки обращается в ноль задолго до этого. В пределах диапазона double
    /// каждая операция <see cref="FloatExp"/> округляет мантиссу ровно как double, поэтому
    /// смещения совпадают с прежним double-вычислением бит-в-бит.
    /// </summary>
    private static FloatExp DeepViewWidth(PhoenixState state) => 4.0 / state.Zoom;

    private static void RenderDeepZoom(PhoenixState state, byte[] pixels, int width, int height,
        int stride, int threadCount, CancellationToken token, Action<int>? progress)
    {
        DeepZoomPlan plan = PlanDeepZoom(state);
        ReferenceOrbit orbit = GetReferenceOrbit(state, plan.ReferenceBits);
        if (IsDegenerateOrbit(orbit))
        {
            // Опереться не на что: центр вылетел за радиус за считаные шаги, а значит и весь
            // кадр лежит далеко снаружи множества и выходит однородным. Плоская ступень здесь
            // и быстрее, и достаточна — квантование её координат на однородном кадре не видно.
            RenderPlain(state, pixels, width, height, stride, threadCount, token, progress);
            return;
        }

        var parameters = new DeepParameters(state, orbit);
        LinearSkipTable? skip = plan.UseExtendedDelta ? GetLinearSkipTable(state, orbit, parameters) : null;
        FloatExp viewWidth = DeepViewWidth(state);
        long completed = 0;

        Parallel.For(0, height,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threadCount), CancellationToken = token },
            y =>
            {
                int row = y * stride;
                FloatExp deltaImaginary = (height / 2.0 - y) * viewWidth / width;
                for (int x = 0; x < width; x++)
                {
                    if ((x & 63) == 0) token.ThrowIfCancellationRequested();
                    FloatExp deltaReal = (x - width / 2.0) * viewWidth / width;
                    PixelMetrics metrics = DeepZoomPixelDispatch(state, orbit, parameters, plan, skip,
                        deltaReal, deltaImaginary, token);
                    WritePixel(pixels, row + x * 4, ResolveColor(state, metrics));
                }

                int rows = (int)Interlocked.Increment(ref completed);
                if (rows == height || rows % Math.Max(1, height / 100) == 0)
                    progress?.Invoke(rows * 100 / height);
            });
    }

    private static byte[]? RenderDeepZoomTile(PhoenixState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token)
    {
        DeepZoomPlan plan = PlanDeepZoom(state);
        ReferenceOrbit orbit = GetReferenceOrbit(state, plan.ReferenceBits);
        if (IsDegenerateOrbit(orbit))
            return RenderPlainTile(state, canvasWidth, canvasHeight, tile, token);

        var parameters = new DeepParameters(state, orbit);
        LinearSkipTable? skip = plan.UseExtendedDelta ? GetLinearSkipTable(state, orbit, parameters) : null;
        FloatExp viewWidth = DeepViewWidth(state);
        byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            FloatExp deltaImaginary = (canvasHeight / 2.0 - (tile.Y + localY)) * viewWidth / canvasWidth;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                FloatExp deltaReal = (tile.X + localX - canvasWidth / 2.0) * viewWidth / canvasWidth;
                PixelMetrics metrics = DeepZoomPixelDispatch(state, orbit, parameters, plan, skip,
                    deltaReal, deltaImaginary, token);
                WritePixel(pixels, (localY * tile.Width + localX) * 4, ResolveColor(state, metrics));
            }
        }
        return token.IsCancellationRequested ? null : pixels;
    }

    /// <summary>
    /// Ядро по плану кадра: до порога <see cref="ExtendedDeltaBits"/> — только double, за ним —
    /// гибридный режим с линейным пропуском (таблица <paramref name="skip"/> есть только там).
    /// </summary>
    private static PixelMetrics DeepZoomPixelDispatch(
        PhoenixState state, ReferenceOrbit orbit, in DeepParameters parameters, DeepZoomPlan plan,
        LinearSkipTable? skip, FloatExp deltaReal, FloatExp deltaImaginary, CancellationToken token) =>
        plan.UseExtendedDelta
            ? DeepZoomPixel<HybridMode>(state, orbit, parameters, skip, deltaReal, deltaImaginary, token)
            : DeepZoomPixel<DoubleOnlyMode>(state, orbit, parameters, null, deltaReal, deltaImaginary, token);

}
