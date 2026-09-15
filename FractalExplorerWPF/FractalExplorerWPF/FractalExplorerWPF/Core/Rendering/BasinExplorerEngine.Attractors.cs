using System.Globalization;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Итерации отображения z → R(z): рациональные P/Q и произвольные f(z) с параметром c. Пиксель
/// раскрашивается по притягивающему циклу, в допуск точки которого попала орбита. Циклы ищутся
/// заранее: по теореме Фату каждый притягивающий цикл рационального отображения притягивает
/// критическую точку, поэтому основные затравки — критические точки, а сетка вокруг нуля страхует
/// трансцендентные функции и численно неудачные орбиты.
/// </summary>
public sealed partial class BasinExplorerEngine
{
    private const int MaxAttractorCount = 64;
    private const int DiscoveryGridSize = 20;

    private CompiledComplexExpression? _mapDerivative;
    private BasinAttractor[] _attractors = [];
    private int _infinityIndex = -1;
    private bool _mapsInfinityToFinite;
    private Complex _infinityImage;
    private bool? _infinityAttractingByDegrees;
    private bool _escapeActive = true;
    private double _cycleTolerance = 1e-6;
    private int _maxPeriod = 8;
    private double _attractorSearchRadius = 3;
    private double _escapeRadius = 1e6;
    private BasinInfinityHandling _infinityHandling;

    private int _captureCount;
    private double[] _captureRe = [];
    private double[] _captureIm = [];
    private double[] _captureToleranceSquared = [];
    private int[] _captureOwner = [];
    private int[] _capturePoint = [];
    private int[] _attractorStart = [];
    private double _captureMinRe, _captureMaxRe, _captureMinIm, _captureMaxIm;

    /// <summary>Значение параметра c; применяется при следующей установке формулы.</summary>
    public Complex ParameterC { get; set; }

    public int MaxPeriod
    {
        get => _maxPeriod;
        set => _maxPeriod = Math.Clamp(value, 1, 64);
    }

    /// <summary>Относительный допуск захвата орбиты точкой цикла (масштабируется на max(1, |q|)).</summary>
    public double CycleTolerance
    {
        get => _cycleTolerance;
        set
        {
            _cycleTolerance = Math.Clamp(value, 1e-12, 0.1);
            RebuildCaptureTable();
        }
    }

    public double AttractorSearchRadius
    {
        get => _attractorSearchRadius;
        set => _attractorSearchRadius = Math.Clamp(value, 0.01, 1e6);
    }

    public double EscapeRadius
    {
        get => _escapeRadius;
        set => _escapeRadius = Math.Clamp(value, 2, 1e150);
    }

    public BasinInfinityHandling InfinityHandling
    {
        get => _infinityHandling;
        set
        {
            if (_infinityHandling == value) return;
            _infinityHandling = value;
            if (IsReady && !IsRootMethod) ReplaceAttractors(_attractors);
        }
    }

    /// <summary>0 — раскрашиваются все периоды, иначе только циклы указанного периода.</summary>
    public int PeriodFilter { get; set; }

    public IReadOnlyList<BasinAttractor> Attractors => _attractors;
    public IReadOnlyList<Complex> CriticalPoints { get; private set; } = [];

    /// <summary>Короткое описание поведения бесконечности для панели окна.</summary>
    public string InfinityDescription { get; private set; } = string.Empty;

    public bool InfinityIsAttractor => _infinityIndex >= 0;

    #region Map setup

    /// <summary>Рациональное отображение R(z) = P(z)/Q(z); в P и Q можно использовать параметр c.</summary>
    public bool SetRationalMap(string numerator, string denominator, out string debugInfo, bool discoverAttractors = true)
    {
        try
        {
            ExpressionNode p = ParseWithParameter(numerator, ParameterC);
            ExpressionNode q = ParseWithParameter(denominator, ParameterC);
            if (q is NumberNode { Value: var constant } && constant == Complex.Zero)
                throw new InvalidOperationException("Знаменатель Q(z) тождественно равен нулю.");
            ExpressionNode map = new BinaryOpNode(p, "/", q).Simplify();
            bool polynomial = NewtonRootFinder.TryGetPolynomialCoefficients(p, out Complex[] pc) &
                              NewtonRootFinder.TryGetPolynomialCoefficients(q, out Complex[] qc);
            ConfigureMap(map, polynomial ? pc : null, polynomial ? qc : null, discoverAttractors,
                $"P(z) = {p}{Environment.NewLine}Q(z) = {q}");
            debugInfo = DebugInfo;
            return true;
        }
        catch (Exception ex)
        {
            ResetFormula();
            DebugInfo = debugInfo = $"ОШИБКА ПАРСИНГА:{Environment.NewLine}{ex.Message}";
            return false;
        }
    }

