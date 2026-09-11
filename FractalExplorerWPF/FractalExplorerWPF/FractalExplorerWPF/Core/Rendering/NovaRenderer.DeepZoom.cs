using System.Globalization;
using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Вторая ступень точности Nova — пертурбационный движок, как у семейства Мандельброта и
/// Феникса.
///
/// Прежде у Nova было две ступени: double до зума 2e9 и прямая итерация в
/// <see cref="FractalExplorer.Utilities.ComplexDecimal"/> выше него. Вторая считала
/// <c>Pow</c> через логарифм и экспоненту <b>на каждый пиксель каждой итерации</b>, поэтому
/// стоила сотен тактов на шаг, а упиралась всё равно в 28 знаков decimal — отсюда и потолок
/// зума 1e15 в окне. Теперь выше <see cref="DeepZoomThreshold"/> кадр считает этот движок:
/// арифметика произвольной точности нужна ему один раз на кадр (опорная орбита), а пиксели
/// идут в double. Ниже порога обе прежние ступени сохранены без изменений.
///
/// <para><b>Почему пертурбация вообще применима.</b> Формула Nova в исходной записи
/// <c>z ← z − m·(z^p − 1)/(p·z^(p−1)) + c</c> выглядит неудобной: в ней деление и две
/// комплексные степени. Но <c>m·(z^p − 1)/(p·z^(p−1)) = (m/p)·(z − z^(1−p))</c> — обе
/// степени берутся на одной и той же ветви логарифма, поэтому <c>z^p·z^(1−p) = z</c> точно,
/// и запись сворачивается в</para>
/// <code>
///   z ← (1 − m/p)·z + (m/p)·z^k + c,   k = 1 − p
/// </code>
/// <para>то есть одно линейное слагаемое и одна степень. Возмущение такой формы
/// раскладывается точно:</para>
/// <code>
///   δ' = (1 − m/p)·δ + (m/p)·[(Z+δ)^k − Z^k] + δc
/// </code>
/// <para>Оба слагаемых имеют порядок δ, разностей близких величин в них нет, и вся
/// трудность сосредоточена в приращении степени — см. <see cref="PerturbPower"/>.</para>
///
/// <para><b>Отличие от Мандельброта и Феникса — степень не обязана быть целой.</b> Окно Nova
/// принимает комплексную <c>p</c>, поэтому <c>k = 1 − p</c> тоже комплексна. Биномиальное
/// разложение, на котором стоят те два движка, здесь работает только для целой <c>p</c>;
/// для остальных приращение считается через <c>log1p</c> и <c>expm1</c> с явной поправкой
/// ветви. Опорной орбите, соответственно, нужен комплексный логарифм произвольной точности —
/// ради этого в <see cref="BigFloatMath"/> появился <see cref="BigFloatMath.Log"/>, а в
/// <see cref="ComplexBigFloat"/> — <see cref="ComplexBigFloat.Log"/> и
/// <see cref="ComplexBigFloat.Pow(ComplexBigFloat, ComplexBigFloat)"/>.</para>
///
/// <para><b>Орбита Nova сходится.</b> Формула — это метод Ньютона для <c>z^p = 1</c> со
/// сдвигом на <c>c</c>, поэтому опорная точка почти всегда приходит в неподвижную точку за
/// десятки шагов. Дальше орбита стоит на месте, и остаток массива заполняется без счёта
/// (см. <see cref="ComputeReferenceOrbit"/>) — на типовом кадре это экономит почти всю
/// стоимость произвольной точности.</para>
///
/// <para><b>Глубина здесь досталась не ценой скорости, а вместе с ней.</b> Замер на кадре
/// 320×220 при 500 итерациях и <c>p = 3</c>: этот движок — 0.5 с, прежняя плоская
/// double-ступень — 2.5 с, прежняя decimal-ступень — около пяти минут (замерено на 64×44 и
/// пересчитано по площади). Обе прежние вызывали <c>Pow</c> через логарифм и экспоненту на
/// каждом шаге каждого пикселя; здесь целая степень раскладывается умножениями, а
/// трансцендентные функции остаются только у дробной и комплексной.</para>
/// </summary>
public static partial class NovaRenderer
{
    /// <summary>
    /// Зум, выше которого кадр считает пертурбационный движок. Значение то же, что у
    /// семейства Мандельброта и Феникса: около 1.5e9 шаг между пикселями перестаёт надёжно
    /// отличаться от нуля в double-координатах порядка единицы.
    /// </summary>
    private const double DeepZoomThreshold = 1.5e9;

