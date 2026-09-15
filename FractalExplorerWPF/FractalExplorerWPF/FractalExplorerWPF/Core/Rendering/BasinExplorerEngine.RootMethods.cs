using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Методы поиска корней без глубокого зума: Мюллер (три точки, квадратичная интерполяция),
/// Лагерр (с Ньютоном для сравнения) и секущие (две точки). Орбита считается сошедшейся, когда
/// последнее приближение попало в допуск известного корня.
/// </summary>
public sealed partial class BasinExplorerEngine
{
    public MullerSeedMode MullerSeedMode { get; set; }
    public Complex MullerOffset { get; set; } = new(0.25, 0);
    public Complex MullerAnchorA { get; set; } = new(-1, 0);
    public Complex MullerAnchorB { get; set; } = new(1, 0);

    public bool LaguerreAutoDegree { get; set; } = true;
    public double LaguerreDegree { get; set; } = 3;
    public LaguerreComparisonMode LaguerreComparison { get; set; }

    /// <summary>
    /// n в формуле Лагерра: степень полинома в режиме «авто», иначе ручное значение. При n = 1
    /// шаг Лагерра в точности совпадает с шагом Ньютона.
    /// </summary>
    public double EffectiveLaguerreDegree =>
        LaguerreAutoDegree && PolynomialDegree > 0 ? PolynomialDegree : Math.Clamp(LaguerreDegree, 1, 1024);

    public SecantPlaneMode SecantPlaneMode { get; set; }
    public Complex SecantFirstPoint { get; set; } = new(1, 1);
    public Complex SecantOffset { get; set; } = new(0.25, 0);
    public SecantStateAxis SecantHorizontalAxis { get; set; } = SecantStateAxis.ReX0;
    public SecantStateAxis SecantVerticalAxis { get; set; } = SecantStateAxis.ReX1;
    public Complex SecantBaseX0 { get; set; }
    public Complex SecantBaseX1 { get; set; }

    /// <summary>Лежит ли плоскость обзора в плоскости z (иначе — срез пространства состояний секущих).</summary>
    public bool ViewIsComplexPlane => Kind != BasinExplorerKind.Secant || SecantPlaneMode != SecantPlaneMode.StateSlice;

    #region Seeds

    internal (Complex X0, Complex X1, Complex X2) MullerSeeds(Complex z) => MullerSeedMode switch
    {
        MullerSeedMode.Trailing => (z - 2 * MullerOffset, z - MullerOffset, z),
        MullerSeedMode.FixedAnchors => (MullerAnchorA, MullerAnchorB, z),
        _ => (z - MullerOffset, z + MullerOffset, z)
    };

    internal (Complex X0, Complex X1) SecantSeeds(double planeX, double planeY)
    {
        switch (SecantPlaneMode)
        {
            case SecantPlaneMode.OffsetPair:
            {
                var z = new Complex(planeX, planeY);
                return (z - SecantOffset, z);
            }
            case SecantPlaneMode.StateSlice:
            {
                Span<double> state = stackalloc double[4];
                state[0] = SecantBaseX0.Real;
                state[1] = SecantBaseX0.Imaginary;
                state[2] = SecantBaseX1.Real;
                state[3] = SecantBaseX1.Imaginary;
                int horizontal = (int)SecantHorizontalAxis;
                int vertical = (int)SecantVerticalAxis;
                if (vertical == horizontal) vertical = (horizontal + 2) % 4;
                state[horizontal] = planeX;
                state[vertical] = planeY;
                return (new Complex(state[0], state[1]), new Complex(state[2], state[3]));
            }
            default:
                return (SecantFirstPoint, new Complex(planeX, planeY));
        }
    }

    #endregion

    #region Orbits

    /// <summary>
    /// Мюллер: через три последние точки проводится парабола (разделённые разности), следующим
    /// приближением берётся ближайший к x₂ её корень — знаменатель с большим модулем.
    /// </summary>
    internal BasinOrbitResult MullerOrbit(Complex x0, Complex x1, Complex x2)
    {
        CompiledComplexExpression f = _function!;
        Complex f0 = f.Evaluate(x0);
        Complex f1 = f.Evaluate(x1);
        Complex f2 = f.Evaluate(x2);
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        AddHistory(history, ref historyCount, ref historyNext, x2);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            if (TryFinishRootStep(x1, x2, f2, iteration, out BasinOrbitResult finished)) return finished;
            if (!TryMullerStep(x0, f0, x1, f1, x2, f2, out Complex next))
                return new BasinOrbitResult(BasinOrbitOutcome.Degenerate, iteration, iteration, x2);

            x0 = x1; f0 = f1;
            x1 = x2; f1 = f2;
            x2 = next; f2 = f.Evaluate(next);
            AddHistory(history, ref historyCount, ref historyNext, x2);
        }

