using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Serialization;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class NewtonPoolsWindow : Window
{
    private const double BaseScale = 3.0;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private NewtonPoolsEngine _formulaEngine = new();
    private readonly NewtonPaletteManager _paletteManager = new();
    private readonly NewtonSaveStore _saveStore = new();
    private readonly Random _random = new();
    private CancellationTokenSource? _renderCts;
    private bool _isRendering;
    private bool _controlsVisible = true;
    private bool _updatingZoomText;
    private readonly FullscreenToggle _fullscreen = new();

    private static readonly FloatExp MinZoom = FloatExp.FromDouble(0.001);

    /// <summary>Потолок зума — 1e1000, как у семейства Мандельброта и Феникса.</summary>
    private static readonly FloatExp MaxZoom = FloatExp.Pow10(1000);

    private readonly DoubleDeepZoomViewport _viewport =
        new(BaseScale, MinZoom, MaxZoom, NewtonPoolsEngine.DeepZoomThreshold);
    private string _appliedFormula = "z^3-1";
    private TiledRenderSession? _activeSession;

    private readonly string[] _presetFormulas =
    [
        "z^3-1", "z^4-1", "z^5-1", "z^6-1", "z^10-1", "z^3-2*z+2",
        "z^5-z^2+1", "z^6+3*z^3-2", "z^4-4*z^2+4", "z^7+z^4-z+1",
        "z^8+15*z^4-16", "z^4+z^3+z^2+z+1", "z^2-i", "(z^2-1)*(z-2*i)",
        "(1+2*i)*z^2+z-1", "0.5*z^3-1.25*z+2", "(2+i)*z^3-(1-2*i)*z+1",
        "i*z^4+z-1", "(1+0.5*i)*z^2-z+(2-3*i)", "(0.3+1.7*i)*z^3+(1-i)",
        "(2-i)*z^5+(3+2*i)*z^2-1", "-2*z^3+0.75*z^2-1", "z^6-1.5*z^3+0.25",
        "-0.1*z^4+z-2", "(1/2)*z^3+(3/4)*z-1", "(2+3*i)*(z^2)-(1-i)*z+4",
        "(z^2-1)/(z^2+1)", "(z^3-1)/(z^3+1)", "z^2/(z-1)^2",
        "(z^4-1)/(z*z-2*z+1)", "(z-1)*(z+1)*(z-0.2)*(z+0.2)",
        "(z-1)^2*(z+1)", "z^7-0.5*z^3+1", "z^5+(0.3-0.8*i)*z^2-(1.2+0.1*i)",
        "(z-1)*(z+1)*(z-0.1)*(z+0.1)*(z-0.01)", "(z-1)*(z-0.3)*(z+0.7)*(z-0.05)",
        "(z-1)^2*(z+1)*(z-0.2)", "sin(z)-1", "z-exp(-z)", "cos(z)-z",
        "z^3-exp(z)", "log(z)-1", "tan(z)-z",
        "sin(z)-0.5", "cos(z)-0.5", "tan(z)-1",
        "asin(z)-0.5", "acos(z)-1", "atan(z)-0.5",
        "sinh(z)-1", "cosh(z)-2", "tanh(z)-0.5",
        "exp(z)-2", "ln(z)-2", "sqrt(z)-1", "sin(sqrt(z))-0.5",
        "sin(z)-z^3", "z^3-exp(-z)", "log(z)-z^3", "tan(z)-z^3",
        "asin(z^4)-0.5", "acos(z^4)-1", "atan(z^3)-0.5",
        "ln(z^4)-z^2", "sqrt(z)-z^3", "sin(sqrt(z^3))-z"
    ];

    public NewtonPoolsWindow()
    {
        InitializeComponent();
        _viewport.AttachPreview(StablePreviewImage, CanvasHost);
        _renderTimer.Tick += RenderTimer_OnTick;
        FormulaPresetBox.ItemsSource = _presetFormulas;
        FormulaPresetBox.SelectedIndex = 0;
        FormulaBox.Text = _appliedFormula;
        MethodBox.SelectedIndex = 0;
        RelaxedPlaneModeBox.SelectedIndex = 0;
        RootSearchModeBox.SelectedIndex = 0;
        RootDisplayModeBox.SelectedIndex = 0;
        DiagnosticColoringBox.SelectedIndex = 0;
        RenderThreadsBox.Fill(ThreadsBox);

        UpdateMethodControls();
        UpdateDiagnosticModeHint();
        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
        FormulaBox.TextChanged += FormulaInput_OnChanged;
        RootToleranceBox.TextChanged += FormulaInput_OnChanged;
        RootSearchRadiusBox.TextChanged += FormulaInput_OnChanged;
        ApplyFormula(showMessage: false);
    }

    public NewtonState CaptureState(string saveName)
    {
        NumericInput.Prepare(this);
        if (_needsFormulaPreparation) throw new InvalidOperationException("Дождитесь поиска корней или примените формулу.");
        if (!int.TryParse(IterationsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations is < 1 or > 100_000)
            throw new InvalidOperationException("Итерации должны быть целым числом от 1 до 100000.");
        int order = ReadHouseholderOrder();
        if (!TryReadRootSettings(out double rootTolerance, out double rootSearchRadius, out string rootError))
            throw new InvalidOperationException(rootError);
        bool validRelaxedSettings = TryReadRelaxedSettings(out Complex relaxation, out Complex fixedInitialZ, out string relaxedError);
        if (!validRelaxedSettings && SelectedMethod == NewtonIterationMethod.RelaxedNewton)
            throw new InvalidOperationException(relaxedError);
        if (!validRelaxedSettings)
        {
            relaxation = Complex.One;
            fixedInitialZ = new Complex(0.5, 0.5);
        }
        NewtonColorPalette palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name);
        (double centerX, double centerY, string? exactX, string? exactY) = _viewport.CaptureCenter();
        return new NewtonState
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            Formula = _appliedFormula,
            MaxIterations = iterations,
            Zoom = _viewport.Zoom,
            CenterX = centerX,
            CenterY = centerY,
            CenterXExact = exactX,
            CenterYExact = exactY,
            IterationMethod = SelectedMethod,
            HouseholderOrder = order,
            RelaxedPlaneMode = SelectedRelaxedPlaneMode,
            Relaxation = relaxation,
            FixedInitialZ = fixedInitialZ,
            RootSearchMode = SelectedRootSearchMode,
            RootDisplayMode = SelectedRootDisplayMode,
            DiagnosticColoringMode = SelectedDiagnosticColoringMode,
            RootTolerance = rootTolerance,
            RootSearchRadius = rootSearchRadius,
            Roots = [.. _formulaEngine.Roots],
            Palette = palette
        };
    }

    public void LoadState(NewtonState state)
    {
        _formulaCts?.Cancel();
        _renderCts?.Cancel();
        FormulaPresetBox.SelectedIndex = -1;
        FormulaBox.Text = state.Formula;
        IterationsBox.Text = state.MaxIterations.ToString(CultureInfo.InvariantCulture);
        _viewport.Restore(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
        SetZoomText();
        MethodBox.SelectedIndex = (int)state.IterationMethod;
        HouseholderOrderBox.Text = Math.Clamp(state.HouseholderOrder, 2, 12).ToString(CultureInfo.InvariantCulture);
        RelaxedPlaneModeBox.SelectedIndex = Math.Clamp((int)state.RelaxedPlaneMode, 0, 1);
        Complex relaxation = IsFinite(state.Relaxation) ? state.Relaxation : Complex.One;
        Complex fixedInitialZ = IsFinite(state.FixedInitialZ) ? state.FixedInitialZ : new Complex(0.5, 0.5);
        RelaxationRealBox.Text = relaxation.Real.ToString("G10", CultureInfo.InvariantCulture);
        RelaxationImaginaryBox.Text = relaxation.Imaginary.ToString("G10", CultureInfo.InvariantCulture);
        FixedInitialZRealBox.Text = fixedInitialZ.Real.ToString("G10", CultureInfo.InvariantCulture);
        FixedInitialZImaginaryBox.Text = fixedInitialZ.Imaginary.ToString("G10", CultureInfo.InvariantCulture);
        RootSearchModeBox.SelectedIndex = (int)state.RootSearchMode;
        RootDisplayModeBox.SelectedIndex = (int)state.RootDisplayMode;
        DiagnosticColoringBox.SelectedIndex = Math.Clamp((int)state.DiagnosticColoringMode, 0, 4);
        RootToleranceBox.Text = Math.Clamp(state.RootTolerance, 1e-12, 0.1).ToString("G6", CultureInfo.InvariantCulture);
        RootSearchRadiusBox.Text = Math.Clamp(state.RootSearchRadius, 0.01, 1e9).ToString("G6", CultureInfo.InvariantCulture);
        _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        UpdatePreviewTransform();
        IReadOnlyList<Complex>? savedRoots = state.Roots.Count > 0 || state.RootSearchMode == NewtonRootSearchMode.ManualOnly
            ? state.Roots
            : null;
        ApplyFormula(showMessage: true, savedRoots);
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        NewtonState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    private NewtonIterationMethod SelectedMethod => MethodBox.SelectedIndex switch
    {
        1 => NewtonIterationMethod.Halley,
        2 => NewtonIterationMethod.Householder,
        3 => NewtonIterationMethod.RelaxedNewton,
        _ => NewtonIterationMethod.Newton
    };

    private NewtonRelaxedPlaneMode SelectedRelaxedPlaneMode => RelaxedPlaneModeBox.SelectedIndex == 1
        ? NewtonRelaxedPlaneMode.LambdaPlane
        : NewtonRelaxedPlaneMode.ZPlane;

    private bool IsLambdaParameterPlaneSelected =>
        SelectedMethod == NewtonIterationMethod.RelaxedNewton &&
        SelectedRelaxedPlaneMode == NewtonRelaxedPlaneMode.LambdaPlane;

    private NewtonRootSearchMode SelectedRootSearchMode => RootSearchModeBox.SelectedIndex switch
    {
        1 => NewtonRootSearchMode.Adaptive,
        2 => NewtonRootSearchMode.ManualOnly,
        _ => NewtonRootSearchMode.Automatic
    };

    private NewtonRootDisplayMode SelectedRootDisplayMode => RootDisplayModeBox.SelectedIndex switch
    {
        0 => NewtonRootDisplayMode.Hidden,
        2 => NewtonRootDisplayMode.MarkersWithCoordinates,
        _ => NewtonRootDisplayMode.Markers
    };

    private NewtonDiagnosticColoringMode SelectedDiagnosticColoringMode => DiagnosticColoringBox.SelectedIndex switch
    {
        1 => NewtonDiagnosticColoringMode.OrbitOutcome,
        2 => NewtonDiagnosticColoringMode.CyclesOnly,
        3 => NewtonDiagnosticColoringMode.Residual,
        4 => NewtonDiagnosticColoringMode.FinalValuePhase,
        _ => NewtonDiagnosticColoringMode.Disabled
    };

    private void FormulaPresetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || FormulaPresetBox.SelectedItem is not string formula) return;
        FormulaBox.Text = formula;
        ApplyFormula(showMessage: true);
    }

    private void ApplyFormulaButton_OnClick(object sender, RoutedEventArgs e) => ApplyFormula(showMessage: true);

    private void RootSearchModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _formulaCts?.Cancel();
        if (RootSearchRadiusPanel is not null)
            RootSearchRadiusPanel.IsEnabled = SelectedRootSearchMode != NewtonRootSearchMode.ManualOnly;
    }

    private void FindRootsButton_OnClick(object sender, RoutedEventArgs e) => ApplyFormula(showMessage: true);

    private async void AddRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!await EnsurePreparedAsync()) return;
        if (!TryParseComplex(ManualRootBox.Text, out Complex root))
        {
            MessageBox.Show(this, "Введите комплексное число, например 1+2*i или -0.5*i.",
                "Добавление корня", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var roots = _formulaEngine.Roots.ToList();
        roots.Add(root);
        _formulaEngine.ReplaceRoots(roots);
        ManualRootBox.Clear();
        RefreshRootUi();
        StatusText.Text = $"Корень {FormatComplex(root)} добавлен вручную.";
        ScheduleRender();
    }

    private async void RemoveRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!await EnsurePreparedAsync()) return;
        int index = RootListBox.SelectedIndex;
        if (index < 0 || index >= _formulaEngine.Roots.Count)
        {
            MessageBox.Show(this, "Выберите корень в списке.", "Удаление корня",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Complex removed = _formulaEngine.Roots[index];
        var roots = _formulaEngine.Roots.ToList();
        roots.RemoveAt(index);
        _formulaEngine.ReplaceRoots(roots);
        RefreshRootUi();
        StatusText.Text = $"Корень {FormatComplex(removed)} удалён.";
        ScheduleRender();
    }

    private void RootDisplayModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRootOverlay();

    private void RefreshRootUi()
    {
        if (RootListBox is null) return;
        RootCountText.Text = $"Корней: {_formulaEngine.Roots.Count} · {_formulaEngine.RootSearchStrategy}";
        RootListBox.ItemsSource = _formulaEngine.Roots
            .Select((root, index) => $"{index + 1}. {FormatComplex(root)}")
            .ToArray();
        UpdateRootOverlay();
    }

    private void UpdateRootOverlay()
    {
        if (RootOverlay is null) return;
        RootOverlay.Children.Clear();
        if (SelectedRootDisplayMode == NewtonRootDisplayMode.Hidden ||
            IsLambdaParameterPlaneSelected ||
            _formulaEngine.Roots.Count == 0) return;

        double width = RootOverlay.ActualWidth;
        double height = RootOverlay.ActualHeight;
        if (width <= 0 || height <= 0) return;

        IReadOnlyList<Color> colors = NewtonPaletteManager.AdjustColors(_paletteManager.ActivePalette, _formulaEngine.Roots.Count);
        for (int index = 0; index < _formulaEngine.Roots.Count; index++)
        {
            Complex root = _formulaEngine.Roots[index];
            (double offsetX, double offsetY) = RootScreenOffset(root, width);
            double x = width / 2 + offsetX;
            double y = height / 2 - offsetY;
            if (!(x >= -80 && x <= width + 80 && y >= -30 && y <= height + 30)) continue;

            Color color = colors[index % colors.Count];
            var marker = new Ellipse
            {
                Width = 13,
                Height = 13,
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(marker, x - marker.Width / 2);
            Canvas.SetTop(marker, y - marker.Height / 2);
            RootOverlay.Children.Add(marker);

            if (SelectedRootDisplayMode != NewtonRootDisplayMode.MarkersWithCoordinates) continue;
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 24)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Text = $"r{index + 1} = {FormatComplex(root)}"
                }
            };
            Canvas.SetLeft(label, x + 9);
            Canvas.SetTop(label, y - 11);
            RootOverlay.Children.Add(label);
        }
    }

    /// <summary>
    /// Смещение корня от центра полотна в экранных пикселях. На глубине разность считается в
    /// BigFloat: и центр, и сама разность там уже вне разрядности double.
    /// </summary>
    private (double X, double Y) RootScreenOffset(Complex root, double width)
    {
        if (!_viewport.IsDeep)
        {
            double worldWidth = BaseScale / Math.Max(0.001, _viewport.Zoom.ToDouble());
            return ((root.Real - _viewport.CenterX) * width / worldWidth, (root.Imaginary - _viewport.CenterY) * width / worldWidth);
        }

        FloatExp viewWidth = _viewport.ViewWidth;
        using var precision = _viewport.PrecisionScope();
        FloatExp offsetX = FloatExp.FromBigFloat(BigFloat.FromDouble(root.Real) - _viewport.ExactCenterX) / viewWidth * width;
        FloatExp offsetY = FloatExp.FromBigFloat(BigFloat.FromDouble(root.Imaginary) - _viewport.ExactCenterY) / viewWidth * width;
        return (offsetX.ToDouble(), offsetY.ToDouble());
    }

    private bool TryReadRootSettings(out double tolerance, out double radius, out string error)
    {
        if (!TryReadDouble(RootToleranceBox.Text, out tolerance) || tolerance is < 1e-12 or > 0.1)
        {
            radius = 0;
            error = "Точность корней должна быть числом от 1e-12 до 0.1.";
            return false;
        }
        if (!TryReadDouble(RootSearchRadiusBox.Text, out radius) || radius is < 0.01 or > 1e9)
        {
            error = "Радиус поиска должен быть числом от 0.01 до 1e9.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool TryReadRelaxedSettings(out Complex relaxation, out Complex fixedInitialZ, out string error)
    {
        double lambdaReal = 0;
        double lambdaImaginary = 0;
        double initialReal = 0;
        double initialImaginary = 0;
        bool validRelaxation = TryReadDouble(RelaxationRealBox.Text, out lambdaReal) &&
                               TryReadDouble(RelaxationImaginaryBox.Text, out lambdaImaginary) &&
                               double.IsFinite(lambdaReal) && double.IsFinite(lambdaImaginary);
        bool validInitial = TryReadDouble(FixedInitialZRealBox.Text, out initialReal) &&
                            TryReadDouble(FixedInitialZImaginaryBox.Text, out initialImaginary) &&
                            double.IsFinite(initialReal) && double.IsFinite(initialImaginary);
        relaxation = validRelaxation ? new Complex(lambdaReal, lambdaImaginary) : Complex.One;
        fixedInitialZ = validInitial ? new Complex(initialReal, initialImaginary) : new Complex(0.5, 0.5);

        if (SelectedMethod == NewtonIterationMethod.RelaxedNewton &&
            SelectedRelaxedPlaneMode == NewtonRelaxedPlaneMode.ZPlane &&
            !validRelaxation)
        {
            error = "Re λ и Im λ должны быть конечными числами.";
            return false;
        }
        if (SelectedMethod == NewtonIterationMethod.RelaxedNewton &&
            SelectedRelaxedPlaneMode == NewtonRelaxedPlaneMode.LambdaPlane &&
            !validInitial)
        {
            error = "Re z₀ и Im z₀ должны быть конечными числами.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryParseComplex(string text, out Complex value)
    {
        value = Complex.Zero;
        try
        {
            ExpressionNode expression = new Parser(new Tokenizer(text.Trim()).Tokenize()).Parse().Simplify();
            value = expression.Evaluate([]);
            return double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatComplex(Complex value)
    {
        string real = value.Real.ToString("0.##########", CultureInfo.InvariantCulture);
        string imaginary = Math.Abs(value.Imaginary).ToString("0.##########", CultureInfo.InvariantCulture);
        return $"{real} {(value.Imaginary < 0 ? '−' : '+')} {imaginary}i";
    }

    private static bool IsFinite(Complex value) =>
        double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private void RandomFormulaButton_OnClick(object sender, RoutedEventArgs e)
    {
        FormulaPresetBox.SelectedIndex = -1;
        FormulaBox.Text = GenerateValidRandomFormula();
        ApplyFormula(showMessage: true);
    }

    private string GenerateValidRandomFormula()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string formula = GenerateRandomFormula();
            var engine = new NewtonPoolsEngine();
            if (engine.SetFormula(formula, out _, discoverRoots: false)) return formula;
        }
        return _presetFormulas[_random.Next(_presetFormulas.Length)];
    }

    private string GenerateRandomFormula() => _random.Next(4) switch
    {
        0 => GenerateCyclotomicFormula(), 1 => GenerateSparseFormula(), 2 => GenerateFactoredFormula(), _ => GeneratePerturbedFormula()
    };

    private string GenerateCyclotomicFormula()
    {
        int degree = _random.Next(3, 11);
        string coefficient = RandomComplexCoefficient();
        return coefficient == "1" ? $"z^{degree} {RandomSignedConstant()}" : $"{coefficient}*z^{degree} {RandomSignedConstant()}";
    }

    private string GenerateSparseFormula()
    {
        int high = _random.Next(4, 9);
        int low = _random.Next(1, high - 1);
        return $"z^{high} {RandomSign()} {RandomRealCoefficient(false)}*z^{low} {RandomSignedConstant()}";
    }

    private string GenerateFactoredFormula()
    {
        string[] offsets = ["-1", "+1", "-0.5", "+0.5", "-0.25", "+0.25", "-i", "+i", "-0.5*i", "+0.5*i", "-(1+i)", "+(1-i)"];
        var terms = Enumerable.Range(0, _random.Next(3, 6)).Select(_ => $"(z{offsets[_random.Next(offsets.Length)]})").ToList();
        if (_random.NextDouble() < 0.35) terms.Add(terms[_random.Next(terms.Count)]);
        return string.Join("*", terms);
    }

    private string GeneratePerturbedFormula()
    {
        int degree = _random.Next(3, 8);
        return $"z^{degree} {RandomSign()} {RandomComplexCoefficient()}*z^{_random.Next(1, degree)} {RandomSignedConstant()}";
    }

    private string RandomComplexCoefficient()
    {
        if (_random.NextDouble() < 0.45) return RandomRealCoefficient(true);
        return $"({RandomRealCoefficient(false)}{(_random.Next(2) == 0 ? "+" : "-")}{RandomRealCoefficient(false)}*i)";
    }

    private string RandomRealCoefficient(bool allowOne)
    {
        string[] values = allowOne
            ? ["1", "0.25", "0.5", "0.75", "1.25", "1.5", "2", "3"]
            : ["0.25", "0.5", "0.75", "1.25", "1.5", "2", "3"];
        return values[_random.Next(values.Length)];
    }

    private string RandomSignedConstant()
    {
        string[] values = ["- 1", "+ 1", "- 2", "+ 2", "- 0.5", "+ 0.5", "- i", "+ i", "- (1+i)", "+ (1-i)"];
        return values[_random.Next(values.Length)];
    }

    private string RandomSign() => _random.Next(2) == 0 ? "+" : "-";

    private void MethodBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMethodControls();
        ScheduleRender();
    }

    private void RelaxedPlaneModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRelaxedPlaneControls();
        ScheduleRender();
    }

    private void DiagnosticColoringBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDiagnosticModeHint();
        ScheduleRender();
    }

    private void UpdateDiagnosticModeHint()
    {
        if (DiagnosticModeHint is null) return;
        DiagnosticModeHint.Text = SelectedDiagnosticColoringMode switch
        {
            NewtonDiagnosticColoringMode.OrbitOutcome =>
                "Корни — цвета палитры; циклы 2–8 — отдельные цвета; жёлтый — нулевая производная или вырожденный шаг, синий — уход за 10⁶, розовый — NaN/переполнение, серый — лимит итераций.",
            NewtonDiagnosticColoringMode.CyclesOnly =>
                "Только циклы: 2 — голубой, 3 — розовый, 4 — оранжевый, 5 — зелёный, 6 — фиолетовый, 7 — бирюзовый, 8 — красный.",
            NewtonDiagnosticColoringMode.Residual =>
                "Логарифмическая шкала остатка |f(z)|: синий — малый остаток, красный — большой, розовый — нечисловое значение.",
            NewtonDiagnosticColoringMode.FinalValuePhase =>
                "Цветовой круг показывает аргумент последнего вычисленного f(z); NaN и переполнение выделяются розовым.",
            _ => "Диагностика выключена: используется обычная палитра бассейнов без анализа циклов."
        };
    }

    private void UpdateMethodControls()
    {
        if (HouseholderOrderPanel is not null) HouseholderOrderPanel.IsEnabled = SelectedMethod == NewtonIterationMethod.Householder;
        UpdateRelaxedPlaneControls();
    }

    private void UpdateRelaxedPlaneControls()
    {
        if (RelaxedNewtonPanel is null || RelaxationPanel is null || FixedInitialZPanel is null) return;
        bool relaxed = SelectedMethod == NewtonIterationMethod.RelaxedNewton;
        bool lambdaPlane = relaxed && SelectedRelaxedPlaneMode == NewtonRelaxedPlaneMode.LambdaPlane;
        RelaxedNewtonPanel.Visibility = relaxed ? Visibility.Visible : Visibility.Collapsed;
        RelaxationPanel.Visibility = lambdaPlane ? Visibility.Collapsed : Visibility.Visible;
        FixedInitialZPanel.Visibility = lambdaPlane ? Visibility.Visible : Visibility.Collapsed;
        if (RelaxedPlaneHint is not null)
            RelaxedPlaneHint.Text = lambdaPlane
                ? "Каждый пиксель задаёт комплексное λ; итерации начинаются из фиксированного z₀. Масштаб и перемещение относятся к плоскости λ."
                : "Каждый пиксель задаёт начальное z; ко всем орбитам применяется одно комплексное λ. При λ = 1 + 0i получается обычный Newton.";
        if (RootDisplayModeBox is not null) RootDisplayModeBox.IsEnabled = !lambdaPlane;
        UpdateRootOverlay();
    }

    private void Parameter_OnChanged(object sender, EventArgs e) { _formulaCts?.Cancel(); ScheduleRender(); }

    private void ZoomBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (_updatingZoomText) return;
        if (FloatExp.TryParse(ZoomBox.Text.Trim().Replace(',', '.'), out FloatExp zoom) && zoom.Sign > 0)
        {
            _viewport.SetZoom(zoom);
            UpdatePreviewTransform();
            ScheduleRender();
        }
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) { _renderTimer.Stop(); _formulaCts?.Cancel(); _renderCts?.Cancel(); }

    private async void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!await EnsurePreparedAsync())
        {
            MessageBox.Show(this, "Сначала примените корректную формулу.", "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new NewtonPaletteWindow(_paletteManager, _formulaEngine.Roots) { Owner = this };
        dialog.PaletteApplied += (_, _) =>
        {
            UpdateRootOverlay();
            ScheduleRender();
        };
        dialog.ShowDialog();
    }

    private async void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); if (!await EnsurePreparedAsync()) return; SaveManagerWindow.Open(this, SaveManagerConfigurations.ForNewton(this, _saveStore)); }

    private async void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!await EnsurePreparedAsync()) return;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        NewtonState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "newton_pools",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void ScheduleRender()
    {
        if (!IsLoaded || _closed) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        if (_viewport.IsPanning) return;
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_viewport.IsPanning) return;

        if (_isRendering)
        {
            ScheduleRender();
            return;
        }

        NewtonState state;
        if (!await EnsurePreparedAsync() || _closed || _viewport.IsPanning || _isRendering) return;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var stopwatch = Stopwatch.StartNew();
        SetRenderingState(true, $"Рендеринг методом {state.IterationMethod}...");
        TiledRenderSession? session = null;
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int factor = SelectedPreviewSsaaFactor;
            int renderWidth = checked(surface.PixelWidth * factor);
            int renderHeight = checked(surface.PixelHeight * factor);
            TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(
                renderWidth, renderHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            NewtonPoolsEngine engine = await Task.Run(() => CreateEngine(state, token), token);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress, _renderCts);
            _activeSession = session;
            CanvasImage.Source = bitmap;

            await session.RenderTilesAsync(tiles, GetThreadCount(),
                (tile, tileToken) => engine.RenderTile(tile, renderWidth, renderHeight, tileToken), token);
            if (token.IsCancellationRequested)
            {
                CanvasImage.Source = null;
                if (!_closed && _formulaCts is null) StatusText.Text = "Рендер отменён";
                return;
            }
            session.Flush();

            StablePreviewImage.Source = session.Snapshot();
            CanvasImage.Source = null;
            // Центр — из состояния, которым кадр посчитан: за время рендера вид могли сдвинуть.
            _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
            UpdatePreviewTransform();
            session.End();
            _activeSession = null;
            string engineName = engine.ShouldUseDeepZoom() ? "глубокий движок" : "double";
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. Корней: {_formulaEngine.Roots.Count}. " +
                              $"Зум {FloatExpJsonConverter.ToDisplay(state.Zoom)} ({engineName}). Стратегия: {strategy}";
        }
        catch (OperationCanceledException)
        {
            CanvasImage.Source = null;
            if (!_closed && _formulaCts is null) StatusText.Text = "Рендер отменён";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка рендера";
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            session?.End();
            _activeSession = null;
            SetRenderingState(false);
        }
    }

    private void CommitAndBakePreview()
    {
        TiledRenderSession? session = _activeSession;
        if (session is null) return;

        session.Cancellation?.Cancel();
        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(ImageLayer) is { } baked)
        {
            StablePreviewImage.Source = baked;
            _viewport.MarkRendered();
            UpdatePreviewTransform();
        }

        CanvasImage.Source = null;
        session.End();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private async Task<BitmapSource> RenderBitmapAsync(NewtonState state, int width, int height, int ssaaFactor,
        CancellationToken token, IProgress<int>? progress)
    {
        // Элементы окна принадлежат UI-потоку: число потоков читается до расчёта.
        int threads = GetThreadCount();
        NewtonPoolsEngine engine = await Task.Run(() => CreateEngine(state, token), token);
        return await SsaaBitmapRenderer.RenderAsync(width, height, ssaaFactor, 4, (pixels, renderWidth, renderHeight, stride, report) =>
            engine.RenderToBuffer(pixels, renderWidth, renderHeight, stride, threads, token, report), token, progress);
    }

    private static NewtonPoolsEngine CreateEngine(NewtonState state, CancellationToken token = default)
    {
        var engine = new NewtonPoolsEngine
        {
            MaxIterations = state.MaxIterations,
            CenterX = state.CenterX,
            CenterY = state.CenterY,
            CenterXExact = state.CenterXExact,
            CenterYExact = state.CenterYExact,
            Zoom = state.Zoom,
            IterationMethod = state.IterationMethod,
            HouseholderOrder = state.HouseholderOrder,
            RelaxedPlaneMode = state.RelaxedPlaneMode,
            Relaxation = state.Relaxation,
            FixedInitialZ = state.FixedInitialZ,
            RootSearchMode = state.RootSearchMode,
            RootTolerance = state.RootTolerance,
            RootSearchRadius = state.RootSearchRadius,
            BackgroundColor = state.Palette.BackgroundColor,
            UseGradient = state.Palette.IsGradient,
            DiagnosticColoringMode = state.DiagnosticColoringMode
        };
        bool useSavedRoots = state.Roots.Count > 0 || state.RootSearchMode == NewtonRootSearchMode.ManualOnly;
        if (!engine.SetFormula(state.Formula, out string debug, !useSavedRoots, token)) throw new InvalidOperationException(debug);
        if (useSavedRoots) engine.ReplaceRoots(state.Roots);
        engine.RootColors = NewtonPaletteManager.AdjustColors(state.Palette, engine.Roots.Count).ToArray();
        return engine;
    }

    private int ReadHouseholderOrder() => int.TryParse(HouseholderOrderBox.Text, out int order) ? Math.Clamp(order, 2, 12) : 3;
    private int GetThreadCount() => RenderThreadsBox.Read(ThreadsBox);
    private int SelectedPreviewSsaaFactor => PreviewSsaaBox.SelectedItem is ComboBoxItem item &&
                                             int.TryParse(item.Tag?.ToString(), out int factor)
        ? factor
        : 1;

    private void SetRenderingState(bool rendering, string? status = null)
    {
        _isRendering = rendering;
        CancelButton.IsEnabled = rendering || _formulaCts is not null;
        if (!rendering) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CommitAndBakePreview();
        _viewport.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight,
            _viewport.WheelFactor(e.Delta));
        UpdatePreviewTransform();
        SetZoomText();
        ScheduleRender();
        e.Handled = true;
    }

    /// <summary>
    /// Зум показывается восемью значащими цифрами, за пределами double — короткой научной
    /// нотацией (1.2345678e+1000); поле принимает обе записи.
    /// </summary>
    private void SetZoomText()
    {
        _updatingZoomText = true;
        try
        {
            ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_viewport.Zoom);
        }
        finally
        {
            _updatingZoomText = false;
        }
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderTimer.Stop();
        _viewport.BeginPan(e.GetPosition(CanvasHost));
        CommitAndBakePreview();
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport.PanTo(e.GetPosition(CanvasHost), CanvasHost.ActualWidth)) UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewport.EndPan()) return;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    private void UpdatePreviewTransform()
    {
        UpdateRootOverlay();
        _viewport.UpdatePreview();
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 290);
        ScheduleRender();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true;
        _formulaCts?.Cancel();
        _renderTimer.Stop();
        _activeSession?.End();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static bool TryReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);
}