    /// <summary>
    /// Опорная орбита обрывается, уйдя выше этого квадрата модуля: дальше её значения уже не
    /// нужны ни одному пикселю (порог выхода у окна не больше 1000), а <c>Z^k</c> при большой
    /// отрицательной степени начал бы терять порядок.
    /// </summary>
    private const double ReferenceEscapeSquared = 1e18;

    /// <summary>
    /// Опорная орбита обрывается и у полюса: при <c>Z → 0</c> величина <c>Z^k</c> с
    /// отрицательной <c>k</c> уходит в бесконечность, а отношение <c>δ/Z</c> перестаёт быть
    /// малым. Пиксели за этой точкой обслуживает ребазирование.
    /// </summary>
    private const double ReferenceVanishSquared = 1e-24;

    /// <summary>Критерий Pauldelbrot: |z|² ниже этой доли от |Zref|² — опорная точка ненадёжна.</summary>
    private const double GlitchToleranceSquared = 1e-6;

    /// <summary>
    /// Наибольшая целая степень, которую движок раскладывает биномиально. Окно принимает
    /// <c>p ∈ [−10, 10]</c>, то есть <c>|k| = |1 − p| ≤ 11</c>; запас до 12 оставлен на
    /// состояние из подправленного вручную файла сохранений. Всё, что больше, уходит на общий
    /// путь через логарифм — он не быстрее, но не ограничен ничем.
    /// </summary>
    private const int MaximumIntegerPower = 12;

    /// <summary>Два π в double — поправка ветви логарифма считается через него.</summary>
    private const double TwoPi = 2 * Math.PI;

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
    /// Нулевая степень движку не по силам: у исходной записи при <c>p = 0</c> знаменатель
    /// <c>p·z^(p−1)</c> тождественно ноль, и свёрнутая форма с делением на <c>p</c> не
    /// существует. Плоская ступень такое состояние обрывает на нулевой итерации и красит кадр
    /// однородно — туда оно и уходит.
    /// </summary>
    private static bool SupportsDeepZoom(NovaState state) => state.PReal != 0 || state.PImaginary != 0;

    internal static bool ShouldUseDeepZoom(NovaState state) =>
        SupportsDeepZoom(state) && (ForceDeepZoomForTests ?? state.Zoom > DeepZoomThreshold);

    /// <summary>
    /// Разрядность мантиссы опорной орбиты: биты на разрешение соседних пикселей
    /// (≈ log2 зума), удвоенный запас по длине орбиты на накопление округлений и 48 бит на
    /// субпиксельную точность и общий люфт. Формула и её обоснование — те же, что у Феникса.
    /// </summary>
    internal static int PlanReferenceBits(NovaState state)
    {
        double zoomBits = state.Zoom > 0 && double.IsFinite(state.Zoom) ? Math.Log2(state.Zoom) : 0;
        int iterationBits = 32 - BitOperations.LeadingZeroCount((uint)Math.Max(state.Iterations, 2));
        int needed = (int)Math.Ceiling(zoomBits) + 2 * iterationBits + 48;
        int rounded = Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
        return ForceReferenceBitsForTests ?? rounded;
    }

    // ------------------------------------------------------------------ reference orbit

    /// <summary>
    /// Опорная орбита центра кадра в double: <c>Re[i]</c> — это <c>Z_i</c>, начиная с
    /// <c>Z₀</c>. Ребазирование возвращается ровно к <c>Z₀</c>, поэтому сдвиг индексации,
    /// нужный Фениксу с его памятью на шаг назад, здесь не требуется.
    /// </summary>
    private sealed class ReferenceOrbit
    {
        public required double[] Re;
        public required double[] Im;

        /// <summary>Количество заполненных точек (индексы 0..<see cref="Length"/>-1).</summary>
        public required int Length;
    }

    private static readonly object _orbitLock = new();
    private static string? _orbitKey;
    private static ReferenceOrbit? _orbitCache;

    /// <summary>
    /// Слишком короткая опорная орбита (центр вылетел или попал в полюс почти сразу) —
    /// единственный случай, когда пертурбации не на что опереться: ребазировать некуда.
    /// </summary>
    private static bool IsDegenerateOrbit(ReferenceOrbit orbit) => orbit.Length < 4;

