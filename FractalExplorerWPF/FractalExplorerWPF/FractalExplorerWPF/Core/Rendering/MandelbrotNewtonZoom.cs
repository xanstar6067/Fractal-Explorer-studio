using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Итог поиска ядра минимандельброта. Координаты возвращаются десятичными строками
/// инвариантной культуры — в том же виде, в каком их хранит
/// <see cref="MandelbrotState.CenterXExact"/>, чтобы точность не терялась по дороге.
/// </summary>
/// <param name="Found">Ядро найдено и пригодно к применению.</param>
/// <param name="CenterX">Действительная часть найденного ядра.</param>
/// <param name="CenterY">Мнимая часть найденного ядра.</param>
/// <param name="Period">Период цикла, к которому привязано ядро.</param>
/// <param name="SuggestedZoom">Зум, при котором минимандельброт занимает заметную часть кадра.</param>
/// <param name="NewtonSteps">Сколько шагов Ньютона потребовалось до сходимости.</param>
/// <param name="DriftInViews">На сколько ширин исходного кадра уехал центр.</param>
/// <param name="Message">Описание результата для пользователя.</param>
public readonly record struct MandelbrotNucleusResult(
    bool Found,
    string CenterX,
    string CenterY,
    int Period,
    FloatExp SuggestedZoom,
    int NewtonSteps,
    double DriftInViews,
    string Message)
{
    public static MandelbrotNucleusResult Failure(string message) =>
        new(false, string.Empty, string.Empty, 0, FloatExp.One, 0, 0, message);
}

/// <summary>
/// Поиск ядра (нуклеуса) ближайшего минимандельброта методом Ньютона — «Newton–Raphson zoom».
///
/// На сверхглубоком зуме навести вид руками невозможно: интересные места — окрестности ядер
/// минимандельбротов и точек Мизюревича, а их размер на глубине 1e1000 составляет порядка
/// 1e-1000 от всей плоскости. Попасть туда панорамированием нельзя в принципе, поэтому
/// глубина без этого инструмента остаётся технически работающей, но неприменимой.
///
/// Схема стандартная и состоит из трёх шагов:
/// 1. <b>Период.</b> Индекс атом-домена — это <c>argmin |zₙ|</c> по опорной орбите центра
///    (<see cref="DetectPeriod"/>). Орбита берётся из кэша рендера, поэтому шаг бесплатный.
/// 2. <b>Ньютон.</b> Ядро периода <c>p</c> — корень уравнения <c>f_c^p(0) = 0</c>. Итерация
///    <c>c ← c − z_p / (dz_p/dc)</c> в <see cref="BigFloat"/> сходится квадратично, поэтому
///    хватает единиц шагов (<see cref="MaxNewtonSteps"/> — страховка).
/// 3. <b>Размер.</b> Классическая оценка размера ядра <c>1/(b·l²)</c> (Jay Hill), где
///    <c>l</c> — множитель цикла, <c>b</c> — поправка. Она и задаёт предлагаемый зум.
///
/// Применимо к формулам вида <c>zᵖ + c</c> с целой <c>p</c>: только у них ядро задаётся
/// аналитическим уравнением, к которому применим Ньютон. Отражённые варианты (Burning Ship,
/// Tricorn, Buffalo, Celtic) не комплексно-аналитичны, у Симоноброта в формуле есть модуль, а
/// у Жюлиа параметр <c>c</c> фиксирован и ядра в динамической плоскости нет.
/// </summary>
public static class MandelbrotNewtonZoom
{
    // Страховка по числу шагов: сходимость квадратичная, на практике нужно 3–8 шагов.
    private const int MaxNewtonSteps = 24;

    // Запас разрядности мантиссы сверх log2(зума) для арифметики Ньютона. Ядро нужно найти
    // заметно точнее размера самого минимандельброта, иначе уточнённый центр «не доедет».
    private const int NewtonGuardBits = 256;

    // Во сколько раз ширина кадра предлагается больше оценённого размера ядра. 8 — вид, где
    // минимандельброт целиком в кадре с полями, а не упирается в границы.
    private const double FramingFactor = 8.0;