    /// <summary>Произвольное отображение f(z) с параметром c (режим периодических циклов).</summary>
    public bool SetMapFormula(string expression, out string debugInfo, bool discoverAttractors = true)
    {
        try
        {
            ExpressionNode map = ParseWithParameter(expression, ParameterC);
            Complex[]? numerator = null, denominator = null;
            if (NewtonRootFinder.TryGetPolynomialCoefficients(map, out Complex[] coefficients))
            {
                numerator = coefficients;
                denominator = [Complex.One];
            }
            else if (map is BinaryOpNode { Operator: "/" } division &&
                     NewtonRootFinder.TryGetPolynomialCoefficients(division.Left, out Complex[] top) &&
                     NewtonRootFinder.TryGetPolynomialCoefficients(division.Right, out Complex[] bottom))
            {
                numerator = top;
                denominator = bottom;
            }
            ConfigureMap(map, numerator, denominator, discoverAttractors, $"f(z) = {map}");
            debugInfo = DebugInfo;
            return true;
        }
        catch (Exception ex)
        {
            ResetFormula();
            DebugInfo = debugInfo = $"ОШИБКА ПАРСИНГА:{Environment.NewLine}{ex.Message}";
            return false;
        }
    }

    private void ConfigureMap(ExpressionNode map, Complex[]? numerator, Complex[]? denominator, bool discover, string header)
    {
        CompiledComplexExpression compiledMap = CompiledComplexExpression.Compile(map);
        CompiledComplexExpression? derivative = null;
        CompiledComplexExpression? secondDerivative = null;
        string derivativeText;
        try
        {
            ExpressionNode first = map.Differentiate("z").Simplify();
            derivative = CompiledComplexExpression.Compile(first);
            derivativeText = first.ToString();
            try { secondDerivative = CompiledComplexExpression.Compile(first.Differentiate("z").Simplify()); }
            catch { secondDerivative = null; }
        }
        catch
        {
            derivativeText = "символьно недоступна — используется численная производная";
        }

        _function = compiledMap;
        _mapDerivative = derivative;
        _firstDerivative = null;
        _secondDerivative = null;
        Roots = [];

        var debug = new StringBuilder();
        debug.AppendLine(header);
        debug.AppendLine($"R(z) = {map}");
        debug.AppendLine($"R'(z) = {derivativeText}");

        _mapsInfinityToFinite = false;
        _infinityImage = Complex.Zero;
        _infinityAttractingByDegrees = null;
        IReadOnlyList<Complex> critical = [];
        if (numerator is not null && denominator is not null)
        {
            Complex[] p = NewtonRootFinder.Trim(numerator);
            Complex[] q = NewtonRootFinder.Trim(denominator);
            if (q.Length == 1 && q[0] == Complex.Zero)
                throw new InvalidOperationException("Знаменатель Q(z) тождественно равен нулю.");
            int degreeP = p.Length == 1 && p[0] == Complex.Zero ? 0 : p.Length - 1;
            int degreeQ = q.Length - 1;
            PolynomialDegree = Math.Max(degreeP, degreeQ);
            debug.AppendLine($"Степень отображения: {PolynomialDegree} (deg P = {degreeP}, deg Q = {degreeQ})");

            if (degreeP >= degreeQ + 2)
            {
                _infinityAttractingByDegrees = true;
                InfinityDescription = "∞ — сверхпритягивающая неподвижная точка";
            }
            else if (degreeP == degreeQ + 1)
            {
                Complex multiplier = q[^1] / p[^1];
                _infinityAttractingByDegrees = multiplier.Magnitude < 1;
                InfinityDescription = $"∞ — неподвижная точка, |λ| = {multiplier.Magnitude.ToString("0.####", CultureInfo.InvariantCulture)}" +
                                      (multiplier.Magnitude < 1 ? " (притягивает)" : " (не притягивает)");
            }
            else
            {
                _mapsInfinityToFinite = true;
                _infinityAttractingByDegrees = false;
                _infinityImage = degreeP == degreeQ ? p[^1] / q[^1] : Complex.Zero;
                InfinityDescription = $"∞ переходит в R(∞) = {BasinExplorerFormatting.Complex(_infinityImage)}";
            }

            Complex[] criticalNumerator = PolySubtract(PolyMultiply(PolyDerivative(p), q), PolyMultiply(p, PolyDerivative(q)));
            if (criticalNumerator.Length > 1 &&
                NewtonRootFinder.TrySolvePolynomial(criticalNumerator, 1e-10, out IReadOnlyList<Complex> criticalRoots))
                critical = criticalRoots;
        }
        else
        {
            PolynomialDegree = 0;
            InfinityDescription = "Не рациональная функция: уход за радиус считается уходом на ∞";
            debug.AppendLine("Отображение не рациональное: критические точки ищутся адаптивно");
            if (derivative is not null) critical = FindCriticalPointsNumerically(derivative, secondDerivative);
        }

        CriticalPoints = critical.Count > 256 ? critical.Take(256).ToArray() : critical;
        debug.AppendLine(InfinityDescription);
        debug.AppendLine($"Критических точек: {CriticalPoints.Count}");

        // Сначала пустой список: он заново решает судьбу ∞ и радиус ухода для новой формулы —
        // поиск циклов на них опирается.
        SetAttractors([]);
        if (discover) DiscoverAttractors(null, keepExisting: false, includeDefaultSeeds: true);

        debug.AppendLine($"Аттракторов: {_attractors.Length}");
        for (int index = 0; index < _attractors.Length; index++) debug.AppendLine("  " + _attractors[index].Describe(index));
        DebugInfo = debug.ToString();
    }