    private static ReferenceOrbit GetReferenceOrbit(NovaState state, int referenceBits)
    {
        string centerXRaw = state.CenterXExact is { Length: > 0 } exactX
            ? exactX
            : state.CenterX.ToString(CultureInfo.InvariantCulture);
        string centerYRaw = state.CenterYExact is { Length: > 0 } exactY
            ? exactY
            : state.CenterY.ToString(CultureInfo.InvariantCulture);

        // В ключе — всё, что участвует в построении орбиты. Пропущенное поле здесь однажды
        // стоило семейству Мандельброта чёрного кадра (кэш отдавал орбиту другой степени),
        // поэтому перечисление умышленно избыточно.
        string key = string.Join('|',
            centerXRaw,
            centerYRaw,
            state.Zoom.ToString("R", CultureInfo.InvariantCulture),
            state.Iterations.ToString(CultureInfo.InvariantCulture),
            ((int)state.Variant).ToString(CultureInfo.InvariantCulture),
            state.PReal.ToString(CultureInfo.InvariantCulture),
            state.PImaginary.ToString(CultureInfo.InvariantCulture),
            state.Z0Real.ToString(CultureInfo.InvariantCulture),
            state.Z0Imaginary.ToString(CultureInfo.InvariantCulture),
            state.M.ToString(CultureInfo.InvariantCulture),
            state.CReal.ToString(CultureInfo.InvariantCulture),
            state.CImaginary.ToString(CultureInfo.InvariantCulture),
            referenceBits.ToString(CultureInfo.InvariantCulture));

        lock (_orbitLock)
        {
            if (_orbitKey == key && _orbitCache is not null) return _orbitCache;

            ReferenceOrbit orbit = ComputeReferenceOrbit(state, centerXRaw, centerYRaw, referenceBits);
            _orbitKey = key;
            _orbitCache = orbit;
            return orbit;
        }
    }

    /// <summary>
    /// Опорная орбита в произвольной точности. Считается по свёрнутой форме
    /// <c>z ← (1 − m/p)·z + (m/p)·z^k + c</c>: она алгебраически тождественна исходной записи
    /// плоской ступени, а стоит одной степени вместо двух и одного деления вместо двух.
    ///
    /// Целая степень берётся бинарным возведением — без логарифма, без выбора ветви и без
    /// трансцендентных функций вообще. Это обычный случай (по умолчанию <c>p = 3</c>), и он
    /// на порядок дешевле общего.
    /// </summary>
    private static ReferenceOrbit ComputeReferenceOrbit(
        NovaState state, string centerXRaw, string centerYRaw, int referenceBits)
    {
        // Парсинг центра тоже внутри области: Parse округляет до рабочей точности.
        using var precision = new BigFloat.PrecisionScope(referenceBits);

        var center = new ComplexBigFloat(BigFloat.Parse(centerXRaw), BigFloat.Parse(centerYRaw));
        bool julia = state.Variant == NovaVariant.Julia;

        // Динамическая плоскость (Julia): пиксель — начальная точка z₀, значит центр задаёт её.
        // Параметрическая (Mandelbrot): пиксель — константа c, а z₀ берётся из параметров.
        ComplexBigFloat current = julia
            ? center
            : ComplexBigFloat.FromDecimal(state.Z0Real, state.Z0Imaginary);
        ComplexBigFloat constant = julia
            ? ComplexBigFloat.FromDecimal(state.CReal, state.CImaginary)
            : center;

        ComplexBigFloat power = ComplexBigFloat.FromDecimal(state.PReal, state.PImaginary);
        ComplexBigFloat relaxation = ComplexBigFloat.FromDecimal(state.M, 0m) / power;
        ComplexBigFloat retained = ComplexBigFloat.One - relaxation;
        ComplexBigFloat exponent = ComplexBigFloat.One - power;
        bool integerExponent = TryIntegerExponent(state, out int exponentValue);

        int capacity = state.Iterations + 1;
        var re = new double[capacity];
        var im = new double[capacity];
        int length = 0;

        for (int index = 0; index < capacity; index++)
        {
            double realDouble = current.Real.ToDouble();
            double imaginaryDouble = current.Imaginary.ToDouble();
            re[index] = realDouble;
            im[index] = imaginaryDouble;
            length = index + 1;

            double magnitudeSquared = realDouble * realDouble + imaginaryDouble * imaginaryDouble;
            if (!double.IsFinite(magnitudeSquared) ||
                magnitudeSquared > ReferenceEscapeSquared ||
                magnitudeSquared < ReferenceVanishSquared) break;

            ComplexBigFloat powered = integerExponent
                ? ComplexBigFloat.Pow(current, exponentValue)
                : ComplexBigFloat.Pow(current, exponent);
            ComplexBigFloat next = retained * current + relaxation * powered + constant;

            // Неподвижная точка на рабочей разрядности: дальше орбита буквально повторяет
            // себя, и остаток заполняется без счёта. Для Nova это не оптимизация на полях —
            // ньютоновская итерация сходится за десятки шагов, а орбиту просят на тысячи.
            if (next.Real == current.Real && next.Imaginary == current.Imaginary)
            {
                Array.Fill(re, realDouble, index, capacity - index);
                Array.Fill(im, imaginaryDouble, index, capacity - index);
                length = capacity;
                break;
            }

            current = next;
        }

        return new ReferenceOrbit { Re = re, Im = im, Length = length };
    }

