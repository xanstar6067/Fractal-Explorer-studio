using System.Numerics;
using System.Text;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed class NewtonPoolsEngine
{
    private const double DiagnosticEscapeRadius = 1e6;
    private const double DerivativeZeroTolerance = 1e-14;
    private ExpressionNode? _formula;
    private ExpressionNode? _firstDerivative;
    private ExpressionNode? _secondDerivative;
    private CompiledComplexExpression? _compiledFormula;
    private CompiledComplexExpression? _compiledFirstDerivative;
    private CompiledComplexExpression? _compiledSecondDerivative;
    private readonly List<ExpressionNode> _inverseDerivatives = [];
    private readonly List<CompiledComplexExpression> _compiledInverseDerivatives = [];
    private int _householderOrder = 3;
    private NewtonIterationMethod _iterationMethod;
    private double _rootTolerance = 1e-6;
    private double _rootSearchRadius = 8;

    public int MaxIterations { get; set; } = 500;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Scale { get; set; } = 3;
    public IReadOnlyList<Complex> Roots { get; private set; } = [];
    public Color[] RootColors { get; set; } = [];
    public Color BackgroundColor { get; set; } = Colors.Black;
    public bool UseGradient { get; set; }
    public NewtonDiagnosticColoringMode DiagnosticColoringMode { get; set; }
    public NewtonRelaxedPlaneMode RelaxedPlaneMode { get; set; }
    public Complex Relaxation { get; set; } = Complex.One;
    public Complex FixedInitialZ { get; set; } = new(0.5, 0.5);
    public NewtonIterationMethod IterationMethod
    {
        get => _iterationMethod;
        set
        {
            if (_iterationMethod == value) return;
            _iterationMethod = value;
            if (_formula is null) return;
            if (value == NewtonIterationMethod.Householder) BuildInverseDerivatives();
            else ClearInverseDerivatives();
        }
    }
    public NewtonRootSearchMode RootSearchMode { get; set; }
    public string RootSearchStrategy { get; private set; } = "Не запускался";

    public double RootTolerance
    {
        get => _rootTolerance;
        set => _rootTolerance = Math.Clamp(value, 1e-12, 0.1);
    }

    public double RootSearchRadius
    {
        get => _rootSearchRadius;
        set => _rootSearchRadius = Math.Clamp(value, 0.01, 1e9);
    }

    public int HouseholderOrder
    {
        get => _householderOrder;
        set
        {
            _householderOrder = Math.Clamp(value, 2, 12);
            if (_formula is not null && IterationMethod == NewtonIterationMethod.Householder) BuildInverseDerivatives();
        }
    }

    public bool SetFormula(string expression, out string debugInfo, bool discoverRoots = true)
    {
        var debug = new StringBuilder();
        try
        {
            List<Token> tokens = new Tokenizer(expression).Tokenize();
            _formula = new Parser(tokens).Parse().Simplify();
            _firstDerivative = _formula.Differentiate("z").Simplify();
            _secondDerivative = _firstDerivative.Differentiate("z").Simplify();
            _compiledFormula = CompiledComplexExpression.Compile(_formula);
            _compiledFirstDerivative = CompiledComplexExpression.Compile(_firstDerivative);
            _compiledSecondDerivative = CompiledComplexExpression.Compile(_secondDerivative);
            if (IterationMethod == NewtonIterationMethod.Householder) BuildInverseDerivatives();
            else ClearInverseDerivatives();
            if (discoverRoots) FindRoots();
            else
            {
                Roots = [];
                RootSearchStrategy = "Использован сохранённый список";
            }

            debug.AppendLine($"Источник: {expression}");
            debug.AppendLine("Токены: " + string.Join(" ", tokens.Select(token => $"[{token.Type}:{token.Value}]")));
            debug.AppendLine($"f(z) = {_formula}");
            debug.AppendLine($"f'(z) = {_firstDerivative}");
            debug.AppendLine($"f''(z) = {_secondDerivative}");
            debug.AppendLine($"Байткод: f={_compiledFormula.InstructionCount} инструкций, " +
                             $"f'={_compiledFirstDerivative.InstructionCount}, f''={_compiledSecondDerivative.InstructionCount}; " +
                             $"макс. стек={Math.Max(_compiledFormula.MaxStackDepth, Math.Max(_compiledFirstDerivative.MaxStackDepth, _compiledSecondDerivative.MaxStackDepth))}");
            debug.AppendLine($"Найдено корней: {Roots.Count}");
            debug.AppendLine($"Поиск: {RootSearchStrategy}");
            debug.AppendLine($"Точность: {RootTolerance:G3}; радиус адаптивного поиска: {RootSearchRadius:G6}");
            debugInfo = debug.ToString();
            return true;
        }
        catch (Exception ex)
        {
            _formula = null;
            _firstDerivative = null;
            _secondDerivative = null;
            _compiledFormula = null;
            _compiledFirstDerivative = null;
            _compiledSecondDerivative = null;
            ClearInverseDerivatives();
            Roots = [];
            RootSearchStrategy = "Ошибка формулы";
            debugInfo = $"ОШИБКА ПАРСИНГА:{Environment.NewLine}{ex.Message}";
            return false;
        }
    }

    public void ReplaceRoots(IEnumerable<Complex> roots)
    {
        double mergeDistance = Math.Max(1e-10, RootTolerance * 0.05);
        var accepted = new List<Complex>();
        foreach (Complex candidate in roots)
        {
            if (!IsFinite(candidate)) continue;
            int existing = accepted.FindIndex(root => (root - candidate).Magnitude <= mergeDistance);
            if (existing < 0) accepted.Add(candidate);
            else accepted[existing] = (accepted[existing] + candidate) / 2;
        }

        Roots = accepted.OrderBy(root => root.Real).ThenBy(root => root.Imaginary).ToArray();
    }

    public void RenderToBuffer(
        byte[] buffer,
        int width,
        int height,
        int stride,
        int threadCount,
        CancellationToken cancellationToken,
        Action<int>? reportProgress = null)
    {
        if (width <= 0 || height <= 0 || stride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(width));

        bool diagnosticsEnabled = DiagnosticColoringMode != NewtonDiagnosticColoringMode.Disabled;
        if (_compiledFormula is null || _compiledFirstDerivative is null || (!diagnosticsEnabled && Roots.Count == 0))
        {
            Fill(buffer, width, height, stride, BackgroundColor);
            reportProgress?.Invoke(100);
            return;
        }

        if (diagnosticsEnabled)
            RenderDiagnosticToBuffer(buffer, width, height, stride, threadCount, cancellationToken, reportProgress);
        else
            RenderNormalToBuffer(buffer, width, height, stride, threadCount, cancellationToken, reportProgress);
    }

    private void RenderNormalToBuffer(
        byte[] buffer,
        int width,
        int height,
        int stride,
        int threadCount,
        CancellationToken cancellationToken,
        Action<int>? reportProgress)
    {
        RenderRows(buffer, width, height, stride, threadCount, cancellationToken, reportProgress, diagnostics: false);
    }

    private void RenderDiagnosticToBuffer(
        byte[] buffer,
        int width,
        int height,
        int stride,
        int threadCount,
        CancellationToken cancellationToken,
        Action<int>? reportProgress)
    {
        RenderRows(buffer, width, height, stride, threadCount, cancellationToken, reportProgress, diagnostics: true);
    }

    private void RenderRows(
        byte[] buffer,
        int width,
        int height,
        int stride,
        int threadCount,
        CancellationToken cancellationToken,
        Action<int>? reportProgress,
        bool diagnostics)
    {

        long completedRows = 0;
        double unitsPerPixel = Scale / width;
        bool lambdaPlane = UsesLambdaParameterPlane;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, threadCount)
        };

        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (cancellationToken.IsCancellationRequested) { loopState.Stop(); return; }
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && cancellationToken.IsCancellationRequested) { loopState.Stop(); return; }
                Complex coordinate = new(
                    CenterX + (x - width / 2.0) * unitsPerPixel,
                    CenterY - (y - height / 2.0) * unitsPerPixel);
                Complex z = lambdaPlane ? FixedInitialZ : coordinate;
                Complex lambda = lambdaPlane ? coordinate : Relaxation;
                Color color;
                if (diagnostics)
                {
                    NewtonOrbitResult result = DiagnoseOrbit(z, lambda);
                    color = GetDiagnosticColor(result);
                }
                else
                {
                    int iteration = IterateNormal(ref z, lambda);
                    color = GetPixelColor(z, iteration);
                }
                int offset = row + x * 4;
                WriteColor(buffer, offset, color);
            }

            int rows = (int)Interlocked.Increment(ref completedRows);
            if (rows == height || rows % Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(rows * 100 / height);
        });
    }

    public byte[]? RenderTile(MandelbrotRenderTile tile, int canvasWidth, int canvasHeight, CancellationToken token)
    {
        byte[] buffer = new byte[checked(tile.Width * tile.Height * 4)];
        bool diagnosticsEnabled = DiagnosticColoringMode != NewtonDiagnosticColoringMode.Disabled;
        if (_compiledFormula is null || _compiledFirstDerivative is null || (!diagnosticsEnabled && Roots.Count == 0))
        {
            Fill(buffer, tile.Width, tile.Height, tile.Width * 4, BackgroundColor);
            return buffer;
        }

        return diagnosticsEnabled
            ? RenderDiagnosticTile(buffer, tile, canvasWidth, canvasHeight, token)
            : RenderNormalTile(buffer, tile, canvasWidth, canvasHeight, token);
    }

    private byte[]? RenderNormalTile(
        byte[] buffer,
        MandelbrotRenderTile tile,
        int canvasWidth,
        int canvasHeight,
        CancellationToken token) => RenderTileCore(buffer, tile, canvasWidth, canvasHeight, token, diagnostics: false);

    private byte[]? RenderDiagnosticTile(
        byte[] buffer,
        MandelbrotRenderTile tile,
        int canvasWidth,
        int canvasHeight,
        CancellationToken token) => RenderTileCore(buffer, tile, canvasWidth, canvasHeight, token, diagnostics: true);

    private byte[]? RenderTileCore(
        byte[] buffer,
        MandelbrotRenderTile tile,
        int canvasWidth,
        int canvasHeight,
        CancellationToken token,
        bool diagnostics)
    {
        double unitsPerPixel = Scale / canvasWidth;
        bool lambdaPlane = UsesLambdaParameterPlane;
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            int canvasY = tile.Y + localY;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 31) == 0 && token.IsCancellationRequested) return null;
                int canvasX = tile.X + localX;
                Complex coordinate = new(
                    CenterX + (canvasX - canvasWidth / 2.0) * unitsPerPixel,
                    CenterY - (canvasY - canvasHeight / 2.0) * unitsPerPixel);
                Complex z = lambdaPlane ? FixedInitialZ : coordinate;
                Complex lambda = lambdaPlane ? coordinate : Relaxation;
                Color color;
                if (diagnostics)
                {
                    NewtonOrbitResult result = DiagnoseOrbit(z, lambda);
                    color = GetDiagnosticColor(result);
                }
                else
                {
                    int iteration = IterateNormal(ref z, lambda);
                    color = GetPixelColor(z, iteration);
                }
                int offset = (localY * tile.Width + localX) * 4;
                WriteColor(buffer, offset, color);
            }
        }
        return buffer;
    }

    private bool UsesLambdaParameterPlane =>
        IterationMethod == NewtonIterationMethod.RelaxedNewton && RelaxedPlaneMode == NewtonRelaxedPlaneMode.LambdaPlane;

    private int IterateNormal(ref Complex z, Complex lambda)
    {
        int iteration = 0;
        double toleranceSquared = RootTolerance * RootTolerance;
        while (iteration < MaxIterations)
        {
            Complex f = _compiledFormula!.Evaluate(z);
            if (!IsFinite(f) || f == Complex.Zero) break;
            if ((MagnitudeSquared(f) <= toleranceSquared || (iteration & 7) == 7) && IsNearKnownRoot(z)) break;

            Complex step = IterationMethod switch
            {
                NewtonIterationMethod.Halley => ComputeHalleyStep(z, f),
                NewtonIterationMethod.Householder => ComputeHouseholderStep(z),
                NewtonIterationMethod.RelaxedNewton => ComputeRelaxedNewtonStep(z, f, lambda),
                _ => ComputeNewtonStep(z, f)
            };

            if (!IsFinite(step) || step == Complex.Zero) break;
            z += step;
            iteration++;
        }
        return iteration;
    }

    public NewtonOrbitResult DiagnoseOrbit(Complex initialPoint) => DiagnoseOrbit(initialPoint, Relaxation);

    public NewtonOrbitResult DiagnoseOrbit(Complex initialPoint, Complex lambda)
    {
        if (_compiledFormula is null || _compiledFirstDerivative is null)
            throw new InvalidOperationException("Сначала задайте корректную формулу.");

        Span<Complex> history = stackalloc Complex[16];
        int historyCount = 0;
        int historyNext = 0;
        Complex z = initialPoint;
        Complex lastValue = new(double.NaN, double.NaN);
        AddHistory(history, ref historyCount, ref historyNext, z);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            if (!IsFinite(z))
                return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, lastValue);
            if (IsEscaped(z))
                return CreateOrbitResult(NewtonOrbitOutcome.Escaped, iteration, z, lastValue);

            Complex f = _compiledFormula.Evaluate(z);
            lastValue = f;
            if (!IsFinite(f))
                return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, f);

            int rootIndex = FindKnownRootIndex(z);
            if (f == Complex.Zero || MagnitudeSquared(f) <= RootTolerance * RootTolerance || rootIndex >= 0)
                return CreateOrbitResult(NewtonOrbitOutcome.ConvergedToRoot, iteration, z, f, rootIndex);

            int cyclePeriod = DetectCycle(history, historyCount, historyNext);
            if (cyclePeriod is >= 2 and <= 8)
                return CreateOrbitResult(NewtonOrbitOutcome.Cycle, iteration, z, f, cyclePeriod: cyclePeriod);

            DiagnosticStep step = ComputeDiagnosticStep(z, f, lambda);
            if (step.Status == DiagnosticStepStatus.NonFinite)
                return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, z, f);
            if (step.Status == DiagnosticStepStatus.ZeroDerivative)
                return CreateOrbitResult(NewtonOrbitOutcome.ZeroDerivative, iteration, z, f);

            z += step.Value;
            AddHistory(history, ref historyCount, ref historyNext, z);
        }

        if (!IsFinite(z)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, MaxIterations, z, lastValue);
        if (IsEscaped(z)) return CreateOrbitResult(NewtonOrbitOutcome.Escaped, MaxIterations, z, lastValue);

        Complex finalValue = _compiledFormula.Evaluate(z);
        if (!IsFinite(finalValue)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, MaxIterations, z, finalValue);
        int finalRootIndex = FindKnownRootIndex(z);
        if (finalValue == Complex.Zero || MagnitudeSquared(finalValue) <= RootTolerance * RootTolerance || finalRootIndex >= 0)
            return CreateOrbitResult(NewtonOrbitOutcome.ConvergedToRoot, MaxIterations, z, finalValue, finalRootIndex);
        int finalCyclePeriod = DetectCycle(history, historyCount, historyNext);
        return finalCyclePeriod is >= 2 and <= 8
            ? CreateOrbitResult(NewtonOrbitOutcome.Cycle, MaxIterations, z, finalValue, cyclePeriod: finalCyclePeriod)
            : CreateOrbitResult(NewtonOrbitOutcome.IterationLimit, MaxIterations, z, finalValue);
    }

    private DiagnosticStep ComputeDiagnosticStep(Complex z, Complex f, Complex lambda)
    {
        switch (IterationMethod)
        {
            case NewtonIterationMethod.Halley:
            {
                Complex first = _compiledFirstDerivative!.Evaluate(z);
                if (!IsFinite(first)) return DiagnosticStep.NonFinite(first);
                if (IsEffectivelyZero(first)) return DiagnosticStep.ZeroDerivative;
                Complex second = _compiledSecondDerivative!.Evaluate(z);
                if (!IsFinite(second)) return DiagnosticStep.NonFinite(second);
                Complex denominator = 2 * first * first - f * second;
                if (!IsFinite(denominator)) return DiagnosticStep.NonFinite(denominator);
                if (IsEffectivelyZero(denominator)) return DiagnosticStep.ZeroDerivative;
                Complex value = -(2 * f * first) / denominator;
                return IsFinite(value) ? DiagnosticStep.Success(value) : DiagnosticStep.NonFinite(value);
            }
            case NewtonIterationMethod.Householder:
            {
                int order = HouseholderOrder;
                if (_compiledInverseDerivatives.Count <= order) return DiagnosticStep.ZeroDerivative;
                Complex previous = _compiledInverseDerivatives[order - 1].Evaluate(z);
                Complex current = _compiledInverseDerivatives[order].Evaluate(z);
                if (!IsFinite(previous)) return DiagnosticStep.NonFinite(previous);
                if (!IsFinite(current)) return DiagnosticStep.NonFinite(current);
                if (IsEffectivelyZero(current)) return DiagnosticStep.ZeroDerivative;
                Complex value = order * previous / current;
                return IsFinite(value) ? DiagnosticStep.Success(value) : DiagnosticStep.NonFinite(value);
            }
            case NewtonIterationMethod.RelaxedNewton:
            {
                Complex derivative = _compiledFirstDerivative!.Evaluate(z);
                if (!IsFinite(derivative)) return DiagnosticStep.NonFinite(derivative);
                if (IsEffectivelyZero(derivative)) return DiagnosticStep.ZeroDerivative;
                Complex value = -lambda * f / derivative;
                return IsFinite(value) ? DiagnosticStep.Success(value) : DiagnosticStep.NonFinite(value);
            }
            default:
            {
                Complex derivative = _compiledFirstDerivative!.Evaluate(z);
                if (!IsFinite(derivative)) return DiagnosticStep.NonFinite(derivative);
                if (IsEffectivelyZero(derivative)) return DiagnosticStep.ZeroDerivative;
                Complex value = -f / derivative;
                return IsFinite(value) ? DiagnosticStep.Success(value) : DiagnosticStep.NonFinite(value);
            }
        }
    }

    private int DetectCycle(Span<Complex> history, int historyCount, int historyNext)
    {
        double tolerance = Math.Clamp(RootTolerance * 4, 1e-10, 1e-4);
        if (historyCount < 4) return 0;

        Complex latest = GetRecent(history, historyNext, 0);
        Complex previous = GetRecent(history, historyNext, 1);
        if (AreClose(latest, previous, tolerance)) return 0;

        for (int period = 2; period <= 8; period++)
        {
            if (historyCount < period * 2) break;
            bool matches = true;
            for (int offset = 0; offset < period; offset++)
            {
                if (AreClose(
                        GetRecent(history, historyNext, offset),
                        GetRecent(history, historyNext, offset + period),
                        tolerance)) continue;
                matches = false;
                break;
            }
            if (matches) return period;
        }
        return 0;
    }

    private int FindKnownRootIndex(Complex z)
    {
        int nearest = -1;
        double nearestDistanceSquared = double.MaxValue;
        for (int index = 0; index < Roots.Count; index++)
        {
            double distanceSquared = MagnitudeSquared(z - Roots[index]);
            if (distanceSquared >= nearestDistanceSquared) continue;
            nearest = index;
            nearestDistanceSquared = distanceSquared;
        }
        return nearestDistanceSquared <= RootTolerance * RootTolerance ? nearest : -1;
    }

    private static void AddHistory(Span<Complex> history, ref int count, ref int next, Complex value)
    {
        history[next] = value;
        next = (next + 1) % history.Length;
        if (count < history.Length) count++;
    }

    private static Complex GetRecent(Span<Complex> history, int next, int offset)
    {
        int index = next - 1 - offset;
        while (index < 0) index += history.Length;
        return history[index];
    }

    private static bool AreClose(Complex left, Complex right, double tolerance)
    {
        double maxMagnitudeSquared = Math.Max(MagnitudeSquared(left), MagnitudeSquared(right));
        double scale = maxMagnitudeSquared > 1 ? Math.Sqrt(maxMagnitudeSquared) : 1;
        double threshold = tolerance * scale;
        return MagnitudeSquared(left - right) <= threshold * threshold;
    }

    private static bool IsEffectivelyZero(Complex value) => MagnitudeSquared(value) <= DerivativeZeroTolerance * DerivativeZeroTolerance;

    private static double MagnitudeSquared(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;

    private static bool IsEscaped(Complex value) =>
        Math.Abs(value.Real) > DiagnosticEscapeRadius || Math.Abs(value.Imaginary) > DiagnosticEscapeRadius;

    private static NewtonOrbitResult CreateOrbitResult(
        NewtonOrbitOutcome outcome,
        int iterations,
        Complex point,
        Complex value,
        int rootIndex = -1,
        int cyclePeriod = 0) => new(
        outcome,
        iterations,
        point,
        value,
        IsFinite(value) ? value.Magnitude : double.PositiveInfinity,
        rootIndex,
        cyclePeriod);

    private Complex ComputeNewtonStep(Complex z, Complex f)
    {
        Complex f1 = _compiledFirstDerivative!.Evaluate(z);
        return !IsFinite(f1) || f1 == Complex.Zero ? Complex.Zero : -f / f1;
    }

    private Complex ComputeRelaxedNewtonStep(Complex z, Complex f, Complex lambda)
    {
        Complex derivative = _compiledFirstDerivative!.Evaluate(z);
        return !IsFinite(derivative) || derivative == Complex.Zero
            ? Complex.Zero
            : -lambda * f / derivative;
    }

    private Complex ComputeHalleyStep(Complex z, Complex f)
    {
        Complex f1 = _compiledFirstDerivative!.Evaluate(z);
        if (!IsFinite(f1) || f1 == Complex.Zero) return Complex.Zero;
        Complex f2 = _compiledSecondDerivative!.Evaluate(z);
        Complex denominator = 2 * f1 * f1 - f * f2;
        return !IsFinite(denominator) || denominator == Complex.Zero ? Complex.Zero : -(2 * f * f1) / denominator;
    }

    private Complex ComputeHouseholderStep(Complex z)
    {
        int order = HouseholderOrder;
        if (_compiledInverseDerivatives.Count <= order) return Complex.Zero;
        Complex previous = _compiledInverseDerivatives[order - 1].Evaluate(z);
        Complex current = _compiledInverseDerivatives[order].Evaluate(z);
        return !IsFinite(previous) || !IsFinite(current) || current == Complex.Zero
            ? Complex.Zero
            : order * previous / current;
    }

    private void BuildInverseDerivatives()
    {
        ClearInverseDerivatives();
        if (_formula is null) return;
        ExpressionNode current = new BinaryOpNode(new NumberNode(Complex.One), "/", _formula).Simplify();
        _inverseDerivatives.Add(current);
        _compiledInverseDerivatives.Add(CompiledComplexExpression.Compile(current));
        for (int index = 1; index <= Math.Max(2, HouseholderOrder); index++)
        {
            current = current.Differentiate("z").Simplify();
            _inverseDerivatives.Add(current);
            _compiledInverseDerivatives.Add(CompiledComplexExpression.Compile(current));
        }
    }

    private void ClearInverseDerivatives()
    {
        _inverseDerivatives.Clear();
        _compiledInverseDerivatives.Clear();
    }

    private void FindRoots()
    {
        if (_formula is null || _firstDerivative is null)
        {
            Roots = [];
            RootSearchStrategy = "Формула не задана";
            return;
        }

        if (RootSearchMode == NewtonRootSearchMode.ManualOnly)
        {
            Roots = [];
            RootSearchStrategy = "Только вручную";
            return;
        }

        if (RootSearchMode == NewtonRootSearchMode.Automatic &&
            NewtonRootFinder.TryFindPolynomialRoots(_formula, RootTolerance, out IReadOnlyList<Complex> polynomialRoots, out int degree))
        {
            Roots = polynomialRoots;
            RootSearchStrategy = degree > 0
                ? $"Aberth–Ehrlich, полином степени {degree}"
                : "Константный полином";
            return;
        }

        Roots = NewtonRootFinder.FindAdaptiveRoots(
            _compiledFormula!.Evaluate,
            _compiledFirstDerivative!.Evaluate,
            0,
            0,
            RootSearchRadius,
            RootTolerance);
        RootSearchStrategy = $"Адаптивное сканирование от 0, радиус {RootSearchRadius:G6}";
    }

    private bool IsNearKnownRoot(Complex z)
    {
        double toleranceSquared = RootTolerance * RootTolerance;
        foreach (Complex root in Roots)
            if (MagnitudeSquared(z - root) <= toleranceSquared) return true;
        return false;
    }

    private Color GetPixelColor(Complex z, int iteration)
    {
        if (RootColors.Length == 0 || Roots.Count == 0) return BackgroundColor;
        int rootIndex = -1;
        double distanceSquared = double.MaxValue;
        for (int index = 0; index < Roots.Count; index++)
        {
            double candidate = MagnitudeSquared(z - Roots[index]);
            if (candidate >= distanceSquared) continue;
            distanceSquared = candidate;
            rootIndex = index;
        }
        if (rootIndex < 0 || distanceSquared > RootTolerance * RootTolerance) return BackgroundColor;

        Color baseColor = RootColors[rootIndex % RootColors.Length];
        if (!UseGradient) return baseColor;
        double t = Math.Min(1, (double)iteration / Math.Max(1, MaxIterations));
        t = 1 - Math.Pow(1 - t, 2);
        return Lerp(baseColor, BackgroundColor, t);
    }

    private Color GetDiagnosticColor(NewtonOrbitResult result) => DiagnosticColoringMode switch
    {
        NewtonDiagnosticColoringMode.CyclesOnly => result.Outcome == NewtonOrbitOutcome.Cycle
            ? GetCycleColor(result.CyclePeriod)
            : BackgroundColor,
        NewtonDiagnosticColoringMode.Residual => GetResidualColor(result),
        NewtonDiagnosticColoringMode.FinalValuePhase => GetPhaseColor(result),
        _ => GetOutcomeColor(result)
    };

    private Color GetOutcomeColor(NewtonOrbitResult result) => result.Outcome switch
    {
        NewtonOrbitOutcome.ConvergedToRoot => result.RootIndex >= 0 && RootColors.Length > 0
            ? RootColors[result.RootIndex % RootColors.Length]
            : Color.FromRgb(70, 220, 120),
        NewtonOrbitOutcome.Cycle => GetCycleColor(result.CyclePeriod),
        NewtonOrbitOutcome.ZeroDerivative => Color.FromRgb(255, 214, 64),
        NewtonOrbitOutcome.Escaped => Color.FromRgb(41, 121, 255),
        NewtonOrbitOutcome.NonFinite => Color.FromRgb(255, 23, 104),
        _ => Color.FromRgb(117, 117, 117)
    };

    private static Color GetCycleColor(int period) => period switch
    {
        2 => Color.FromRgb(0, 229, 255),
        3 => Color.FromRgb(255, 0, 212),
        4 => Color.FromRgb(255, 145, 0),
        5 => Color.FromRgb(118, 255, 3),
        6 => Color.FromRgb(124, 77, 255),
        7 => Color.FromRgb(0, 191, 165),
        8 => Color.FromRgb(255, 23, 68),
        _ => Colors.White
    };

    private Color GetResidualColor(NewtonOrbitResult result)
    {
        if (result.Outcome == NewtonOrbitOutcome.NonFinite) return Color.FromRgb(255, 23, 104);
        if (!double.IsFinite(result.Residual)) return Color.FromRgb(255, 23, 104);
        double minimumLog = Math.Log10(Math.Max(1e-15, RootTolerance));
        const double maximumLog = 6;
        double logResidual = Math.Log10(Math.Max(1e-300, result.Residual));
        double normalized = Math.Clamp((logResidual - minimumLog) / Math.Max(1, maximumLog - minimumLog), 0, 1);
        return ColorFromHsv(240 * (1 - normalized), 0.95, 0.95);
    }

    private static Color GetPhaseColor(NewtonOrbitResult result)
    {
        if (result.Outcome == NewtonOrbitOutcome.NonFinite) return Color.FromRgb(255, 23, 104);
        if (!IsFinite(result.FinalValue)) return Color.FromRgb(255, 23, 104);
        if (result.FinalValue == Complex.Zero) return Colors.Black;
        double hue = (Math.Atan2(result.FinalValue.Imaginary, result.FinalValue.Real) + Math.PI) * 180 / Math.PI;
        return ColorFromHsv(hue, 0.95, 0.95);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        double chroma = value * saturation;
        double segment = hue / 60;
        double secondary = chroma * (1 - Math.Abs(segment % 2 - 1));
        (double red, double green, double blue) = segment switch
        {
            < 1 => (chroma, secondary, 0d),
            < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary),
            < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        double match = value - chroma;
        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static Color Lerp(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(start.A + (end.A - start.A) * amount),
            (byte)Math.Round(start.R + (end.R - start.R) * amount),
            (byte)Math.Round(start.G + (end.G - start.G) * amount),
            (byte)Math.Round(start.B + (end.B - start.B) * amount));
    }

    private static bool IsFinite(Complex value) =>
        double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private static void Fill(byte[] buffer, int width, int height, int stride, Color color)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = y * stride + x * 4;
            buffer[offset] = color.B;
            buffer[offset + 1] = color.G;
            buffer[offset + 2] = color.R;
            buffer[offset + 3] = color.A;
        }
    }

    private static void WriteColor(byte[] buffer, int offset, Color color)
    {
        buffer[offset] = color.B;
        buffer[offset + 1] = color.G;
        buffer[offset + 2] = color.R;
        buffer[offset + 3] = color.A;
    }

    private enum DiagnosticStepStatus
    {
        Success,
        ZeroDerivative,
        NonFinite
    }

    private readonly record struct DiagnosticStep(DiagnosticStepStatus Status, Complex Value)
    {
        public static DiagnosticStep Success(Complex value) => new(DiagnosticStepStatus.Success, value);
        public static DiagnosticStep ZeroDerivative => new(DiagnosticStepStatus.ZeroDerivative, Complex.Zero);
        public static DiagnosticStep NonFinite(Complex value) => new(DiagnosticStepStatus.NonFinite, value);
    }
}