    /// <summary>Решает, есть ли в списке ∞, по режиму и степеням P и Q.</summary>
    private bool ResolveInfinityAttractor() => InfinityHandling switch
    {
        BasinInfinityHandling.Attractor => true,
        BasinInfinityHandling.Escape => false,
        _ => Kind == BasinExplorerKind.RationalMap && _infinityAttractingByDegrees == true
    };

    /// <summary>
    /// Заменяет список аттракторов (например, сохранённым). Конечные циклы проверяются на
    /// корректность и дубли; ∞ добавляется в конец по текущему режиму, а не по списку.
    /// </summary>
    public void ReplaceAttractors(IEnumerable<BasinAttractor> attractors)
    {
        var cycles = new List<BasinAttractor>();
        foreach (BasinAttractor attractor in attractors)
        {
            if (attractor.IsInfinity || attractor.Points.Count == 0 || attractor.Points.Count != attractor.Period) continue;
            if (!attractor.Points.All(IsFinite)) continue;
            AddUniqueCycle(cycles, attractor.Clone());
        }
        SetAttractors(cycles);
    }

    private void SetAttractors(List<BasinAttractor> cycles)
    {
        cycles.Sort(CompareCycles);
        var all = new List<BasinAttractor>(cycles);
        _infinityIndex = -1;
        if (_function is not null && ResolveInfinityAttractor())
        {
            _infinityIndex = all.Count;
            all.Add(new BasinAttractor { IsInfinity = true, Period = 1, Multiplier = Complex.Zero });
        }
        _attractors = [.. all];
        _escapeActive = _infinityIndex >= 0 || !_mapsInfinityToFinite;
        RebuildCaptureTable();
    }

    private static int CompareCycles(BasinAttractor left, BasinAttractor right)
    {
        int period = left.Period.CompareTo(right.Period);
        if (period != 0) return period;
        Complex a = left.Points[0], b = right.Points[0];
        int real = a.Real.CompareTo(b.Real);
        return real != 0 ? real : a.Imaginary.CompareTo(b.Imaginary);
    }