    /// <summary>
    /// Показатель <c>k = 1 − p</c>, когда он целый и умещается в
    /// <see cref="MaximumIntegerPower"/>. Такой показатель и опорная орбита берёт бинарным
    /// возведением, и пиксельное приращение раскладывает биномиально — точно и без
    /// трансцендентных функций.
    /// </summary>
    private static bool TryIntegerExponent(NovaState state, out int exponent)
    {
        exponent = 0;
        if (state.PImaginary != 0 || decimal.Truncate(state.PReal) != state.PReal) return false;

        decimal candidate = 1m - state.PReal;
        if (candidate < -MaximumIntegerPower || candidate > MaximumIntegerPower) return false;

        exponent = (int)candidate;
        return true;
    }

    // ------------------------------------------------------------------ per-pixel perturbation

    /// <summary>
    /// Не зависящие от пикселя величины кадра: свёрнутые константы формулы, показатель,
    /// биномиальные коэффициенты и радиус выхода.
    /// </summary>
    private readonly struct DeepParameters
    {
        /// <summary>Множитель при z: <c>1 − m/p</c>.</summary>
        public readonly double RetainedReal, RetainedImaginary;

        /// <summary>Множитель при <c>z^k</c>: <c>m/p</c>.</summary>
        public readonly double RelaxationReal, RelaxationImaginary;

        /// <summary>Показатель <c>k = 1 − p</c>.</summary>
        public readonly double ExponentReal, ExponentImaginary;

        public readonly int IntegerExponent;
        public readonly bool UseIntegerPower;

        /// <summary>C(|k|, j) для j = 0..|k| — только при целом показателе.</summary>
        public readonly long[] Binomial;

        /// <summary>|p|² — им проверяется вырождение знаменателя исходной записи.</summary>
        public readonly double PowerMagnitudeSquared;

        public readonly double ThresholdSquared;
        public readonly bool Julia;

        public DeepParameters(NovaState state)
        {
            var power = new Complex((double)state.PReal, (double)state.PImaginary);
            Complex relaxation = (double)state.M / power;
            Complex retained = Complex.One - relaxation;
            Complex exponent = Complex.One - power;

            RetainedReal = retained.Real;
            RetainedImaginary = retained.Imaginary;
            RelaxationReal = relaxation.Real;
            RelaxationImaginary = relaxation.Imaginary;
            ExponentReal = exponent.Real;
            ExponentImaginary = exponent.Imaginary;
            PowerMagnitudeSquared = power.Real * power.Real + power.Imaginary * power.Imaginary;

            UseIntegerPower = TryIntegerExponent(state, out IntegerExponent);
            int absolute = Math.Abs(IntegerExponent);
            Binomial = new long[MaximumIntegerPower + 1];
            Binomial[0] = 1;
            for (int index = 1; index <= absolute; index++)
                Binomial[index] = Binomial[index - 1] * (absolute - index + 1) / index;

            // Умножение в decimal и лишь потом приведение — ровно как в плоской ступени.
            ThresholdSquared = (double)(state.Threshold * state.Threshold);
            Julia = state.Variant == NovaVariant.Julia;
        }
    }

