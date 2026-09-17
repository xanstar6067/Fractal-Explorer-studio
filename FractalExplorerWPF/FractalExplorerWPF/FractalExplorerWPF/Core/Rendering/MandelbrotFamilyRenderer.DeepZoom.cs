using System.Globalization;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// «Второй двигатель» рендера семейства Мандельброта — пертурбационный метод.
///
/// Для каждого поддерживаемого варианта (<see cref="SupportsDeepZoom"/>: Mandelbrot/Julia,
/// 5 отражённых, целая степень Multibrot/Simonobrot) лестница точности схлопнута до двух
/// ступеней: плоский double (<see cref="Iterate"/>) до <see cref="PerturbationZoomThreshold"/>
/// (~1.5e9), выше — этот движок, для любого режима окраски. Histogram
/// (<see cref="RenderDeepZoomHistogram"/>) и Distance Estimation
/// (<see cref="RenderDeepZoomDistanceEstimation"/>) — двухпроходные, как и их decimal-версии.
/// Ступень <see cref="decimal"/> (<see cref="IterateDecimal"/>) осталась только для
/// вариантов/степеней вне <see cref="SupportsDeepZoom"/> и как запасной путь при
/// вырожденной опорной орбите.
///
/// 1. Один раз на кадр считается опорная орбита <c>Zₙ</c> в центре области — в
///    <see cref="BigFloat"/> с адаптивной точностью (<see cref="PlanDeepZoom"/>),
///    результат кэшируется в double-массивах.
/// 2. Каждый пиксель итерирует лишь отклонение <c>δₙ = zₙ − Zₙ</c>:
///    <c>δₙ₊₁ = 2·Zₙ·δₙ + δₙ² + δc</c> (для Жюлиа слагаемое δc отсутствует, а δc задаёт δ₀).
///    Представление δ — обычный double либо <see cref="FloatExp"/> за ~1e72 (тот же
///    <see cref="PlanDeepZoom"/>).
/// 3. Потеря значимости («глитчи») лечится rebasing по Zhuoran: когда |zₙ| &lt; |δₙ| или
///    опорная орбита закончилась, δ сбрасывается в <c>z − Z₀</c>, а индекс опорной точки — в 0.
///
/// Раскраска, палитры и <see cref="PixelMetrics"/> переиспользуются из основного файла —
/// поэтому класс объявлен <c>partial</c>.
/// </summary>
public static partial class MandelbrotFamilyRenderer
{
    private sealed class ReferenceOrbit
    {
        public required double[] Re;
        public required double[] Im;

        /// <summary>Количество заполненных точек (индексы 0..<see cref="Length"/>-1).</summary>
        public required int Length;

        /// <summary>Опорная орбита вышла за радиус раньше, чем достигла числа итераций.</summary>
        public required bool Escaped;

        /// <summary>Пирамида BLA для ускорения (null — не построена/не нужна).</summary>
        public BlaTable? Bla;

        /// <summary>
        /// Пирамида BLA с вещественной 2×2 линейной частью — для отражённых вариантов и
        /// Симоноброта (null — не построена/не нужна). С <see cref="Bla"/> взаимоисключающа.
        /// </summary>
        public RealBlaTable? RealBla;
    }

    // Критерий Pauldelbrot для rebasing: если |z|² падает ниже этой доли от |Zref|²,
    // опорная точка считается ненадёжной. 1e-6 — общепринятое значение.
    private const double GlitchToleranceSquared = 1e-6;

    private static readonly object _orbitLock = new();
    private static string? _orbitKey;
    private static ReferenceOrbit? _orbitCache;

    // Тестовый шов: принудительно включает/выключает пертурбационный движок независимо от
    // зума (в пределах поддерживаемых вариантов и режимов). null — по порогу зума.
    internal static bool? ForceDeepZoomForTests { get; set; }

    // Целая степень Multibrot (Generalized), для которой применим глубокий движок:
    // z^p через умножение и биномиальное возмущение (дробная степень требует pow в BigFloat).
    private const int MinMultibrotPower = 2;
    private const int MaxMultibrotPower = 12;

    private static bool IsMultibrotDeepZoomPower(decimal power) =>
        power == decimal.Truncate(power) && power >= MinMultibrotPower && power <= MaxMultibrotPower;

    private static int MultibrotPowerOrZero(MandelbrotState state) =>
        state.Variant == MandelbrotVariant.Generalized && IsMultibrotDeepZoomPower(state.Power)
            ? (int)state.Power
            : 0;

    // Целая степень Симоноброта (zᵖ·|z|ᵖ+c), для которой применим глубокий движок.
    // При чётном p=2q модуль |z|ᵖ = (zr²+zi²)^q — многочлен; при нечётном p=2q+1 это
    // Mᵠ·√M, поэтому нужен BigFloat.Sqrt в опорной орбите и точная
    // пертурбация корня в ядре. Дробная (нужен pow) и отрицательная (полюс в нуле)
    // степень остаются на decimal.
    private const int MinSimonobrotPower = 2;
    private const int MaxSimonobrotPower = 12;

    private static bool IsSimonobrotDeepZoomPower(decimal power) =>
        power == decimal.Truncate(power) && power >= MinSimonobrotPower && power <= MaxSimonobrotPower;

    private static int SimonobrotPowerOrZero(MandelbrotState state) =>
        state.Variant == MandelbrotVariant.Simonobrot && IsSimonobrotDeepZoomPower(state.Power)
            ? (int)state.Power
            : 0;

    private static bool SupportsDeepZoom(MandelbrotState state) => state.Variant switch
    {
        MandelbrotVariant.Mandelbrot or MandelbrotVariant.Julia
            or MandelbrotVariant.BurningShip or MandelbrotVariant.JuliaBurningShip
            or MandelbrotVariant.Tricorn or MandelbrotVariant.Buffalo or MandelbrotVariant.Celtic => true,
        MandelbrotVariant.Generalized => IsMultibrotDeepZoomPower(state.Power),
        MandelbrotVariant.Simonobrot => IsSimonobrotDeepZoomPower(state.Power),
        _ => false,
    };

    private static bool ShouldUseDeepZoom(MandelbrotState state)
    {
        if (!SupportsDeepZoom(state))
            return false;
        // Все режимы окраски обслуживаются здесь: Histogram — двухпроходно
        // (RenderDeepZoomHistogram), Distance Estimation — с производной по опорной орбите
        // (RenderDeepZoomDistanceEstimation), остальные — одним проходом.
        return ForceDeepZoomForTests ?? (state.Zoom > PerturbationZoomThreshold);
    }

    // Варианты семейства с отражением/сопряжением: их линейная часть возмущения — не
    // комплексное умножение, а «свёртка знака» покомпонентно (см. DeepZoomPixelReflected).
    // Ускоряются вещественной 2×2 таблицей RealBlaTable, а не комплексной BlaTable.
    private enum ReflectKind { BurningShip, Buffalo, Tricorn, Celtic }

    private static ReflectKind? ReflectKindOf(MandelbrotVariant variant) => variant switch
    {
        MandelbrotVariant.BurningShip or MandelbrotVariant.JuliaBurningShip => ReflectKind.BurningShip,
        MandelbrotVariant.Buffalo => ReflectKind.Buffalo,
        MandelbrotVariant.Tricorn => ReflectKind.Tricorn,
        MandelbrotVariant.Celtic => ReflectKind.Celtic,
        _ => null,
    };

    private static bool IsJuliaVariant(MandelbrotVariant variant) =>
        variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip;

    // |Zc + δc| − |Zc| без катастрофического сокращения: пока δ не перевернул знак
    // компоненты (обычный случай на глубоком зуме) это ровно ±δc; на перевороте — точное
    // отражённое выражение (δ там уже сравнимо с Z и тут же срабатывает rebasing).
    private static double FoldedDelta(double referenceComponent, double deltaComponent)
    {
        if (referenceComponent > 0.0)
            return deltaComponent > -referenceComponent
                ? deltaComponent
                : -(deltaComponent + 2.0 * referenceComponent);
        if (referenceComponent < 0.0)
            return deltaComponent < -referenceComponent
                ? -deltaComponent
                : deltaComponent + 2.0 * referenceComponent;
        return System.Math.Abs(deltaComponent);
    }

    // Тот же разбор случаев, что и <see cref="FoldedDelta"/>, но δ — в <see cref="FloatExp"/>
    // (для FloatExp-ядра отражённых вариантов). Опорная компонента остаётся double — она из
    // опорной орбиты, ограничена радиусом бейлаута и в расширенном диапазоне не нуждается.
    private static FloatExp FoldedDeltaExp(double referenceComponent, FloatExp deltaComponent)
    {
        if (referenceComponent > 0.0)
            return deltaComponent > -referenceComponent
                ? deltaComponent
                : -(deltaComponent + 2.0 * referenceComponent);
        if (referenceComponent < 0.0)
            return deltaComponent < -referenceComponent
                ? -deltaComponent
                : deltaComponent + 2.0 * referenceComponent;
        return FloatExp.Abs(deltaComponent);
    }

    // ------------------------------------------------------------------ precision planner

    // log2(зума), начиная с которого отклонение δ на пиксель уже не помещается надёжно в
    // обычный double (δ² уходит в денормалы и в ноль): 2^239 ≈ 8.8e71. Ниже порога
    // работает проверенный double-δ путь, бит-в-бит совпадающий с прежним движком; выше —
    // δ ведётся в FloatExp. Порог взят с большим запасом ниже реального отказа double
    // (~1e290), чтобы полоса совпадения с прежним поведением была максимально широкой.
    private const double FloatExpDeltaZoomBits = 239;

    // Тестовый шов: принудительно задаёт представление δ (true — FloatExp, false — double).
    // null — выбор по <see cref="PlanDeepZoom"/>. Используется только из проверочного проекта.
    internal static bool? ForceFloatExpDeltaForTests { get; set; }

    /// <summary>
    /// План точности на кадр: разрядность мантиссы опорной орбиты (адаптивно от глубины
    /// зума и числа итераций, но не ниже <see cref="BigFloat.MinimumPrecisionBits"/>) и
    /// выбор представления δ (double либо FloatExp).
    /// </summary>
    private readonly record struct DeepZoomPlan(int ReferenceBits, bool UseFloatExpDelta);

    private static DeepZoomPlan PlanDeepZoom(MandelbrotState state)
    {
        // Log2 расширенного зума — его двоичная экспонента плюс log2 мантиссы, поэтому
        // формула одинаково работает и на 1e25, и на 1e1000.
        double zoomBits = state.Zoom.Sign > 0 && state.Zoom.IsFinite ? state.Zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;

        // Бит на разрешение соседних пикселей ≈ log2(zoom); удвоенный запас по числу
        // итераций поглощает накопление ошибки округления вдоль опорной орбиты; +48 —
        // суб-пиксельная точность и общий люфт. Ниже ~1e93 формула даёт < 384 и рабочая
        // точность остаётся ровно 384 — прежнее поведение сохраняется бит-в-бит.
        int iterationBits = BitLength(System.Math.Max(state.Iterations, 2));
        int needed = (int)System.Math.Ceiling(zoomBits) + 2 * iterationBits + 48;
        int referenceBits = System.Math.Max(BigFloat.MinimumPrecisionBits, RoundUpToMultiple(needed, 64));

        bool floatExpDelta = ForceFloatExpDeltaForTests ?? (zoomBits >= FloatExpDeltaZoomBits);
        return new DeepZoomPlan(referenceBits, floatExpDelta);
    }

    private static int BitLength(int value) =>
        32 - System.Numerics.BitOperations.LeadingZeroCount((uint)value);

    private static int RoundUpToMultiple(int value, int multiple) =>
        (value + multiple - 1) / multiple * multiple;

