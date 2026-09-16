using System.Numerics;
using System.Text;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Движок исследователей бассейнов притяжения. Методы Мюллера, Лагерра и секущих
/// раскрашивают пиксель по корню, к которому сошлась орбита (частичный файл
/// <c>BasinExplorerEngine.RootMethods</c>); рациональные отображения и режим периодических
/// циклов — по конечному притягивающему циклу (<c>BasinExplorerEngine.Attractors</c>).
/// Логистическая карта добавляет плоскость параметра λ (.Logistic), физические модели —
/// численное интегрирование траекторий среди неподвижных центров (.Physics).
/// Вся арифметика — double: глубокого зума, как у Ньютона, здесь нет.
/// </summary>
public sealed partial class BasinExplorerEngine
{
    /// <summary>Ширина видимой области при зуме 1.</summary>
    public const double BaseViewWidth = 3.0;

    private const int HistoryCapacity = 16;
    private const double RootEscapeRadius = 1e10;
    private const double SpeedBrightnessFloor = 0.1;

    private CompiledComplexExpression? _function;
    private CompiledComplexExpression? _firstDerivative;
    private CompiledComplexExpression? _secondDerivative;
    private double _rootTolerance = 1e-6;
    private double _rootSearchRadius = 8;

    public BasinExplorerEngine(BasinExplorerKind kind) => Kind = kind;

    public BasinExplorerKind Kind { get; }
    public bool IsRootMethod => BasinExplorerCatalog.UsesRoots(Kind);
    public bool IsPhysical => BasinExplorerCatalog.UsesPhysics(Kind);
    public bool IsLogisticParameter => Kind == BasinExplorerKind.ComplexLogistic && LogisticPlane == LogisticPlaneMode.Parameter;
    public bool IsReady => IsPhysical ? _physics is not null : IsLogisticParameter || _function is not null;

    public int MaxIterations { get; set; } = 200;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Zoom { get; set; } = 1;

    public Color[] TargetColors { get; set; } = [];
    public Color BackgroundColor { get; set; } = Colors.Black;
    public BasinColoringMode ColoringMode { get; set; } = BasinColoringMode.ConvergenceSpeed;

    /// <summary>Число итераций (на период цикла), за которое яркость бассейна падает вдвое.</summary>
    public double ShadingScale { get; set; } = 3;

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

    public NewtonRootSearchMode RootSearchMode { get; set; }
    public IReadOnlyList<Complex> Roots { get; private set; } = [];
    public string RootSearchStrategy { get; private set; } = "Не запускался";

    /// <summary>Степень полинома f; 0, если формула не полином.</summary>
    public int PolynomialDegree { get; private set; }

    /// <summary>Нормализованный текст последней принятой формулы (для диагностики).</summary>
    public string DebugInfo { get; private set; } = string.Empty;

    /// <summary>Число цветов, которое нужно палитре: корни или аттракторы.</summary>
    public int TargetCount => IsPhysical ? Physics.Centers.Count : IsLogisticParameter ? MaxPeriod : IsRootMethod ? Roots.Count : Attractors.Count;

    #region Formula setup