    private void RebuildCaptureTable()
    {
        BasinAttractor[] attractors = _attractors;
        int count = attractors.Where(attractor => !attractor.IsInfinity).Sum(attractor => attractor.Points.Count);
        var re = new double[count];
        var im = new double[count];
        var toleranceSquared = new double[count];
        var owner = new int[count];
        var point = new int[count];
        var start = new int[attractors.Length];
        double minRe = double.PositiveInfinity, maxRe = double.NegativeInfinity;
        double minIm = double.PositiveInfinity, maxIm = double.NegativeInfinity;
        int cursor = 0;
        for (int index = 0; index < attractors.Length; index++)
        {
            start[index] = cursor;
            if (attractors[index].IsInfinity) continue;
            for (int j = 0; j < attractors[index].Points.Count; j++)
            {
                Complex q = attractors[index].Points[j];
                double tolerance = CycleTolerance * Math.Max(1, q.Magnitude);
                re[cursor] = q.Real;
                im[cursor] = q.Imaginary;
                toleranceSquared[cursor] = tolerance * tolerance;
                owner[cursor] = index;
                point[cursor] = j;
                minRe = Math.Min(minRe, q.Real - tolerance);
                maxRe = Math.Max(maxRe, q.Real + tolerance);
                minIm = Math.Min(minIm, q.Imaginary - tolerance);
                maxIm = Math.Max(maxIm, q.Imaginary + tolerance);
                cursor++;
            }
        }

        _captureRe = re;
        _captureIm = im;
        _captureToleranceSquared = toleranceSquared;
        _captureOwner = owner;
        _capturePoint = point;
        _attractorStart = start;
        _captureMinRe = minRe;
        _captureMaxRe = maxRe;
        _captureMinIm = minIm;
        _captureMaxIm = maxIm;
        _captureCount = count;
    }

    #endregion

    #region Orbit classification

    internal BasinOrbitResult ClassifyOrbit(Complex z0)
    {
        CompiledComplexExpression map = _function!;
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        double escapeSquared = EscapeRadius * EscapeRadius;
        bool escapeActive = _escapeActive;
        Complex z = z0;
        Complex previous = z0;
        int iteration = 0;

        while (true)
        {
            bool nonFinite = !IsFinite(z);
            if (nonFinite || (escapeActive && MagnitudeSquared(z) > escapeSquared))
            {
                if (_infinityIndex >= 0)
                    return new BasinOrbitResult(BasinOrbitOutcome.Converged, iteration,
                        SmoothEscape(previous, z, iteration), z, _infinityIndex, 0, 1);
                if (nonFinite && _mapsInfinityToFinite)
                {
                    // Полюс: орбита побывала в ∞ и на следующем шаге оказывается в R(∞).
                    previous = z;
                    z = _infinityImage;
                    iteration++;
                    if (iteration > MaxIterations) break;
                    continue;
                }
                return new BasinOrbitResult(nonFinite ? BasinOrbitOutcome.NonFinite : BasinOrbitOutcome.Escaped,
                    iteration, iteration, z);
            }

            if (TryCapture(z, out int captureIndex))
            {
                int owner = _captureOwner[captureIndex];
                int period = _attractors[owner].Period;
                double tolerance = Math.Sqrt(_captureToleranceSquared[captureIndex]);
                double distance = Math.Sqrt(MagnitudeSquared(z - new Complex(_captureRe[captureIndex], _captureIm[captureIndex])));
                double previousDistance = iteration > 0 ? DistanceToAttractor(previous, owner) : distance;
                double smooth = SmoothIteration(iteration, previousDistance, distance, tolerance);
                int phase = ((_capturePoint[captureIndex] - iteration) % period + period) % period;
                return new BasinOrbitResult(BasinOrbitOutcome.Converged, iteration, smooth, z, owner, phase, period);
            }

            if (iteration >= MaxIterations) break;
            AddHistory(history, ref historyCount, ref historyNext, z);
            previous = z;
            z = map.Evaluate(z);
            iteration++;
        }

        int cycle = DetectCycle(history, historyCount, historyNext, Math.Clamp(CycleTolerance * 4, 1e-10, 1e-3), 1);
        return cycle > 0
            ? new BasinOrbitResult(BasinOrbitOutcome.Cycle, MaxIterations, MaxIterations, z, CyclePeriod: cycle)
            : new BasinOrbitResult(BasinOrbitOutcome.IterationLimit, MaxIterations, MaxIterations, z);
    }

    private bool TryCapture(Complex z, out int captureIndex)
    {
        captureIndex = -1;
        if (_captureCount == 0) return false;
        double re = z.Real, im = z.Imaginary;
        if (re < _captureMinRe || re > _captureMaxRe || im < _captureMinIm || im > _captureMaxIm) return false;
        double[] captureRe = _captureRe, captureIm = _captureIm, toleranceSquared = _captureToleranceSquared;
        for (int index = 0; index < captureRe.Length; index++)
        {
            double dx = re - captureRe[index];
            double dy = im - captureIm[index];
            if (dx * dx + dy * dy > toleranceSquared[index]) continue;
            captureIndex = index;
            return true;
        }
        return false;
    }