    /// <summary>
    /// Величина, обратная размеру пикселя — множитель нормировки расстояния для Distance
    /// Estimation. Вне этого режима ядрам передаётся <see cref="FloatExp.One"/>, и
    /// нормировка превращается в тождество.
    /// </summary>
    private static FloatExp PixelDistanceScale(FloatExp viewWidth, int width) =>
        viewWidth.IsZero || !viewWidth.IsFinite ? FloatExp.One : width / viewWidth;

    private static PixelMetrics DeepZoomPixelDispatch(
        DeepZoomPlan plan,
        MandelbrotState state,
        ReferenceOrbit orbit,
        bool isJulia,
        FloatExp deltaReal,
        FloatExp deltaImaginary,
        double escapeSquared,
        FloatExp distanceScale,
        CancellationToken token)
    {
        // Варианты с отражением/сопряжением идут своим ядром; за порогом FloatExpDeltaZoomBits
        // δ ведётся в FloatExp — тем же переключателем плана, что и у z²+c ниже.
        if (ReflectKindOf(state.Variant) is { } reflect)
            return plan.UseFloatExpDelta
                ? DeepZoomPixelReflectedFloatExp(state, orbit, reflect, isJulia,
                    deltaReal, deltaImaginary, escapeSquared, distanceScale, token)
                : DeepZoomPixelReflected(state, orbit, reflect, isJulia,
                    deltaReal.ToDouble(), deltaImaginary.ToDouble(),
                    escapeSquared, distanceScale.ToDouble(), token);

        // Симоноброт целой степени: композиция возмущений zᵖ и |z|ᵖ=M^(p/2) (при нечётном p
        // множитель модуля несёт корень); BLA — вещественная 2×2 (ведущий линейный член не
        // комплексный). δ — double либо FloatExp по тому же плану.
        int simonobrotPower = SimonobrotPowerOrZero(state);
        if (simonobrotPower >= 2)
            return plan.UseFloatExpDelta
                ? DeepZoomPixelSimonobrotFloatExp(state, orbit, simonobrotPower,
                    deltaReal, deltaImaginary, escapeSquared, distanceScale, token)
                : DeepZoomPixelSimonobrot(state, orbit, simonobrotPower,
                    deltaReal.ToDouble(), deltaImaginary.ToDouble(),
                    escapeSquared, distanceScale.ToDouble(), token);

        // Multibrot целой степени: биномиальное возмущение (BLA с p-зависимым A, комплексная
        // таблица). δ — double либо FloatExp по тому же плану.
        int multibrotPower = MultibrotPowerOrZero(state);
        if (multibrotPower >= 3)
            return plan.UseFloatExpDelta
                ? DeepZoomPixelMultibrotFloatExp(state, orbit, multibrotPower,
                    deltaReal, deltaImaginary, escapeSquared, distanceScale, token)
                : DeepZoomPixelMultibrot(state, orbit, multibrotPower,
                    deltaReal.ToDouble(), deltaImaginary.ToDouble(),
                    escapeSquared, distanceScale.ToDouble(), token);
        // p == 2 (или обычные Mandelbrot/Julia) — общий z²+c-путь ниже.

        return plan.UseFloatExpDelta
            ? DeepZoomPixelFloatExp(state, orbit, isJulia, deltaReal, deltaImaginary,
                escapeSquared, distanceScale, token)
            : DeepZoomPixel(state, orbit, isJulia, deltaReal.ToDouble(), deltaImaginary.ToDouble(),
                escapeSquared, distanceScale.ToDouble(), token);
    }

    // ------------------------------------------------------------------ entry points

    private static byte[]? RenderDeepZoomTile(
        MandelbrotState state,
        int canvasWidth,
        int canvasHeight,
        MandelbrotRenderTile tile,
        CancellationToken token)
    {
        DeepZoomPlan plan = PlanDeepZoom(state);
        ReferenceOrbit orbit = GetReferenceOrbit(state, plan.ReferenceBits);
        if (IsDegenerateOrbit(orbit, state.Iterations))
            return state.ColoringMode == MandelbrotColoringMode.DistanceEstimation
                ? RenderDistanceEstimationTile(state, canvasWidth, canvasHeight, tile, token)
                : RenderBruteForceTile(state, canvasWidth, canvasHeight, tile, token);

        if (state.ColoringMode == MandelbrotColoringMode.DistanceEstimation)
            return RenderDeepZoomDistanceEstimationTile(
                state, canvasWidth, canvasHeight, tile, plan, orbit, token);

        bool isJulia = IsJuliaVariant(state.Variant);
        bool trackHistogram = state.ColoringMode == MandelbrotColoringMode.Histogram;
        double escapeSquared = (double)(state.Threshold * state.Threshold);
        FloatExp viewWidth = 3.0 / state.Zoom;
        FloatExp viewHeight = viewWidth * canvasHeight / canvasWidth;

        int stride = checked(tile.Width * 4);
        var buffer = new byte[checked(stride * tile.Height)];

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int y = tile.Y + localY;
            FloatExp deltaImaginary = (0.5 - (double)y / canvasHeight) * viewHeight;
            int row = localY * stride;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                int x = tile.X + localX;
                FloatExp deltaReal = ((double)x / canvasWidth - 0.5) * viewWidth;
                PixelMetrics metrics = DeepZoomPixelDispatch(
                    plan, state, orbit, isJulia, deltaReal, deltaImaginary, escapeSquared,
                    FloatExp.One, token);
                // Тайловый предпросмотр (и Histogram здесь) — та же дешёвая локальная
                // нормализация, что и в обычном RenderTile: полноценное выравнивание по
                // CDF по кадру требует полного кадра (см. RenderDeepZoomHistogram).
                double histogramValue = trackHistogram
                    ? System.Math.Clamp((state.HistogramInputUseSmooth ? metrics.Smooth : metrics.Iterations) /
                                         System.Math.Max(1, state.Iterations), 0, 1)
                    : 0;
                Color color = ResolveColor(state, metrics, histogramValue);
                int offset = row + localX * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
        }

