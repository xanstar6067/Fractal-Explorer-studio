using System.Collections.Concurrent;
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
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly NewtonPoolsEngine _formulaEngine = new();
    private readonly NewtonPaletteManager _paletteManager = new();
    private readonly NewtonSaveStore _saveStore = new();
    private readonly Random _random = new();
    private CancellationTokenSource? _renderCts;
    private bool _isRendering;
    private bool _isPanning;
    private bool _isFullscreen;
    private bool _controlsVisible = true;
    private bool _updatingZoomText;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Point _lastPanPoint;
    private double _centerX;
    private double _centerY;
    private FloatExp _zoom = FloatExp.One;

    /// <summary>
    /// Центр в произвольной точности. Ведётся начиная с <see cref="NewtonPoolsEngine.DeepZoomThreshold"/>,
    /// когда double перестаёт различать соседние пиксели; ниже порога источник истины —
    /// <see cref="_centerX"/>/<see cref="_centerY"/>.
    /// </summary>
    private BigFloat _centerXExact, _centerYExact;
    private bool _deepZoomEngaged;
    private BigFloat _renderedCenterXExact, _renderedCenterYExact;
    private FloatExp _renderedZoom = FloatExp.One;
    private bool _hasRenderedFrame;

    private static readonly FloatExp MinZoom = FloatExp.FromDouble(0.001);

    /// <summary>Потолок зума — 1e1000, как у семейства Мандельброта и Феникса.</summary>
    private static readonly FloatExp MaxZoom = FloatExp.Pow10(1000);

    private const int CenterPrecisionGuardBits = 96;
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private string _appliedFormula = "z^3-1";
    private RenderSession? _activeSession;

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
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StablePreviewImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StablePreviewImage.RenderTransform = _previewTransform;
        _visualizationTimer.Tick += (_, _) =>
        {
            if (_activeSession is not null) FlushVisualizationEvents(_activeSession, false);
        };
        _renderTimer.Tick += RenderTimer_OnTick;
        FormulaPresetBox.ItemsSource = _presetFormulas;
        FormulaPresetBox.SelectedIndex = 0;
        FormulaBox.Text = _appliedFormula;
        MethodBox.SelectedIndex = 0;
        RelaxedPlaneModeBox.SelectedIndex = 0;
        RootSearchModeBox.SelectedIndex = 0;
        RootDisplayModeBox.SelectedIndex = 0;
        DiagnosticColoringBox.SelectedIndex = 0;
        for (int count = 1; count <= Environment.ProcessorCount; count++) ThreadsBox.Items.Add(count);
        ThreadsBox.Items.Add("Auto");
        ThreadsBox.SelectedItem = "Auto";
        ApplyFormula(showMessage: false);
        UpdateMethodControls();
        UpdateDiagnosticModeHint();
        Loaded += (_, _) => ScheduleRender();
    }

    public NewtonState CaptureState(string saveName)
    {
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
        return new NewtonState
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            Formula = _appliedFormula,
            MaxIterations = iterations,
            Zoom = _zoom,
            CenterX = _deepZoomEngaged ? _centerXExact.ToDouble() : _centerX,
            CenterY = _deepZoomEngaged ? _centerYExact.ToDouble() : _centerY,
            CenterXExact = _deepZoomEngaged ? _centerXExact.ToInvariantString() : null,
            CenterYExact = _deepZoomEngaged ? _centerYExact.ToInvariantString() : null,
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
        _renderCts?.Cancel();
        FormulaPresetBox.SelectedIndex = -1;
        FormulaBox.Text = state.Formula;
        IterationsBox.Text = state.MaxIterations.ToString(CultureInfo.InvariantCulture);
        _zoom = state.Zoom.Sign > 0 && state.Zoom.IsFinite ? FloatExp.Clamp(state.Zoom, MinZoom, MaxZoom) : FloatExp.One;
        _centerX = state.CenterX;
        _centerY = state.CenterY;
        _deepZoomEngaged = false;
        if (state.CenterXExact is { Length: > 0 } exactX && state.CenterYExact is { Length: > 0 } exactY)
        {
            try
            {
                // Разбор округляет до рабочей точности потока — на глубине её поднимаем заранее.
                using var precision = CenterPrecisionScope();
                _centerXExact = BigFloat.Parse(exactX);
                _centerYExact = BigFloat.Parse(exactY);
                _deepZoomEngaged = _zoom >= NewtonPoolsEngine.DeepZoomThreshold;
                if (_deepZoomEngaged)
                {
                    _centerX = _centerXExact.ToDouble();
                    _centerY = _centerYExact.ToDouble();
                }
            }
            catch (FormatException)
            {
                // Испорченная строка точного центра — не повод не открыть сохранение.
                _deepZoomEngaged = false;
            }
        }
        if (!_deepZoomEngaged) SyncDeepZoomState();
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
        if (ApplyFormula(showMessage: true, savedRoots)) ScheduleRender();
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

    private bool ApplyFormula(bool showMessage, IReadOnlyList<Complex>? suppliedRoots = null)
    {
        if (!TryReadRootSettings(out double tolerance, out double searchRadius, out string rootError))
        {
            StatusText.Text = rootError;
            if (showMessage) MessageBox.Show(this, rootError, "Параметры поиска", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _formulaEngine.HouseholderOrder = ReadHouseholderOrder();
        _formulaEngine.CenterX = _centerX;
        _formulaEngine.CenterY = _centerY;
        _formulaEngine.RootTolerance = tolerance;
        _formulaEngine.RootSearchRadius = searchRadius;
        _formulaEngine.RootSearchMode = SelectedRootSearchMode;
        bool discoverRoots = suppliedRoots is null;
        if (!_formulaEngine.SetFormula(FormulaBox.Text.Trim(), out string debug, discoverRoots))
        {
            DebugOutput.Text = debug;
            RootCountText.Text = "Найдено корней: 0";
            StatusText.Text = "Ошибка формулы";
            if (showMessage) MessageBox.Show(this, "Проверьте формулу: есть синтаксическая ошибка.", "Ошибка формулы", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (suppliedRoots is not null)
        {
            _formulaEngine.ReplaceRoots(suppliedRoots);
            debug += $"{Environment.NewLine}Загружено/задано корней: {_formulaEngine.Roots.Count}";
        }
        _appliedFormula = FormulaBox.Text.Trim();
        DebugOutput.Text = debug;
        RefreshRootUi();
        StatusText.Text = _formulaEngine.Roots.Count == 0
            ? "Формула корректна, но корни не найдены. Увеличьте радиус или добавьте корень вручную."
            : $"Формула применена. {_formulaEngine.RootSearchStrategy}";
        ScheduleRender();
        return true;
    }

    private void RootSearchModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RootSearchRadiusPanel is not null)
            RootSearchRadiusPanel.IsEnabled = SelectedRootSearchMode != NewtonRootSearchMode.ManualOnly;
    }

    private void FindRootsButton_OnClick(object sender, RoutedEventArgs e) => ApplyFormula(showMessage: true);

    private void AddRootButton_OnClick(object sender, RoutedEventArgs e)
    {
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

    private void RemoveRootButton_OnClick(object sender, RoutedEventArgs e)
    {
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
        if (!_deepZoomEngaged)
        {
            double worldWidth = BaseScale / Math.Max(0.001, _zoom.ToDouble());
            return ((root.Real - _centerX) * width / worldWidth, (root.Imaginary - _centerY) * width / worldWidth);
        }

        FloatExp viewWidth = BaseScale / _zoom;
        using var precision = CenterPrecisionScope();
        FloatExp offsetX = FloatExp.FromBigFloat(BigFloat.FromDouble(root.Real) - _centerXExact) / viewWidth * width;
        FloatExp offsetY = FloatExp.FromBigFloat(BigFloat.FromDouble(root.Imaginary) - _centerYExact) / viewWidth * width;
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
            if (engine.SetFormula(formula, out _)) return formula;
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

    private void Parameter_OnChanged(object sender, EventArgs e) => ScheduleRender();

    private void ZoomBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingZoomText) return;
        if (FloatExp.TryParse(ZoomBox.Text.Trim().Replace(',', '.'), out FloatExp zoom) && zoom.Sign > 0)
        {
            _zoom = FloatExp.Clamp(zoom, MinZoom, MaxZoom);
            SyncDeepZoomState();
            UpdatePreviewTransform();
            ScheduleRender();
        }
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => _ = RenderPreviewAsync();
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_formulaEngine.SetFormula(_appliedFormula, out _))
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

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForNewton(this, _saveStore));

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
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
        if (!IsLoaded) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        if (_isPanning) return;
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isPanning) return;

        if (_isRendering)
        {
            ScheduleRender();
            return;
        }

        NewtonState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var stopwatch = Stopwatch.StartNew();
        SetRenderingState(true, $"Рендеринг методом {state.IterationMethod}...");
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
            NewtonPoolsEngine engine = CreateEngine(state);
            var session = new RenderSession(bitmap, tiles.Count, renderWidth, renderHeight, _renderCts);
            _activeSession = session;
            CanvasImage.Source = bitmap;
            RenderOverlay.BeginSession(renderWidth, renderHeight);
            _visualizationTimer.Start();

            await RenderTilesAsync(engine, tiles, session, GetThreadCount(), token);
            if (token.IsCancellationRequested)
            {
                CanvasImage.Source = null;
                StatusText.Text = "Рендер отменён";
                return;
            }
            FlushVisualizationEvents(session, true);

            BitmapSource completed = session.Bitmap.Clone();
            completed.Freeze();
            StablePreviewImage.Source = completed;
            CanvasImage.Source = null;
            // Центр — из состояния, которым кадр посчитан: за время рендера вид могли сдвинуть.
            using (CenterPrecisionScope(state.Zoom))
            {
                _renderedCenterXExact = state.CenterXExact is { Length: > 0 } renderedX
                    ? BigFloat.Parse(renderedX)
                    : BigFloat.FromDouble(state.CenterX);
                _renderedCenterYExact = state.CenterYExact is { Length: > 0 } renderedY
                    ? BigFloat.Parse(renderedY)
                    : BigFloat.FromDouble(state.CenterY);
            }
            _renderedZoom = state.Zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            RenderOverlay.EndSession();
            _activeSession = null;
            string engineName = engine.ShouldUseDeepZoom() ? "глубокий движок" : "double";
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. Корней: {_formulaEngine.Roots.Count}. " +
                              $"Зум {FloatExpJsonConverter.ToDisplay(state.Zoom)} ({engineName}). Стратегия: {strategy}";
        }
        catch (OperationCanceledException)
        {
            CanvasImage.Source = null;
            StatusText.Text = "Рендер отменён";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка рендера";
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _visualizationTimer.Stop();
            RenderOverlay.EndSession();
            _activeSession = null;
            SetRenderingState(false);
        }
    }

    private static async Task RenderTilesAsync(NewtonPoolsEngine engine, IReadOnlyList<MandelbrotRenderTile> tiles,
        RenderSession session, int threadCount, CancellationToken token)
    {
        var queue = new ConcurrentQueue<MandelbrotRenderTile>(tiles);
        Task[] workers = Enumerable.Range(0, Math.Clamp(threadCount, 1, Environment.ProcessorCount)).Select(_ => Task.Run(() =>
        {
            while (queue.TryDequeue(out MandelbrotRenderTile tile))
            {
                if (token.IsCancellationRequested) return;
                session.Events.Enqueue(new TileRenderEvent(true, tile, null));
                byte[]? pixels = engine.RenderTile(tile, session.RenderWidth, session.RenderHeight, token);
                if (pixels is null || token.IsCancellationRequested) return;
                session.Events.Enqueue(new TileRenderEvent(false, tile, pixels));
            }
        })).ToArray();
        await Task.WhenAll(workers);
    }

    private void FlushVisualizationEvents(RenderSession session, bool drainAll)
    {
        int processed = 0;
        bool changed = false;
        while ((drainAll || processed < 512) && session.Events.TryDequeue(out TileRenderEvent entry))
        {
            if (entry.IsStart) RenderOverlay.StartTile(entry.Tile);
            else if (entry.Pixels is not null)
            {
                if (ProgressiveRenderBitmap.WriteTile(session.Bitmap, entry.Tile, entry.Pixels))
                {
                    RenderOverlay.CompleteTile(entry.Tile);
                    session.CompletedTiles++;
                }
            }
            processed++;
            changed = true;
        }
        if (!changed) return;
        RenderOverlay.Refresh();
        RenderProgress.Value = session.TileCount == 0 ? 0 : session.CompletedTiles * 100.0 / session.TileCount;
    }

    private void CommitAndBakePreview()
    {
        RenderSession? session = _activeSession;
        if (session is null) return;

        session.Cancellation.Cancel();
        FlushVisualizationEvents(session, true);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(ImageLayer);
        try
        {
            var baked = new RenderTargetBitmap(surface.PixelWidth, surface.PixelHeight,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            baked.Render(ImageLayer);
            baked.Freeze();
            StablePreviewImage.Source = baked;
            _renderedCenterXExact = _deepZoomEngaged ? _centerXExact : BigFloat.FromDouble(_centerX);
            _renderedCenterYExact = _deepZoomEngaged ? _centerYExact : BigFloat.FromDouble(_centerY);
            _renderedZoom = _zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
        }
        catch (InvalidOperationException)
        {
            // Layout can briefly be unavailable during minimization or a resize transition.
        }

        CanvasImage.Source = null;
        _visualizationTimer.Stop();
        RenderOverlay.EndSession();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private async Task<BitmapSource> RenderBitmapAsync(NewtonState state, int width, int height, int ssaaFactor,
        CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaaFactor, 1, 4);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] buffer = new byte[checked(stride * renderHeight)];
        NewtonPoolsEngine engine = CreateEngine(state);
        int threads = GetThreadCount();
        await Task.Run(() => engine.RenderToBuffer(buffer, renderWidth, renderHeight, stride, threads, token,
            value => progress?.Report(factor == 1 ? value : value * 90 / 100)));

        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
        source.Freeze();
        if (factor == 1 || token.IsCancellationRequested) return source;
        return await Task.Run(() => BitmapResampler.ResizeLanczos3(source, width, height, token, value => progress?.Report(value)));
    }

    private static NewtonPoolsEngine CreateEngine(NewtonState state)
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
        if (!engine.SetFormula(state.Formula, out string debug, !useSavedRoots)) throw new InvalidOperationException(debug);
        if (useSavedRoots) engine.ReplaceRoots(state.Roots);
        engine.RootColors = NewtonPaletteManager.AdjustColors(state.Palette, engine.Roots.Count).ToArray();
        return engine;
    }

    private int ReadHouseholderOrder() => int.TryParse(HouseholderOrderBox.Text, out int order) ? Math.Clamp(order, 2, 12) : 3;
    private int GetThreadCount() => ThreadsBox.SelectedItem?.ToString() == "Auto" ? Environment.ProcessorCount : Math.Max(1, Convert.ToInt32(ThreadsBox.SelectedItem, CultureInfo.InvariantCulture));
    private int SelectedPreviewSsaaFactor => PreviewSsaaBox.SelectedItem is ComboBoxItem item &&
                                             int.TryParse(item.Tag?.ToString(), out int factor)
        ? factor
        : 1;

    private void SetRenderingState(bool rendering, string? status = null)
    {
        _isRendering = rendering;
        CancelButton.IsEnabled = rendering;
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
        Point mouse = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double fractionX = mouse.X / width - 0.5;
        double fractionY = height / 2 - mouse.Y;

        FloatExp previousZoom = _zoom;
        double step = WheelZoomStep;
        _zoom = FloatExp.Clamp(_zoom * (e.Delta > 0 ? step : 1 / step), MinZoom, MaxZoom);

        // Точка под курсором остаётся на месте. Сдвиг — разность ширин области в FloatExp: на
        // глубине сами ширины вне double, а ApplyCenterShift кладёт сдвиг в BigFloat-центр.
        FloatExp viewWidthDelta = BaseScale / previousZoom - BaseScale / _zoom;
        SyncDeepZoomState();
        ApplyCenterShift(fractionX * viewWidthDelta, fractionY / width * viewWidthDelta);

        UpdatePreviewTransform();
        SetZoomText();
        ScheduleRender();
        e.Handled = true;
    }

    /// <summary>Множитель зума на щелчок колеса: ×1.2, с Ctrl — ×10, с Shift — точные ×1.05.</summary>
    private static double WheelZoomStep =>
        (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10.0
        : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.05
        : 1.2;

    /// <summary>
    /// Зум показывается восемью значащими цифрами, за пределами double — короткой научной
    /// нотацией (1.2345678e+1000); поле принимает обе записи.
    /// </summary>
    private void SetZoomText()
    {
        _updatingZoomText = true;
        try
        {
            ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_zoom);
        }
        finally
        {
            _updatingZoomText = false;
        }
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderTimer.Stop();
        _isPanning = true;
        CommitAndBakePreview();
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        Point current = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        FloatExp viewWidth = BaseScale / _zoom;
        ApplyCenterShift((_lastPanPoint.X - current.X) / width * viewWidth,
            (current.Y - _lastPanPoint.Y) / width * viewWidth);
        _lastPanPoint = current;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    /// <summary>
    /// Прибавляет к центру сдвиг в мировых координатах: на глубине — в BigFloat-центр с
    /// адаптивной точностью (double-приближение обновляется следом), на мелком зуме — в double.
    /// </summary>
    private void ApplyCenterShift(FloatExp shiftX, FloatExp shiftY)
    {
        if (_deepZoomEngaged)
        {
            using var precision = CenterPrecisionScope();
            // ToBigFloat переносит мантиссу сдвига целиком: на глубине сам сдвиг в double не представим.
            _centerXExact += shiftX.ToBigFloat();
            _centerYExact += shiftY.ToBigFloat();
            _centerX = _centerXExact.ToDouble();
            _centerY = _centerYExact.ToDouble();
        }
        else
        {
            _centerX += shiftX.ToDouble();
            _centerY += shiftY.ToDouble();
        }
    }

    /// <summary>
    /// Разрядность арифметики центра на UI-потоке: биты на разрешение пикселей (log2 зума) плюс
    /// запас на субпиксельную точность и сотни сдвигов пана без повторного рендера.
    /// </summary>
    private static int CenterPrecisionBits(FloatExp zoom)
    {
        double zoomBits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int needed = (int)Math.Ceiling(zoomBits) + CenterPrecisionGuardBits;
        return Math.Max(BigFloat.MinimumPrecisionBits, (needed + 63) / 64 * 64);
    }

    private BigFloat.PrecisionScope CenterPrecisionScope() => CenterPrecisionScope(_zoom);

    private static BigFloat.PrecisionScope CenterPrecisionScope(FloatExp zoom) => new(CenterPrecisionBits(zoom));

    /// <summary>
    /// Заводит или глушит ведение центра в BigFloat по текущему зуму: вверх через порог центр
    /// переносится из double, вниз double снова становится источником истины.
    /// </summary>
    private void SyncDeepZoomState()
    {
        bool shouldEngage = _zoom >= NewtonPoolsEngine.DeepZoomThreshold;
        if (shouldEngage && !_deepZoomEngaged)
        {
            using var precision = CenterPrecisionScope();
            _centerXExact = BigFloat.FromDouble(_centerX);
            _centerYExact = BigFloat.FromDouble(_centerY);
            _deepZoomEngaged = true;
        }
        else if (!shouldEngage && _deepZoomEngaged)
        {
            _centerX = _centerXExact.ToDouble();
            _centerY = _centerYExact.ToDouble();
            _deepZoomEngaged = false;
        }
    }

    private void UpdatePreviewTransform()
    {
        UpdateRootOverlay();
        if (!_hasRenderedFrame || _renderedZoom.Sign <= 0 || _zoom.Sign <= 0 || CanvasHost.ActualWidth <= 0) return;
        // Отношение зумов и сдвиг в ширинах кадра — порядка единицы, но сами зумы и разность
        // центров на глубине вне double: всё считается в FloatExp и лишь в конце в пиксели.
        double scale = (_zoom / _renderedZoom).ToDouble();
        FloatExp currentScale = BaseScale / _zoom;
        double width = CanvasHost.ActualWidth;
        using var precision = CenterPrecisionScope();
        BigFloat currentCenterX = _deepZoomEngaged ? _centerXExact : BigFloat.FromDouble(_centerX);
        BigFloat currentCenterY = _deepZoomEngaged ? _centerYExact : BigFloat.FromDouble(_centerY);
        double translationX = (FloatExp.FromBigFloat(_renderedCenterXExact - currentCenterX) / currentScale * width).ToDouble();
        double translationY = (FloatExp.FromBigFloat(currentCenterY - _renderedCenterYExact) / currentScale * width).ToDouble();
        if (!double.IsFinite(scale) || !double.IsFinite(translationX) || !double.IsFinite(translationY)) return;
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = translationX;
        _previewTranslation.Y = translationY;
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 290);
        ScheduleRender();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && _isFullscreen) ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
        }
        _isFullscreen = !_isFullscreen;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _visualizationTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private sealed class RenderSession(WriteableBitmap bitmap, int tileCount, int renderWidth, int renderHeight,
        CancellationTokenSource cancellation)
    {
        public WriteableBitmap Bitmap { get; } = bitmap;
        public int TileCount { get; } = tileCount;
        public int RenderWidth { get; } = renderWidth;
        public int RenderHeight { get; } = renderHeight;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public int CompletedTiles { get; set; }
        public ConcurrentQueue<TileRenderEvent> Events { get; } = new();
    }

    private readonly record struct TileRenderEvent(bool IsStart, MandelbrotRenderTile Tile, byte[]? Pixels);
}