    private double DistanceToAttractor(Complex z, int owner)
    {
        int start = _attractorStart[owner];
        int end = start + _attractors[owner].Points.Count;
        double best = double.PositiveInfinity;
        for (int index = start; index < end; index++)
        {
            double dx = z.Real - _captureRe[index];
            double dy = z.Imaginary - _captureIm[index];
            best = Math.Min(best, dx * dx + dy * dy);
        }
        return Math.Sqrt(best);
    }

    /// <summary>
    /// Плавный номер итерации ухода на ∞: доля шага, на которой log|z| пересёк log R, по локальному
    /// темпу роста log|z| (для zᵈ это log d, для линейного ухода — медленнее).
    /// </summary>
    private double SmoothEscape(Complex previous, Complex z, int iteration)
    {
        if (iteration <= 0) return 0;
        double logRadius = Math.Log(EscapeRadius);
        double logPrevious = 0.5 * Math.Log(MagnitudeSquared(previous));
        double logCurrent = IsFinite(z) ? 0.5 * Math.Log(MagnitudeSquared(z)) : double.NaN;
        if (!double.IsFinite(logCurrent) || !(logPrevious > 0) || !(logCurrent > logPrevious) || !(logRadius > logPrevious))
            return iteration;
        double growth = Math.Log(logCurrent / logPrevious);
        if (!(growth > 1e-12)) return iteration;
        return iteration - 1 + Math.Clamp(Math.Log(logRadius / logPrevious) / growth, 0, 1);
    }

    private Color AttractorResultColor(BasinOrbitResult result)
    {
        switch (ColoringMode)
        {
            case BasinColoringMode.OrbitOutcome:
                return OutcomeColor(result);
            case BasinColoringMode.IterationCount:
                return result.Outcome is BasinOrbitOutcome.Converged or BasinOrbitOutcome.Escaped or BasinOrbitOutcome.NonFinite
                    ? IterationHeatColor(result.SmoothIterations)
                    : BackgroundColor;
        }

        if (result.Outcome != BasinOrbitOutcome.Converged || result.TargetIndex < 0) return BackgroundColor;
        BasinAttractor attractor = _attractors[result.TargetIndex];
        if (PeriodFilter > 0 && (attractor.IsInfinity || attractor.Period != PeriodFilter)) return BackgroundColor;

        Color color = ColoringMode == BasinColoringMode.Period && !attractor.IsInfinity
            ? PeriodColor(attractor.Period)
            : TargetColor(result.TargetIndex);
        if (ColoringMode == BasinColoringMode.CyclePhase) color = PhaseTint(color, result.Phase, attractor.Period);
        return ColoringMode == BasinColoringMode.Basins ? color : ShadeBySpeed(color, result.SmoothIterations, attractor.Period);
    }

    /// <summary>
    /// Фаза орбиты внутри цикла периода p сдвигает оттенок на 90°·фаза/p: у каждого бассейна цикла
    /// видны p непосредственных компонент, по-разному окрашенных, а цвет остаётся узнаваемым
    /// (от красного — к оранжевому и жёлтому).
    /// </summary>
    private static Color PhaseTint(Color color, int phase, int period)
    {
        if (period <= 1 || phase <= 0) return color;
        (double hue, double saturation, double value) = ToHsv(color);
        if (saturation < 0.08) return Lerp(color, Color.FromRgb(40, 40, 40), 0.6 * phase / period);
        return ColorFromHsv(hue + 90.0 * phase / period, Math.Max(saturation, 0.55), Math.Max(value, 0.55));
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return (hue < 0 ? hue + 360 : hue, max == 0 ? 0 : delta / max, max);
    }

    private IReadOnlyList<Complex> TraceMapOrbit(Complex point, int limit)
    {
        var trace = new List<Complex> { point };
        Complex z = point;
        double escapeSquared = EscapeRadius * EscapeRadius;
        while (trace.Count < limit)
        {
            if (TryCapture(z, out _)) break;
            Complex next = _function!.Evaluate(z);
            if (!IsFinite(next))
            {
                if (_infinityIndex >= 0 || !_mapsInfinityToFinite) break;
                next = _infinityImage;
            }
            if (_escapeActive && MagnitudeSquared(next) > escapeSquared) break;
            z = next;
            trace.Add(z);
        }
        return trace;
    }

