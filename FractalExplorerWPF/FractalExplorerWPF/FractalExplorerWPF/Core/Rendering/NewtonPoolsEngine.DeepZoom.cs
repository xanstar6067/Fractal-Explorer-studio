using System.Globalization;
using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Глубокий зум бассейнов Ньютона — пертурбационный движок по произвольной формуле.
///
/// <para><b>Опорная орбита.</b> Центр кадра итерируется в <see cref="BigFloat"/> тем же
/// методом, что и пиксели (Newton, Halley, Householder, Relaxed). На каждом шаге сохраняются
/// double-копии результатов всех инструкций байткода нужных выражений (f, f′, f″ или обратных
/// производных Householder) — «слоты», см. <see cref="CompiledComplexExpression"/>. Ньютоновская
/// орбита сходится к корню: как только шаг опорной точки перестаёт влиять на рабочую
/// разрядность, орбита объявляется сошедшейся, и все дальнейшие итерации читают её последнюю
/// точку.</para>
///
/// <para><b>Пиксель.</b> Состояние — отклонение <c>δ = z − Z</c>. Каждое выражение считается в
/// пертурбационной форме (приращение результата без вычитания близких величин), из приращений
/// собирается приращение шага: у Ньютона <c>Δ(f/f′) = (Δf − (F/G)·Δg)/(G+Δg)</c>, у Галлея и
/// Хаусхолдера — то же тождество частного. Проверки (корень, цикл, уход, нулевая производная)
/// выполняются на восстановленных <c>z = Z + δ</c> и <c>f = F + Δf</c> ровно по правилам плоской
/// ступени.</para>
///
/// <para><b>Передача в double.</b> Когда |δ| дорастает до 2⁻²⁸ от масштаба опорной точки,
/// соседние пиксели различаются уже на ~2⁻³⁸ — на тысячи ulp выше шума double, и дальше пиксель
/// досчитывает обычная double-итерация (<see cref="ContinueNormal"/>/<see cref="ContinueDiagnostic"/>):
/// она в разы дешевле пертурбации. Так же пиксель уходит в double, если опорная орбита кончилась
/// или пертурбационная формула дала нечисло (опорная точка ровно в нуле знаменателя).</para>
///
/// <para><b>Линейный пропуск.</b> Пока δ крошечное, шаг линеен: <c>δₙ = Aₙ·p</c>, где p —
/// смещение пикселя, а <c>Aₙ₊₁ = N′(Zₙ)·Aₙ</c> (в плоскости λ — <c>N_z·Aₙ + N_λ</c>). Коэффициенты
/// считаются один раз на кадр из струй второго порядка; одновременно ведётся квадратичный
/// коэффициент <c>Bₙ</c>, и пропуск для пикселя допустим, пока <c>|B|·|p|² ≤ 2⁻³⁸·|A|·|p|</c>
/// и <c>|A·p| ≤ 2⁻⁴²</c>. На глубине 1e1000 это почти вся хаотическая часть орбиты — без пропуска
/// δ просто не поместилось бы в double. Пороги подобраны замером: кадры 800×600 до 1e1000 с ними
/// совпадают бит-в-бит с пропуском до 2⁻⁶⁴ и считаются примерно на 40 % быстрее, а при 2⁻³⁰
/// картинка уже начинает меняться. Ни одна проверка плоской ступени на пропущенных шагах
/// сработать не может: пропуск обрывается раньше, чем опорная точка подходит к корню, к уходу, к
/// нулю производной или к повтору истории.</para>
///
/// <para><b>Где метод упирается.</b> Если опорная орбита проходит ближе ~1e-16 к критической
/// точке шага (N′ = 0 не у корня), первые порядки приращения сокращаются, и точность δ на этом
/// шаге ограничена ulp. На типичных кадрах (границы бассейнов, «цветки» вокруг прообразов
/// полюсов) такого не случается; у самих прообразов полюсов пиксели улетают за диапазон double —
/// и плоская ступень там тоже красит фоном.</para>
/// </summary>
public sealed partial class NewtonPoolsEngine
{
    /// <summary>Зум, выше которого кадр считает этот движок — тот же порог, что у Мандельброта, Nova и Феникса.</summary>
    internal const double DeepZoomThreshold = 1.5e9;

    /// <summary>Шов для проверок: включает или выключает движок независимо от зума.</summary>
    internal static bool? ForceDeepZoomForTests { get; set; }

    /// <summary>Шов для проверок: подменяет разрядность опорной орбиты.</summary>
    internal static int? ForceReferenceBitsForTests { get; set; }

    /// <summary>Шов для проверок: false отключает линейный пропуск.</summary>
    internal static bool? ForceLinearSkipForTests { get; set; }

    /// <summary>
    /// Шов для проверок: подменяет порог передачи пикселя в double (<c>|δ|²/max(1,|Z|²)</c>).
    /// Большой порог заставляет пертурбацию вести орбиту целиком даже на мелком зуме, где её
    /// можно сравнить с плоской ступенью; бесконечный — не передавать никогда.
    /// </summary>
    internal static double? ForceHandoffRatioSquaredForTests { get; set; }

    /// <summary>Диагностика для проверок: включает счёт пропущенных итераций в <see cref="SkippedIterationsForTests"/>.</summary>
    internal static bool CountSkippedIterationsForTests { get; set; }