    /// <summary>
    /// Пертурбационное ядро Nova. Шаг выводится вычитанием опорной рекуррентности из
    /// пиксельной:
    /// <code>
    ///   δ' = (1 − m/p)·δ + (m/p)·Δ(Z^k) + δc,   Δ(Z^k) = (Z+δ)^k − Z^k
    /// </code>
    /// В динамической плоскости (Julia) пиксель задаёт <c>δ₀</c>, а <c>δc = 0</c>; в
    /// параметрической (Mandelbrot) наоборот — <c>δ₀ = 0</c>, а пиксель задаёт <c>δc</c>.
    ///
    /// Обрывы воспроизводят плоскую ступень шаг в шаг: уход за порог, приближение к полюсу,
    /// вырождение знаменателя <c>p·z^(p−1)</c> и потеря конечности. Знаменатель здесь не
    /// считается отдельно — он равен <c>p / z^k</c>, и его модуль берётся из уже посчитанной
    /// степени.
    /// </summary>
    private static (int Iteration, double MagnitudeSquared) DeepZoomPixel(
        NovaState state,
        ReferenceOrbit orbit,
        in DeepParameters parameters,
        double deltaPixelReal,
        double deltaPixelImaginary,
        CancellationToken token)
    {
        int maximum = state.Iterations;

        double deltaReal = parameters.Julia ? deltaPixelReal : 0;
        double deltaImaginary = parameters.Julia ? deltaPixelImaginary : 0;
        double deltaConstantReal = parameters.Julia ? 0 : deltaPixelReal;
        double deltaConstantImaginary = parameters.Julia ? 0 : deltaPixelImaginary;

        Span<double> powersReal = stackalloc double[MaximumIntegerPower];
        Span<double> powersImaginary = stackalloc double[MaximumIntegerPower];

        int referenceIndex = 0;
        int iteration = 0;
        double currentReal = orbit.Re[0] + deltaReal;
        double currentImaginary = orbit.Im[0] + deltaImaginary;
        double magnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;

        while (iteration < maximum && magnitudeSquared <= parameters.ThresholdSquared)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return (0, 0);
            if (magnitudeSquared < 1e-12) break;

            PerturbPower(orbit.Re[referenceIndex], orbit.Im[referenceIndex],
                deltaReal, deltaImaginary, parameters, powersReal, powersImaginary,
                out double poweredReal, out double poweredImaginary,
                out double deltaPoweredReal, out double deltaPoweredImaginary);

            // z^k в самой точке — множитель, через который виден знаменатель p·z^(p−1) = p/z^k.
            double pointReal = poweredReal + deltaPoweredReal;
            double pointImaginary = poweredImaginary + deltaPoweredImaginary;
            double pointMagnitudeSquared = pointReal * pointReal + pointImaginary * pointImaginary;
            if (!double.IsFinite(pointMagnitudeSquared) || pointMagnitudeSquared == 0 ||
                parameters.PowerMagnitudeSquared < 1e-24 * pointMagnitudeSquared) break;

            double nextDeltaReal =
                parameters.RetainedReal * deltaReal - parameters.RetainedImaginary * deltaImaginary +
                parameters.RelaxationReal * deltaPoweredReal - parameters.RelaxationImaginary * deltaPoweredImaginary +
                deltaConstantReal;
            double nextDeltaImaginary =
                parameters.RetainedReal * deltaImaginary + parameters.RetainedImaginary * deltaReal +
                parameters.RelaxationReal * deltaPoweredImaginary + parameters.RelaxationImaginary * deltaPoweredReal +
                deltaConstantImaginary;

            deltaReal = nextDeltaReal;
            deltaImaginary = nextDeltaImaginary;
            referenceIndex++;

            // Ребазирование — точное тождество, поэтому выполняется до восстановления z:
            // само z от него не меняется, меняется только опорная точка отсчёта.
            TryRebase(orbit, ref referenceIndex, ref deltaReal, ref deltaImaginary);

            currentReal = orbit.Re[referenceIndex] + deltaReal;
            currentImaginary = orbit.Im[referenceIndex] + deltaImaginary;
            if (!double.IsFinite(currentReal) || !double.IsFinite(currentImaginary)) break;

            iteration++;
            magnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;
        }