    #endregion

    #region Cycle discovery

    /// <summary>
    /// Ищет притягивающие циклы периодов 1…<see cref="MaxPeriod"/>. Затравки по умолчанию —
    /// критические точки, образ ∞, ноль (асимптотическое значение exp) и сетка в радиусе поиска;
    /// дополнительные затравки получают тот же увеличенный бюджет итераций, что и критические точки.
    /// </summary>
    /// <returns>Сколько новых циклов добавлено.</returns>
    public int DiscoverAttractors(IEnumerable<Complex>? extraSeeds, bool keepExisting, bool includeDefaultSeeds)
    {
        if (_function is null) return 0;
        var cycles = keepExisting
            ? _attractors.Where(attractor => !attractor.IsInfinity).Select(attractor => attractor.Clone()).ToList()
            : [];
        int before = cycles.Count;
        int intensiveBudget = Math.Clamp(MaxIterations * 25, 5_000, 60_000);
        int gridBudget = Math.Clamp(MaxIterations * 2, 400, 2_000);

        void Try(Complex seed, int budget)
        {
            if (cycles.Count >= MaxAttractorCount || !IsFinite(seed)) return;
            BasinAttractor? cycle = TryFindCycle(seed, budget);
            if (cycle is not null) AddUniqueCycle(cycles, cycle);
        }

        if (includeDefaultSeeds)
        {
            foreach (Complex critical in CriticalPoints) Try(critical, intensiveBudget);
            if (_mapsInfinityToFinite) Try(_infinityImage, intensiveBudget);
            Try(Complex.Zero, intensiveBudget);
        }
        if (extraSeeds is not null)
            foreach (Complex seed in extraSeeds) Try(seed, intensiveBudget);
        if (includeDefaultSeeds)
        {
            double radius = AttractorSearchRadius;
            for (int y = 0; y < DiscoveryGridSize; y++)
            for (int x = 0; x < DiscoveryGridSize; x++)
            {
                // Сдвиг на полшага: узлы не попадают на оси симметрии, где орбиты часто вырождаются.
                double re = -radius + 2 * radius * (x + 0.5) / DiscoveryGridSize;
                double im = -radius + 2 * radius * (y + 0.5) / DiscoveryGridSize;
                Try(new Complex(re, im), gridBudget);
            }
        }

        SetAttractors(cycles);
        return Math.Max(0, cycles.Count - before);
    }

    /// <summary>Затравки на сетке видимой области — для циклов, чьи бассейны далеко от нуля.</summary>
    public int DiscoverAttractorsInView(int canvasWidth, int canvasHeight, int gridSize = 16)
    {
        if (_function is null || canvasWidth <= 0 || canvasHeight <= 0) return 0;
        double unitsPerPixel = UnitsPerPixel(canvasWidth);
        var seeds = new List<Complex>(gridSize * gridSize);
        for (int y = 0; y < gridSize; y++)
        for (int x = 0; x < gridSize; x++)
        {
            double canvasX = (x + 0.5) * canvasWidth / gridSize;
            double canvasY = (y + 0.5) * canvasHeight / gridSize;
            seeds.Add(new Complex(
                CenterX + (canvasX - canvasWidth / 2.0) * unitsPerPixel,
                CenterY - (canvasY - canvasHeight / 2.0) * unitsPerPixel));
        }
        return DiscoverAttractors(seeds, keepExisting: true, includeDefaultSeeds: false);
    }