    // Проверка на расходимость: всё множество Мандельброта лежит в круге |c| ≤ 2, поэтому
    // ядро вне |c| ≤ 2.5 означает, что Ньютон улетел, а не нашёл деталь. Ограничения «не
    // дальше N кадров» здесь нет намеренно: период определяется по атом-домену, то есть
    // описывает минимандельброт, чей домен накрывает центр, а такие домены бывают намного
    // крупнее кадра — ядро закономерно оказывается вне него. Это осмысленный переход к той
    // детали, которой принадлежит текущее место, поэтому величина смещения возвращается в
    // результате и показывается пользователю, а не служит поводом для отказа.
    private const double MaxNucleusRadiusSquared = 6.25;

    /// <summary>Поддерживается ли поиск ядра для этого варианта и степени.</summary>
    public static bool IsSupported(MandelbrotVariant variant, decimal power) => variant switch
    {
        MandelbrotVariant.Mandelbrot => true,
        MandelbrotVariant.Generalized => power == decimal.Truncate(power) && power >= 2 && power <= 12,
        _ => false,
    };

    /// <summary>Причина, по которой поиск недоступен — текстом для пользователя.</summary>
    public static string UnsupportedReason(MandelbrotVariant variant) => variant switch
    {
        MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip =>
            "В множестве Жюлиа константа C фиксирована, ядер минимандельбротов в динамической плоскости нет.",
        MandelbrotVariant.BurningShip or MandelbrotVariant.Tricorn
            or MandelbrotVariant.Buffalo or MandelbrotVariant.Celtic =>
            "Формула варианта не комплексно-аналитична (содержит модули компонент), метод Ньютона к ней неприменим.",
        MandelbrotVariant.Simonobrot =>
            "Формула Симоноброта содержит модуль |z|, метод Ньютона к ней неприменим.",
        MandelbrotVariant.Generalized =>
            "Поиск ядра доступен только для целой степени от 2 до 12.",
        _ => "Поиск ядра для этого варианта не поддерживается.",
    };