        return (iteration, magnitudeSquared);
    }

    /// <summary>
    /// Перенос δ в начало опорной орбиты. Само <c>z</c> при этом не меняется — меняется лишь
    /// точка отсчёта, поэтому операция точна при любом выборе момента, а её смысл в том,
    /// чтобы вернуть δ значащие разряды, когда оно подобралось к самому <c>z</c>.
    ///
    /// Критериев два: классический Pauldelbrot (<c>|z|²</c> много меньше <c>|Z|²</c> — опорная
    /// точка перестала описывать пиксель) и исчерпание орбиты, когда продолжать просто не на
    /// чем. У Nova работает практически только первый: опорная орбита сходится в неподвижную
    /// точку и заполняется ею до конца, так что исчерпания не наступает вовсе.
    ///
    /// Срабатывает перенос часто — на замере около трети всех шагов, тогда как у Феникса это
    /// полтора процента. Причина в том, что обе величины у Nova порядка единицы: опорная точка
    /// стоит в своей неподвижной точке, а орбита пикселя у границы гуляет, и условие
    /// «<c>|z|</c> меньше <c>|δ|</c>» выполняется то и дело. Вреда в этом нет — перенос точен
    /// при любом выборе момента, — но и счётчика срабатываний здесь, в отличие от Феникса, не
    /// заведено: при такой частоте один общий <c>Interlocked</c> на все потоки рендера стоил
    /// бы около трети времени кадра, а сторожить им нечего. У Феникса счётчик защищает
    /// условие «не хуже», которого здесь просто нет.
    /// </summary>
    private static void TryRebase(
        ReferenceOrbit orbit, ref int referenceIndex, ref double deltaReal, ref double deltaImaginary)
    {
        bool exhausted = referenceIndex >= orbit.Length - 1;
        double referenceReal = orbit.Re[referenceIndex];
        double referenceImaginary = orbit.Im[referenceIndex];
        double currentReal = referenceReal + deltaReal;
        double currentImaginary = referenceImaginary + deltaImaginary;
        double currentMagnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;

        if (!exhausted)
        {
            double deltaMagnitudeSquared = deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            double referenceMagnitudeSquared =
                referenceReal * referenceReal + referenceImaginary * referenceImaginary;
            if (currentMagnitudeSquared >= deltaMagnitudeSquared &&
                currentMagnitudeSquared >= GlitchToleranceSquared * referenceMagnitudeSquared) return;
        }

        deltaReal = currentReal - orbit.Re[0];
        deltaImaginary = currentImaginary - orbit.Im[0];
        referenceIndex = 0;
    }

    /// <summary>
    /// Значение <c>Z^k</c> и его точное приращение <c>(Z+δ)^k − Z^k</c>. Разности близких
    /// величин нет ни в одной из двух веток.
    ///
    /// <b>Целый показатель</b> раскладывается биномиально: <c>Δ(Z^q) = Σⱼ C(q,j)·Z^(q−j)·δʲ</c>
    /// — сумма, а не разность, поэтому сокращения нет ни при какой глубине. Отрицательный
    /// показатель берётся из положительного тождеством
    /// <c>1/(Z^q+Δ) − 1/Z^q = −Δ/(Z^q·(Z^q+Δ))</c>: в числителе снова приращение, а не
    /// разность обратных величин.
    ///
    /// <b>Дробный и комплексный</b> — через <c>expm1</c> и <c>log1p</c>:
    /// <c>(Z+δ)^k − Z^k = Z^k·(e^(k·L) − 1)</c>, где <c>L = Log(Z+δ) − Log Z</c>. Малую
    /// величину <c>L</c> даёт <c>log1p(δ/Z)</c>, а <c>e^(k·L) − 1</c> — комплексный
    /// <c>expm1</c>; обе функции возвращают именно приращение, не теряя его на фоне единицы.
    /// </summary>
    private static void PerturbPower(
        double referenceReal, double referenceImaginary,
        double deltaReal, double deltaImaginary,
        in DeepParameters parameters,
        Span<double> powersReal, Span<double> powersImaginary,
        out double valueReal, out double valueImaginary,
        out double deltaValueReal, out double deltaValueImaginary)
    {
        if (parameters.UseIntegerPower)
        {
            int exponent = parameters.IntegerExponent;
            if (exponent == 0)
            {
                // Z⁰ ≡ 1: константа, приращения нет.
                valueReal = 1;
                valueImaginary = 0;
                deltaValueReal = 0;
                deltaValueImaginary = 0;
                return;
            }

            IntegerPowerDelta(referenceReal, referenceImaginary, deltaReal, deltaImaginary,
                Math.Abs(exponent), parameters.Binomial, powersReal, powersImaginary,
                out double positiveReal, out double positiveImaginary,
                out double positiveDeltaReal, out double positiveDeltaImaginary);

            if (exponent > 0)
            {
                valueReal = positiveReal;
                valueImaginary = positiveImaginary;
                deltaValueReal = positiveDeltaReal;
                deltaValueImaginary = positiveDeltaImaginary;
                return;
            }

            // Обратная степень: значение — 1/Z^q, приращение — −Δ/(Z^q·(Z^q+Δ)).
            Divide(1, 0, positiveReal, positiveImaginary, out valueReal, out valueImaginary);
            Multiply(positiveReal, positiveImaginary,
                positiveReal + positiveDeltaReal, positiveImaginary + positiveDeltaImaginary,
                out double denominatorReal, out double denominatorImaginary);
            Divide(-positiveDeltaReal, -positiveDeltaImaginary, denominatorReal, denominatorImaginary,
                out deltaValueReal, out deltaValueImaginary);
            return;
        }

        var reference = new Complex(referenceReal, referenceImaginary);
        Complex value = Complex.Pow(reference, new Complex(parameters.ExponentReal, parameters.ExponentImaginary));
        valueReal = value.Real;
        valueImaginary = value.Imaginary;

        if (deltaReal == 0 && deltaImaginary == 0)
        {
            deltaValueReal = 0;
            deltaValueImaginary = 0;
            return;
        }

        Divide(deltaReal, deltaImaginary, referenceReal, referenceImaginary,
            out double ratioReal, out double ratioImaginary);
        ComplexLog1p(ratioReal, ratioImaginary, out double logReal, out double logImaginary);

        // Поправка ветви. Разность главных логарифмов равна log1p(δ/Z) лишь тогда, когда Z и
        // Z+δ лежат по одну сторону разреза; на разрезе она отличается на 2πi. Для целого k
        // это ничего не меняло бы, но целый k сюда и не попадает.
        double turns = Math.Round(
            (Math.Atan2(referenceImaginary + deltaImaginary, referenceReal + deltaReal) -
             Math.Atan2(referenceImaginary, referenceReal) - logImaginary) / TwoPi);
        if (turns != 0) logImaginary += TwoPi * turns;

        Multiply(parameters.ExponentReal, parameters.ExponentImaginary, logReal, logImaginary,
            out double scaledReal, out double scaledImaginary);
        ComplexExpm1(scaledReal, scaledImaginary, out double growthReal, out double growthImaginary);
        Multiply(valueReal, valueImaginary, growthReal, growthImaginary,
            out deltaValueReal, out deltaValueImaginary);
    }

    /// <summary>
    /// <c>Z^q</c> и <c>(Z+δ)^q − Z^q</c> для целого <c>q &gt; 0</c> биномиальным разложением.
    /// Буфер под <c>Z^j</c> и таблица коэффициентов приходят снаружи: оба живут дольше одной
    /// итерации, и выделять их на каждый шаг незачем.
    /// </summary>
    private static void IntegerPowerDelta(
        double baseReal, double baseImaginary,
        double deltaReal, double deltaImaginary,
        int power,
        ReadOnlySpan<long> binomial,
        Span<double> powersReal, Span<double> powersImaginary,
        out double valueReal, out double valueImaginary,
        out double deltaValueReal, out double deltaValueImaginary)
    {
        powersReal[0] = 1;
        powersImaginary[0] = 0;
        for (int index = 1; index < power; index++)
        {
            powersReal[index] = powersReal[index - 1] * baseReal - powersImaginary[index - 1] * baseImaginary;
            powersImaginary[index] = powersReal[index - 1] * baseImaginary + powersImaginary[index - 1] * baseReal;
        }

        valueReal = powersReal[power - 1] * baseReal - powersImaginary[power - 1] * baseImaginary;
        valueImaginary = powersReal[power - 1] * baseImaginary + powersImaginary[power - 1] * baseReal;

        double accumulatorReal = 0, accumulatorImaginary = 0;
        double deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary;
        for (int index = 1; index <= power; index++)
        {
            double termReal = powersReal[power - index];
            double termImaginary = powersImaginary[power - index];
            accumulatorReal += binomial[index] *
                (termReal * deltaPowerReal - termImaginary * deltaPowerImaginary);
            accumulatorImaginary += binomial[index] *
                (termReal * deltaPowerImaginary + termImaginary * deltaPowerReal);

            double nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
            deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
            deltaPowerReal = nextDeltaPowerReal;
        }

        deltaValueReal = accumulatorReal;
        deltaValueImaginary = accumulatorImaginary;
    }

    private static void Multiply(double leftReal, double leftImaginary, double rightReal, double rightImaginary,
        out double resultReal, out double resultImaginary)
    {
        resultReal = leftReal * rightReal - leftImaginary * rightImaginary;
        resultImaginary = leftReal * rightImaginary + leftImaginary * rightReal;
    }

    private static void Divide(double leftReal, double leftImaginary, double rightReal, double rightImaginary,
        out double resultReal, out double resultImaginary)
    {
        double denominator = rightReal * rightReal + rightImaginary * rightImaginary;
        resultReal = (leftReal * rightReal + leftImaginary * rightImaginary) / denominator;
        resultImaginary = (leftImaginary * rightReal - leftReal * rightImaginary) / denominator;
    }

    /// <summary>
    /// <c>e^x − 1</c> без потери малой величины на фоне единицы. Формула Кэхэна: множитель
    /// <c>x/ln u</c> при <c>u = e^x</c> компенсирует обе ошибки — и вычитания, и самой
    /// экспоненты, — оставляя около одного младшего разряда.
    /// </summary>
    private static double ExpMinusOne(double value)
    {
        double exponential = Math.Exp(value);
        if (exponential == 1) return value;
        double shifted = exponential - 1;
        if (shifted == -1) return -1;
        return shifted * value / Math.Log(exponential);
    }

    /// <summary>
    /// <c>ln(1+x)</c> без потери малой величины. Та же компенсация Кэхэна, что и у
    /// <see cref="ExpMinusOne"/>, только в обратную сторону.
    /// </summary>
    private static double LogOnePlus(double value)
    {
        double shifted = 1 + value;
        if (shifted == 1) return value;
        return Math.Log(shifted) * value / (shifted - 1);
    }

    /// <summary>
    /// Комплексный <c>ln(1+u)</c> на главной ветви. Вещественная часть —
    /// <c>½·ln(1 + 2·Re u + |u|²)</c>: аргумент собран так, чтобы единица не съедала малое
    /// <c>u</c>. Мнимая — обычный арктангенс, у которого при малом <c>u</c> сокращения нет.
    /// </summary>
    private static void ComplexLog1p(double real, double imaginary, out double resultReal, out double resultImaginary)
    {
        resultReal = 0.5 * LogOnePlus(2 * real + real * real + imaginary * imaginary);
        resultImaginary = Math.Atan2(imaginary, 1 + real);
    }

    /// <summary>
    /// Комплексный <c>e^w − 1</c>. Вещественная часть записана как
    /// <c>expm1(a)·cos b − 2·sin²(b/2)</c>: второе слагаемое — это <c>cos b − 1</c> в форме,
    /// где нет вычитания близких величин, поэтому малое <c>w</c> не теряется.
    /// </summary>
    private static void ComplexExpm1(double real, double imaginary, out double resultReal, out double resultImaginary)
    {
        double growth = ExpMinusOne(real);
        double halfSine = Math.Sin(0.5 * imaginary);
        resultReal = growth * Math.Cos(imaginary) - 2 * halfSine * halfSine;
        resultImaginary = (growth + 1) * Math.Sin(imaginary);
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>
    /// Ширина видимой области. Раскладка пикселей дальше повторяет плоскую ступень дословно —
    /// <c>(x − width/2)·scale/width</c>, включая порядок умножения и деления: обе оси делятся
    /// на <b>ширину</b> полотна (пиксели квадратные), а координата берётся по краю пикселя, без
    /// сдвига на полпикселя. Иначе на самом пороге глубокий кадр разъезжался бы с плоским.
    /// </summary>
    private static double DeepViewWidth(NovaState state) => 4.0 / state.Zoom;

    private static Color DeepZoomColor(NovaState state, in DeepParameters parameters, ReferenceOrbit orbit,
        double deltaReal, double deltaImaginary, bool selectorPalette, CancellationToken token)
    {
        (int iteration, double magnitudeSquared) =
            DeepZoomPixel(state, orbit, parameters, deltaReal, deltaImaginary, token);
        return selectorPalette
            ? FireColor(iteration, state.Iterations)
            : ResolveColor(state, iteration, Smooth(iteration, state, magnitudeSquared));
    }

    private static void RenderDeepZoom(NovaState state, byte[] pixels, int width, int height, int stride,
        int threadCount, CancellationToken token, Action<int>? progress)
    {
        ReferenceOrbit orbit = GetReferenceOrbit(state, PlanReferenceBits(state));
        if (IsDegenerateOrbit(orbit))
        {
            // Опереться не на что: центр вылетел или попал в полюс за считаные шаги, а значит
            // и весь кадр выходит однородным. Плоская ступень здесь и быстрее, и достаточна.
            RenderPlain(state, pixels, width, height, stride, threadCount, token, progress);
            return;
        }

        var parameters = new DeepParameters(state);
        double viewWidth = DeepViewWidth(state);
        long completed = 0;

        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, threadCount)
        }, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            double deltaImaginary = (height / 2.0 - y) * viewWidth / width;
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                double deltaReal = (x - width / 2.0) * viewWidth / width;
                Color color = DeepZoomColor(state, parameters, orbit, deltaReal, deltaImaginary, false, token);
                int offset = row + x * 4;
                pixels[offset] = color.B; pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R; pixels[offset + 3] = color.A;
            }
            int rows = (int)Interlocked.Increment(ref completed);
            if (rows == height || rows % Math.Max(1, height / 100) == 0) progress?.Invoke(rows * 100 / height);
        });
    }

    private static byte[]? RenderDeepZoomTile(NovaState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token, bool selectorPalette)
    {
        ReferenceOrbit orbit = GetReferenceOrbit(state, PlanReferenceBits(state));
        if (IsDegenerateOrbit(orbit))
            return RenderPlainTile(state, canvasWidth, canvasHeight, tile, token, selectorPalette);

        var parameters = new DeepParameters(state);
        double viewWidth = DeepViewWidth(state);
        byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            double deltaImaginary = (canvasHeight / 2.0 - (tile.Y + localY)) * viewWidth / canvasWidth;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                double deltaReal = (tile.X + localX - canvasWidth / 2.0) * viewWidth / canvasWidth;
                Color color = DeepZoomColor(state, parameters, orbit, deltaReal, deltaImaginary,
                    selectorPalette, token);
                int offset = (localY * tile.Width + localX) * 4;
                pixels[offset] = color.B; pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R; pixels[offset + 3] = color.A;
            }
        }
        return token.IsCancellationRequested ? null : pixels;
    }
}
