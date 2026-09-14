using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Итог поиска ядра Феникса. Координаты — десятичные строки инвариантной культуры, в том же
/// виде, в каком их хранит <see cref="PhoenixState.CenterXExact"/>.
/// </summary>
/// <param name="Found">Ядро найдено и пригодно к применению.</param>
/// <param name="CenterX">Действительная часть найденной точки.</param>
/// <param name="CenterY">Мнимая часть найденной точки.</param>
/// <param name="Period">Номер итерации, на которой орбита проходит через ноль.</param>
/// <param name="SuggestedZoom">Зум, при котором найденная деталь занимает заметную часть кадра.</param>
/// <param name="NewtonSteps">Сколько шагов Ньютона потребовалось до сходимости.</param>
/// <param name="DriftInViews">На сколько ширин исходного кадра уехал центр.</param>
/// <param name="Message">Описание результата для пользователя.</param>
public readonly record struct PhoenixNucleusResult(
    bool Found,
    string CenterX,
    string CenterY,
    int Period,
    FloatExp SuggestedZoom,
    int NewtonSteps,
    double DriftInViews,
    string Message)
{
    public static PhoenixNucleusResult Failure(string message) =>
        new(false, string.Empty, string.Empty, 0, FloatExp.One, 0, 0, message);
}

/// <summary>
/// Поиск ядра методом Ньютона для Феникса — та же схема, что у
/// <see cref="MandelbrotNewtonZoom"/>, перенесённая на рекуррентность с памятью.
///
/// На сверхглубоком зуме интересные места занимают порядка обратного зума от всей плоскости,
/// и панорамированием туда не попасть. Ядро здесь — точка, чья орбита на итерации p проходит
/// ровно через ноль: <c>z_p = 0</c>. В параметрической плоскости это центр мини-копии
/// множества (как ядро минимандельброта), в динамической — прообраз нуля, вокруг которого
/// лежит уменьшенная копия окрестности нуля.
/// <list type="number">
/// <item><b>Период</b> — по опорной орбите центра из кэша рендера: наименьшее n, чей корень
/// попадает в кадр, иначе <c>argmin |zₙ|</c> (см. <see cref="DetectPeriods"/>).</item>
/// <item><b>Ньютон</b> по <c>z_p(x) = 0</c>, где x — пиксель плоскости (z₀ в динамической, c1 в
/// параметрической). Производная ведётся той же рекуррентностью второго порядка:
/// <c>dzₙ₊₁ = (a·zₙ^(a−1) + c1·b·zₙ^(b−1))·dzₙ + c2·dzₙ₋₁ (+ zₙᵇ по c1)</c>.</item>
/// <item><b>Размер</b>: в динамической плоскости <c>1/|dz_p/dz₀|</c> — деталь, которую p шагов
/// растягивают до единичного масштаба; в параметрической — классическая оценка
/// <c>1/(|λ|·|dz_p/dc|)</c>, где λ — произведение множителей шагов вдоль цикла.</item>
/// </list>
///
/// Применимо только к классическому варианту: у Трикорна сопряжение, у горящего, кельтского и
/// буффало — модули компонент, и формула не комплексно-аналитична.
/// </summary>
public static class PhoenixNewtonZoom
{
    private const int MaxNewtonSteps = 24;
    private const int NewtonGuardBits = 256;

    // Ширина кадра в размерах найденной детали: деталь целиком в кадре и с полями.
    private const double FramingFactor = 8.0;

    // Ньютон, улетевший дальше этого радиуса, не нашёл деталь: орбита любой точки за ним
    // уходит на бесконечность за первые шаги при любых разумных параметрах.
    private const double MaxNucleusRadiusSquared = 1e4;

    public static bool IsSupported(PhoenixState state) => state.Variant == PhoenixVariant.Classic;

    public static string UnsupportedReason(PhoenixState state) =>
        "Поиск ядра доступен только для классического варианта: у остальных формула содержит " +
        "сопряжение или модули компонент и не комплексно-аналитична, метод Ньютона к ней неприменим.";

    /// <summary>
    /// Ищет ядро для текущего вида. Считает в <see cref="BigFloat"/> и на большом периоде
    /// работает секунды — вызывать с фонового потока.
    /// </summary>
    public static PhoenixNucleusResult FindNucleus(
        PhoenixState state, CancellationToken token, Action<int>? reportProgress = null)
    {
        if (!IsSupported(state)) return PhoenixNucleusResult.Failure(UnsupportedReason(state));

        reportProgress?.Invoke(2);
        IReadOnlyList<int> periods = DetectPeriods(state, out string periodFailure);
        if (periods.Count == 0) return PhoenixNucleusResult.Failure(periodFailure);

        // Кандидаты по порядку предпочтения; первый сошедшийся и невырожденный — ответ.
        PhoenixNucleusResult result = default;
        foreach (int period in periods)
        {
            result = TryPeriod(state, period, token, reportProgress);
            if (result.Found || token.IsCancellationRequested) break;
        }
        return result;
    }