    internal BasinAttractor? TryFindCycle(Complex seed, int budget)
    {
        int maxPeriod = MaxPeriod;
        int windowLength = 2 * maxPeriod + 2;
        Span<Complex> window = stackalloc Complex[windowLength];
        Complex z = seed;
        int used = 0;
        while (used < budget)
        {
            int chunk = Math.Min(48, budget - used);
            for (int step = 0; step < chunk; step++)
                if (!TryDiscoveryStep(ref z)) return null;
            used += chunk;

            window[0] = z;
            for (int index = 1; index < windowLength; index++)
            {
                Complex next = window[index - 1];
                if (!TryDiscoveryStep(ref next)) return null;
                window[index] = next;
            }
            used += windowLength - 1;

            double scale = Math.Max(1, z.Magnitude);
            double detect = Math.Clamp(CycleTolerance * 1e3, 1e-7, 1e-2) * scale;
            double detectSquared = detect * detect;
            for (int period = 1; period <= maxPeriod; period++)
            {
                if (MagnitudeSquared(window[period] - window[0]) > detectSquared ||
                    MagnitudeSquared(window[period + 1] - window[1]) > detectSquared) continue;
                BasinAttractor? cycle = RefineCycle(window[0], period);
                if (cycle is not null) return cycle;
                break;
            }
            z = window[windowLength - 1];
        }
        return null;
    }

    private bool TryDiscoveryStep(ref Complex z)
    {
        Complex next = _function!.Evaluate(z);
        if (!IsFinite(next))
        {
            if (!_mapsInfinityToFinite || _infinityIndex >= 0) return false;
            next = _infinityImage;
        }
        if (_escapeActive && MagnitudeSquared(next) > EscapeRadius * EscapeRadius) return false;
        z = next;
        return true;
    }

    /// <summary>
    /// Уточняет цикл методом Ньютона для G(z) = Rᵖ(z) − z, G' = (Rᵖ)'(z) − 1, затем проверяет
    /// замыкание, минимальность периода и притяжение |λ| = |(Rᵖ)'| &lt; 1.
    /// </summary>
    private BasinAttractor? RefineCycle(Complex start, int period)
    {
        Complex z = start;
        double scale = Math.Max(1, start.Magnitude);
        bool refined = false;
        for (int round = 0; round < 64; round++)
        {
            if (!TryIterateWithDerivative(z, period, out Complex value, out Complex derivative)) break;
            Complex g = value - z;
            if (g == Complex.Zero) { refined = true; break; }
            Complex dg = derivative - Complex.One;
            if (!IsFinite(dg) || dg == Complex.Zero) break;
            Complex step = g / dg;
            if (!IsFinite(step)) break;
            double magnitude = step.Magnitude;
            if (magnitude > 0.1 * scale) step *= 0.1 * scale / magnitude;
            z -= step;
            if (magnitude <= 1e-14 * Math.Max(1, z.Magnitude)) { refined = true; break; }
        }

        if (!refined || !IsClosedCycle(z, period, 1e-9))
        {
            if (!IsClosedCycle(start, period, CycleTolerance)) return null;
            z = start;
        }

        var points = new List<Complex>(period) { z };
        for (int index = 1; index < period; index++)
        {
            Complex next = z;
            if (!TryIterateWithDerivative(points[index - 1], 1, out next, out _)) return null;
            points.Add(next);
        }

        double pointScale = Math.Max(1, z.Magnitude);
        for (int divisor = 1; divisor < period; divisor++)
        {
            if (period % divisor != 0) continue;
            if ((points[divisor] - points[0]).Magnitude > 1e-7 * pointScale) continue;
            period = divisor;
            points.RemoveRange(divisor, points.Count - divisor);
            break;
        }

        Complex multiplier = Complex.One;
        foreach (Complex point in points)
        {
            Complex derivative = MapDerivative(point);
            if (!IsFinite(derivative)) return null;
            multiplier *= derivative;
        }
        // Численная производная ошибается примерно на 1e-7, поэтому без символьной нейтральные
        // циклы (|λ| = 1) отсекаются с запасом.
        double attractionMargin = _mapDerivative is null ? 1e-5 : 1e-9;
        if (!IsFinite(multiplier) || multiplier.Magnitude >= 1 - attractionMargin) return null;

        int first = 0;
        double tieTolerance = 1e-9 * pointScale;
        for (int index = 1; index < points.Count; index++)
        {
            Complex candidate = points[index], best = points[first];
            if (candidate.Real < best.Real - tieTolerance ||
                (Math.Abs(candidate.Real - best.Real) <= tieTolerance && candidate.Imaginary < best.Imaginary))
                first = index;
        }
        List<Complex> ordered = [.. points.Skip(first), .. points.Take(first)];
        return new BasinAttractor { Period = period, Points = ordered, Multiplier = multiplier };
    }