    /// <summary>
    /// Формула метода поиска корней. Корни ищет тот же поисковик, что и у бассейнов Ньютона
    /// (Aberth–Ehrlich для полиномов, адаптивное сканирование для остальных функций).
    /// </summary>
    public bool SetRootFormula(string expression, out string debugInfo, bool discoverRoots = true)
    {
        var debug = new StringBuilder();
        try
        {
            var rootEngine = new NewtonPoolsEngine
            {
                RootTolerance = RootTolerance,
                RootSearchRadius = RootSearchRadius,
                RootSearchMode = RootSearchMode
            };
            if (!rootEngine.SetFormula(expression, out string rootDebug, discoverRoots))
                throw new InvalidOperationException(rootDebug);

            ExpressionNode formula = new Parser(new Tokenizer(expression).Tokenize()).Parse().Simplify();
            ExpressionNode first = formula.Differentiate("z").Simplify();
            ExpressionNode second = first.Differentiate("z").Simplify();
            _function = CompiledComplexExpression.Compile(formula);
            _firstDerivative = CompiledComplexExpression.Compile(first);
            _secondDerivative = CompiledComplexExpression.Compile(second);
            PolynomialDegree = NewtonRootFinder.TryGetPolynomialCoefficients(formula, out Complex[] coefficients)
                ? Math.Max(0, coefficients.Length - 1)
                : 0;
            Roots = rootEngine.Roots;
            RootSearchStrategy = rootEngine.RootSearchStrategy;

            debug.AppendLine($"Источник: {expression}");
            debug.AppendLine($"f(z) = {formula}");
            debug.AppendLine($"f'(z) = {first}");
            debug.AppendLine($"f''(z) = {second}");
            debug.AppendLine(PolynomialDegree > 0 ? $"Полином степени {PolynomialDegree}" : "Не полином: для Лагерра используется ручное n");
            debug.AppendLine($"Корней: {Roots.Count} · {RootSearchStrategy}");
            DebugInfo = debugInfo = debug.ToString();
            return true;
        }
        catch (Exception ex)
        {
            ResetFormula();
            RootSearchStrategy = "Ошибка формулы";
            DebugInfo = debugInfo = $"ОШИБКА ПАРСИНГА:{Environment.NewLine}{ex.Message}";
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
        if (RootSearchStrategy is "Не запускался") RootSearchStrategy = "Использован сохранённый список";
    }

    private void ResetFormula()
    {
        _function = null;
        _firstDerivative = null;
        _secondDerivative = null;
        _mapDerivative = null;
        PolynomialDegree = 0;
        Roots = [];
        CriticalPoints = [];
        _attractors = [];
        RebuildCaptureTable();
    }

    /// <summary>
    /// Разбирает выражение, подставляя значение параметра <paramref name="c"/> вместо переменной c.
    /// Кроме z и c допускаются только константы i, pi и e.
    /// </summary>
    private static ExpressionNode ParseWithParameter(string expression, Complex c)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new InvalidOperationException("Пустое выражение.");
        ExpressionNode parsed = new Parser(new Tokenizer(expression).Tokenize()).Parse();
        return Substitute(parsed, c).Simplify();
    }

    private static ExpressionNode Substitute(ExpressionNode node, Complex c) => node switch
    {
        VariableNode { Name: "c" } => new NumberNode(c),
        VariableNode { Name: "z" or "i" or "pi" or "e" } => node,
        VariableNode variable => throw new InvalidOperationException(
            $"Переменная '{variable.Name}' не поддерживается. Допустимы z, c, i, pi и e (умножение пишите явно: c*z)."),
        UnaryOpNode unary => new UnaryOpNode(unary.Operator, Substitute(unary.Operand, c)),
        BinaryOpNode binary => new BinaryOpNode(Substitute(binary.Left, c), binary.Operator, Substitute(binary.Right, c)),
        FunctionNode function => new FunctionNode(function.Name, Substitute(function.Argument, c)),
        _ => node
    };

    #endregion

    #region Rendering

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

        if (!HasAnythingToDraw)
        {
            Fill(buffer, width, height, stride, BackgroundColor);
            reportProgress?.Invoke(100);
            return;
        }