    private static PhoenixNucleusResult TryPeriod(
        PhoenixState state, int period, CancellationToken token, Action<int>? reportProgress)
    {
        using var precision = new BigFloat.PrecisionScope(NewtonPrecisionBits(state.Zoom));
        var formula = new Formula(state);

        var start = new ComplexBigFloat(
            ParseCenter(state.CenterXExact, state.CenterX),
            ParseCenter(state.CenterYExact, state.CenterY));
        FloatExp viewWidth = 4.0 / state.Zoom;
        BigFloat tolerance = (viewWidth * 1e-15).ToBigFloat();

        ComplexBigFloat point = start;
        int steps = 0;
        bool converged = false;
        for (; steps < MaxNewtonSteps; steps++)
        {
            if (token.IsCancellationRequested) return PhoenixNucleusResult.Failure("Поиск ядра отменён.");
            reportProgress?.Invoke(5 + steps * 80 / MaxNewtonSteps);

            (ComplexBigFloat value, ComplexBigFloat derivative, _) = formula.Orbit(point, period, false, token);
            if (token.IsCancellationRequested) return PhoenixNucleusResult.Failure("Поиск ядра отменён.");
            if (derivative.MagnitudeSquared.IsZero)
                return PhoenixNucleusResult.Failure(
                    $"Производная обратилась в ноль на периоде {period}: шаг Ньютона неопределён.");

            ComplexBigFloat step = value / derivative;
            point -= step;
            if (BigFloat.Abs(step.Real) <= tolerance && BigFloat.Abs(step.Imaginary) <= tolerance)
            {
                converged = true;
                steps++;
                break;
            }
        }

        if (!converged)
            return PhoenixNucleusResult.Failure(
                $"Ньютон не сошёлся за {MaxNewtonSteps} шагов (период {period}). " +
                "Сместите вид ближе к детали или увеличьте число итераций.");

        double radiusSquared = FloatExp.FromBigFloat(point.MagnitudeSquared).ToDouble();
        if (!double.IsFinite(radiusSquared) || radiusSquared > MaxNucleusRadiusSquared)
            return PhoenixNucleusResult.Failure(
                $"Ньютон разошёлся: точка периода {period} ушла далеко за пределы фрактала. " +
                "Наведите вид точнее или увеличьте число итераций.");

        // Вырожденный корень: в параметрической плоскости при z₀ = z₋₁ = 0 точка c1 = 0 даёт
        // zₙ ≡ 0 для любого n и притягивает Ньютон с любого периода.
        (_, _, bool trivial) = formula.Orbit(point, period, true, token);
        if (trivial)
            return PhoenixNucleusResult.Failure(
                $"Ньютон сошёлся к вырожденной точке, где орбита тождественно равна нулю (период {period}). " +
                "Сместите вид от неё и повторите.");

        ComplexBigFloat drift = point - start;
        FloatExp driftMagnitude = FloatExp.Sqrt(FloatExp.FromBigFloat(drift.MagnitudeSquared));
        // Смещение в ширинах кадра на глубине бывает вне double (ядро крупной детали за 1e300
        // ширин от центра) — тогда ToDouble даёт ∞, и это честный ответ, а не повод для нуля.
        FloatExp driftViews = viewWidth.Sign > 0 ? driftMagnitude / viewWidth : FloatExp.Zero;
        double driftInViews = driftViews.ToDouble();
        if (double.IsNaN(driftInViews)) driftInViews = 0.0;

        reportProgress?.Invoke(92);
        FloatExp size = formula.EstimateSize(point, period, token);
        if (token.IsCancellationRequested) return PhoenixNucleusResult.Failure("Поиск ядра отменён.");
        FloatExp suggestedZoom = size.Sign > 0 && size.IsFinite ? 4.0 / (size * FramingFactor) : state.Zoom;
        if (!suggestedZoom.IsFinite || suggestedZoom.Sign <= 0) suggestedZoom = state.Zoom;

        reportProgress?.Invoke(100);
        string driftText = driftInViews < 0.01
            ? "центр почти не сместился"
            : driftInViews < 1000.0
                ? $"центр смещён на {driftInViews:0.##} ширин кадра"
                : $"центр смещён далеко за кадр (на {DriftText(driftViews)} его ширин)";
        return new PhoenixNucleusResult(true,
            point.Real.ToInvariantString(), point.Imaginary.ToInvariantString(),
            period, suggestedZoom, steps, driftInViews,
            $"Ядро периода {period} найдено за {steps} " +
            $"{(steps == 1 ? "шаг" : steps < 5 ? "шага" : "шагов")} Ньютона, {driftText}.");
    }