    /// <summary>
    /// Нули R' для нерациональной функции: Ньютон для R' из узлов сетки в радиусе поиска. Дешевле
    /// общего адаптивного поиска корней, а критические точки здесь нужны только как затравки.
    /// </summary>
    private List<Complex> FindCriticalPointsNumerically(CompiledComplexExpression derivative, CompiledComplexExpression? secondDerivative)
    {
        const int grid = 12;
        var found = new List<Complex>();
        double radius = AttractorSearchRadius;
        for (int y = 0; y < grid; y++)
        for (int x = 0; x < grid; x++)
        {
            var z = new Complex(-radius + 2 * radius * (x + 0.5) / grid, -radius + 2 * radius * (y + 0.5) / grid);
            bool converged = false;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                Complex value = derivative.Evaluate(z);
                if (!IsFinite(value)) break;
                if (value == Complex.Zero) { converged = true; break; }
                Complex slope;
                if (secondDerivative is not null) slope = secondDerivative.Evaluate(z);
                else
                {
                    double h = 1e-7 * Math.Max(1, z.Magnitude);
                    slope = (derivative.Evaluate(z + h) - derivative.Evaluate(z - h)) / (2 * h);
                }
                Complex step = value / slope;
                if (!IsFinite(step)) break;
                z -= step;
                if (step.Magnitude <= 1e-13 * Math.Max(1, z.Magnitude)) { converged = true; break; }
            }
            if (!converged || !IsFinite(z) || z.Magnitude > 2 * radius) continue;
            Complex residual = derivative.Evaluate(z);
            if (!IsFinite(residual) || residual.Magnitude > 1e-8) continue;
            if (found.Any(point => AreClose(point, z, 1e-6))) continue;
            found.Add(z);
            if (found.Count >= 64) return found;
        }
        return found;
    }

    private bool IsClosedCycle(Complex z, int period, double relativeTolerance)
    {
        if (!TryIterateWithDerivative(z, period, out Complex value, out _)) return false;
        return (value - z).Magnitude <= relativeTolerance * Math.Max(1, z.Magnitude);
    }

    private bool TryIterateWithDerivative(Complex z, int count, out Complex value, out Complex derivative)
    {
        value = z;
        derivative = Complex.One;
        for (int index = 0; index < count; index++)
        {
            Complex local = MapDerivative(value);
            Complex next = _function!.Evaluate(value);
            if (!IsFinite(local) || !IsFinite(next)) return false;
            derivative *= local;
            value = next;
        }
        return IsFinite(derivative);
    }

    private Complex MapDerivative(Complex z)
    {
        if (_mapDerivative is not null) return _mapDerivative.Evaluate(z);
        double h = 1e-7 * Math.Max(1, z.Magnitude);
        return (_function!.Evaluate(z + h) - _function.Evaluate(z - h)) / (2 * h);
    }

    private static bool AddUniqueCycle(List<BasinAttractor> cycles, BasinAttractor candidate)
    {
        foreach (BasinAttractor existing in cycles)
        {
            if (existing.Period != candidate.Period) continue;
            if (existing.Points.Any(point => AreClose(point, candidate.Points[0], 1e-6))) return false;
        }
        if (cycles.Count >= MaxAttractorCount) return false;
        cycles.Add(candidate);
        return true;
    }

    #endregion

    #region Polynomial arithmetic

    private static Complex[] PolyDerivative(Complex[] coefficients)
    {
        if (coefficients.Length <= 1) return [Complex.Zero];
        var result = new Complex[coefficients.Length - 1];
        for (int index = 1; index < coefficients.Length; index++) result[index - 1] = index * coefficients[index];
        return NewtonRootFinder.Trim(result);
    }

    private static Complex[] PolyMultiply(Complex[] left, Complex[] right)
    {
        var result = new Complex[left.Length + right.Length - 1];
        for (int i = 0; i < left.Length; i++)
        for (int j = 0; j < right.Length; j++)
            result[i + j] += left[i] * right[j];
        return NewtonRootFinder.Trim(result);
    }

    private static Complex[] PolySubtract(Complex[] left, Complex[] right)
    {
        var result = new Complex[Math.Max(left.Length, right.Length)];
        for (int index = 0; index < left.Length; index++) result[index] += left[index];
        for (int index = 0; index < right.Length; index++) result[index] -= right[index];
        return NewtonRootFinder.Trim(result);
    }

    #endregion
}