    internal static long SkippedIterationsForTests;

    /// <summary>Потолок памяти под слоты опорной орбиты: дальше неё орбита обрывается и пиксели уходят в double.</summary>
    private const long ReferenceMemoryBudget = 384L * 1024 * 1024;

    private static readonly double LinearSkipTolerance = Math.ScaleB(1.0, -38);
    private static readonly double LinearSkipTarget = Math.ScaleB(1.0, -42);
    private static readonly double PlainHandoffRatioSquared = Math.ScaleB(1.0, -56);
    private static readonly FloatExp FloatExpInfinity = FloatExp.FromDouble(double.PositiveInfinity);

    private enum DeepStepStatus
    {
        Success,
        ZeroDerivative,
        NonFinite,
        Handoff
    }

    // ------------------------------------------------------------------ plan

    internal bool ShouldUseDeepZoom() =>
        DeepExpressions() is not null && (ForceDeepZoomForTests ?? Zoom > DeepZoomThreshold);

    /// <summary>
    /// Разрядность опорной орбиты: биты на разрешение соседних пикселей (≈ log2 зума), удвоенная
    /// длина орбиты на накопление округлений и 48 бит люфта — план Nova и Феникса.
    /// </summary>
    internal int PlanReferenceBits()
    {
        double zoomBits = Zoom.Sign > 0 && Zoom.IsFinite ? Zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int iterationBits = 32 - BitOperations.LeadingZeroCount((uint)Math.Max(MaxIterations, 2));
        int needed = (int)Math.Ceiling(zoomBits) + 2 * iterationBits + 48;
        int rounded = Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
        return ForceReferenceBitsForTests ?? rounded;
    }

    /// <summary>
    /// Выражения, которые нужны шагу текущего метода: всегда f (проверки), дальше f′ (Newton,
    /// Relaxed), f′ и f″ (Halley) или пара обратных производных (Householder). null — движку не
    /// на что опереться, и кадр остаётся плоской ступени.
    /// </summary>
    private CompiledComplexExpression[]? DeepExpressions()
    {
        if (_compiledFormula is null || _compiledFirstDerivative is null) return null;
        return IterationMethod switch
        {
            NewtonIterationMethod.Halley when _compiledSecondDerivative is not null =>
                [_compiledFormula, _compiledFirstDerivative, _compiledSecondDerivative],
            NewtonIterationMethod.Halley => null,
            NewtonIterationMethod.Householder when _compiledInverseDerivatives.Count > HouseholderOrder =>
                [_compiledFormula, _compiledInverseDerivatives[HouseholderOrder - 1], _compiledInverseDerivatives[HouseholderOrder]],
            NewtonIterationMethod.Householder => null,
            _ => [_compiledFormula, _compiledFirstDerivative]
        };
    }