    /// <summary>Порядок смещения для сообщения: в double-диапазоне обычной записью, за ним — степенью десяти.</summary>
    private static string DriftText(FloatExp driftViews)
    {
        double value = driftViews.ToDouble();
        return double.IsFinite(value)
            ? value.ToString("0.##e+0", System.Globalization.CultureInfo.InvariantCulture)
            : $"~1e+{Math.Floor(driftViews.Log10()).ToString("0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static int NewtonPrecisionBits(FloatExp zoom)
    {
        double zoomBits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int needed = (int)Math.Ceiling(zoomBits) + NewtonGuardBits;
        return Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
    }

    private static BigFloat ParseCenter(string? exact, decimal fallback) =>
        exact is { Length: > 0 } text ? BigFloat.Parse(text) : BigFloat.FromDecimal(fallback);

    /// <summary>
    /// Периоды-кандидаты в порядке предпочтения. Первый — «период круга кадра»: наименьшее
    /// n ≥ 1, при котором линейная оценка корня <c>zₙ(x) = 0</c> попадает в кадр, то есть
    /// <c>|zₙ| ≤ |dzₙ/dx|·r</c> (r — половина ширины кадра). Так находится ядро, лежащее в
    /// самом кадре; линейная оценка на мелком зуме бывает груба, и Ньютон по такому периоду не
    /// сходится. Второй — <c>argmin |zₙ|</c>, как у семейства Мандельброта: он
    /// указывает на деталь, чей домен накрывает центр, но на глубине это нередко крупная
    /// деталь далеко за кадром (замер: у ядра периода 2754 в 0.3 ширины от центра argmin
    /// выбирал период 2730 в 14 ширинах).
    ///
    /// Орбита движка сдвинута на единицу: <c>zₙ = Re[n + 1]</c>. Индекс 0 пропускается: в
    /// параметрической плоскости z₀ — общий старт, в динамической — сам центр.
    /// </summary>
    private static IReadOnlyList<int> DetectPeriods(PhoenixState state, out string failure)
    {
        failure = string.Empty;
        (double[] re, double[] im, int length) = PhoenixRenderer.GetCenterOrbitForAnalysis(state);
        if (length < 4)
        {
            failure = "Опорная орбита центра слишком коротка: вид лежит там, откуда орбита убегает " +
                      "за считаные итерации. Приблизьтесь к границе фрактала.";
            return [];
        }

        var periods = new List<int>(2);
        int ballPeriod = BallPeriod(state, re, im, length);
        if (ballPeriod > 0) periods.Add(ballPeriod);

        int period = 0;
        double best = double.MaxValue;
        for (int n = 1; n + 1 < length; n++)
        {
            double magnitudeSquared = re[n + 1] * re[n + 1] + im[n + 1] * im[n + 1];
            if (!double.IsFinite(magnitudeSquared) || magnitudeSquared >= best) continue;
            best = magnitudeSquared;
            period = n;
        }

        if (period > 0 && period != ballPeriod) periods.Add(period);
        if (periods.Count == 0)
            failure = "Не удалось определить период: на опорной орбите нет выраженного минимума |z|.";
        return periods;
    }

    /// <summary>
    /// Наименьшее n, для которого <c>|zₙ| ≤ |dzₙ/dx|·r</c>, или 0. Производная ведётся вдоль
    /// double-орбиты в <see cref="FloatExp"/>: на глубине 1e1000 она растёт до 1e1000 и в double
    /// не помещается. Для оценки «попадает ли корень в кадр» точности double-орбиты хватает.
    /// </summary>
    private static int BallPeriod(PhoenixState state, double[] re, double[] im, int length)
    {
        bool parameterPlane = state.PlaneMode == PhoenixPlaneMode.ParameterC1;
        double c1Real = parameterPlane ? (double)state.CenterX : (double)state.C1Real;
        double c1Imaginary = parameterPlane ? (double)state.CenterY : (double)state.C1Imaginary;
        double c2Real = (double)state.C2Real, c2Imaginary = (double)state.C2Imaginary;
        FloatExp radius = 2.0 / state.Zoom;
        int a = state.PrimaryPower, b = state.SecondaryPower;

        FloatExp dzReal = parameterPlane ? FloatExp.Zero : FloatExp.One, dzImaginary = FloatExp.Zero;
        FloatExp dwReal = FloatExp.Zero, dwImaginary = FloatExp.Zero;
        for (int n = 0; n + 2 < length; n++)
        {
            double zReal = re[n + 1], zImaginary = im[n + 1];
            // Множитель шага a·z^(a−1) + c1·b·z^(b−1) и z^b — в double: орбита ограничена радиусом.
            (double primaryReal, double primaryImaginary) = ComplexPower(zReal, zImaginary, a - 1);
            double multiplierReal = a * primaryReal, multiplierImaginary = a * primaryImaginary;
            double secondaryReal = 1, secondaryImaginary = 0;
            if (b > 0)
            {
                (double lowerReal, double lowerImaginary) = ComplexPower(zReal, zImaginary, b - 1);
                double termReal = b * lowerReal, termImaginary = b * lowerImaginary;
                multiplierReal += c1Real * termReal - c1Imaginary * termImaginary;
                multiplierImaginary += c1Real * termImaginary + c1Imaginary * termReal;
                (secondaryReal, secondaryImaginary) = ComplexPower(zReal, zImaginary, b);
            }

            FloatExp nextReal = multiplierReal * dzReal - multiplierImaginary * dzImaginary
                                + (c2Real * dwReal - c2Imaginary * dwImaginary);
            FloatExp nextImaginary = multiplierReal * dzImaginary + multiplierImaginary * dzReal
                                     + (c2Real * dwImaginary + c2Imaginary * dwReal);
            if (parameterPlane)
            {
                nextReal += secondaryReal;
                nextImaginary += secondaryImaginary;
            }
            dwReal = dzReal; dwImaginary = dzImaginary;
            dzReal = nextReal; dzImaginary = nextImaginary;

            int index = n + 1; // теперь dz — производная z_index
            double nextZReal = re[index + 1], nextZImaginary = im[index + 1];
            double magnitude = Math.Sqrt(nextZReal * nextZReal + nextZImaginary * nextZImaginary);
            if (!double.IsFinite(magnitude)) return 0;
            FloatExp reach = FloatExp.Sqrt(FloatExp.MagnitudeSquared(dzReal, dzImaginary)) * radius;
            if (reach.IsFinite && FloatExp.FromDouble(magnitude) <= reach) return index;
        }
        return 0;
    }

    private static (double Real, double Imaginary) ComplexPower(double real, double imaginary, int power)
    {
        double resultReal = 1, resultImaginary = 0;
        for (int k = 0; k < power; k++)
            (resultReal, resultImaginary) = (resultReal * real - resultImaginary * imaginary,
                resultReal * imaginary + resultImaginary * real);
        return (resultReal, resultImaginary);
    }

    /// <summary>Формула Феникса в произвольной точности с производной по пикселю плоскости.</summary>
    private readonly struct Formula
    {
        private readonly int _primaryPower, _secondaryPower;
        private readonly bool _parameterPlane;
        private readonly ComplexBigFloat _c1, _c2, _initial, _previous;

        public Formula(PhoenixState state)
        {
            _primaryPower = state.PrimaryPower;
            _secondaryPower = state.SecondaryPower;
            _parameterPlane = state.PlaneMode == PhoenixPlaneMode.ParameterC1;
            _c1 = ComplexBigFloat.FromDecimal(state.C1Real, state.C1Imaginary);
            _c2 = ComplexBigFloat.FromDecimal(state.C2Real, state.C2Imaginary);
            // Та же оговорка об автоматическом старте, что у рендера: при b > 0 и нулевых
            // z₀/z₋₁ параметрическая карта иначе вырождается.
            bool automaticStart = state.SecondaryPower > 0 &&
                                  state.InitialZReal == 0 && state.InitialZImaginary == 0 &&
                                  state.InitialPreviousReal == 0 && state.InitialPreviousImaginary == 0;
            _initial = automaticStart ? ComplexBigFloat.One : ComplexBigFloat.FromDecimal(state.InitialZReal, state.InitialZImaginary);
            _previous = ComplexBigFloat.FromDecimal(state.InitialPreviousReal, state.InitialPreviousImaginary);
        }

        /// <summary>
        /// <c>z_p</c> и его производная по x. С <paramref name="checkTrivial"/> дополнительно
        /// сообщает, что вся орбита z₀..z_{p−1} нулевая (вырожденный корень).
        /// </summary>
        public (ComplexBigFloat Value, ComplexBigFloat Derivative, bool Trivial) Orbit(
            ComplexBigFloat x, int period, bool checkTrivial, CancellationToken token)
        {
            ComplexBigFloat c1 = _parameterPlane ? x : _c1;
            ComplexBigFloat z = _parameterPlane ? _initial : x;
            ComplexBigFloat w = _previous;
            ComplexBigFloat dz = _parameterPlane ? ComplexBigFloat.Zero : ComplexBigFloat.One;
            ComplexBigFloat dw = ComplexBigFloat.Zero;
            // Вырожденна только орбита, нулевая целиком — начиная со стартовой точки: при
            // периоде 1 промежуточных точек нет вовсе, и без проверки старта любой корень
            // считался бы вырожденным.
            bool trivial = checkTrivial && IsNegligible(z);
            for (int n = 0; n < period; n++)
            {
                if ((n & 1023) == 0 && token.IsCancellationRequested) break;
                ComplexBigFloat secondary = Power(z, _secondaryPower);
                ComplexBigFloat nextDerivative = Multiplier(z, c1) * dz + _c2 * dw;
                if (_parameterPlane) nextDerivative += secondary;
                ComplexBigFloat next = Power(z, _primaryPower) + c1 * secondary + _c2 * w;
                w = z;
                z = next;
                dw = dz;
                dz = nextDerivative;
                if (trivial && n + 1 < period && !IsNegligible(z)) trivial = false;
            }
            return (z, dz, trivial);
        }

        /// <summary>
        /// Размер детали. Динамическая плоскость: <c>1/|dz_p/dz₀|</c>. Параметрическая:
        /// <c>1/(|λ|·|dz_p/dc|)</c>, λ — произведение множителей шагов на итерациях 1..p−1
        /// (z₀ — общий старт и в цикл не входит).
        /// </summary>
        public FloatExp EstimateSize(ComplexBigFloat x, int period, CancellationToken token)
        {
            ComplexBigFloat c1 = _parameterPlane ? x : _c1;
            ComplexBigFloat z = _parameterPlane ? _initial : x;
            ComplexBigFloat w = _previous;
            ComplexBigFloat dz = _parameterPlane ? ComplexBigFloat.Zero : ComplexBigFloat.One;
            ComplexBigFloat dw = ComplexBigFloat.Zero;
            FloatExp multiplier = FloatExp.One;
            for (int n = 0; n < period; n++)
            {
                if ((n & 1023) == 0 && token.IsCancellationRequested) return FloatExp.Zero;
                ComplexBigFloat stepMultiplier = Multiplier(z, c1);
                if (n > 0) multiplier *= FloatExp.Sqrt(FloatExp.FromBigFloat(stepMultiplier.MagnitudeSquared));
                ComplexBigFloat secondary = Power(z, _secondaryPower);
                ComplexBigFloat nextDerivative = stepMultiplier * dz + _c2 * dw;
                if (_parameterPlane) nextDerivative += secondary;
                ComplexBigFloat next = Power(z, _primaryPower) + c1 * secondary + _c2 * w;
                w = z;
                z = next;
                dw = dz;
                dz = nextDerivative;
            }

            FloatExp derivative = FloatExp.Sqrt(FloatExp.FromBigFloat(dz.MagnitudeSquared));
            FloatExp denominator = _parameterPlane ? multiplier * derivative : derivative;
            return denominator.Sign > 0 && denominator.IsFinite ? 1.0 / denominator : FloatExp.Zero;
        }

        /// <summary><c>a·z^(a−1) + c1·b·z^(b−1)</c> — множитель шага по z.</summary>
        private ComplexBigFloat Multiplier(ComplexBigFloat z, ComplexBigFloat c1)
        {
            ComplexBigFloat primary = Power(z, _primaryPower - 1) * (long)_primaryPower;
            if (_secondaryPower == 0) return primary;
            return primary + c1 * (Power(z, _secondaryPower - 1) * (long)_secondaryPower);
        }

        private static ComplexBigFloat Power(ComplexBigFloat z, int power) => power switch
        {
            <= 0 => ComplexBigFloat.One,
            1 => z,
            2 => z * z,
            _ => ComplexBigFloat.Pow(z, power)
        };

        private static bool IsNegligible(ComplexBigFloat z) =>
            z.MagnitudeSquared.IsZero || FloatExp.FromBigFloat(z.MagnitudeSquared).Log2() < -2 * (BigFloat.WorkingPrecisionBits - 16);
    }
}