    /// <summary>
    /// Ищет ядро ближайшего минимандельброта для текущего вида. Метод считает в
    /// <see cref="BigFloat"/> и при большом периоде работает секунды — вызывать следует с
    /// фонового потока, передавая <paramref name="token"/> и <paramref name="reportProgress"/>
    /// (прогресс в процентах).
    ///
    /// Определение периода бесплатно, пока опорная орбита центра уже посчитана для текущих
    /// параметров (обычный случай — сразу после отрисовки кадра). Если параметры с момента
    /// последнего рендера менялись, к работе добавляется расчёт самой орбиты, и на большой
    /// глубине при миллионе итераций он измеряется десятками секунд; отменить его нельзя —
    /// кэш опорных орбит рендера токена не принимает.
    /// </summary>
    public static MandelbrotNucleusResult FindNucleus(
        MandelbrotState state, CancellationToken token, Action<int>? reportProgress = null)
    {
        if (!IsSupported(state.Variant, state.Power))
            return MandelbrotNucleusResult.Failure(UnsupportedReason(state.Variant));

        int power = state.Variant == MandelbrotVariant.Generalized ? (int)state.Power : 2;

        // --- 1. Период: argmin |zₙ| по опорной орбите центра.
        reportProgress?.Invoke(2);
        int period = DetectPeriod(state, out string periodFailure);
        if (period <= 0) return MandelbrotNucleusResult.Failure(periodFailure);

        // --- 2. Ньютон по уравнению f_c^p(0) = 0.
        using var precision = new BigFloat.PrecisionScope(NewtonPrecisionBits(state.Zoom));

        BigFloat centerX = ParseCenter(state.CenterXExact, state.CenterX);
        BigFloat centerY = ParseCenter(state.CenterYExact, state.CenterY);
        var start = new ComplexBigFloat(centerX, centerY);

        FloatExp viewWidth = 3.0 / state.Zoom;
        // Сходимость считается достигнутой, когда шаг стал заведомо меньше любой детали кадра.
        BigFloat tolerance = (viewWidth * 1e-15).ToBigFloat();

        ComplexBigFloat nucleus = start;
        int steps = 0;
        bool converged = false;

        for (; steps < MaxNewtonSteps; steps++)
        {
            if (token.IsCancellationRequested)
                return MandelbrotNucleusResult.Failure("Поиск ядра отменён.");
            reportProgress?.Invoke(5 + steps * 80 / MaxNewtonSteps);

            (ComplexBigFloat value, ComplexBigFloat derivative) =
                OrbitWithDerivative(nucleus, period, power, token);
            // Прерванный обход орбиты возвращает частичный результат — использовать его как
            // шаг Ньютона нельзя.
            if (token.IsCancellationRequested)
                return MandelbrotNucleusResult.Failure("Поиск ядра отменён.");

            if (derivative.MagnitudeSquared.IsZero)
                return MandelbrotNucleusResult.Failure(
                    $"Производная обратилась в ноль на периоде {period}: шаг Ньютона неопределён.");

            ComplexBigFloat step = value / derivative;
            nucleus -= step;

            if (BigFloat.Abs(step.Real) <= tolerance && BigFloat.Abs(step.Imaginary) <= tolerance)
            {
                converged = true;
                steps++;
                break;
            }
        }

        if (!converged)
            return MandelbrotNucleusResult.Failure(
                $"Ньютон не сошёлся за {MaxNewtonSteps} шагов (период {period}). " +
                "Попробуйте сместить вид ближе к центру минимандельброта или увеличить число итераций.");

        // Уехавший далеко результат означает, что найдено не то ядро, которое видно в кадре.
        double nucleusRadiusSquared = FloatExp.FromBigFloat(nucleus.MagnitudeSquared).ToDouble();
        if (!double.IsFinite(nucleusRadiusSquared) || nucleusRadiusSquared > MaxNucleusRadiusSquared)
            return MandelbrotNucleusResult.Failure(
                $"Ньютон разошёлся: точка периода {period} вышла за круг |c| ≤ 2, в котором " +
                "целиком лежит множество. Наведите вид точнее или увеличьте число итераций.");

        ComplexBigFloat drift = nucleus - start;
        FloatExp driftMagnitude = FloatExp.Sqrt(FloatExp.FromBigFloat(drift.MagnitudeSquared));
        double driftInViews = viewWidth.Sign > 0 ? (driftMagnitude / viewWidth).ToDouble() : 0.0;
        if (!double.IsFinite(driftInViews)) driftInViews = 0.0;

        // --- 3. Оценка размера ядра и предлагаемый зум.
        reportProgress?.Invoke(92);
        FloatExp sizeMagnitude = EstimateNucleusSize(nucleus, period, power, token);
        if (token.IsCancellationRequested)
            return MandelbrotNucleusResult.Failure("Поиск ядра отменён.");
        FloatExp suggestedZoom = sizeMagnitude.Sign > 0 && sizeMagnitude.IsFinite
            ? 3.0 / (sizeMagnitude * FramingFactor)
            : state.Zoom;
        if (!suggestedZoom.IsFinite || suggestedZoom.Sign <= 0) suggestedZoom = state.Zoom;

        reportProgress?.Invoke(100);
        string driftText = driftInViews < 0.01
            ? "центр почти не сместился"
            : driftInViews < 1000.0
                ? $"центр смещён на {driftInViews:0.##} ширин кадра"
                : $"центр смещён далеко за кадр (на {driftInViews:0.##e+0} его ширин)";
        return new MandelbrotNucleusResult(
            true,
            nucleus.Real.ToInvariantString(),
            nucleus.Imaginary.ToInvariantString(),
            period,
            suggestedZoom,
            steps,
            driftInViews,
            $"Ядро периода {period} найдено за {steps} " +
            $"{(steps == 1 ? "шаг" : steps < 5 ? "шага" : "шагов")} Ньютона, {driftText}.");
    }

    /// <summary>
    /// Разрядность мантиссы для арифметики Ньютона: нужно разрешить размер ядра (порядка
    /// обратного зума) с большим запасом.
    /// </summary>
    private static int NewtonPrecisionBits(FloatExp zoom)
    {
        double zoomBits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int needed = (int)System.Math.Ceiling(zoomBits) + NewtonGuardBits;
        return System.Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
    }

    private static BigFloat ParseCenter(string? exact, decimal fallback) =>
        exact is { Length: > 0 } text ? BigFloat.Parse(text) : BigFloat.FromDecimal(fallback);