    private string CenterXRaw => CenterXExact is { Length: > 0 } exact ? exact : CenterX.ToString("R", CultureInfo.InvariantCulture);
    private string CenterYRaw => CenterYExact is { Length: > 0 } exact ? exact : CenterY.ToString("R", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ reference orbit

    private sealed class DeepReference
    {
        public required double[] Re;
        public required double[] Im;
        public required Complex[][] Slots;
        public required int[] SlotCounts;

        /// <summary>Количество заполненных точек.</summary>
        public required int Length;

        /// <summary>Орбита сошлась: итерации за <see cref="Length"/> читают последнюю точку.</summary>
        public required bool Converged;

        /// <summary>λ опорной точки в double: центр кадра в плоскости λ, иначе параметр Relaxed.</summary>
        public required Complex Lambda;

        public int Map(int iteration) => iteration < Length ? iteration : Converged ? Length - 1 : -1;

        public Complex ZAt(int index) => new(Re[index], Im[index]);

        public ReadOnlySpan<Complex> SlotsAt(int expression, int index) =>
            new(Slots[expression], index * SlotCounts[expression], SlotCounts[expression]);
    }

    private static readonly object ReferenceLock = new();
    private static string? _referenceKey;
    private static DeepReference? _referenceCache;

    private DeepReference GetDeepReference(CompiledComplexExpression[] expressions, int bits, CancellationToken token)
    {
        // В ключе — всё, что участвует в построении орбиты: формула задаёт и производные.
        string key = string.Join('|',
            _formulaText,
            ((int)IterationMethod).ToString(CultureInfo.InvariantCulture),
            HouseholderOrder.ToString(CultureInfo.InvariantCulture),
            ((int)RelaxedPlaneMode).ToString(CultureInfo.InvariantCulture),
            Relaxation.Real.ToString("R", CultureInfo.InvariantCulture),
            Relaxation.Imaginary.ToString("R", CultureInfo.InvariantCulture),
            FixedInitialZ.Real.ToString("R", CultureInfo.InvariantCulture),
            FixedInitialZ.Imaginary.ToString("R", CultureInfo.InvariantCulture),
            CenterXRaw,
            CenterYRaw,
            MaxIterations.ToString(CultureInfo.InvariantCulture),
            bits.ToString(CultureInfo.InvariantCulture));

        lock (ReferenceLock)
        {
            if (_referenceKey == key && _referenceCache is not null) return _referenceCache;
            DeepReference reference = ComputeDeepReference(expressions, bits, token);
            _referenceKey = key;
            _referenceCache = reference;
            return reference;
        }
    }

    /// <summary>
    /// Опорная орбита в произвольной точности. Отменяема: на 1e1000 с трансцендентной формулой
    /// она считается долго, а быстрый зум колесом отменяет кадр раньше, чем орбита готова, —
    /// недосчитанная орбита в кэш не попадает.
    /// </summary>
    private DeepReference ComputeDeepReference(CompiledComplexExpression[] expressions, int bits, CancellationToken token)
    {
        using var precision = new BigFloat.PrecisionScope(bits);

        var center = new ComplexBigFloat(BigFloat.Parse(CenterXRaw), BigFloat.Parse(CenterYRaw));
        bool lambdaPlane = UsesLambdaParameterPlane;
        ComplexBigFloat z = lambdaPlane ? ComplexBigFloat.FromDouble(FixedInitialZ.Real, FixedInitialZ.Imaginary) : center;
        ComplexBigFloat lambda = lambdaPlane ? center : ComplexBigFloat.FromDouble(Relaxation.Real, Relaxation.Imaginary);

        int expressionCount = expressions.Length;
        var slotCounts = new int[expressionCount];
        long bytesPerIteration = 16;
        for (int expression = 0; expression < expressionCount; expression++)
        {
            slotCounts[expression] = expressions[expression].SlotCount;
            bytesPerIteration += 16L * slotCounts[expression];
        }
        int capacityLimit = (int)Math.Min(MaxIterations + 1L, Math.Max(16, ReferenceMemoryBudget / bytesPerIteration));
        int capacity = Math.Min(capacityLimit, 256);
        var re = new double[capacity];
        var im = new double[capacity];
        var slots = new Complex[expressionCount][];
        for (int expression = 0; expression < expressionCount; expression++)
            slots[expression] = new Complex[capacity * slotCounts[expression]];
        var values = new ComplexBigFloat[expressionCount][];
        for (int expression = 0; expression < expressionCount; expression++)
            values[expression] = new ComplexBigFloat[expressions[expression].InstructionCount];
        var results = new ComplexBigFloat[expressionCount];

        int length = 0;
        bool converged = false;
        for (int index = 0; index < capacityLimit; index++)
        {
            token.ThrowIfCancellationRequested();
            if (index == capacity)
            {
                capacity = (int)Math.Min(capacityLimit, capacity * 2L);
                Array.Resize(ref re, capacity);
                Array.Resize(ref im, capacity);
                for (int expression = 0; expression < expressionCount; expression++)
                    Array.Resize(ref slots[expression], capacity * slotCounts[expression]);
            }

            var zDouble = z.ToComplex();
            if (!IsFinite(zDouble)) break;
            re[index] = zDouble.Real;
            im[index] = zDouble.Imaginary;

            ComplexBigFloat step;
            try
            {
                bool representable = true;
                for (int expression = 0; expression < expressionCount && representable; expression++)
                {
                    Span<Complex> target = slots[expression].AsSpan(index * slotCounts[expression], slotCounts[expression]);
                    results[expression] = expressions[expression].EvaluateBig(z, values[expression], target);
                    foreach (Complex slot in target)
                        representable &= IsFinite(slot);
                }
                // Значение вне диапазона double пикселю не опора: орбита кончается, дальше — double.
                if (!representable) break;

                length = index + 1;
                ComplexBigFloat f = results[0];
                if (f.Real.IsZero && f.Imaginary.IsZero)
                {
                    converged = true;
                    break;
                }

                step = ReferenceStep(f, results, lambda);
            }
            catch (Exception exception) when (exception is ArithmeticException or ArgumentException)
            {
                break;
            }

            if (IsNegligibleStep(step, z, bits))
            {
                converged = true;
                break;
            }
            z += step;
        }

        return new DeepReference
        {
            Re = re, Im = im, Slots = slots, SlotCounts = slotCounts,
            Length = length, Converged = converged,
            Lambda = lambdaPlane ? center.ToComplex() : Relaxation
        };
    }

    private ComplexBigFloat ReferenceStep(ComplexBigFloat f, ComplexBigFloat[] results, ComplexBigFloat lambda) =>
        IterationMethod switch
        {
            NewtonIterationMethod.Halley =>
                -((f * results[1] * 2) / (results[1] * results[1] * 2 - f * results[2])),
            NewtonIterationMethod.Householder => results[1] * HouseholderOrder / results[2],
            NewtonIterationMethod.RelaxedNewton => -(lambda * f / results[1]),
            _ => -(f / results[1])
        };

    /// <summary>Шаг ниже рабочей разрядности относительно |z| — дальше орбита стоит на месте.</summary>
    private static bool IsNegligibleStep(ComplexBigFloat step, ComplexBigFloat z, int bits)
    {
        int stepExponent = Math.Max(step.Real.BinaryExponent, step.Imaginary.BinaryExponent);
        if (stepExponent == int.MinValue) return true;
        int zExponent = Math.Max(0, Math.Max(z.Real.BinaryExponent, z.Imaginary.BinaryExponent));
        return stepExponent < zExponent - (bits - 16);
    }

    // ------------------------------------------------------------------ linear skip

    private sealed class DeepSkipTable
    {
        /// <summary><c>δₙ = Aₙ·p</c> для n = 0..<see cref="Limit"/>.</summary>
        public required FloatExp[] AReal, AImaginary;

        /// <summary><c>|p| ≤ Radius[n]</c> ⇒ шаги 0..n линейны. Префиксный минимум — не возрастает.</summary>
        public required FloatExp[] Radius;

        public required int Limit;

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

    private static readonly object SkipLock = new();
    private static DeepReference? _skipReference;
    private static string? _skipKey;
    private static DeepSkipTable? _skipCache;

    private DeepSkipTable? GetSkipTable(DeepReference reference, CompiledComplexExpression[] expressions, bool diagnostics)
    {
        if (ForceLinearSkipForTests == false) return null;
        // Орбита уже несёт формулу, метод, центр и итерации; сюда — то, что влияет на обрыв пропуска.
        string key = string.Join('|',
            diagnostics ? "1" : "0",
            RootTolerance.ToString("R", CultureInfo.InvariantCulture),
            string.Join(';', Roots.Select(root =>
                root.Real.ToString("R", CultureInfo.InvariantCulture) + "," +
                root.Imaginary.ToString("R", CultureInfo.InvariantCulture))));

        lock (SkipLock)
        {
            if (ReferenceEquals(_skipReference, reference) && _skipKey == key && _skipCache is not null)
                return _skipCache;
            DeepSkipTable table = BuildSkipTable(reference, expressions, diagnostics);
            _skipReference = reference;
            _skipKey = key;
            _skipCache = table;
            return table;
        }
    }

    private DeepSkipTable BuildSkipTable(DeepReference reference, CompiledComplexExpression[] expressions, bool diagnostics)
    {
        // Шаг n читает точку n и восстанавливает z по точке n+1: у несошедшейся орбиты последняя
        // точка шагом не пропускается.
        int capacity = reference.Converged ? MaxIterations : Math.Min(MaxIterations, reference.Length - 1);
        capacity = Math.Max(0, capacity);
        var aReal = new FloatExp[capacity + 1];
        var aImaginary = new FloatExp[capacity + 1];
        var radius = new FloatExp[Math.Max(1, capacity)];

        var jets = new ComplexJet[expressions.Length][];
        for (int expression = 0; expression < expressions.Length; expression++)
            jets[expression] = new ComplexJet[expressions[expression].InstructionCount];

        bool lambdaPlane = UsesLambdaParameterPlane;
        FloatExp ar = lambdaPlane ? FloatExp.Zero : FloatExp.One, ai = FloatExp.Zero;
        FloatExp br = FloatExp.Zero, bi = FloatExp.Zero;
        aReal[0] = ar;
        aImaginary[0] = ai;

        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        FloatExp running = FloatExpInfinity;
        int limit = 0;
        Complex lambda = reference.Lambda;

        for (int step = 0; step < capacity; step++)
        {
            int index = reference.Map(step);
            int nextIndex = reference.Map(step + 1);
            if (index < 0 || nextIndex < 0) break;
            Complex point = reference.ZAt(index);
            AddHistory(history, ref historyCount, ref historyNext, point);
            if (!IsReferenceStepSafe(reference, expressions, index, point, diagnostics,
                    history, historyCount, historyNext)) break;

            ComplexJet f = expressions[0].EvaluateJet(reference.SlotsAt(0, index), jets[0]);
            ComplexJet first = expressions[1].EvaluateJet(reference.SlotsAt(1, index), jets[1]);
            ComplexJet second = expressions.Length > 2
                ? expressions[2].EvaluateJet(reference.SlotsAt(2, index), jets[2])
                : default;

            // Коэффициенты шага N(z) = z + S(z): n1 = ∂N/∂z, n2 = ∂²N/∂z²; в плоскости λ ещё
            // nl = ∂N/∂λ и nzl = ∂²N/∂z∂λ (∂²N/∂λ² = 0: шаг линеен по λ).
            Complex n1, n2, nl = Complex.Zero, nzl = Complex.Zero;
            if (lambdaPlane)
            {
                ComplexJet ratio = f / first;
                n1 = 1 - lambda * ratio.First;
                n2 = -lambda * ratio.Second;
                nl = -ratio.Value;
                nzl = -ratio.First;
                if (!ratio.IsFinite) break;
            }
            else
            {
                ComplexJet increment = IterationMethod switch
                {
                    NewtonIterationMethod.Halley => -((Complex)2 * (f * first) / ((Complex)2 * (first * first) - f * second)),
                    NewtonIterationMethod.Householder => (Complex)HouseholderOrder * (first / second),
                    NewtonIterationMethod.RelaxedNewton => -Relaxation * (f / first),
                    _ => -(f / first)
                };
                if (!increment.IsFinite) break;
                n1 = 1 + increment.First;
                n2 = increment.Second;
            }

            // A' = n1·A (+ nl); B' = n1·B + ½·n2·A² (+ nzl·A).
            FloatExp squareReal = ar * ar - ai * ai;
            FloatExp squareImaginary = 2.0 * (ar * ai);
            FloatExp nextAr = n1.Real * ar - n1.Imaginary * ai + nl.Real;
            FloatExp nextAi = n1.Real * ai + n1.Imaginary * ar + nl.Imaginary;
            FloatExp nextBr = n1.Real * br - n1.Imaginary * bi +
                              0.5 * (n2.Real * squareReal - n2.Imaginary * squareImaginary) +
                              (nzl.Real * ar - nzl.Imaginary * ai);
            FloatExp nextBi = n1.Real * bi + n1.Imaginary * br +
                              0.5 * (n2.Real * squareImaginary + n2.Imaginary * squareReal) +
                              (nzl.Real * ai + nzl.Imaginary * ar);
            if (!nextAr.IsFinite || !nextAi.IsFinite || !nextBr.IsFinite || !nextBi.IsFinite) break;

            FloatExp magnitudeA = FloatExp.Sqrt(FloatExp.MagnitudeSquared(nextAr, nextAi));
            FloatExp magnitudeB = FloatExp.Sqrt(FloatExp.MagnitudeSquared(nextBr, nextBi));
            double scale = Math.Max(1, reference.ZAt(nextIndex).Magnitude);
            FloatExp quadraticRadius = magnitudeB.IsZero ? FloatExpInfinity : LinearSkipTolerance * magnitudeA / magnitudeB;
            FloatExp sizeRadius = magnitudeA.IsZero ? FloatExpInfinity : LinearSkipTarget * scale / magnitudeA;
            if (quadraticRadius < running) running = quadraticRadius;
            if (sizeRadius < running) running = sizeRadius;
            radius[step] = running;
            if (running.IsZero) break;

            ar = nextAr;
            ai = nextAi;
            br = nextBr;
            bi = nextBi;
            aReal[step + 1] = ar;
            aImaginary[step + 1] = ai;
            limit = step + 1;
        }

        return new DeepSkipTable { AReal = aReal, AImaginary = aImaginary, Radius = radius, Limit = limit };
    }

    /// <summary>
    /// Проверки плоской ступени на итерации опорной точки не сработают и у пикселя, отличающегося
    /// от неё на ничтожную долю: все пороги взяты с запасом вдвое.
    /// </summary>
    private bool IsReferenceStepSafe(DeepReference reference, CompiledComplexExpression[] expressions, int index,
        Complex point, bool diagnostics, Span<Complex> history, int historyCount, int historyNext)
    {
        if (!IsFinite(point)) return false;
        Complex f = reference.SlotsAt(0, index)[expressions[0].ResultSlot];
        if (!IsFinite(f) || f == Complex.Zero) return false;
        double margin = 4 * RootTolerance * RootTolerance;
        if (NearestRootDistanceSquared(point) <= margin) return false;
        if (diagnostics)
        {
            if (Math.Abs(point.Real) > DiagnosticEscapeRadius / 2 || Math.Abs(point.Imaginary) > DiagnosticEscapeRadius / 2)
                return false;
            if (MagnitudeSquared(f) <= margin) return false;
            if (HistoryHasClosePairs(history, historyCount, historyNext, 2)) return false;
        }

        double zeroMargin = 4 * DerivativeZeroTolerance * DerivativeZeroTolerance;
        Complex first = reference.SlotsAt(1, index)[expressions[1].ResultSlot];
        if (!IsFinite(first)) return false;
        switch (IterationMethod)
        {
            case NewtonIterationMethod.Halley:
            {
                Complex second = reference.SlotsAt(2, index)[expressions[2].ResultSlot];
                Complex denominator = 2 * first * first - f * second;
                return MagnitudeSquared(first) > zeroMargin && IsFinite(second) && IsFinite(denominator) &&
                       MagnitudeSquared(denominator) > zeroMargin && IsFinite(2 * f * first / denominator);
            }
            case NewtonIterationMethod.Householder:
            {
                Complex current = reference.SlotsAt(2, index)[expressions[2].ResultSlot];
                return IsFinite(current) && MagnitudeSquared(current) > zeroMargin && IsFinite(first / current);
            }
            default:
                return MagnitudeSquared(first) > zeroMargin && IsFinite(f / first);
        }
    }

    // ------------------------------------------------------------------ frame

    private sealed class DeepFrame
    {
        public required DeepReference Reference;
        public required DeepSkipTable? Skip;
        public required CompiledComplexExpression[] Expressions;
        public required FloatExp ViewWidth;
        public required bool LambdaPlane;
        public required bool Diagnostics;
        public required double HandoffRatioSquared;
    }

    private sealed class DeepScratch
    {
        public readonly Complex[][] Deltas;
        public readonly Complex[] History = new Complex[HistoryCapacity];

        public DeepScratch(CompiledComplexExpression[] expressions)
        {
            Deltas = new Complex[expressions.Length][];
            for (int expression = 0; expression < expressions.Length; expression++)
                Deltas[expression] = new Complex[expressions[expression].InstructionCount];
        }
    }

    private readonly object _deepFrameLock = new();
    private DeepFrame? _deepFrame;

    /// <summary>
    /// Кадр глубокого движка для этого экземпляра: тайлы рендерятся параллельно, а свойства
    /// движка за время рендера не меняются, поэтому орбита и таблица пропуска берутся один раз.
    /// null — опорная орбита вырождена, и кадр считает плоская ступень.
    /// </summary>
    private DeepFrame? GetDeepFrame(bool diagnostics, CancellationToken token)
    {
        lock (_deepFrameLock)
        {
            double handoff = ForceHandoffRatioSquaredForTests ?? PlainHandoffRatioSquared;
            if (_deepFrame is not null && _deepFrame.Diagnostics == diagnostics &&
                (_deepFrame.Skip is null) == (ForceLinearSkipForTests == false) &&
                _deepFrame.HandoffRatioSquared.Equals(handoff))
                return _deepFrame;

            CompiledComplexExpression[]? expressions = DeepExpressions();
            if (expressions is null) return null;
            DeepReference reference = GetDeepReference(expressions, PlanReferenceBits(), token);
            if (reference.Length == 0) return null;
            _deepFrame = new DeepFrame
            {
                Reference = reference,
                Skip = GetSkipTable(reference, expressions, diagnostics),
                Expressions = expressions,
                ViewWidth = FloatExp.FromDouble(BaseViewWidth) / Zoom,
                LambdaPlane = UsesLambdaParameterPlane,
                Diagnostics = diagnostics,
                HandoffRatioSquared = handoff
            };
            return _deepFrame;
        }
    }

    private void RenderDeepRows(byte[] buffer, int width, int height, int stride, int threadCount,
        CancellationToken token, Action<int>? reportProgress, bool diagnostics)
    {
        DeepFrame? frame = GetDeepFrame(diagnostics, token);
        if (frame is null)
        {
            RenderRows(buffer, width, height, stride, threadCount, token, reportProgress, diagnostics);
            return;
        }

        long completedRows = 0;
        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threadCount) },
            () => new DeepScratch(frame.Expressions),
            (y, loopState, scratch) =>
            {
                if (token.IsCancellationRequested) { loopState.Stop(); return scratch; }
                FloatExp pixelImaginary = (height / 2.0 - y) * frame.ViewWidth / width;
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return scratch; }
                    FloatExp pixelReal = (x - width / 2.0) * frame.ViewWidth / width;
                    WriteColor(buffer, row + x * 4, DeepPixelColor(frame, scratch, pixelReal, pixelImaginary));
                }