        long completedRows = 0;
        double unitsPerPixel = UnitsPerPixel(width);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threadCount), CancellationToken = cancellationToken };
        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (cancellationToken.IsCancellationRequested) { loopState.Stop(); return; }
            int row = y * stride;
            double planeY = CenterY - (y - height / 2.0) * unitsPerPixel;
            for (int x = 0; x < width; x++)
            {
                if ((x & 31) == 0 && cancellationToken.IsCancellationRequested) { loopState.Stop(); return; }
                double planeX = CenterX + (x - width / 2.0) * unitsPerPixel;
                WriteColor(buffer, row + x * 4, ComputeColor(planeX, planeY, cancellationToken));
            }

            int rows = (int)Interlocked.Increment(ref completedRows);
            if (rows == height || rows % Math.Max(1, height / 100) == 0)
                reportProgress?.Invoke(rows * 100 / height);
        });
    }

    public byte[]? RenderTile(MandelbrotRenderTile tile, int canvasWidth, int canvasHeight, CancellationToken token)
    {
        byte[] buffer = new byte[checked(tile.Width * tile.Height * 4)];
        if (!HasAnythingToDraw)
        {
            Fill(buffer, tile.Width, tile.Height, tile.Width * 4, BackgroundColor);
            return buffer;
        }

        double unitsPerPixel = UnitsPerPixel(canvasWidth);
        for (int localY = 0; localY < tile.Height; localY++)
        {
            if (token.IsCancellationRequested) return null;
            double planeY = CenterY - (tile.Y + localY - canvasHeight / 2.0) * unitsPerPixel;
            for (int localX = 0; localX < tile.Width; localX++)
            {
                if ((localX & 15) == 0 && token.IsCancellationRequested) return null;
                double planeX = CenterX + (tile.X + localX - canvasWidth / 2.0) * unitsPerPixel;
                WriteColor(buffer, (localY * tile.Width + localX) * 4, ComputeColor(planeX, planeY, token));
            }
        }
        return buffer;
    }

    /// <summary>
    /// Без корней (аттракторов) обычная раскраска даёт сплошной фон — тогда кадр заливается сразу,
    /// не прогоняя каждую орбиту до лимита итераций. Диагностика и тепловая карта считаются всегда.
    /// </summary>
    private bool HasAnythingToDraw =>
        IsReady && (TargetCount > 0 || ColoringMode is BasinColoringMode.OrbitOutcome or BasinColoringMode.IterationCount);

    private double UnitsPerPixel(int width) => BaseViewWidth / Math.Max(0.001, Zoom) / width;

    /// <summary>Цвет точки плоскости обзора (для секущих в режиме среза — точки среза).</summary>
    public Color ComputeColor(double planeX, double planeY, CancellationToken token = default)
    {
        if (IsPhysical) return PhysicalResultColor(PhysicalOrbit(planeX, planeY, token));
        if (IsLogisticParameter) return LogisticParameterColor(LogisticParameterOrbit(new Complex(planeX, planeY), token));
        switch (Kind)
        {
            case BasinExplorerKind.Laguerre when LaguerreComparison == LaguerreComparisonMode.Disagreement:
                return DisagreementColor(new Complex(planeX, planeY));
            case BasinExplorerKind.Muller:
            case BasinExplorerKind.Laguerre:
            case BasinExplorerKind.Secant:
                return RootResultColor(AnalyzePoint(planeX, planeY));
            default:
                return AttractorResultColor(ClassifyOrbit(new Complex(planeX, planeY)));
        }
    }

    /// <summary>Итог орбиты для точки плоскости обзора — для подсказки под курсором и проверок.</summary>
    public BasinOrbitResult AnalyzePoint(double planeX, double planeY)
    {
        if (IsPhysical) return PhysicalOrbit(planeX, planeY);
        if (IsLogisticParameter) return LogisticParameterOrbit(new Complex(planeX, planeY));
        var point = new Complex(planeX, planeY);
        switch (Kind)
        {
            case BasinExplorerKind.Muller:
            {
                (Complex x0, Complex x1, Complex x2) = MullerSeeds(point);
                return MullerOrbit(x0, x1, x2);
            }
            case BasinExplorerKind.Laguerre:
                return LaguerreComparison == LaguerreComparisonMode.Newton ? NewtonOrbit(point) : LaguerreOrbit(point);
            case BasinExplorerKind.Secant:
            {
                (Complex x0, Complex x1) = SecantSeeds(planeX, planeY);
                return SecantOrbit(x0, x1);
            }
            default:
                return ClassifyOrbit(point);
        }
    }

    #endregion

    #region Coloring

    private Color TargetColor(int index) =>
        index >= 0 && TargetColors.Length > 0 ? TargetColors[index % TargetColors.Length] : BackgroundColor;

    private Color RootResultColor(BasinOrbitResult result)
    {
        switch (ColoringMode)
        {
            case BasinColoringMode.OrbitOutcome:
                return OutcomeColor(result);
            case BasinColoringMode.IterationCount:
                return result.Outcome is BasinOrbitOutcome.Converged or BasinOrbitOutcome.Escaped
                    ? IterationHeatColor(result.SmoothIterations)
                    : BackgroundColor;
            case BasinColoringMode.Basins:
                return result.Outcome == BasinOrbitOutcome.Converged && result.TargetIndex >= 0
                    ? TargetColor(result.TargetIndex)
                    : BackgroundColor;
            default:
                return result.Outcome == BasinOrbitOutcome.Converged && result.TargetIndex >= 0
                    ? ShadeBySpeed(TargetColor(result.TargetIndex), result.SmoothIterations)
                    : BackgroundColor;
        }
    }

    private Color OutcomeColor(BasinOrbitResult result) => result.Outcome switch
    {
        BasinOrbitOutcome.Converged => result.TargetIndex >= 0 && TargetColors.Length > 0
            ? TargetColor(result.TargetIndex)
            : Color.FromRgb(70, 220, 120),
        BasinOrbitOutcome.Cycle => CycleDiagnosticColor(result.CyclePeriod),
        BasinOrbitOutcome.Degenerate => Color.FromRgb(255, 214, 64),
        BasinOrbitOutcome.Escaped => Color.FromRgb(41, 121, 255),
        BasinOrbitOutcome.NonFinite => Color.FromRgb(255, 23, 104),
        _ => Color.FromRgb(117, 117, 117)
    };

    /// <summary>
    /// Яркость по скорости сходимости: вдвое тусклее за <see cref="ShadingScale"/> итераций на
    /// период цикла, дальше — логарифмически, поэтому и быстрые, и медленные бассейны сохраняют
    /// различимые полосы. Итерации делятся на период: цикл периода p приближается к себе за p шагов.
    /// </summary>
    private Color ShadeBySpeed(Color color, double smoothIterations, int period = 1)
    {
        double scale = Math.Max(0.1, ShadingScale) * Math.Max(1, period);
        double decay = 1 + Math.Log2(1 + Math.Max(0, smoothIterations) / scale);
        double brightness = SpeedBrightnessFloor + (1 - SpeedBrightnessFloor) / decay;
        return Lerp(BackgroundColor, color, brightness);
    }

    private Color IterationHeatColor(double smoothIterations)
    {
        double t = Math.Clamp(Math.Log(1 + Math.Max(0, smoothIterations)) / Math.Log(1 + Math.Max(1, MaxIterations)), 0, 1);
        return ColorFromHsv(240 * (1 - t), 0.9, 0.95);
    }

    /// <summary>Цвета периодов 2–8 совпадают с диагностикой бассейнов Ньютона.</summary>
    private static Color CycleDiagnosticColor(int period) => period switch
    {
        1 => Color.FromRgb(255, 255, 255),
        2 => Color.FromRgb(0, 229, 255),
        3 => Color.FromRgb(255, 0, 212),
        4 => Color.FromRgb(255, 145, 0),
        5 => Color.FromRgb(118, 255, 3),
        6 => Color.FromRgb(124, 77, 255),
        7 => Color.FromRgb(0, 191, 165),
        8 => Color.FromRgb(255, 23, 68),
        _ => Colors.White
    };

    /// <summary>Отдельный оттенок периода: шаг золотого угла по цветовому кругу.</summary>
    public static Color PeriodColor(int period) =>
        ColorFromHsv(355 + (Math.Max(1, period) - 1) * 137.50776405003785, 0.8, 0.96);

    internal static Color ColorFromHsv(double hue, double saturation, double value)
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

    internal static Color Lerp(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(start.A + (end.A - start.A) * amount),
            (byte)Math.Round(start.R + (end.R - start.R) * amount),
            (byte)Math.Round(start.G + (end.G - start.G) * amount),
            (byte)Math.Round(start.B + (end.B - start.B) * amount));
    }

    #endregion

    #region Helpers

    private static bool IsFinite(Complex value) => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private static double MagnitudeSquared(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;

    /// <summary>
    /// Плавный номер итерации по двум последним расстояниям до цели: логарифмическая интерполяция
    /// момента, когда расстояние пересекло допуск. Результат лежит в [n − 1, n].
    /// </summary>
    private static double SmoothIteration(int iteration, double previousDistance, double distance, double tolerance)
    {
        if (iteration <= 0) return 0;
        if (!(previousDistance > tolerance) || !(distance < previousDistance)) return iteration;
        // При сверхлинейной сходимости последнее расстояние быстро упирается в погрешность самого
        // корня или точки цикла (~1e-13) и начинает шуметь от пикселя к пикселю; ниже этого
        // уровня оно не несёт информации, поэтому ограничивается снизу.
        double floor = Math.Max(tolerance * 1e-3, 1e-12 * Math.Max(1, previousDistance));
        double ratio = Math.Log(previousDistance / Math.Max(distance, floor));
        if (!(ratio > 0)) return iteration;
        double fraction = Math.Log(previousDistance / tolerance) / ratio;
        return iteration - 1 + Math.Clamp(fraction, 0, 1);
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

    /// <summary>Наименьший период 2–8 (для корней) или 1–8 (для отображений), замкнувший историю орбиты.</summary>
    private static int DetectCycle(Span<Complex> history, int historyCount, int historyNext, double tolerance, int minimumPeriod)
    {
        if (historyCount < 4) return 0;
        if (minimumPeriod > 1 &&
            AreClose(GetRecent(history, historyNext, 0), GetRecent(history, historyNext, 1), tolerance)) return 0;

        for (int period = minimumPeriod; period <= 8; period++)
        {
            if (historyCount < period * 2) break;
            bool matches = true;
            for (int offset = 0; offset < period; offset++)
            {
                if (AreClose(GetRecent(history, historyNext, offset),
                        GetRecent(history, historyNext, offset + period), tolerance)) continue;
                matches = false;
                break;
            }
            if (matches) return period;
        }
        return 0;
    }

    private static void Fill(byte[] buffer, int width, int height, int stride, Color color)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            WriteColor(buffer, y * stride + x * 4, color);
    }

    private static void WriteColor(byte[] buffer, int offset, Color color)
    {
        buffer[offset] = color.B;
        buffer[offset + 1] = color.G;
        buffer[offset + 2] = color.R;
        buffer[offset + 3] = color.A;
    }

    #endregion
}