    /// <summary>
    /// Период атом-домена центра: индекс <c>n ≥ 1</c>, на котором |zₙ| минимален. Орбита
    /// берётся из кэша глубокого движка — того самого, что уже посчитан для текущего кадра,
    /// поэтому шаг не стоит ничего. Индекс 0 пропускается: z₀ = 0 и даёт тривиальный минимум.
    /// </summary>
    private static int DetectPeriod(MandelbrotState state, out string failure)
    {
        failure = string.Empty;
        (double[] re, double[] im, int length) = MandelbrotFamilyRenderer.GetCenterOrbitForAnalysis(state);

        if (length < 3)
        {
            failure = "Опорная орбита центра слишком коротка: вид лежит в области, " +
                      "откуда орбита убегает за считаные итерации. Приблизьтесь к границе множества.";
            return 0;
        }

        int period = 0;
        double best = double.MaxValue;
        for (int index = 1; index < length; index++)
        {
            double magnitudeSquared = re[index] * re[index] + im[index] * im[index];
            if (!double.IsFinite(magnitudeSquared) || magnitudeSquared >= best) continue;
            best = magnitudeSquared;
            period = index;
        }

        if (period <= 0)
        {
            failure = "Не удалось определить период: на опорной орбите нет выраженного минимума |z|.";
            return 0;
        }

        return period;
    }

    /// <summary>
    /// <c>p</c>-я итерация нуля вместе с её производной по <c>c</c>:
    /// <c>z ← zᵖʷ + c</c>, <c>dz ← pw·zᵖʷ⁻¹·dz + 1</c>.
    /// </summary>
    private static (ComplexBigFloat Value, ComplexBigFloat Derivative) OrbitWithDerivative(
        ComplexBigFloat c, int period, int power, CancellationToken token)
    {
        ComplexBigFloat z = ComplexBigFloat.Zero;
        ComplexBigFloat dz = ComplexBigFloat.Zero;

        for (int index = 0; index < period; index++)
        {
            if ((index & 4095) == 0 && token.IsCancellationRequested) break;
            // Производная считается по z предыдущего шага, поэтому идёт первой.
            dz = CycleMultiplier(z, power) * dz + 1;
            z = Step(z, power) + c;
        }

        return (z, dz);
    }

    /// <summary>Один шаг формулы без свободного члена: <c>zᵖʷ</c>.</summary>
    private static ComplexBigFloat Step(ComplexBigFloat z, int power) =>
        power == 2 ? z * z : ComplexBigFloat.Pow(z, power);

    /// <summary>
    /// Множитель шага <c>d(zᵖʷ)/dz = pw·zᵖʷ⁻¹</c>. Для квадратичного случая это просто 2z.
    /// </summary>
    private static ComplexBigFloat CycleMultiplier(ComplexBigFloat z, int power) =>
        power == 2 ? z * 2L : ComplexBigFloat.Pow(z, power - 1) * (long)power;

    /// <summary>
    /// Оценка размера ядра по классической формуле <c>1/(b·l²)</c>: <c>l</c> — множитель
    /// цикла (произведение множителей шагов), <c>b</c> — поправка <c>Σ 1/l</c>. Возвращается
    /// модуль в расширенном диапазоне: на глубине 1e1000 сам размер в double не представим.
    /// </summary>
    private static FloatExp EstimateNucleusSize(
        ComplexBigFloat nucleus, int period, int power, CancellationToken token)
    {
        ComplexBigFloat z = ComplexBigFloat.Zero;
        ComplexBigFloat l = ComplexBigFloat.One;
        ComplexBigFloat b = ComplexBigFloat.One;

        for (int index = 1; index < period; index++)
        {
            if ((index & 4095) == 0 && token.IsCancellationRequested) break;
            z = Step(z, power) + nucleus;
            l = CycleMultiplier(z, power) * l;
            if (l.MagnitudeSquared.IsZero) return FloatExp.Zero;
            b += ComplexBigFloat.One / l;
        }

        ComplexBigFloat denominator = b * l * l;
        if (denominator.MagnitudeSquared.IsZero) return FloatExp.Zero;

        ComplexBigFloat size = ComplexBigFloat.One / denominator;
        return FloatExp.Sqrt(FloatExp.FromBigFloat(size.MagnitudeSquared));
    }
}