                int rows = (int)Interlocked.Increment(ref completedRows);
                if (rows == height || rows % Math.Max(1, height / 100) == 0)
                    reportProgress?.Invoke(rows * 100 / height);
                return scratch;
            },
            _ => { });
    }

    private byte[]? RenderDeepTile(byte[] buffer, MandelbrotRenderTile tile, int canvasWidth, int canvasHeight,
        CancellationToken token, bool diagnostics)
    {
        DeepFrame? frame = GetDeepFrame(diagnostics, token);
        if (frame is null) return RenderTileCore(buffer, tile, canvasWidth, canvasHeight, token, diagnostics);

        var scratch = new DeepScratch(frame.Expressions);
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            FloatExp pixelImaginary = (canvasHeight / 2.0 - (tile.Y + localY)) * frame.ViewWidth / canvasWidth;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                FloatExp pixelReal = (tile.X + localX - canvasWidth / 2.0) * frame.ViewWidth / canvasWidth;
                WriteColor(buffer, (localY * tile.Width + localX) * 4,
                    DeepPixelColor(frame, scratch, pixelReal, pixelImaginary));
            }
        }
        return token.IsCancellationRequested ? null : buffer;
    }

    private Color DeepPixelColor(DeepFrame frame, DeepScratch scratch, FloatExp pixelReal, FloatExp pixelImaginary)
    {
        if (frame.Diagnostics) return GetDiagnosticColor(DeepDiagnosePixel(frame, scratch, pixelReal, pixelImaginary));
        int iteration = DeepNormalPixel(frame, scratch, pixelReal, pixelImaginary, out Complex z);
        return GetPixelColor(z, iteration);
    }

    // ------------------------------------------------------------------ per-pixel kernel

    /// <summary>Начальное состояние пикселя: δ после линейного пропуска, δλ и номер итерации.</summary>
    private int StartPixel(DeepFrame frame, FloatExp pixelReal, FloatExp pixelImaginary,
        out Complex delta, out Complex deltaLambda, out Complex lambdaPixel)
    {
        var pixel = new Complex(pixelReal.ToDouble(), pixelImaginary.ToDouble());
        delta = frame.LambdaPlane ? Complex.Zero : pixel;
        deltaLambda = frame.LambdaPlane ? pixel : Complex.Zero;
        lambdaPixel = frame.LambdaPlane ? frame.Reference.Lambda + pixel : Relaxation;
        if (frame.Skip is not { Limit: > 0 } skip) return 0;

        int skipped = skip.FindSkip(FloatExp.Sqrt(FloatExp.MagnitudeSquared(pixelReal, pixelImaginary)));
        if (skipped == 0) return 0;
        if (CountSkippedIterationsForTests) Interlocked.Add(ref SkippedIterationsForTests, skipped);
        FloatExp ar = skip.AReal[skipped], ai = skip.AImaginary[skipped];
        delta = new Complex((ar * pixelReal - ai * pixelImaginary).ToDouble(), (ar * pixelImaginary + ai * pixelReal).ToDouble());
        return skipped;
    }

    private int DeepNormalPixel(DeepFrame frame, DeepScratch scratch, FloatExp pixelReal, FloatExp pixelImaginary,
        out Complex finalZ)
    {
        DeepReference reference = frame.Reference;
        CompiledComplexExpression formula = frame.Expressions[0];
        int iteration = StartPixel(frame, pixelReal, pixelImaginary,
            out Complex delta, out Complex deltaLambda, out Complex lambdaPixel);
        double toleranceSquared = RootTolerance * RootTolerance;

        while (iteration < MaxIterations)
        {
            int index = reference.Map(iteration);
            Complex referenceZ = reference.ZAt(index);
            Complex z = referenceZ + delta;
            if (MagnitudeSquared(delta) >= frame.HandoffRatioSquared * Math.Max(1, MagnitudeSquared(referenceZ)))
            {
                iteration = ContinueNormal(ref z, lambdaPixel, iteration);
                finalZ = z;
                return iteration;
            }

            ReadOnlySpan<Complex> formulaSlots = reference.SlotsAt(0, index);
            Complex referenceF = formulaSlots[formula.ResultSlot];
            Complex deltaF = formula.EvaluatePerturbed(formulaSlots, delta, scratch.Deltas[0]);
            Complex f = referenceF + deltaF;
            if (!IsFinite(f) || f == Complex.Zero || (MagnitudeSquared(f) <= toleranceSquared || (iteration & 7) == 7) && IsNearKnownRoot(z))
            {
                finalZ = z;
                return iteration;
            }

            DeepStepStatus status = PerturbedStep(frame, scratch, index, delta, deltaLambda, referenceF, deltaF, f,
                false, out Complex stepValue, out Complex deltaStep);
            if (status == DeepStepStatus.Handoff)
            {
                iteration = ContinueNormal(ref z, lambdaPixel, iteration);
                finalZ = z;
                return iteration;
            }
            if (status != DeepStepStatus.Success)
            {
                finalZ = z;
                return iteration;
            }

            delta += deltaStep;
            iteration++;
            if (reference.Map(iteration) < 0)
            {
                Complex next = z + stepValue;
                iteration = ContinueNormal(ref next, lambdaPixel, iteration);
                finalZ = next;
                return iteration;
            }
        }

        finalZ = reference.ZAt(reference.Map(iteration)) + delta;
        return iteration;
    }

    private NewtonOrbitResult DeepDiagnosePixel(DeepFrame frame, DeepScratch scratch, FloatExp pixelReal, FloatExp pixelImaginary)
    {
        DeepReference reference = frame.Reference;
        CompiledComplexExpression formula = frame.Expressions[0];
        int iteration = StartPixel(frame, pixelReal, pixelImaginary,
            out Complex delta, out Complex deltaLambda, out Complex lambdaPixel);
        double toleranceSquared = RootTolerance * RootTolerance;

        // История плоской ступени — точки z₀..zₙ; на пропущенных шагах пиксель неотличим от опоры.
        Span<Complex> history = scratch.History;
        int historyCount = 0, historyNext = 0;
        for (int past = Math.Max(0, iteration - (HistoryCapacity - 1)); past < iteration; past++)
            AddHistory(history, ref historyCount, ref historyNext, reference.ZAt(reference.Map(past)));
        AddHistory(history, ref historyCount, ref historyNext, reference.ZAt(reference.Map(iteration)) + delta);
        Complex lastValue = iteration > 0
            ? reference.SlotsAt(0, reference.Map(iteration - 1))[formula.ResultSlot]
            : new Complex(double.NaN, double.NaN);

        while (iteration < MaxIterations)
        {
            int index = reference.Map(iteration);
            Complex referenceZ = reference.ZAt(index);
            Complex z = referenceZ + delta;
            if (MagnitudeSquared(delta) >= frame.HandoffRatioSquared * Math.Max(1, MagnitudeSquared(referenceZ)))
                return ContinueDiagnostic(z, lambdaPixel, iteration, history, historyCount, historyNext, lastValue);

            if (!IsFinite(z)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, lastValue);
            if (IsEscaped(z)) return CreateOrbitResult(NewtonOrbitOutcome.Escaped, iteration, z, lastValue);

            ReadOnlySpan<Complex> formulaSlots = reference.SlotsAt(0, index);
            Complex referenceF = formulaSlots[formula.ResultSlot];
            Complex deltaF = formula.EvaluatePerturbed(formulaSlots, delta, scratch.Deltas[0]);
            Complex f = referenceF + deltaF;
            lastValue = f;
            if (!IsFinite(f)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, f);

            int rootIndex = FindKnownRootIndex(z);
            if (f == Complex.Zero || MagnitudeSquared(f) <= toleranceSquared || rootIndex >= 0)
                return CreateOrbitResult(NewtonOrbitOutcome.ConvergedToRoot, iteration, z, f, rootIndex);

            int cyclePeriod = DetectCycle(history, historyCount, historyNext);
            if (cyclePeriod is >= 2 and <= 8)
                return CreateOrbitResult(NewtonOrbitOutcome.Cycle, iteration, z, f, cyclePeriod: cyclePeriod);

            DeepStepStatus status = PerturbedStep(frame, scratch, index, delta, deltaLambda, referenceF, deltaF, f,
                true, out Complex stepValue, out Complex deltaStep);
            switch (status)
            {
                case DeepStepStatus.Handoff:
                    return ContinueDiagnostic(z, lambdaPixel, iteration, history, historyCount, historyNext, lastValue);
                case DeepStepStatus.NonFinite:
                    return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, f);
                case DeepStepStatus.ZeroDerivative:
                    return CreateOrbitResult(NewtonOrbitOutcome.ZeroDerivative, iteration, z, f);
            }

            delta += deltaStep;
            iteration++;
            if (reference.Map(iteration) < 0)
            {
                Complex next = z + stepValue;
                AddHistory(history, ref historyCount, ref historyNext, next);
                return ContinueDiagnostic(next, lambdaPixel, iteration, history, historyCount, historyNext, lastValue);
            }
            AddHistory(history, ref historyCount, ref historyNext, reference.ZAt(reference.Map(iteration)) + delta);
        }

        return FinishDiagnostic(reference.ZAt(reference.Map(iteration)) + delta, history, historyCount, historyNext, lastValue);
    }

    /// <summary>
    /// Шаг пикселя: значение <c>s(z)</c> и приращение <c>s(z) − S(Z)</c>. Решения о нулевой
    /// производной и нечисле принимаются по значениям в пикселе и дословно повторяют плоскую
    /// ступень (точный ноль в обычной раскраске, порог 1e-14 в диагностической);
    /// <see cref="DeepStepStatus.Handoff"/> — пертурбационная формула сама не удалась, и пиксель
    /// досчитывается в double.
    /// </summary>
    private DeepStepStatus PerturbedStep(DeepFrame frame, DeepScratch scratch, int index, Complex delta,
        Complex deltaLambda, Complex referenceF, Complex deltaF, Complex f, bool diagnostics,
        out Complex stepValue, out Complex deltaStep)
    {
        stepValue = default;
        deltaStep = default;
        DeepReference reference = frame.Reference;
        CompiledComplexExpression firstExpression = frame.Expressions[1];
        ReadOnlySpan<Complex> firstSlots = reference.SlotsAt(1, index);
        Complex referenceG = firstSlots[firstExpression.ResultSlot];
        Complex deltaG = firstExpression.EvaluatePerturbed(firstSlots, delta, scratch.Deltas[1]);
        Complex g = referenceG + deltaG;
        Complex referenceStep;

        switch (IterationMethod)
        {
            case NewtonIterationMethod.Halley:
            {
                if (!IsFinite(g)) return DeepStepStatus.NonFinite;
                if (diagnostics ? IsEffectivelyZero(g) : g == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                CompiledComplexExpression secondExpression = frame.Expressions[2];
                ReadOnlySpan<Complex> secondSlots = reference.SlotsAt(2, index);
                Complex referenceH = secondSlots[secondExpression.ResultSlot];
                Complex deltaH = secondExpression.EvaluatePerturbed(secondSlots, delta, scratch.Deltas[2]);
                Complex h = referenceH + deltaH;
                if (diagnostics && !IsFinite(h)) return DeepStepStatus.NonFinite;
                Complex denominator = 2 * g * g - f * h;
                if (!IsFinite(denominator)) return DeepStepStatus.NonFinite;
                if (diagnostics ? IsEffectivelyZero(denominator) : denominator == Complex.Zero)
                    return DeepStepStatus.ZeroDerivative;

                Complex referenceNumerator = 2 * referenceF * referenceG;
                Complex referenceDenominator = 2 * referenceG * referenceG - referenceF * referenceH;
                Complex deltaNumerator = 2 * (referenceF * deltaG + deltaF * g);
                Complex deltaDenominator = 2 * deltaG * (referenceG + g) - (referenceF * deltaH + deltaF * h);
                Complex ratio = referenceNumerator / referenceDenominator;
                referenceStep = -ratio;
                deltaStep = -(deltaNumerator - ratio * deltaDenominator) / (referenceDenominator + deltaDenominator);
                break;
            }
            case NewtonIterationMethod.Householder:
            {
                CompiledComplexExpression currentExpression = frame.Expressions[2];
                ReadOnlySpan<Complex> currentSlots = reference.SlotsAt(2, index);
                Complex referenceQ = currentSlots[currentExpression.ResultSlot];
                Complex deltaQ = currentExpression.EvaluatePerturbed(currentSlots, delta, scratch.Deltas[2]);
                Complex q = referenceQ + deltaQ;
                if (!IsFinite(g) || !IsFinite(q)) return DeepStepStatus.NonFinite;
                if (diagnostics ? IsEffectivelyZero(q) : q == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                int order = HouseholderOrder;
                Complex ratio = referenceG / referenceQ;
                referenceStep = order * ratio;
                deltaStep = order * (deltaG - ratio * deltaQ) / q;
                break;
            }
            default:
            {
                if (!IsFinite(g)) return DeepStepStatus.NonFinite;
                if (diagnostics ? IsEffectivelyZero(g) : g == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                Complex ratio = referenceF / referenceG;
                Complex deltaRatio = (deltaF - ratio * deltaG) / g;
                if (IterationMethod != NewtonIterationMethod.RelaxedNewton)
                {
                    referenceStep = -ratio;
                    deltaStep = -deltaRatio;
                }
                else if (frame.LambdaPlane)
                {
                    Complex lambda = reference.Lambda;
                    referenceStep = -lambda * ratio;
                    deltaStep = -(lambda * deltaRatio + deltaLambda * (ratio + deltaRatio));
                }
                else
                {
                    referenceStep = -Relaxation * ratio;
                    deltaStep = -Relaxation * deltaRatio;
                }
                break;
            }
        }

        if (!IsFinite(deltaStep) || !IsFinite(referenceStep)) return DeepStepStatus.Handoff;
        stepValue = referenceStep + deltaStep;
        if (!IsFinite(stepValue)) return DeepStepStatus.NonFinite;
        if (!diagnostics && stepValue == Complex.Zero) return DeepStepStatus.ZeroDerivative;
        return DeepStepStatus.Success;
    }
}