        return token.IsCancellationRequested ? null : buffer;
    }

    private static void RenderDeepZoom(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        CancellationToken token,
        Action<int>? reportProgress)
    {
        DeepZoomPlan plan = PlanDeepZoom(state);
        ReferenceOrbit orbit = GetReferenceOrbit(state, plan.ReferenceBits);
        int threads = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = System.Math.Clamp(threads, 1, Environment.ProcessorCount)
        };

        if (IsDegenerateOrbit(orbit, state.Iterations))
        {
            // Вырожденная орбита обслуживается тем же путём, что и до Фазы 7: двухпроходные
            // режимы (Histogram, Distance Estimation) — прежним decimal-рендером (скорость
            // здесь не критична — редкий пограничный случай), остальные — brute-force в
            // double (см. Фазу 2).
            if (state.ColoringMode is MandelbrotColoringMode.Histogram
                or MandelbrotColoringMode.DistanceEstimation)
            {
                decimal decimalViewWidth = DecimalViewWidth(state.Zoom);
                decimal decimalViewHeight = decimalViewWidth * height / width;
                if (state.ColoringMode == MandelbrotColoringMode.DistanceEstimation)
                {
                    RenderDistanceEstimation(state, buffer, width, height, stride,
                        decimalViewWidth, decimalViewHeight, options, token, reportProgress);
                    return;
                }
                int completedHistogramRows = 0;
                RenderHistogram(state, buffer, width, height, stride, decimalViewWidth, decimalViewHeight,
                    options, token, ref completedHistogramRows, reportProgress);
                return;
            }
            RenderBruteForceFull(state, buffer, width, height, stride, token, reportProgress);
            return;
        }

        bool isJulia = IsJuliaVariant(state.Variant);
        double escapeSquared = (double)(state.Threshold * state.Threshold);
        FloatExp viewWidth = 3.0 / state.Zoom;
        FloatExp viewHeight = viewWidth * height / width;

        if (state.ColoringMode == MandelbrotColoringMode.Histogram)
        {
            RenderDeepZoomHistogram(state, buffer, width, height, stride, plan, orbit, isJulia,
                escapeSquared, viewWidth, viewHeight, options, token, reportProgress);
            return;
        }

        if (state.ColoringMode == MandelbrotColoringMode.DistanceEstimation)
        {
            RenderDeepZoomDistanceEstimation(state, buffer, width, height, stride, plan, orbit, isJulia,
                DistanceEstimationEscapeSquared(state), viewWidth, viewHeight, options, token, reportProgress);
            return;
        }

        int completedRows = 0;

        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int row = y * stride;
            FloatExp deltaImaginary = (0.5 - (double)y / height) * viewHeight;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                FloatExp deltaReal = ((double)x / width - 0.5) * viewWidth;
                PixelMetrics metrics = DeepZoomPixelDispatch(
                    plan, state, orbit, isJulia, deltaReal, deltaImaginary, escapeSquared,
                    FloatExp.One, token);
                Color color = ResolveColor(state, metrics, 0);
                int offset = row + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }

            int done = Interlocked.Increment(ref completedRows);
            if (done == height || done % System.Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(done * 100 / height);
        });
    }

    // Histogram на глубоком движке: та же двухпроходная схема, что и decimal-версия
    // (RenderHistogram) — первый проход копит Iterations/Smooth и гистограмму бинов,
    // между проходами строится CDF, второй проход красит по ней. Ядро пикселя то же самое
    // (DeepZoomPixelDispatch), что и у Smooth/Trap/Stripe — Iterations/Smooth считаются
    // безусловно во всех режимах, поэтому BLA/FloatExp/отражение/степени работают как обычно.
    private static void RenderDeepZoomHistogram(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        DeepZoomPlan plan,
        ReferenceOrbit orbit,
        bool isJulia,
        double escapeSquared,
        FloatExp viewWidth,
        FloatExp viewHeight,
        ParallelOptions options,
        CancellationToken token,
        Action<int>? reportProgress)
    {
        var smoothValues = new double[checked(width * height)];
        var iterationValues = new int[smoothValues.Length];
        var bins = new int[state.Iterations + 1];
        object histogramLock = new();
        int scanRows = 0;

        // Счётчики бинов — по одному массиву на рабочий поток: см. RenderHistogram.
        Parallel.For(0, height, options, () => new int[bins.Length], (y, loopState, localBins) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return localBins; }
            FloatExp deltaImaginary = (0.5 - (double)y / height) * viewHeight;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return localBins; }
                FloatExp deltaReal = ((double)x / width - 0.5) * viewWidth;
                PixelMetrics value = DeepZoomPixelDispatch(
                    plan, state, orbit, isJulia, deltaReal, deltaImaginary, escapeSquared,
                    FloatExp.One, token);
                smoothValues[row + x] = value.Smooth;
                iterationValues[row + x] = value.Iterations;
                int bin = state.HistogramInputUseSmooth
                    ? System.Math.Clamp((int)System.Math.Floor(value.Smooth), 0, state.Iterations)
                    : System.Math.Clamp(value.Iterations, 0, state.Iterations);
                localBins[bin]++;
            }
            int done = Interlocked.Increment(ref scanRows);
            reportProgress?.Invoke(done * 65 / height);
            return localBins;
        },
        localBins =>
        {
            lock (histogramLock)
            {
                for (int i = 0; i < bins.Length; i++) bins[i] += localBins[i];
            }
        });

        if (token.IsCancellationRequested) return;

        long total = (long)width * height;
        var cdf = new double[bins.Length];
        long cumulative = 0;
        for (int i = 0; i <= state.Iterations; i++)
        {
            cumulative += bins[i];
            cdf[i] = total == 0 ? 0 : (double)cumulative / total;
        }

        int coloredRows = 0;
        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int metricRow = y * width;
            int outputRow = y * stride;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                PixelMetrics value = new(iterationValues[metricRow + x], smoothValues[metricRow + x], 0, 0);
                int bin = state.HistogramInputUseSmooth
                    ? System.Math.Clamp((int)System.Math.Floor(value.Smooth), 0, state.Iterations)
                    : System.Math.Clamp(value.Iterations, 0, state.Iterations);
                double normalized = value.Iterations >= state.Iterations
                    ? 0
                    : state.HistogramEnabledEqualization
                        ? cdf[bin]
                        : bin / (double)System.Math.Max(1, state.Iterations);
                Color color = ResolveColor(state, value, normalized);
                int offset = outputRow + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
            int done = Interlocked.Increment(ref coloredRows);
            reportProgress?.Invoke(65 + done * 35 / height);
        });
    }

    // Distance Estimation требует радиуса выхода не меньше 2 — тот же зажим, что и в
    // <see cref="Iterate"/>/<see cref="IterateDecimal"/>.
    private static double DistanceEstimationEscapeSquared(MandelbrotState state) =>
        System.Math.Max(4.0, (double)(state.Threshold * state.Threshold));

    // Distance Estimation на глубоком движке: та же двухпроходная схема, что и у
    // decimal-ступени (RenderDistanceEstimation) — первый проход считает базовый цвет и поле
    // расстояний с рамкой в один пиксель, второй затеняет рельеф по градиенту этого поля.
    // Производная берётся из ядра (см. AdvanceDerivative), поэтому здесь ничего не меняется
    // от варианта: работают и отражённые, и степенные ядра.
    //
    // Расстояния хранятся НОРМИРОВАННЫМИ на размер пикселя (d/px — «расстояние в пикселях»),
    // а в затенение передаётся pixelSize = 1. Это тождественная подстановка: градиент
    // Δ(d/px)/2 равен Δd/(2·px) из decimal-версии, а шаг контурных линий и так задан в
    // пикселях. Без нормировки не обойтись: поле хранится во float, и уже на зуме ~1e40
    // абсолютные расстояния (~1e-43) схлопнулись бы в ноль — рельеф исчез бы целиком.
    private static void RenderDeepZoomDistanceEstimation(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        DeepZoomPlan plan,
        ReferenceOrbit orbit,
        bool isJulia,
        double escapeSquared,
        FloatExp viewWidth,
        FloatExp viewHeight,
        ParallelOptions options,
        CancellationToken token,
        Action<int>? reportProgress)
    {
        int sampleWidth = checked(width + 2);
        int sampleHeight = checked(height + 2);
        var distances = new float[checked(sampleWidth * sampleHeight)];
        // Расстояния считаются сразу в единицах пикселя (см. комментарий выше): ядро
        // получает обратный размер пикселя и возвращает уже нормированное значение. На
        // сверхглубоком зуме абсолютное расстояние непредставимо ни во float поля, ни в
        // double — представимо только это отношение.
        FloatExp distanceScale = PixelDistanceScale(viewWidth, width);
        int sampledRows = 0;

        Parallel.For(0, sampleHeight, options, (sampleY, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int y = sampleY - 1;
            FloatExp deltaImaginary = (0.5 - (double)y / height) * viewHeight;
            int distanceRow = sampleY * sampleWidth;
            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                if ((sampleX & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                int x = sampleX - 1;
                FloatExp deltaReal = ((double)x / width - 0.5) * viewWidth;
                PixelMetrics metrics = DeepZoomPixelDispatch(
                    plan, state, orbit, isJulia, deltaReal, deltaImaginary, escapeSquared,
                    distanceScale, token);
                distances[distanceRow + sampleX] = StoreDistance(metrics.Distance);
                if (sampleX is > 0 && sampleX <= width && sampleY is > 0 && sampleY <= height)
                {
                    Color baseColor = ResolveDistanceBaseColor(state, metrics);
                    WriteColor(buffer, (sampleY - 1) * stride + (sampleX - 1) * 4, baseColor);
                }
            }

            int done = Interlocked.Increment(ref sampledRows);
            reportProgress?.Invoke(done * 70 / sampleHeight);
        });

        if (token.IsCancellationRequested) return;

        ShadeDistanceField(state, buffer, width, height, stride, distances, 1.0,
            options, token, reportProgress);
    }

    // Тайловая версия того же двухпроходного DE (нормировка расстояний — см. выше).
    private static byte[]? RenderDeepZoomDistanceEstimationTile(
        MandelbrotState state,
        int canvasWidth,
        int canvasHeight,
        MandelbrotRenderTile tile,
        DeepZoomPlan plan,
        ReferenceOrbit orbit,
        CancellationToken token)
    {
        bool isJulia = IsJuliaVariant(state.Variant);
        double escapeSquared = DistanceEstimationEscapeSquared(state);
        FloatExp viewWidth = 3.0 / state.Zoom;
        FloatExp viewHeight = viewWidth * canvasHeight / canvasWidth;
        FloatExp distanceScale = PixelDistanceScale(viewWidth, canvasWidth);

        int stride = checked(tile.Width * 4);
        var buffer = new byte[checked(stride * tile.Height)];
        int sampleWidth = checked(tile.Width + 2);
        int sampleHeight = checked(tile.Height + 2);
        var distances = new float[checked(sampleWidth * sampleHeight)];

        for (int sampleY = 0; sampleY < sampleHeight; sampleY++)
        {
            if (token.IsCancellationRequested) return null;
            int y = tile.Y + sampleY - 1;
            FloatExp deltaImaginary = (0.5 - (double)y / canvasHeight) * viewHeight;
            int distanceRow = sampleY * sampleWidth;
            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                int x = tile.X + sampleX - 1;
                FloatExp deltaReal = ((double)x / canvasWidth - 0.5) * viewWidth;
                PixelMetrics metrics = DeepZoomPixelDispatch(
                    plan, state, orbit, isJulia, deltaReal, deltaImaginary, escapeSquared,
                    distanceScale, token);
                distances[distanceRow + sampleX] = StoreDistance(metrics.Distance);
                if (sampleX is > 0 && sampleX <= tile.Width &&
                    sampleY is > 0 && sampleY <= tile.Height)
                {
                    Color baseColor = ResolveDistanceBaseColor(state, metrics);
                    WriteColor(buffer, (sampleY - 1) * stride + (sampleX - 1) * 4, baseColor);
                }
            }
        }

        if (token.IsCancellationRequested) return null;

        ShadeDistanceFieldTile(state, buffer, tile.Width, tile.Height, stride, distances, 1.0);
        return buffer;
    }

    // ------------------------------------------------------------------ reference orbit

    // Короткая опорная орбита (центр ушёл за радиус почти сразу) — единственный случай,
    // когда пертурбации нечем оперировать. Рано вышедшая, но не мгновенно, орбита
    // отлично обслуживается rebasing'ом — это обычный случай для глубокого «внешнего» вида.
    private static bool IsDegenerateOrbit(ReferenceOrbit orbit, int iterations) => orbit.Length < 4;

    private static ReferenceOrbit GetReferenceOrbit(MandelbrotState state, int referenceBits)
    {
        string centerXRaw = state.CenterXExact is { Length: > 0 } exactX
            ? exactX
            : state.CenterX.ToString(CultureInfo.InvariantCulture);
        string centerYRaw = state.CenterYExact is { Length: > 0 } exactY
            ? exactY
            : state.CenterY.ToString(CultureInfo.InvariantCulture);

        string key = string.Join('|',
            centerXRaw,
            centerYRaw,
            state.Zoom.ToInvariantString(),
            state.Iterations.ToString(CultureInfo.InvariantCulture),
            ((int)state.Variant).ToString(CultureInfo.InvariantCulture),
            state.JuliaCReal.ToString(CultureInfo.InvariantCulture),
            state.JuliaCImaginary.ToString(CultureInfo.InvariantCulture),
            state.Threshold.ToString(CultureInfo.InvariantCulture),
            state.Power.ToString(CultureInfo.InvariantCulture),
            state.UseInversion.ToString(CultureInfo.InvariantCulture),
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

    private static ReferenceOrbit ComputeReferenceOrbit(
        MandelbrotState state, string centerXRaw, string centerYRaw, int referenceBits)
    {
        // Вся арифметика центра и опорной орбиты — с адаптивной точностью; ниже ~1e93
        // referenceBits == MinimumPrecisionBits, поэтому парсинг и итерация идут ровно
        // так же, как раньше. Парсим внутри области: Parse тоже округляет до рабочей точности.
        using var precision = new BigFloat.PrecisionScope(referenceBits);

        BigFloat centerX = BigFloat.Parse(centerXRaw);
        BigFloat centerY = BigFloat.Parse(centerYRaw);

        int capacity = state.Iterations + 1;
        var re = new double[capacity];
        var im = new double[capacity];

        bool isJulia = IsJuliaVariant(state.Variant);
        ReflectKind? reflect = ReflectKindOf(state.Variant);
        int multibrotPower = MultibrotPowerOrZero(state);     // 0, либо p ∈ [2, 12]
        int simonobrotPower = SimonobrotPowerOrZero(state);   // 0, либо целое p ∈ [2, 12]
        // UseInversion (только Симоноброт): в формулу каждый шаг подставляется -re вместо re.
        bool invertReal = state.Variant == MandelbrotVariant.Simonobrot && state.UseInversion;
        BigFloat constantReal = isJulia
            ? BigFloat.FromDecimal(state.JuliaCReal)
            : invertReal ? -centerX : centerX;
        BigFloat constantImaginary = isJulia ? BigFloat.FromDecimal(state.JuliaCImaginary) : centerY;
        BigFloat zReal = isJulia ? centerX : BigFloat.Zero;
        BigFloat zImaginary = isJulia ? centerY : BigFloat.Zero;
        BigFloat two = BigFloat.FromInt(2);

        // Опорную орбиту гоняем до большого радиуса, чтобы она давала полезные точки как
        // можно дольше; настоящий bailout пикселя гораздо меньше, а хвост закрывает rebasing.
        const double referenceEscapeSquared = 1e18;
        int length = 0;
        bool escaped = false;

        for (int index = 0; index < capacity; index++)
        {
            double realDouble = zReal.ToDouble();
            double imaginaryDouble = zImaginary.ToDouble();
            re[index] = realDouble;
            im[index] = imaginaryDouble;
            length = index + 1;

            double magnitudeSquared = realDouble * realDouble + imaginaryDouble * imaginaryDouble;
            if (!double.IsFinite(magnitudeSquared) || magnitudeSquared > referenceEscapeSquared)
            {
                escaped = true;
                break;
            }

            if (reflect is { } kind)
            {
                (zReal, zImaginary) = StepReflectedReference(
                    kind, zReal, zImaginary, constantReal, constantImaginary, two);
            }
            else if (simonobrotPower >= 2)
            {
                (zReal, zImaginary) = StepSimonobrotReference(
                    simonobrotPower, zReal, zImaginary, constantReal, constantImaginary);
            }
            else if (multibrotPower >= 3)
            {
                // z ← zᵖ + c  (степенями умножения)
                BigFloat powerReal = zReal, powerImaginary = zImaginary;
                for (int e = 1; e < multibrotPower; e++)
                {
                    BigFloat nr = powerReal * zReal - powerImaginary * zImaginary;
                    powerImaginary = powerReal * zImaginary + powerImaginary * zReal;
                    powerReal = nr;
                }
                zReal = powerReal + constantReal;
                zImaginary = powerImaginary + constantImaginary;
            }
            else
            {
                BigFloat nextReal = zReal * zReal - zImaginary * zImaginary + constantReal;
                BigFloat nextImaginary = two * zReal * zImaginary + constantImaginary;
                zReal = nextReal;
                zImaginary = nextImaginary;
            }
        }

        var orbit = new ReferenceOrbit { Re = re, Im = im, Length = length, Escaped = escaped };

        // Пирамида BLA — для z²+c (Mandelbrot/Julia) и целой степени Multibrot (A = p·Zᵖ⁻¹)
        // комплексная, для отражённых вариантов и Симоноброта — вещественная 2×2
        // (см. RealBlaTable). Наборы вариантов не пересекаются, поэтому одна из таблиц всегда
        // null. δcmax — консервативная оценка |δc| по кадру (полная ширина вида 3/zoom), не
        // зависит от размера полотна, поэтому кэшируется вместе с орбитой.
        double escapeSquared = (double)(state.Threshold * state.Threshold);
        // δcmax ведётся в расширенном диапазоне: на зуме за 1.8e308 ширина вида в double
        // обращается в ноль, и оценка «вклад δc в радиус применимости BLA» молча пропадала —
        // радиусы выходили завышенными, то есть BLA применялся там, где уже не вправе.
        FloatExp deltaCMax = state.Zoom.Sign > 0 && state.Zoom.IsFinite
            ? 3.0 / state.Zoom
            : FloatExp.Zero;
        bool complexLinearPart = reflect is null && simonobrotPower == 0;
        orbit.Bla = complexLinearPart
            ? BlaTable.Build(re, im, length, isJulia, escapeSquared, deltaCMax,
                multibrotPower >= 2 ? multibrotPower : 2)
            : null;
        orbit.RealBla = complexLinearPart
            ? null
            : RealBlaTable.Build(re, im, length, isJulia, escapeSquared, deltaCMax,
                reflect, simonobrotPower);

        return orbit;
    }

    // Один шаг опорной орбиты Симоноброта в BigFloat: z ← zᵖ·|z|ᵖ + c = zᵖ·M^(p/2) + c,
    // M = zr²+zi². Чётное p=2q: M^(p/2) = Mᵠ — только умножения. Нечётное p=2q+1:
    // M^(p/2) = Mᵠ·√M, и корень — единственное место во всём движке, где BigFloat.Sqrt нужен.
    // При z=0 множитель нулевой, значит z ← c — как и особый случай в IterateOnce.
    private static (BigFloat, BigFloat) StepSimonobrotReference(
        int power, BigFloat zReal, BigFloat zImaginary, BigFloat cReal, BigFloat cImaginary)
    {
        BigFloat powerReal = zReal, powerImaginary = zImaginary;
        for (int e = 1; e < power; e++)
        {
            BigFloat nr = powerReal * zReal - powerImaginary * zImaginary;
            powerImaginary = powerReal * zImaginary + powerImaginary * zReal;
            powerReal = nr;
        }

        BigFloat magnitudeSquared = zReal * zReal + zImaginary * zImaginary;
        int halfPower = power / 2;
        BigFloat magnitudePower = magnitudeSquared;
        for (int e = 1; e < halfPower; e++) magnitudePower *= magnitudeSquared;
        if ((power & 1) != 0)
        {
            // Mᵠ·√M; при q = 0 степень пуста — но нечётное p здесь не меньше 3, значит q ≥ 1.
            magnitudePower *= BigFloat.Sqrt(magnitudeSquared);
        }

        return (powerReal * magnitudePower + cReal,
                powerImaginary * magnitudePower + cImaginary);
    }

    // Один шаг опорной орбиты отражённого варианта в BigFloat. Совпадает с IterateOnce:
    // BurningShip w=(|zr|,-|zi|); Buffalo w=(|zr|,|zi|); Tricorn w=(zr,-zi); затем w²+c.
    // Celtic: re=|zr²-zi²|+cr, im=2·zr·zi+ci.
    private static (BigFloat, BigFloat) StepReflectedReference(
        ReflectKind kind, BigFloat zReal, BigFloat zImaginary, BigFloat cReal, BigFloat cImaginary, BigFloat two)
    {
        if (kind == ReflectKind.Celtic)
        {
            BigFloat u = zReal * zReal - zImaginary * zImaginary;
            BigFloat celticReal = (u.Sign < 0 ? -u : u) + cReal;
            BigFloat celticImaginary = two * zReal * zImaginary + cImaginary;
            return (celticReal, celticImaginary);
        }

        BigFloat wReal, wImaginary;
        switch (kind)
        {
            case ReflectKind.BurningShip:
                wReal = zReal.Sign < 0 ? -zReal : zReal;
                wImaginary = zImaginary.Sign < 0 ? zImaginary : -zImaginary;
                break;
            case ReflectKind.Buffalo:
                wReal = zReal.Sign < 0 ? -zReal : zReal;
                wImaginary = zImaginary.Sign < 0 ? -zImaginary : zImaginary;
                break;
            default: // Tricorn
                wReal = zReal;
                wImaginary = -zImaginary;
                break;
        }

        return (wReal * wReal - wImaginary * wImaginary + cReal,
                two * wReal * wImaginary + cImaginary);
    }

    // ------------------------------------------------------------------ per-pixel perturbation

    /// <summary>
    /// Шаг рекуррентности производной для Distance Estimation: <c>D ← J(z)·D + ∂f/∂c</c>.
    ///
    /// Пертурбация не даёт отдельного «возмущения производной» — она и не нужна: якобиан
    /// <see cref="GetIterationJacobian"/> зависит только от самого <c>z</c>, а <c>z = Z + δ</c>
    /// в каждом ядре и так собирается в double для ловушки/полос. Точности хватает с запасом:
    /// опорная орбита хранится в double (относительная погрешность ~1e-16), rebasing не даёт
    /// |z| упасть ниже 1e-3·|Zref| (критерий Pauldelbrot), поэтому относительная погрешность
    /// каждого множителя ≤ ~1e-13, а по N итерациям она накапливается лишь линейно (N·1e-13).
    /// Расстояние идёт в затенение рельефа, где значимы 2–3 цифры, — запас огромный.
    ///
    /// Само <c>D</c> ведётся в обычном double: на глубоком зуме |D| ~ zoom·ширина кадра
    /// (~1e93 при 1e90), до переполнения double ещё ~200 порядков. Пиксели вплотную к границе
    /// переполняются и там же, что и на плоской ступени, дают distance = 0 (см.
    /// <see cref="EstimateDistance"/>) — поведение совпадает с <see cref="Iterate"/>.
    /// </summary>
    private static Jacobian2 AdvanceDerivative(
        MandelbrotState state,
        Jacobian2 derivative,
        Jacobian2 parameterDerivative,
        double currentReal,
        double currentImaginary) =>
        Jacobian2.Multiply(GetIterationJacobian(state, currentReal, currentImaginary), derivative) +
        parameterDerivative;

    /// <summary>
    /// Общий хвост всех пертурбационных ядер: сглаженное число итераций по последнему |z|²
    /// и сборка <see cref="PixelMetrics"/>. Distance Estimation читает точку выхода за радиус
    /// (<paramref name="escapeReal"/>/<paramref name="escapeImaginary"/>) и накопленную
    /// производную — ровно те же аргументы, что и плоская ступень в <see cref="Iterate"/>.
    /// </summary>
    private static PixelMetrics FinishDeepZoomPixel(
        int iteration,
        double magnitudeSquared,
        double minTrap,
        double stripe,
        bool estimateDistance,
        double escapeReal,
        double escapeImaginary,
        Jacobian2 derivative,
        double distanceScale)
    {
        double smooth = iteration;
        if (magnitudeSquared > 1)
        {
            double logZn = System.Math.Log(magnitudeSquared) / 2;
            const double smoothingPower = 2;
            double nu = System.Math.Log(System.Math.Max(logZn, 1e-300) / System.Math.Log(smoothingPower)) /
                        System.Math.Log(smoothingPower);
            if (double.IsFinite(nu)) smooth = iteration + 1 - nu;
        }

        return new PixelMetrics(
            iteration,
            smooth,
            minTrap == double.MaxValue ? 0 : minTrap,
            iteration == 0 ? 0 : stripe / iteration,
            estimateDistance
                ? EstimateDistance(escapeReal, escapeImaginary, derivative) * distanceScale
                : 0);
    }

    /// <summary>
    /// Шаг рекуррентности производной с накоплением в расширенном диапазоне — расширенный
    /// двойник <see cref="AdvanceDerivative"/> для FloatExp-ядра. Якобиан шага остаётся
    /// double: он зависит только от <c>z</c>, ограниченного радиусом бейлаута.
    /// </summary>
    private static Jacobian2Exp AdvanceDerivativeExp(
        MandelbrotState state,
        Jacobian2Exp derivative,
        Jacobian2 parameterDerivative,
        double currentReal,
        double currentImaginary) =>
        Jacobian2Exp.Multiply(GetIterationJacobian(state, currentReal, currentImaginary), derivative) +
        parameterDerivative;

    /// <summary>
    /// Хвост FloatExp-ядра: то же, что <see cref="FinishDeepZoomPixel"/>, но расстояние
    /// считается по расширенной производной и нормируется на размер пикселя внутри
    /// расширенного диапазона (см. <see cref="EstimateDistanceExp"/>).
    /// </summary>
    private static PixelMetrics FinishDeepZoomPixelExp(
        int iteration,
        double magnitudeSquared,
        double minTrap,
        double stripe,
        bool estimateDistance,
        double escapeReal,
        double escapeImaginary,
        Jacobian2Exp derivative,
        FloatExp distanceScale)
    {
        PixelMetrics metrics = FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe,
            false, escapeReal, escapeImaginary, Jacobian2.Zero, 1.0);
        return estimateDistance
            ? metrics with { Distance = EstimateDistanceExp(escapeReal, escapeImaginary, derivative, distanceScale) }
            : metrics;
    }

    private static PixelMetrics DeepZoomPixel(
        MandelbrotState state,
        ReferenceOrbit orbit,
        bool isJulia,
        double deltaConstantReal,
        double deltaConstantImaginary,
        double escapeSquared,
        double distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        // Мандельброт: δ₀ = 0, а δc добавляется каждый шаг. Жюлиа: c постоянна, поэтому
        // δc не добавляется, но задаёт начальное возмущение δ₀.
        double deltaReal = isJulia ? deltaConstantReal : 0.0;
        double deltaImaginary = isJulia ? deltaConstantImaginary : 0.0;
        double addReal = isJulia ? 0.0 : deltaConstantReal;
        double addImaginary = isJulia ? 0.0 : deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2 derivative = isJulia ? Jacobian2.Identity : Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);

        // BLA доступен только для гладкой окраски: пропуск итераций несовместим ни с
        // накоплением орбитальной ловушки / полосовой суммы по каждому шагу, ни с
        // рекуррентностью производной для Distance Estimation.
        BlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.Bla
            : null;

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            if (bla is not null &&
                bla.TryLookup(referenceIndex, deltaReal * deltaReal + deltaImaginary * deltaImaginary,
                    maxIterations - iteration,
                    out double blaAx, out double blaAy, out double blaBx, out double blaBy, out int blaSteps))
            {
                // δ ← A·δ + B·δc  (комплексно), пропуская blaSteps итераций разом
                double skippedReal = blaAx * deltaReal - blaAy * deltaImaginary
                                   + blaBx * addReal - blaBy * addImaginary;
                double skippedImaginary = blaAx * deltaImaginary + blaAy * deltaReal
                                        + blaBx * addImaginary + blaBy * addReal;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal;
                double currentImaginary = referenceImaginary + deltaImaginary;
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));
                if (estimateDistance)
                    derivative = AdvanceDerivative(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // δ ← 2·Z·δ + δ² + δc
                double twoZDeltaReal = 2 * (referenceReal * deltaReal - referenceImaginary * deltaImaginary);
                double twoZDeltaImaginary = 2 * (referenceReal * deltaImaginary + referenceImaginary * deltaReal);
                double deltaSquaredReal = deltaReal * deltaReal - deltaImaginary * deltaImaginary;
                double deltaSquaredImaginary = 2 * deltaReal * deltaImaginary;
                deltaReal = twoZDeltaReal + deltaSquaredReal + addReal;
                deltaImaginary = twoZDeltaImaginary + deltaSquaredImaginary + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal;
            double fullImaginary = nextReferenceImaginary + deltaImaginary;
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            // Rebasing по Zhuoran (|z| < |δ|) плюс критерий Pauldelbrot (|z|² ≪ |Zref|²):
            // и то и другое означает, что опорная точка перестала быть хорошим приближением.
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                // δ отсчитывается от опорной точки, поэтому при сбросе индекса в 0 нужно
                // δ = z − Z₀. Для Мандельброта Z₀ = 0 (вычитание — no-op, бит-в-бит как
                // раньше); для Жюлиа Z₀ = центр, и без вычитания rebasing давал глитчи.
                deltaReal = fullReal - orbit.Re[0];
                deltaImaginary = fullImaginary - orbit.Im[0];
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Тот же алгоритм, что <see cref="DeepZoomPixel"/>, но отклонение δ ведётся в
    // <see cref="FloatExp"/>: на зуме за ~1e72 δ и особенно δ² перестают помещаться в
    // обычный double. Опорная орбита и все проверки (escape, rebasing) остаются в double —
    // там значения ограничены и расширенный диапазон не нужен. Ниже порога FloatExp этот
    // путь не вызывается, поэтому расхождение округления с double-путём картинку не задевает.
    private static PixelMetrics DeepZoomPixelFloatExp(
        MandelbrotState state,
        ReferenceOrbit orbit,
        bool isJulia,
        FloatExp deltaConstantReal,
        FloatExp deltaConstantImaginary,
        double escapeSquared,
        FloatExp distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        FloatExp deltaReal = isJulia ? deltaConstantReal : FloatExp.Zero;
        FloatExp deltaImaginary = isJulia ? deltaConstantImaginary : FloatExp.Zero;
        FloatExp addReal = isJulia ? FloatExp.Zero : deltaConstantReal;
        FloatExp addImaginary = isJulia ? FloatExp.Zero : deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        // Производная для Distance Estimation здесь тоже расширенная: |D| растёт примерно как
        // сам зум и за 1.8e308 в double не помещается (см. Jacobian2Exp).
        Jacobian2Exp derivative = isJulia ? Jacobian2Exp.Identity : Jacobian2Exp.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);

        BlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.Bla
            : null;

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            if (bla is not null &&
                bla.TryLookup(referenceIndex,
                    FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble(),
                    maxIterations - iteration,
                    out double blaAx, out double blaAy, out double blaBx, out double blaBy, out int blaSteps))
            {
                // δ ← A·δ + B·δc  (A, B — double из таблицы; δ, δc — FloatExp)
                FloatExp skippedReal = deltaReal * blaAx - deltaImaginary * blaAy
                                     + addReal * blaBx - addImaginary * blaBy;
                FloatExp skippedImaginary = deltaReal * blaAy + deltaImaginary * blaAx
                                          + addReal * blaBy + addImaginary * blaBx;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal.ToDouble();
                double currentImaginary = referenceImaginary + deltaImaginary.ToDouble();
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivativeExp(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // δ ← 2·Z·δ + δ² + δc
                FloatExp twoZDeltaReal = (deltaReal * referenceReal - deltaImaginary * referenceImaginary) * 2.0;
                FloatExp twoZDeltaImaginary = (deltaReal * referenceImaginary + deltaImaginary * referenceReal) * 2.0;
                FloatExp deltaSquaredReal = deltaReal * deltaReal - deltaImaginary * deltaImaginary;
                FloatExp deltaSquaredImaginary = deltaReal * deltaImaginary * 2.0;
                deltaReal = twoZDeltaReal + deltaSquaredReal + addReal;
                deltaImaginary = twoZDeltaImaginary + deltaSquaredImaginary + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal.ToDouble();
            double fullImaginary = nextReferenceImaginary + deltaImaginary.ToDouble();
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            // |δ|²: при действительно малом δ обращается в 0 и не даёт ложного rebasing;
            // ближе к |δ| ~ |z| снова становится числом в диапазоне double — как и нужно.
            double deltaMagnitudeSquared = FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                // δ = z − Z₀ (для Мандельброта Z₀ = 0; для Жюлиа — центр). См. double-ядро.
                deltaReal = FloatExp.FromDouble(fullReal - orbit.Re[0]);
                deltaImaginary = FloatExp.FromDouble(fullImaginary - orbit.Im[0]);
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixelExp(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Пертурбационное ядро для отражённых вариантов (Burning Ship, Julia Burning Ship,
    // Tricorn, Buffalo, Celtic). Их формула — «свёртка знака» компонент z, затем z²+c,
    // поэтому линейная часть возмущения не комплексная: свёрнутое δ считается точным
    // разбором случаев (<see cref="FoldedDelta"/>), а ускоряется вещественной 2×2 таблицей
    // (<see cref="RealBlaTable"/>), а не комплексной. δ здесь всегда double — за порогом
    // FloatExpDeltaZoomBits используется двойник <see cref="DeepZoomPixelReflectedFloatExp"/>.
    // Опорная орбита и все проверки (escape, rebasing) — как в <see cref="DeepZoomPixel"/>.
    private static PixelMetrics DeepZoomPixelReflected(
        MandelbrotState state,
        ReferenceOrbit orbit,
        ReflectKind kind,
        bool isJulia,
        double deltaConstantReal,
        double deltaConstantImaginary,
        double escapeSquared,
        double distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        double deltaReal = isJulia ? deltaConstantReal : 0.0;
        double deltaImaginary = isJulia ? deltaConstantImaginary : 0.0;
        double addReal = isJulia ? 0.0 : deltaConstantReal;
        double addImaginary = isJulia ? 0.0 : deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2 derivative = isJulia ? Jacobian2.Identity : Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);

        // BLA с вещественной 2×2 линейной частью. Условия те же, что у комплексного:
        // пропуск итераций несовместим с накоплением ловушки/полос по каждому шагу и с
        // рекуррентностью производной для Distance Estimation.
        RealBlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.RealBla
            : null;

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            // В режимах ловушки/полос/DE таблица отключена — |δ|² тогда и не считается.
            double blaDeltaMagnitudeSquared = bla is null
                ? 0.0
                : deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            if (bla is not null && bla.CanSkip(referenceIndex, blaDeltaMagnitudeSquared) &&
                bla.TryLookup(referenceIndex, blaDeltaMagnitudeSquared,
                    maxIterations - iteration,
                    out double blaA11, out double blaA12, out double blaA21, out double blaA22,
                    out double blaB11, out double blaB12, out double blaB21, out double blaB22,
                    out int blaSteps))
            {
                // δ ← A·δ + B·δc  (вещественная 2×2), пропуская blaSteps итераций разом
                double skippedReal = blaA11 * deltaReal + blaA12 * deltaImaginary
                                   + blaB11 * addReal + blaB12 * addImaginary;
                double skippedImaginary = blaA21 * deltaReal + blaA22 * deltaImaginary
                                        + blaB21 * addReal + blaB22 * addImaginary;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
                if (CountRealBlaSkipsForTests)
                    Interlocked.Add(ref RealBlaSkippedIterationsForTests, blaSteps);
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                // Ловушка/полосы считаются по z (до свёртки), как в обычном Iterate.
                double currentReal = referenceReal + deltaReal;
                double currentImaginary = referenceImaginary + deltaImaginary;
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivative(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                if (kind == ReflectKind.Celtic)
                {
                    // u = Re(z²) = zr²−zi² ⇒ δu = 2Zr·δr − 2Zi·δi + δr² − δi² ;  Re' = |u| + cr
                    // v = Im(z²) = 2 zr zi ⇒ δv = 2(Zr·δi + Zi·δr) + 2 δr δi   ;  Im' = v + ci
                    double deltaU = 2 * (referenceReal * deltaReal - referenceImaginary * deltaImaginary)
                                  + deltaReal * deltaReal - deltaImaginary * deltaImaginary;
                    double deltaV = 2 * (referenceReal * deltaImaginary + referenceImaginary * deltaReal)
                                  + 2 * deltaReal * deltaImaginary;
                    deltaReal = FoldedDelta(referenceReal * referenceReal - referenceImaginary * referenceImaginary, deltaU) + addReal;
                    deltaImaginary = deltaV + addImaginary;
                }
                else
                {
                    // Свёрнутая опорная точка W и свёрнутое отклонение δ_w = fold(Z+δ) − fold(Z).
                    double foldedReferenceReal, foldedReferenceImaginary, foldedDeltaReal, foldedDeltaImaginary;
                    switch (kind)
                    {
                        case ReflectKind.BurningShip:
                            foldedReferenceReal = System.Math.Abs(referenceReal);
                            foldedReferenceImaginary = -System.Math.Abs(referenceImaginary);
                            foldedDeltaReal = FoldedDelta(referenceReal, deltaReal);
                            foldedDeltaImaginary = -FoldedDelta(referenceImaginary, deltaImaginary);
                            break;
                        case ReflectKind.Buffalo:
                            foldedReferenceReal = System.Math.Abs(referenceReal);
                            foldedReferenceImaginary = System.Math.Abs(referenceImaginary);
                            foldedDeltaReal = FoldedDelta(referenceReal, deltaReal);
                            foldedDeltaImaginary = FoldedDelta(referenceImaginary, deltaImaginary);
                            break;
                        default: // Tricorn — сопряжение, знак определён всегда
                            foldedReferenceReal = referenceReal;
                            foldedReferenceImaginary = -referenceImaginary;
                            foldedDeltaReal = deltaReal;
                            foldedDeltaImaginary = -deltaImaginary;
                            break;
                    }

                    // δ ← 2·W·δ_w + δ_w² + δc
                    double twoWDeltaReal = 2 * (foldedReferenceReal * foldedDeltaReal - foldedReferenceImaginary * foldedDeltaImaginary);
                    double twoWDeltaImaginary = 2 * (foldedReferenceReal * foldedDeltaImaginary + foldedReferenceImaginary * foldedDeltaReal);
                    double foldedDeltaSquaredReal = foldedDeltaReal * foldedDeltaReal - foldedDeltaImaginary * foldedDeltaImaginary;
                    double foldedDeltaSquaredImaginary = 2 * foldedDeltaReal * foldedDeltaImaginary;
                    deltaReal = twoWDeltaReal + foldedDeltaSquaredReal + addReal;
                    deltaImaginary = twoWDeltaImaginary + foldedDeltaSquaredImaginary + addImaginary;
                }

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal;
            double fullImaginary = nextReferenceImaginary + deltaImaginary;
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = fullReal - orbit.Re[0];
                deltaImaginary = fullImaginary - orbit.Im[0];
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Тот же алгоритм, что <see cref="DeepZoomPixelReflected"/>, но δ ведётся в
    // <see cref="FloatExp"/> — используется за порогом FloatExpDeltaZoomBits, тем же
    // переключателем плана, что и у z²+c (<see cref="DeepZoomPixelFloatExp"/>). Опорная
    // орбита и все проверки (escape, rebasing) остаются в double — как и в double-δ ядре,
    // так и в FloatExp-ядре z²+c. Вещественная BLA (A, B) тоже остаётся double: пропуск
    // применяется к δ через <see cref="FloatExp"/>-умножение, а сами коэффициенты не зависят
    // от представления δ.
    private static PixelMetrics DeepZoomPixelReflectedFloatExp(
        MandelbrotState state,
        ReferenceOrbit orbit,
        ReflectKind kind,
        bool isJulia,
        FloatExp deltaConstantReal,
        FloatExp deltaConstantImaginary,
        double escapeSquared,
        FloatExp distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        FloatExp deltaReal = isJulia ? deltaConstantReal : FloatExp.Zero;
        FloatExp deltaImaginary = isJulia ? deltaConstantImaginary : FloatExp.Zero;
        FloatExp addReal = isJulia ? FloatExp.Zero : deltaConstantReal;
        FloatExp addImaginary = isJulia ? FloatExp.Zero : deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2Exp derivative = isJulia ? Jacobian2Exp.Identity : Jacobian2Exp.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);

        RealBlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.RealBla
            : null;

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            double blaDeltaMagnitudeSquared = bla is null
                ? 0.0
                : FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            if (bla is not null && bla.CanSkip(referenceIndex, blaDeltaMagnitudeSquared) &&
                bla.TryLookup(referenceIndex, blaDeltaMagnitudeSquared,
                    maxIterations - iteration,
                    out double blaA11, out double blaA12, out double blaA21, out double blaA22,
                    out double blaB11, out double blaB12, out double blaB21, out double blaB22,
                    out int blaSteps))
            {
                // δ ← A·δ + B·δc  (вещественная 2×2, A/B — double; δ, δc — FloatExp)
                FloatExp skippedReal = deltaReal * blaA11 + deltaImaginary * blaA12
                                     + addReal * blaB11 + addImaginary * blaB12;
                FloatExp skippedImaginary = deltaReal * blaA21 + deltaImaginary * blaA22
                                          + addReal * blaB21 + addImaginary * blaB22;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
                if (CountRealBlaSkipsForTests)
                    Interlocked.Add(ref RealBlaSkippedIterationsForTests, blaSteps);
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal.ToDouble();
                double currentImaginary = referenceImaginary + deltaImaginary.ToDouble();
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivativeExp(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                if (kind == ReflectKind.Celtic)
                {
                    FloatExp deltaU = (referenceReal * deltaReal - referenceImaginary * deltaImaginary) * 2.0
                                     + deltaReal * deltaReal - deltaImaginary * deltaImaginary;
                    FloatExp deltaV = (referenceReal * deltaImaginary + referenceImaginary * deltaReal) * 2.0
                                     + deltaReal * deltaImaginary * 2.0;
                    deltaReal = FoldedDeltaExp(referenceReal * referenceReal - referenceImaginary * referenceImaginary, deltaU) + addReal;
                    deltaImaginary = deltaV + addImaginary;
                }
                else
                {
                    double foldedReferenceReal, foldedReferenceImaginary;
                    FloatExp foldedDeltaReal, foldedDeltaImaginary;
                    switch (kind)
                    {
                        case ReflectKind.BurningShip:
                            foldedReferenceReal = System.Math.Abs(referenceReal);
                            foldedReferenceImaginary = -System.Math.Abs(referenceImaginary);
                            foldedDeltaReal = FoldedDeltaExp(referenceReal, deltaReal);
                            foldedDeltaImaginary = -FoldedDeltaExp(referenceImaginary, deltaImaginary);
                            break;
                        case ReflectKind.Buffalo:
                            foldedReferenceReal = System.Math.Abs(referenceReal);
                            foldedReferenceImaginary = System.Math.Abs(referenceImaginary);
                            foldedDeltaReal = FoldedDeltaExp(referenceReal, deltaReal);
                            foldedDeltaImaginary = FoldedDeltaExp(referenceImaginary, deltaImaginary);
                            break;
                        default: // Tricorn — сопряжение, знак определён всегда
                            foldedReferenceReal = referenceReal;
                            foldedReferenceImaginary = -referenceImaginary;
                            foldedDeltaReal = deltaReal;
                            foldedDeltaImaginary = -deltaImaginary;
                            break;
                    }

                    FloatExp twoWDeltaReal = (foldedReferenceReal * foldedDeltaReal - foldedReferenceImaginary * foldedDeltaImaginary) * 2.0;
                    FloatExp twoWDeltaImaginary = (foldedReferenceReal * foldedDeltaImaginary + foldedReferenceImaginary * foldedDeltaReal) * 2.0;
                    FloatExp foldedDeltaSquaredReal = foldedDeltaReal * foldedDeltaReal - foldedDeltaImaginary * foldedDeltaImaginary;
                    FloatExp foldedDeltaSquaredImaginary = foldedDeltaReal * foldedDeltaImaginary * 2.0;
                    deltaReal = twoWDeltaReal + foldedDeltaSquaredReal + addReal;
                    deltaImaginary = twoWDeltaImaginary + foldedDeltaSquaredImaginary + addImaginary;
                }

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal.ToDouble();
            double fullImaginary = nextReferenceImaginary + deltaImaginary.ToDouble();
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = FloatExp.FromDouble(fullReal - orbit.Re[0]);
                deltaImaginary = FloatExp.FromDouble(fullImaginary - orbit.Im[0]);
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixelExp(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Пертурбационное ядро Multibrot (Generalized) целой степени p ≥ 3: формула zᵖ+c.
    // Возмущение — точное биномиальное разложение (Z+δ)ᵖ − Zᵖ = Σₖ C(p,k)·Zᵖ⁻ᵏ·δᵏ (без
    // вычитания ⇒ без катастрофического сокращения). Линейный член A = p·Zᵖ⁻¹ комплексный,
    // поэтому BLA (с p-зависимой таблицей) применяется как обычно. δ здесь всегда double —
    // за порогом FloatExpDeltaZoomBits используется двойник
    // <see cref="DeepZoomPixelMultibrotFloatExp"/>.
    private static PixelMetrics DeepZoomPixelMultibrot(
        MandelbrotState state,
        ReferenceOrbit orbit,
        int power,
        double deltaConstantReal,
        double deltaConstantImaginary,
        double escapeSquared,
        double distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        // Generalized не бывает Жюлиа: δ₀ = 0, δc добавляется каждый шаг.
        double deltaReal = 0.0;
        double deltaImaginary = 0.0;
        double addReal = deltaConstantReal;
        double addImaginary = deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2 derivative = Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia: false);

        BlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.Bla
            : null;

        // Биномиальные коэффициенты C(p,k) для фиксированного p (p ≤ 12 ⇒ помещаются в long).
        Span<long> binomial = stackalloc long[power + 1];
        binomial[0] = 1;
        for (int k = 1; k <= power; k++) binomial[k] = binomial[k - 1] * (power - k + 1) / k;
        Span<double> zPowerReal = stackalloc double[power];
        Span<double> zPowerImaginary = stackalloc double[power];

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            if (bla is not null &&
                bla.TryLookup(referenceIndex, deltaReal * deltaReal + deltaImaginary * deltaImaginary,
                    maxIterations - iteration,
                    out double blaAx, out double blaAy, out double blaBx, out double blaBy, out int blaSteps))
            {
                double skippedReal = blaAx * deltaReal - blaAy * deltaImaginary
                                   + blaBx * addReal - blaBy * addImaginary;
                double skippedImaginary = blaAx * deltaImaginary + blaAy * deltaReal
                                        + blaBx * addImaginary + blaBy * addReal;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal;
                double currentImaginary = referenceImaginary + deltaImaginary;
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivative(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // Zᵏ, k = 0..p−1.
                zPowerReal[0] = 1.0;
                zPowerImaginary[0] = 0.0;
                for (int j = 1; j < power; j++)
                {
                    zPowerReal[j] = zPowerReal[j - 1] * referenceReal - zPowerImaginary[j - 1] * referenceImaginary;
                    zPowerImaginary[j] = zPowerReal[j - 1] * referenceImaginary + zPowerImaginary[j - 1] * referenceReal;
                }

                // Σ_{k=1}^{p} C(p,k)·Zᵖ⁻ᵏ·δᵏ
                double accumulatorReal = 0.0, accumulatorImaginary = 0.0;
                double deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary; // δ¹
                for (int k = 1; k <= power; k++)
                {
                    double zTermReal = zPowerReal[power - k];
                    double zTermImaginary = zPowerImaginary[power - k];
                    double termReal = zTermReal * deltaPowerReal - zTermImaginary * deltaPowerImaginary;
                    double termImaginary = zTermReal * deltaPowerImaginary + zTermImaginary * deltaPowerReal;
                    accumulatorReal += binomial[k] * termReal;
                    accumulatorImaginary += binomial[k] * termImaginary;

                    double nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
                    deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
                    deltaPowerReal = nextDeltaPowerReal;
                }

                deltaReal = accumulatorReal + addReal;
                deltaImaginary = accumulatorImaginary + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal;
            double fullImaginary = nextReferenceImaginary + deltaImaginary;
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = fullReal - orbit.Re[0];
                deltaImaginary = fullImaginary - orbit.Im[0];
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Тот же алгоритм, что <see cref="DeepZoomPixelMultibrot"/>, но δ ведётся в
    // <see cref="FloatExp"/> — используется за порогом FloatExpDeltaZoomBits. Опорная орбита,
    // Zᵏ и биномиальные коэффициенты остаются double (зависят только от опорной орбиты);
    // комплексная BLA (A, B) — тоже double, как и в FloatExp-ядре z²+c.
    private static PixelMetrics DeepZoomPixelMultibrotFloatExp(
        MandelbrotState state,
        ReferenceOrbit orbit,
        int power,
        FloatExp deltaConstantReal,
        FloatExp deltaConstantImaginary,
        double escapeSquared,
        FloatExp distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;

        // Generalized не бывает Жюлиа: δ₀ = 0, δc добавляется каждый шаг.
        FloatExp deltaReal = FloatExp.Zero;
        FloatExp deltaImaginary = FloatExp.Zero;
        FloatExp addReal = deltaConstantReal;
        FloatExp addImaginary = deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2Exp derivative = Jacobian2Exp.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia: false);

        BlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.Bla
            : null;

        Span<long> binomial = stackalloc long[power + 1];
        binomial[0] = 1;
        for (int k = 1; k <= power; k++) binomial[k] = binomial[k - 1] * (power - k + 1) / k;
        Span<double> zPowerReal = stackalloc double[power];
        Span<double> zPowerImaginary = stackalloc double[power];

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            if (bla is not null &&
                bla.TryLookup(referenceIndex, FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble(),
                    maxIterations - iteration,
                    out double blaAx, out double blaAy, out double blaBx, out double blaBy, out int blaSteps))
            {
                FloatExp skippedReal = deltaReal * blaAx - deltaImaginary * blaAy
                                     + addReal * blaBx - addImaginary * blaBy;
                FloatExp skippedImaginary = deltaReal * blaAy + deltaImaginary * blaAx
                                          + addReal * blaBy + addImaginary * blaBx;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal.ToDouble();
                double currentImaginary = referenceImaginary + deltaImaginary.ToDouble();
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivativeExp(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // Zᵏ, k = 0..p−1.
                zPowerReal[0] = 1.0;
                zPowerImaginary[0] = 0.0;
                for (int j = 1; j < power; j++)
                {
                    zPowerReal[j] = zPowerReal[j - 1] * referenceReal - zPowerImaginary[j - 1] * referenceImaginary;
                    zPowerImaginary[j] = zPowerReal[j - 1] * referenceImaginary + zPowerImaginary[j - 1] * referenceReal;
                }

                // Σ_{k=1}^{p} C(p,k)·Zᵖ⁻ᵏ·δᵏ
                FloatExp accumulatorReal = FloatExp.Zero, accumulatorImaginary = FloatExp.Zero;
                FloatExp deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary; // δ¹
                for (int k = 1; k <= power; k++)
                {
                    double zTermReal = zPowerReal[power - k];
                    double zTermImaginary = zPowerImaginary[power - k];
                    FloatExp termReal = zTermReal * deltaPowerReal - zTermImaginary * deltaPowerImaginary;
                    FloatExp termImaginary = zTermReal * deltaPowerImaginary + zTermImaginary * deltaPowerReal;
                    accumulatorReal += binomial[k] * termReal;
                    accumulatorImaginary += binomial[k] * termImaginary;

                    FloatExp nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
                    deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
                    deltaPowerReal = nextDeltaPowerReal;
                }

                deltaReal = accumulatorReal + addReal;
                deltaImaginary = accumulatorImaginary + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal.ToDouble();
            double fullImaginary = nextReferenceImaginary + deltaImaginary.ToDouble();
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = FloatExp.FromDouble(fullReal - orbit.Re[0]);
                deltaImaginary = FloatExp.FromDouble(fullImaginary - orbit.Im[0]);
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixelExp(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Пертурбационное ядро Симоноброта целой степени p: формула zᵖ·|z|ᵖ+c = zᵖ·M^(p/2)+c,
    // M=|z|². Возмущение — композиция точных разложений, без вычитания близких величин:
    //   δw = (Z+δ)ᵖ−Zᵖ = Σₖ C(p,k)·Zᵖ⁻ᵏ·δᵏ                (комплексное, как у Multibrot)
    //   δm = |Z+δ|²−M = 2(Zr·δr+Zi·δi)+δr²+δi²              (вещественное, сумма — не разность)
    //   δs = (M+δm)ᵠ−Mᵠ = Σⱼ C(q,j)·Mᵠ⁻ʲ·δmʲ                (вещественное биномиальное), q = ⌊p/2⌋
    //   δp = δs при чётном p; при нечётном P = Mᵠ·√M и δp = Mᵠ·δ√M + √M·δs + δs·δ√M,
    //        где δ√M = δm/(√(M+δm)+√M) — тождество, убирающее вычитание корней
    //   δ' = W·δp + P·δw + δw·δp + δc,  W = Zᵖ, P = M^(p/2)
    // Ведущий линейный член (P·p·Zᵖ⁻¹·δ плюс W·p·M^(p/2−1)·(Zr·δr+Zi·δi)) — вещественная 2×2
    // карта, не комплексное умножение, поэтому ускоряется RealBlaTable, а не BlaTable.
    // δ здесь всегда double — за порогом FloatExpDeltaZoomBits используется двойник
    // <see cref="DeepZoomPixelSimonobrotFloatExp"/>.
    private static PixelMetrics DeepZoomPixelSimonobrot(
        MandelbrotState state,
        ReferenceOrbit orbit,
        int power,
        double deltaConstantReal,
        double deltaConstantImaginary,
        double escapeSquared,
        double distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;
        int halfPower = power / 2;              // q = ⌊p/2⌋
        bool oddPower = (power & 1) != 0;       // p = 2q+1 ⇒ множитель модуля несёт ещё и √M

        // Симоноброт не бывает Жюлиа: δ₀ = 0, δc добавляется каждый шаг. UseInversion —
        // знак вещественной части добавки (см. ComputeReferenceOrbit).
        double deltaReal = 0.0;
        double deltaImaginary = 0.0;
        double addReal = state.UseInversion ? -deltaConstantReal : deltaConstantReal;
        double addImaginary = deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2 derivative = Jacobian2.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia: false);

        // BLA с вещественной 2×2 линейной частью — условия те же, что у комплексного.
        RealBlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.RealBla
            : null;

        Span<long> binomialPower = stackalloc long[power + 1];
        binomialPower[0] = 1;
        for (int k = 1; k <= power; k++) binomialPower[k] = binomialPower[k - 1] * (power - k + 1) / k;

        Span<long> binomialHalf = stackalloc long[halfPower + 1];
        binomialHalf[0] = 1;
        for (int k = 1; k <= halfPower; k++) binomialHalf[k] = binomialHalf[k - 1] * (halfPower - k + 1) / k;

        Span<double> zPowerReal = stackalloc double[power + 1];
        Span<double> zPowerImaginary = stackalloc double[power + 1];
        Span<double> magnitudePower = stackalloc double[halfPower + 1];

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            // В режимах ловушки/полос/DE таблица отключена — |δ|² тогда и не считается.
            double blaDeltaMagnitudeSquared = bla is null
                ? 0.0
                : deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            if (bla is not null && bla.CanSkip(referenceIndex, blaDeltaMagnitudeSquared) &&
                bla.TryLookup(referenceIndex, blaDeltaMagnitudeSquared,
                    maxIterations - iteration,
                    out double blaA11, out double blaA12, out double blaA21, out double blaA22,
                    out double blaB11, out double blaB12, out double blaB21, out double blaB22,
                    out int blaSteps))
            {
                // δ ← A·δ + B·δc  (вещественная 2×2), пропуская blaSteps итераций разом
                double skippedReal = blaA11 * deltaReal + blaA12 * deltaImaginary
                                   + blaB11 * addReal + blaB12 * addImaginary;
                double skippedImaginary = blaA21 * deltaReal + blaA22 * deltaImaginary
                                        + blaB21 * addReal + blaB22 * addImaginary;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
                if (CountRealBlaSkipsForTests)
                    Interlocked.Add(ref RealBlaSkippedIterationsForTests, blaSteps);
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal;
                double currentImaginary = referenceImaginary + deltaImaginary;
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivative(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // Zᵏ, k = 0..power (включительно — нужна и W = Zᵖ).
                zPowerReal[0] = 1.0;
                zPowerImaginary[0] = 0.0;
                for (int j = 1; j <= power; j++)
                {
                    zPowerReal[j] = zPowerReal[j - 1] * referenceReal - zPowerImaginary[j - 1] * referenceImaginary;
                    zPowerImaginary[j] = zPowerReal[j - 1] * referenceImaginary + zPowerImaginary[j - 1] * referenceReal;
                }

                // Mᵏ, k = 0..halfPower (M = |Z|²; нужна и Mᵠ).
                double referenceMagnitudeSquaredHere = referenceReal * referenceReal + referenceImaginary * referenceImaginary;
                magnitudePower[0] = 1.0;
                for (int j = 1; j <= halfPower; j++)
                    magnitudePower[j] = magnitudePower[j - 1] * referenceMagnitudeSquaredHere;

                // δw = Σ_{k=1}^{p} C(p,k)·Zᵖ⁻ᵏ·δᵏ   (комплексное)
                double deltaWReal = 0.0, deltaWImaginary = 0.0;
                double deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary; // δ¹
                for (int k = 1; k <= power; k++)
                {
                    double zr = zPowerReal[power - k], zi = zPowerImaginary[power - k];
                    deltaWReal += binomialPower[k] * (zr * deltaPowerReal - zi * deltaPowerImaginary);
                    deltaWImaginary += binomialPower[k] * (zr * deltaPowerImaginary + zi * deltaPowerReal);

                    double nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
                    deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
                    deltaPowerReal = nextDeltaPowerReal;
                }

                // δm = |Z+δ|² − M = 2(Zr·δr + Zi·δi) + δr² + δi²   (сумма, не разность — точно)
                double deltaM = 2.0 * (referenceReal * deltaReal + referenceImaginary * deltaImaginary)
                               + deltaReal * deltaReal + deltaImaginary * deltaImaginary;

                // δs = Σ_{j=1}^{q} C(q,j)·M^(q-j)·δmʲ   (возмущение Mᵠ, вещественное)
                double deltaS = 0.0;
                double deltaMPower = deltaM; // δm¹
                for (int j = 1; j <= halfPower; j++)
                {
                    deltaS += binomialHalf[j] * magnitudePower[halfPower - j] * deltaMPower;
                    deltaMPower *= deltaM;
                }

                // Множитель модуля P = M^(p/2) и его возмущение δp = (M+δm)^(p/2) − P.
                // Чётное p: P = Mᵠ и δp = δs — выражения ниже те же, что и до нечётной
                // степени. Нечётное p = 2q+1: P = Mᵠ·√M, а корень возмущается тождеством
                // δ√M = δm/(√(M+δm)+√M) — знаменатель ≈ 2√M, вычитания близких величин нет.
                double factorP = magnitudePower[halfPower];
                double deltaP = deltaS;
                if (oddPower)
                {
                    double rootM = System.Math.Sqrt(referenceMagnitudeSquaredHere);
                    double shifted = referenceMagnitudeSquaredHere + deltaM;   // = |Z+δ|² ≥ 0
                    double rootSum = (shifted > 0.0 ? System.Math.Sqrt(shifted) : 0.0) + rootM;
                    double deltaRoot = rootSum > 0.0 ? deltaM / rootSum : 0.0;
                    deltaP = factorP * deltaRoot + rootM * deltaS + deltaS * deltaRoot;
                    factorP *= rootM;
                }

                // δ' = W·δp + P·δw + δw·δp + δc
                double wReal = zPowerReal[power], wImaginary = zPowerImaginary[power];
                deltaReal = wReal * deltaP + factorP * deltaWReal + deltaWReal * deltaP + addReal;
                deltaImaginary = wImaginary * deltaP + factorP * deltaWImaginary + deltaWImaginary * deltaP + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal;
            double fullImaginary = nextReferenceImaginary + deltaImaginary;
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = deltaReal * deltaReal + deltaImaginary * deltaImaginary;
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = fullReal - orbit.Re[0];
                deltaImaginary = fullImaginary - orbit.Im[0];
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // Тот же алгоритм, что <see cref="DeepZoomPixelSimonobrot"/>, но δ (и все производные от
    // него величины — δw, δm, δs, δp, δ√M) ведутся в <see cref="FloatExp"/>, используется за
    // порогом FloatExpDeltaZoomBits. Опорная орбита, Zᵏ, Mᵏ, factorP и rootM остаются double —
    // они зависят только от опорной орбиты, ограниченной радиусом бейлаута. Вещественная BLA
    // (A, B) — тоже double, как и в остальных FloatExp-ядрах.
    private static PixelMetrics DeepZoomPixelSimonobrotFloatExp(
        MandelbrotState state,
        ReferenceOrbit orbit,
        int power,
        FloatExp deltaConstantReal,
        FloatExp deltaConstantImaginary,
        double escapeSquared,
        FloatExp distanceScale,
        CancellationToken token)
    {
        int maxIterations = state.Iterations;
        bool trackTrap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == MandelbrotColoringMode.StripeAverage;
        int halfPower = power / 2;              // q = ⌊p/2⌋
        bool oddPower = (power & 1) != 0;       // p = 2q+1 ⇒ множитель модуля несёт ещё и √M

        // Симоноброт не бывает Жюлиа: δ₀ = 0, δc добавляется каждый шаг. UseInversion —
        // знак вещественной части добавки (см. ComputeReferenceOrbit).
        FloatExp deltaReal = FloatExp.Zero;
        FloatExp deltaImaginary = FloatExp.Zero;
        FloatExp addReal = state.UseInversion ? -deltaConstantReal : deltaConstantReal;
        FloatExp addImaginary = deltaConstantImaginary;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        Jacobian2Exp derivative = Jacobian2Exp.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia: false);

        RealBlaTable? bla = BlaEnabled && !trackTrap && !trackStripe && !estimateDistance
            ? orbit.RealBla
            : null;

        Span<long> binomialPower = stackalloc long[power + 1];
        binomialPower[0] = 1;
        for (int k = 1; k <= power; k++) binomialPower[k] = binomialPower[k - 1] * (power - k + 1) / k;

        Span<long> binomialHalf = stackalloc long[halfPower + 1];
        binomialHalf[0] = 1;
        for (int k = 1; k <= halfPower; k++) binomialHalf[k] = binomialHalf[k - 1] * (halfPower - k + 1) / k;

        Span<double> zPowerReal = stackalloc double[power + 1];
        Span<double> zPowerImaginary = stackalloc double[power + 1];
        Span<double> magnitudePower = stackalloc double[halfPower + 1];

        int referenceIndex = 0;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        double minTrap = double.MaxValue;
        double stripe = 0;
        bool escaped = false;

        while (iteration < maxIterations)
        {
            if ((iteration & 8191) == 0 && token.IsCancellationRequested) return default;

            double blaDeltaMagnitudeSquared = bla is null
                ? 0.0
                : FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            if (bla is not null && bla.CanSkip(referenceIndex, blaDeltaMagnitudeSquared) &&
                bla.TryLookup(referenceIndex, blaDeltaMagnitudeSquared,
                    maxIterations - iteration,
                    out double blaA11, out double blaA12, out double blaA21, out double blaA22,
                    out double blaB11, out double blaB12, out double blaB21, out double blaB22,
                    out int blaSteps))
            {
                // δ ← A·δ + B·δc  (вещественная 2×2), пропуская blaSteps итераций разом
                FloatExp skippedReal = deltaReal * blaA11 + deltaImaginary * blaA12
                                     + addReal * blaB11 + addImaginary * blaB12;
                FloatExp skippedImaginary = deltaReal * blaA21 + deltaImaginary * blaA22
                                          + addReal * blaB21 + addImaginary * blaB22;
                deltaReal = skippedReal;
                deltaImaginary = skippedImaginary;
                referenceIndex += blaSteps;
                iteration += blaSteps;
                if (CountRealBlaSkipsForTests)
                    Interlocked.Add(ref RealBlaSkippedIterationsForTests, blaSteps);
            }
            else
            {
                double referenceReal = orbit.Re[referenceIndex];
                double referenceImaginary = orbit.Im[referenceIndex];

                double currentReal = referenceReal + deltaReal.ToDouble();
                double currentImaginary = referenceImaginary + deltaImaginary.ToDouble();
                if (trackTrap)
                    minTrap = System.Math.Min(minTrap,
                        System.Math.Min(System.Math.Abs(currentReal), System.Math.Abs(currentImaginary)));
                if (trackStripe)
                    stripe += 0.5 + 0.5 * System.Math.Sin(
                        state.StripeFrequency * System.Math.Atan2(currentImaginary, currentReal));

                if (estimateDistance)
                    derivative = AdvanceDerivativeExp(state, derivative, parameterDerivative,
                        currentReal, currentImaginary);

                // Zᵏ, k = 0..power (включительно — нужна и W = Zᵖ).
                zPowerReal[0] = 1.0;
                zPowerImaginary[0] = 0.0;
                for (int j = 1; j <= power; j++)
                {
                    zPowerReal[j] = zPowerReal[j - 1] * referenceReal - zPowerImaginary[j - 1] * referenceImaginary;
                    zPowerImaginary[j] = zPowerReal[j - 1] * referenceImaginary + zPowerImaginary[j - 1] * referenceReal;
                }

                // Mᵏ, k = 0..halfPower (M = |Z|²; нужна и Mᵠ).
                double referenceMagnitudeSquaredHere = referenceReal * referenceReal + referenceImaginary * referenceImaginary;
                magnitudePower[0] = 1.0;
                for (int j = 1; j <= halfPower; j++)
                    magnitudePower[j] = magnitudePower[j - 1] * referenceMagnitudeSquaredHere;

                // δw = Σ_{k=1}^{p} C(p,k)·Zᵖ⁻ᵏ·δᵏ   (комплексное)
                FloatExp deltaWReal = FloatExp.Zero, deltaWImaginary = FloatExp.Zero;
                FloatExp deltaPowerReal = deltaReal, deltaPowerImaginary = deltaImaginary; // δ¹
                for (int k = 1; k <= power; k++)
                {
                    double zr = zPowerReal[power - k], zi = zPowerImaginary[power - k];
                    deltaWReal += binomialPower[k] * (zr * deltaPowerReal - zi * deltaPowerImaginary);
                    deltaWImaginary += binomialPower[k] * (zr * deltaPowerImaginary + zi * deltaPowerReal);

                    FloatExp nextDeltaPowerReal = deltaPowerReal * deltaReal - deltaPowerImaginary * deltaImaginary;
                    deltaPowerImaginary = deltaPowerReal * deltaImaginary + deltaPowerImaginary * deltaReal;
                    deltaPowerReal = nextDeltaPowerReal;
                }

                // δm = |Z+δ|² − M = 2(Zr·δr + Zi·δi) + δr² + δi²   (сумма, не разность — точно)
                FloatExp deltaM = (referenceReal * deltaReal + referenceImaginary * deltaImaginary) * 2.0
                                 + deltaReal * deltaReal + deltaImaginary * deltaImaginary;

                // δs = Σ_{j=1}^{q} C(q,j)·M^(q-j)·δmʲ   (возмущение Mᵠ, вещественное)
                FloatExp deltaS = FloatExp.Zero;
                FloatExp deltaMPower = deltaM; // δm¹
                for (int j = 1; j <= halfPower; j++)
                {
                    deltaS += (binomialHalf[j] * magnitudePower[halfPower - j]) * deltaMPower;
                    deltaMPower *= deltaM;
                }

                // Множитель модуля P = M^(p/2) и его возмущение δp = (M+δm)^(p/2) − P.
                // Чётное p: P = Mᵠ и δp = δs — выражения ниже те же, что и до нечётной
                // степени. Нечётное p = 2q+1: P = Mᵠ·√M, а корень возмущается тождеством
                // δ√M = δm/(√(M+δm)+√M) — знаменатель ≈ 2√M, вычитания близких величин нет.
                double factorP = magnitudePower[halfPower];
                FloatExp deltaP = deltaS;
                if (oddPower)
                {
                    double rootM = System.Math.Sqrt(referenceMagnitudeSquaredHere);
                    FloatExp shifted = referenceMagnitudeSquaredHere + deltaM;   // = |Z+δ|² ≥ 0
                    FloatExp rootSum = (shifted.Sign > 0 ? FloatExp.Sqrt(shifted) : FloatExp.Zero) + rootM;
                    FloatExp deltaRoot = rootSum.Sign > 0 ? deltaM / rootSum : FloatExp.Zero;
                    deltaP = factorP * deltaRoot + rootM * deltaS + deltaS * deltaRoot;
                    factorP *= rootM;
                }

                // δ' = W·δp + P·δw + δw·δp + δc
                double wReal = zPowerReal[power], wImaginary = zPowerImaginary[power];
                deltaReal = wReal * deltaP + factorP * deltaWReal + deltaWReal * deltaP + addReal;
                deltaImaginary = wImaginary * deltaP + factorP * deltaWImaginary + deltaWImaginary * deltaP + addImaginary;

                referenceIndex++;
                iteration++;
            }

            double nextReferenceReal = referenceIndex < orbit.Length ? orbit.Re[referenceIndex] : 0.0;
            double nextReferenceImaginary = referenceIndex < orbit.Length ? orbit.Im[referenceIndex] : 0.0;
            double fullReal = nextReferenceReal + deltaReal.ToDouble();
            double fullImaginary = nextReferenceImaginary + deltaImaginary.ToDouble();
            magnitudeSquared = fullReal * fullReal + fullImaginary * fullImaginary;

            if (magnitudeSquared > escapeSquared)
            {
                escapeReal = fullReal;
                escapeImaginary = fullImaginary;
                escaped = true;
                break;
            }

            double deltaMagnitudeSquared = FloatExp.MagnitudeSquared(deltaReal, deltaImaginary).ToDouble();
            double referenceMagnitudeSquared =
                nextReferenceReal * nextReferenceReal + nextReferenceImaginary * nextReferenceImaginary;
            if (referenceIndex >= orbit.Length - 1 ||
                magnitudeSquared < deltaMagnitudeSquared ||
                magnitudeSquared < GlitchToleranceSquared * referenceMagnitudeSquared)
            {
                deltaReal = FloatExp.FromDouble(fullReal - orbit.Re[0]);
                deltaImaginary = FloatExp.FromDouble(fullImaginary - orbit.Im[0]);
                referenceIndex = 0;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixelExp(iteration, magnitudeSquared, minTrap, stripe,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }

    // ------------------------------------------------------------------ brute-force safety net

    private static (double X, double Y) ProjectCenterToDouble(MandelbrotState state)
    {
        double x = state.CenterXExact is { Length: > 0 } exactX
            ? BigFloat.Parse(exactX).ToDouble()
            : (double)state.CenterX;
        double y = state.CenterYExact is { Length: > 0 } exactY
            ? BigFloat.Parse(exactY).ToDouble()
            : (double)state.CenterY;
        return (x, y);
    }

    // Запасной путь на случай вырожденной опорной орбиты (центр в глубоком внешнем регионе,
    // орбита выходит за радиус почти сразу). Такой вид почти однороден: всё убегает за
    // считаные итерации, накапливать ошибку нечему, и обычного double достаточно. Ступень
    // decimal здесь больше не используется.
    private static byte[]? RenderBruteForceTile(
        MandelbrotState state,
        int canvasWidth,
        int canvasHeight,
        MandelbrotRenderTile tile,
        CancellationToken token)
    {
        int stride = checked(tile.Width * 4);
        var buffer = new byte[checked(stride * tile.Height)];
        var (centerX, centerY) = ProjectCenterToDouble(state);
        // Вырожденная опорная орбита означает почти однородный вид, поэтому сужение ширины
        // вида до double безопасно: за 1.8e308 зума шаг пикселя обращается в ноль и все
        // пиксели совпадают с центром — ровно то, что этот путь и без того рисует.
        double viewWidth = (3.0 / state.Zoom).ToDouble();
        double viewHeight = viewWidth * canvasHeight / canvasWidth;
        bool trackHistogram = state.ColoringMode == MandelbrotColoringMode.Histogram;

        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int y = tile.Y + localY;
            double imaginary = centerY + (0.5 - (double)y / canvasHeight) * viewHeight;
            int row = localY * stride;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                int x = tile.X + localX;
                double real = centerX + ((double)x / canvasWidth - 0.5) * viewWidth;
                PixelMetrics metrics = Iterate(state, real, imaginary, token);
                double histogramValue = trackHistogram
                    ? System.Math.Clamp((state.HistogramInputUseSmooth ? metrics.Smooth : metrics.Iterations) /
                                         System.Math.Max(1, state.Iterations), 0, 1)
                    : 0;
                Color color = ResolveColor(state, metrics, histogramValue);
                int offset = row + localX * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
        }

        return token.IsCancellationRequested ? null : buffer;
    }

    private static void RenderBruteForceFull(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int stride,
        CancellationToken token,
        Action<int>? reportProgress)
    {
        int threads = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = System.Math.Clamp(threads, 1, Environment.ProcessorCount)
        };
        var (centerX, centerY) = ProjectCenterToDouble(state);
        // См. RenderBruteForceTile: вид однороден, сужение до double здесь ничего не теряет.
        double viewWidth = (3.0 / state.Zoom).ToDouble();
        double viewHeight = viewWidth * height / width;
        int completedRows = 0;

        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            int row = y * stride;
            double imaginary = centerY + (0.5 - (double)y / height) * viewHeight;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                double real = centerX + ((double)x / width - 0.5) * viewWidth;
                PixelMetrics metrics = Iterate(state, real, imaginary, token);
                Color color = ResolveColor(state, metrics, 0);
                int offset = row + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }

            int done = Interlocked.Increment(ref completedRows);
            if (done == height || done % System.Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(done * 100 / height);
        });
    }
}