        if (TryFinishRootStep(x1, x2, f2, MaxIterations, out BasinOrbitResult last)) return last;
        return LimitResult(x2, history, historyCount, historyNext);
    }

    internal static bool TryMullerStep(Complex x0, Complex f0, Complex x1, Complex f1, Complex x2, Complex f2, out Complex next)
    {
        next = x2;
        Complex h1 = x1 - x0;
        Complex h2 = x2 - x1;
        Complex span = h1 + h2;
        if (h1 == Complex.Zero || h2 == Complex.Zero || span == Complex.Zero) return false;

        Complex d1 = (f1 - f0) / h1;
        Complex d2 = (f2 - f1) / h2;
        Complex a = (d2 - d1) / span;
        Complex b = a * h2 + d2;
        Complex discriminant = Complex.Sqrt(b * b - 4 * a * f2);
        Complex plus = b + discriminant;
        Complex minus = b - discriminant;
        Complex denominator = MagnitudeSquared(plus) >= MagnitudeSquared(minus) ? plus : minus;
        if (denominator == Complex.Zero || !IsFinite(denominator)) return false;
        next = x2 - 2 * f2 / denominator;
        return IsFinite(next);
    }

    /// <summary>
    /// Лагерр: G = f'/f, H = G² − f''/f, шаг n/(G ± √((n−1)(nH − G²))) с большим по модулю
    /// знаменателем. Для полинома степени n метод сходится кубически к простым корням.
    /// </summary>
    internal BasinOrbitResult LaguerreOrbit(Complex z)
    {
        double n = EffectiveLaguerreDegree;
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        AddHistory(history, ref historyCount, ref historyNext, z);
        Complex previous = z;
        Complex value = _function!.Evaluate(z);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            if (TryFinishRootStep(previous, z, value, iteration, out BasinOrbitResult finished)) return finished;
            if (!TryLaguerreStep(z, value, n, out Complex step, out bool nonFinite))
                return new BasinOrbitResult(nonFinite ? BasinOrbitOutcome.NonFinite : BasinOrbitOutcome.Degenerate,
                    iteration, iteration, z);

            previous = z;
            z -= step;
            value = _function.Evaluate(z);
            AddHistory(history, ref historyCount, ref historyNext, z);
        }

        if (TryFinishRootStep(previous, z, value, MaxIterations, out BasinOrbitResult last)) return last;
        return LimitResult(z, history, historyCount, historyNext);
    }

    private bool TryLaguerreStep(Complex z, Complex value, double n, out Complex step, out bool nonFinite)
    {
        step = Complex.Zero;
        nonFinite = false;
        Complex first = _firstDerivative!.Evaluate(z);
        Complex second = _secondDerivative!.Evaluate(z);
        if (!IsFinite(first) || !IsFinite(second)) { nonFinite = true; return false; }

        Complex g = first / value;
        Complex h = g * g - second / value;
        Complex root = Complex.Sqrt((n - 1) * (n * h - g * g));
        Complex plus = g + root;
        Complex minus = g - root;
        Complex denominator = MagnitudeSquared(plus) >= MagnitudeSquared(minus) ? plus : minus;
        if (!IsFinite(denominator)) { nonFinite = true; return false; }
        if (denominator == Complex.Zero) return false;
        step = n / denominator;
        if (IsFinite(step)) return true;
        nonFinite = true;
        return false;
    }

    /// <summary>Ньютон для той же формулы — вторая половина сравнения в окне Лагерра.</summary>
    internal BasinOrbitResult NewtonOrbit(Complex z)
    {
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        AddHistory(history, ref historyCount, ref historyNext, z);
        Complex previous = z;
        Complex value = _function!.Evaluate(z);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            if (TryFinishRootStep(previous, z, value, iteration, out BasinOrbitResult finished)) return finished;
            Complex derivative = _firstDerivative!.Evaluate(z);
            if (!IsFinite(derivative)) return new BasinOrbitResult(BasinOrbitOutcome.NonFinite, iteration, iteration, z);
            if (derivative == Complex.Zero) return new BasinOrbitResult(BasinOrbitOutcome.Degenerate, iteration, iteration, z);
            Complex step = value / derivative;
            if (!IsFinite(step)) return new BasinOrbitResult(BasinOrbitOutcome.NonFinite, iteration, iteration, z);

            previous = z;
            z -= step;
            value = _function.Evaluate(z);
            AddHistory(history, ref historyCount, ref historyNext, z);
        }

        if (TryFinishRootStep(previous, z, value, MaxIterations, out BasinOrbitResult last)) return last;
        return LimitResult(z, history, historyCount, historyNext);
    }

    /// <summary>
    /// Секущие: x₂ = x₁ − f(x₁)(x₁ − x₀)/(f(x₁) − f(x₀)). Состояние метода — пара (x₀, x₁), поэтому
    /// бассейны живут в четырёхмерном пространстве, а окно показывает его двумерные сечения.
    /// </summary>
    internal BasinOrbitResult SecantOrbit(Complex x0, Complex x1)
    {
        CompiledComplexExpression f = _function!;
        Complex f0 = f.Evaluate(x0);
        Complex f1 = f.Evaluate(x1);
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        AddHistory(history, ref historyCount, ref historyNext, x1);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            if (TryFinishRootStep(x0, x1, f1, iteration, out BasinOrbitResult finished)) return finished;
            if (!IsFinite(f0)) return new BasinOrbitResult(BasinOrbitOutcome.NonFinite, iteration, iteration, x1);
            Complex denominator = f1 - f0;
            if (denominator == Complex.Zero)
                return new BasinOrbitResult(BasinOrbitOutcome.Degenerate, iteration, iteration, x1);
            Complex next = x1 - f1 * (x1 - x0) / denominator;
            if (!IsFinite(next)) return new BasinOrbitResult(BasinOrbitOutcome.NonFinite, iteration, iteration, x1);

            x0 = x1; f0 = f1;
            x1 = next; f1 = f.Evaluate(next);
            AddHistory(history, ref historyCount, ref historyNext, x1);
        }

        if (TryFinishRootStep(x0, x1, f1, MaxIterations, out BasinOrbitResult last)) return last;
        return LimitResult(x1, history, historyCount, historyNext);
    }

    /// <summary>
    /// Общая проверка очередного приближения <paramref name="z"/>: попадание в допуск корня, точный
    /// ноль функции, нечисловое значение и уход. <paramref name="previous"/> — предыдущее приближение
    /// для плавного номера итерации.
    /// </summary>
    private bool TryFinishRootStep(Complex previous, Complex z, Complex value, int iteration, out BasinOrbitResult result)
    {
        if (!IsFinite(z) || !IsFinite(value))
        {
            result = new BasinOrbitResult(BasinOrbitOutcome.NonFinite, iteration, iteration, z);
            return true;
        }
        if (Math.Abs(z.Real) > RootEscapeRadius || Math.Abs(z.Imaginary) > RootEscapeRadius)
        {
            result = new BasinOrbitResult(BasinOrbitOutcome.Escaped, iteration, iteration, z);
            return true;
        }

        int rootIndex = NearestRootWithinTolerance(z, out double distanceSquared);
        if (rootIndex >= 0)
        {
            Complex root = Roots[rootIndex];
            double smooth = SmoothIteration(iteration, (previous - root).Magnitude, Math.Sqrt(distanceSquared), RootTolerance);
            result = new BasinOrbitResult(BasinOrbitOutcome.Converged, iteration, smooth, z, rootIndex);
            return true;
        }
        if (value == Complex.Zero)
        {
            result = new BasinOrbitResult(BasinOrbitOutcome.Converged, iteration, iteration, z);
            return true;
        }

        result = default;
        return false;
    }

    private int NearestRootWithinTolerance(Complex z, out double distanceSquared)
    {
        int nearest = -1;
        distanceSquared = double.MaxValue;
        IReadOnlyList<Complex> roots = Roots;
        for (int index = 0; index < roots.Count; index++)
        {
            double candidate = MagnitudeSquared(z - roots[index]);
            if (candidate >= distanceSquared) continue;
            distanceSquared = candidate;
            nearest = index;
        }
        return distanceSquared <= RootTolerance * RootTolerance ? nearest : -1;
    }

    private BasinOrbitResult LimitResult(Complex z, Span<Complex> history, int historyCount, int historyNext)
    {
        int period = DetectCycle(history, historyCount, historyNext, Math.Clamp(RootTolerance * 4, 1e-10, 1e-4), 2);
        return period > 0
            ? new BasinOrbitResult(BasinOrbitOutcome.Cycle, MaxIterations, MaxIterations, z, CyclePeriod: period)
            : new BasinOrbitResult(BasinOrbitOutcome.IterationLimit, MaxIterations, MaxIterations, z);
    }

    /// <summary>
    /// Карта расхождения: где Лагерр и Ньютон приходят к одному корню — приглушённый цвет,
    /// где к разным — яркий цвет корня Лагерра, где сходится только один метод — белый.
    /// </summary>
    private Color DisagreementColor(Complex z)
    {
        BasinOrbitResult laguerre = LaguerreOrbit(z);
        BasinOrbitResult newton = NewtonOrbit(z);
        int laguerreRoot = laguerre.Outcome == BasinOrbitOutcome.Converged ? laguerre.TargetIndex : -1;
        int newtonRoot = newton.Outcome == BasinOrbitOutcome.Converged ? newton.TargetIndex : -1;
        if (laguerreRoot < 0 && newtonRoot < 0) return BackgroundColor;
        if (laguerreRoot < 0 || newtonRoot < 0) return Colors.White;
        Color color = TargetColor(laguerreRoot);
        return laguerreRoot == newtonRoot ? Lerp(BackgroundColor, color, 0.22) : color;
    }

    #endregion

    #region Orbit trace

    /// <summary>
    /// Последовательность приближений для точки плоскости обзора — для рисования орбиты поверх
    /// полотна. В срезе пространства состояний секущих экранные координаты не являются z, поэтому
    /// орбита там не строится.
    /// </summary>
    public IReadOnlyList<Complex> TraceOrbit(double planeX, double planeY, int maxPoints = 160)
    {
        if (!IsReady) return [];
        var point = new Complex(planeX, planeY);
        var trace = new List<Complex>();
        int limit = Math.Clamp(Math.Min(maxPoints, MaxIterations + 3), 2, 4096);
        switch (Kind)
        {
            case BasinExplorerKind.Muller:
            {
                (Complex x0, Complex x1, Complex x2) = MullerSeeds(point);
                Complex f0 = _function!.Evaluate(x0), f1 = _function.Evaluate(x1), f2 = _function.Evaluate(x2);
                trace.Add(x0); trace.Add(x1); trace.Add(x2);
                while (trace.Count < limit && !IsTraceFinished(x2, f2))
                {
                    if (!TryMullerStep(x0, f0, x1, f1, x2, f2, out Complex next)) break;
                    x0 = x1; f0 = f1; x1 = x2; f1 = f2; x2 = next; f2 = _function.Evaluate(next);
                    trace.Add(x2);
                }
                break;
            }
            case BasinExplorerKind.Laguerre:
            {
                Complex z = point;
                Complex value = _function!.Evaluate(z);
                trace.Add(z);
                bool newton = LaguerreComparison == LaguerreComparisonMode.Newton;
                while (trace.Count < limit && !IsTraceFinished(z, value))
                {
                    Complex step;
                    if (newton)
                    {
                        Complex derivative = _firstDerivative!.Evaluate(z);
                        step = value / derivative;
                        if (!IsFinite(step)) break;
                    }
                    else if (!TryLaguerreStep(z, value, EffectiveLaguerreDegree, out step, out _)) break;
                    z -= step;
                    value = _function.Evaluate(z);
                    trace.Add(z);
                }
                break;
            }
            case BasinExplorerKind.Secant:
            {
                if (!ViewIsComplexPlane) return [];
                (Complex x0, Complex x1) = SecantSeeds(planeX, planeY);
                Complex f0 = _function!.Evaluate(x0), f1 = _function.Evaluate(x1);
                trace.Add(x0); trace.Add(x1);
                while (trace.Count < limit && !IsTraceFinished(x1, f1))
                {
                    Complex denominator = f1 - f0;
                    if (denominator == Complex.Zero) break;
                    Complex next = x1 - f1 * (x1 - x0) / denominator;
                    if (!IsFinite(next)) break;
                    x0 = x1; f0 = f1; x1 = next; f1 = _function.Evaluate(next);
                    trace.Add(x1);
                }
                break;
            }
            default:
                return TraceMapOrbit(point, limit);
        }
        return trace;
    }

    private bool IsTraceFinished(Complex z, Complex value) =>
        !IsFinite(z) || !IsFinite(value) || value == Complex.Zero ||
        Math.Abs(z.Real) > RootEscapeRadius || Math.Abs(z.Imaginary) > RootEscapeRadius ||
        NearestRootWithinTolerance(z, out _) >= 0;

    #endregion
}
