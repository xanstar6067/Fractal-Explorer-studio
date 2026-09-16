using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Универсальное окно раздела «Бассейны притяжения»: методы Мюллера, Лагерра и секущих,
/// рациональные отображения, периодические циклы, логистическая карта и физические модели.
/// Режим задаётся при создании окна; разметка
/// показывает только панели выбранного режима. Корни и аттракторы ищутся на UI-потоке при
/// применении формулы и сохраняются в состоянии, а каждый кадр считает свежий движок.
/// </summary>
public partial class BasinExplorerWindow : Window
{
    private const double BaseScale = BasinExplorerEngine.BaseViewWidth;
    private const double MinZoom = 0.001;
    private const double MaxZoom = 1e12;

    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly BasinExplorerEngine _engine;
    private readonly NewtonPaletteManager _paletteManager = new();
    private readonly BasinExplorerSaveStore _saveStore;
    private readonly BasinExplorerDefinition _definition;
    private readonly Random _random = new();
    private readonly IReadOnlyList<BasinExplorerState> _presets;
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private CancellationTokenSource? _renderCts;
    private RenderSession? _activeSession;
    private bool _isRendering;
    private bool _isPanning;
    private bool _isFullscreen;
    private bool _controlsVisible = true;

    /// <summary>Подавляет обработчики изменений, пока окно само заполняет элементы управления.</summary>
    private bool _updatingUi = true;

    /// <summary>Параметр c изменён — перед рендером формулу нужно применить заново.</summary>
    private bool _formulaDirty;

    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Point _lastPanPoint;
    private double _centerX;
    private double _centerY;
    private double _zoom = 1;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedZoom = 1;
    private bool _hasRenderedFrame;

    /// <summary>Фильтр периода из загружаемого состояния — применяется, когда список циклов построен.</summary>
    private int? _pendingPeriodFilter;
    private IReadOnlyList<Complex> _orbitTrace = [];
    private string _appliedFormula = string.Empty;
    private string _appliedNumerator = string.Empty;
    private string _appliedDenominator = string.Empty;
    private Complex _appliedParameterC;

    public BasinExplorerWindow(BasinExplorerKind kind)
    {
        InitializeComponent();
        Kind = kind;
        _definition = BasinExplorerCatalog.GetDefinition(kind);
        _engine = new BasinExplorerEngine(kind);
        _saveStore = new BasinExplorerSaveStore(kind);
        Title = _definition.Title;
        PanelTitleText.Text = _definition.PanelTitle;
        KindDescriptionText.Text = KindDescription(kind);
        NavigationHintText.Text = _definition.Hint;

        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StablePreviewImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StablePreviewImage.RenderTransform = _previewTransform;
        _visualizationTimer.Tick += (_, _) =>
        {
            if (_activeSession is not null) FlushVisualizationEvents(_activeSession, false);
        };
        _renderTimer.Tick += RenderTimer_OnTick;
        for (int count = 1; count <= Environment.ProcessorCount; count++) ThreadsBox.Items.Add(count);
        ThreadsBox.Items.Add("Auto");
        ThreadsBox.SelectedItem = "Auto";

        ConfigureKindLayout();
        ConfigureExtendedOptions();
        ConfigurePlanarOptions();
        _presets = BasinExplorerCatalog.GetPresets(kind);
        PresetBox.ItemsSource = _presets.Select(preset => preset.SaveName).ToArray();
        PresetBox.SelectedIndex = 0;
        _updatingUi = false;
        ApplyState(_presets[0], keepPalette: true, showMessage: false);
        Loaded += (_, _) =>
        {
            UpdateOverlay();
            ScheduleRender();
        };
    }

    public BasinExplorerKind Kind { get; }
    public string DisplayTitle => _definition.Title;

    private bool UsesRoots => _definition.UsesRoots;
    private bool UsesPhysics => BasinExplorerCatalog.UsesPhysics(Kind);
    private bool UsesPlanar => BasinExplorerCatalog.UsesPlanar(Kind);
    private bool IsLogisticParameter => Kind == BasinExplorerKind.ComplexLogistic && LogisticPlaneBox.SelectedIndex == 1;

    #region Save manager and export API

    public BasinExplorerState CaptureState(string saveName)
    {
        EnsureFormulaApplied();
        var state = new BasinExplorerState
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            Kind = Kind,
            MaxIterations = ReadInt(IterationsBox, "Итерации", 1, 100_000),
            Zoom = _zoom,
            CenterX = _centerX,
            CenterY = _centerY,
            ColoringMode = SelectedColoringMode,
            ShadingScale = ReadDouble(ShadingScaleBox, "Шкала яркости", 0.1, 1e6),
            MarkerMode = SelectedMarkerMode,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name),
            Formula = _appliedFormula,
            Numerator = _appliedNumerator,
            Denominator = _appliedDenominator,
            ParameterC = _appliedParameterC
        };

        CaptureExtendedSettings(state);
        if (UsesPlanar)
        {
            state.Planar = _engine.PlanarSettings;
            state.PlanarAttractors = _engine.PlanarAttractors.Select(a => a.Clone()).ToList();
            state.UseSavedPlanarAttractors = true;
            return state;
        }
        if (UsesPhysics) return state;
        if (UsesRoots)
        {
            state.RootTolerance = ReadDouble(RootToleranceBox, "Точность корней", 1e-12, 0.1);
            state.RootSearchRadius = ReadDouble(RootSearchRadiusBox, "Радиус поиска", 0.01, 1e9);
            state.RootSearchMode = SelectedRootSearchMode;
            state.Roots = [.. _engine.Roots];
            CaptureMethodSettings(state);
        }
        else
        {
            state.MaxPeriod = ReadInt(MaxPeriodBox, "Максимальный период", 1, 64);
            state.CycleTolerance = ReadDouble(CycleToleranceBox, "Допуск цикла", 1e-12, 0.1);
            state.AttractorSearchRadius = ReadDouble(AttractorSearchRadiusBox, "Радиус затравок", 0.01, 1e6);
            state.EscapeRadius = ReadDouble(EscapeRadiusBox, "Радиус ухода", 2, 1e150);
            state.InfinityHandling = SelectedInfinityHandling;
            state.PeriodFilter = SelectedPeriodFilter;
            state.Attractors = _engine.Attractors.Where(attractor => !attractor.IsInfinity)
                .Select(attractor => attractor.Clone()).ToList();
            // После неудачной формулы список пуст не по выбору пользователя — кадр ищет циклы сам.
            state.UseSavedAttractors = _engine.IsReady;
        }
        return state;
    }

    private void CaptureMethodSettings(BasinExplorerState state)
    {
        switch (Kind)
        {
            case BasinExplorerKind.Muller:
                state.MullerSeedMode = SelectedMullerSeedMode;
                bool anchors = state.MullerSeedMode == MullerSeedMode.FixedAnchors;
                state.MullerOffset = anchors
                    ? ReadComplexLenient(MullerOffsetRealBox, MullerOffsetImaginaryBox, new Complex(0.25, 0))
                    : ReadNonZeroComplex(MullerOffsetRealBox, MullerOffsetImaginaryBox, "Шаг h");
                state.MullerAnchorA = anchors
                    ? ReadComplex(MullerAnchorARealBox, MullerAnchorAImaginaryBox, "Якорь a")
                    : ReadComplexLenient(MullerAnchorARealBox, MullerAnchorAImaginaryBox, new Complex(-1, 0));
                state.MullerAnchorB = anchors
                    ? ReadComplex(MullerAnchorBRealBox, MullerAnchorBImaginaryBox, "Якорь b")
                    : ReadComplexLenient(MullerAnchorBRealBox, MullerAnchorBImaginaryBox, Complex.One);
                if (anchors && state.MullerAnchorA == state.MullerAnchorB)
                    throw new InvalidOperationException("Якоря a и b должны различаться.");
                break;
            case BasinExplorerKind.Laguerre:
                state.LaguerreAutoDegree = LaguerreAutoDegreeBox.IsChecked == true;
                state.LaguerreDegree = state.LaguerreAutoDegree
                    ? ReadDoubleLenient(LaguerreDegreeBox, 3, 1, 1024)
                    : ReadDouble(LaguerreDegreeBox, "Параметр n", 1, 1024);
                state.LaguerreComparison = SelectedLaguerreComparison;
                break;
            case BasinExplorerKind.Secant:
                state.SecantPlaneMode = SelectedSecantPlaneMode;
                state.SecantFirstPoint = state.SecantPlaneMode == SecantPlaneMode.FixedFirstPoint
                    ? ReadComplex(SecantFirstRealBox, SecantFirstImaginaryBox, "Точка x₀")
                    : ReadComplexLenient(SecantFirstRealBox, SecantFirstImaginaryBox, new Complex(1, 1));
                state.SecantOffset = state.SecantPlaneMode == SecantPlaneMode.OffsetPair
                    ? ReadNonZeroComplex(SecantOffsetRealBox, SecantOffsetImaginaryBox, "Шаг h")
                    : ReadComplexLenient(SecantOffsetRealBox, SecantOffsetImaginaryBox, new Complex(0.25, 0));
                state.SecantHorizontalAxis = (SecantStateAxis)Math.Clamp(SecantHorizontalAxisBox.SelectedIndex, 0, 3);
                state.SecantVerticalAxis = (SecantStateAxis)Math.Clamp(SecantVerticalAxisBox.SelectedIndex, 0, 3);
                bool slice = state.SecantPlaneMode == SecantPlaneMode.StateSlice;
                if (slice && state.SecantHorizontalAxis == state.SecantVerticalAxis)
                    throw new InvalidOperationException("Оси среза должны быть разными координатами.");
                state.SecantBaseX0 = slice
                    ? ReadComplex(SecantBaseX0RealBox, SecantBaseX0ImaginaryBox, "База x₀")
                    : ReadComplexLenient(SecantBaseX0RealBox, SecantBaseX0ImaginaryBox, Complex.Zero);
                state.SecantBaseX1 = slice
                    ? ReadComplex(SecantBaseX1RealBox, SecantBaseX1ImaginaryBox, "База x₁")
                    : ReadComplexLenient(SecantBaseX1RealBox, SecantBaseX1ImaginaryBox, Complex.Zero);
                break;
        }
    }

    public void LoadState(BasinExplorerState state)
    {
        _updatingUi = true;
        PresetBox.SelectedIndex = -1;
        _updatingUi = false;
        ApplyState(state, keepPalette: false, showMessage: true);
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        BasinExplorerState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    /// <summary>
    /// Движок кадра по снятому состоянию. Корни и аттракторы берутся из состояния, поэтому поиск
    /// повторяется только для готовых примеров, у которых их ещё нет.
    /// </summary>
    internal static BasinExplorerEngine CreateEngine(BasinExplorerState state, CancellationToken token = default)
    {
        var engine = new BasinExplorerEngine(state.Kind)
        {
            MaxIterations = state.MaxIterations,
            CenterX = state.CenterX,
            CenterY = state.CenterY,
            Zoom = state.Zoom,
            RootTolerance = state.RootTolerance,
            RootSearchRadius = state.RootSearchRadius,
            RootSearchMode = state.RootSearchMode,
            MullerSeedMode = state.MullerSeedMode,
            MullerOffset = state.MullerOffset,
            MullerAnchorA = state.MullerAnchorA,
            MullerAnchorB = state.MullerAnchorB,
            LaguerreAutoDegree = state.LaguerreAutoDegree,
            LaguerreDegree = state.LaguerreDegree,
            LaguerreComparison = state.LaguerreComparison,
            SecantPlaneMode = state.SecantPlaneMode,
            SecantFirstPoint = state.SecantFirstPoint,
            SecantOffset = state.SecantOffset,
            SecantHorizontalAxis = state.SecantHorizontalAxis,
            SecantVerticalAxis = state.SecantVerticalAxis,
            SecantBaseX0 = state.SecantBaseX0,
            SecantBaseX1 = state.SecantBaseX1,
            ParameterC = state.ParameterC,
            MaxPeriod = state.MaxPeriod,
            CycleTolerance = state.CycleTolerance,
            AttractorSearchRadius = state.AttractorSearchRadius,
            EscapeRadius = state.EscapeRadius,
            InfinityHandling = state.InfinityHandling,
            PeriodFilter = state.PeriodFilter,
            ColoringMode = state.ColoringMode,
            ShadingScale = state.ShadingScale,
            BackgroundColor = state.Palette.BackgroundColor
        };

        engine.LogisticPlane = state.LogisticPlane;
        engine.LogisticSeed = state.LogisticSeed;
        if (engine.IsPlanar)
        {
            engine.ConfigurePlanar(state.Planar, state.Formula, state.UseSavedPlanarAttractors ? state.PlanarAttractors : null, token);
            engine.TargetColors = NewtonPaletteManager.AdjustColors(state.Palette, engine.TargetCount).ToArray();
            return engine;
        }
        if (engine.IsPhysical)
        {
            engine.ConfigurePhysics(state.Physics);
            engine.TargetColors = NewtonPaletteManager.AdjustColors(state.Palette, engine.TargetCount).ToArray();
            return engine;
        }

        bool ok;
        string debug;
        if (BasinExplorerCatalog.UsesRoots(state.Kind))
        {
            bool useSavedRoots = state.Roots.Count > 0 || state.RootSearchMode == NewtonRootSearchMode.ManualOnly;
            ok = engine.SetRootFormula(state.Formula, out debug, !useSavedRoots);
            if (ok && useSavedRoots) engine.ReplaceRoots(state.Roots);
        }
        else
        {
            bool useSaved = state.UseSavedAttractors;
            ok = state.Kind == BasinExplorerKind.ComplexLogistic
                ? engine.SetLogisticMap(out debug, !useSaved)
                : state.Kind == BasinExplorerKind.RationalMap
                ? engine.SetRationalMap(state.Numerator, state.Denominator, out debug, !useSaved)
                : engine.SetMapFormula(state.Formula, out debug, !useSaved);
            if (ok && useSaved && !engine.IsLogisticParameter) engine.ReplaceAttractors(state.Attractors);
        }
        if (!ok) throw new InvalidOperationException(debug);
        engine.TargetColors = NewtonPaletteManager.AdjustColors(state.Palette, engine.TargetCount).ToArray();
        return engine;
    }

    #endregion

    #region Layout and state population

    private static string KindDescription(BasinExplorerKind kind) => kind switch
    {
        BasinExplorerKind.GradientDescent => "Цвет — минимум V(x,y), к которому приходит алгоритм. Сравните GD, momentum, Nesterov и Adam при разных шагах.",
        BasinExplorerKind.ComplexGradientFlow => "Непрерывный спуск по V = ½|f(z)|²: z′ = −f(z)·conj(f′(z)). Цвет — конечный корень; яркость — время.",
        BasinExplorerKind.PolynomialVectorField => "Система x′ = f(x,y), y′ = g(x,y). Цвет — устойчивая точка или предельный цикл; яркость — время приближения.",
        BasinExplorerKind.ComplexLogistic => "zₙ₊₁ = λ·zₙ·(1−zₙ). Бассейны при фиксированном λ и карта притягивающих периодов на плоскости параметра.",
        BasinExplorerKind.MagneticPendulum => "Упрощённый маятник над магнитами: притяжение центров, возвращающая сила подвеса и трение. Цвет показывает, у какого магнита он успокоится.",
        BasinExplorerKind.GravityCenters => "Частица в поле неподвижных центров с настраиваемой массой, трением, начальной скоростью и радиусом захвата.",
        BasinExplorerKind.Muller =>
            "xₙ₊₁ — ближайший корень параболы через три последние точки. Пиксель задаёт последнюю точку тройки; порядок сходимости ≈ 1.84 без производных.",
        BasinExplorerKind.Laguerre =>
            "Шаг n/(G ± √((n−1)(nH − G²))), где G = f'/f, H = G² − f''/f. Для полинома степени n метод сходится кубически и почти из любой точки.",
        BasinExplorerKind.Secant =>
            "xₙ₊₁ = xₙ − f(xₙ)(xₙ − xₙ₋₁)/(f(xₙ) − f(xₙ₋₁)). Состояние — пара точек, поэтому бассейны живут в четырёхмерном пространстве.",
        BasinExplorerKind.RationalMap =>
            "zₙ₊₁ = P(zₙ)/Q(zₙ). Каждый притягивающий цикл притягивает критическую точку (теорема Фату), поэтому поиск циклов начинается с них.",
        _ =>
            "zₙ₊₁ = f(zₙ) с параметром c: притягивающие циклы периодов 1…N, свой цвет каждого цикла, яркость по скорости сходимости, уходящие и нераспознанные орбиты — цвет фона."
    };

    private void ConfigureKindLayout()
    {
        bool rational = Kind == BasinExplorerKind.RationalMap;
        FormulaPanel.Visibility = rational ? Visibility.Collapsed : Visibility.Visible;
        FormulaLabel.Text = Kind == BasinExplorerKind.PeriodicCycles ? "Отображение f(z), можно использовать c" : "Функция f(z)";
        RationalFormulaPanel.Visibility = rational ? Visibility.Visible : Visibility.Collapsed;
        ParameterCPanel.Visibility = UsesRoots ? Visibility.Collapsed : Visibility.Visible;
        RandomFormulaButton.Visibility = UsesRoots ? Visibility.Visible : Visibility.Collapsed;
        RandomFormulaColumn.Width = UsesRoots ? new GridLength(44) : new GridLength(0);
        RootsExpander.Visibility = UsesRoots ? Visibility.Visible : Visibility.Collapsed;
        AttractorsExpander.Visibility = UsesRoots ? Visibility.Collapsed : Visibility.Visible;
        MullerPanel.Visibility = Kind == BasinExplorerKind.Muller ? Visibility.Visible : Visibility.Collapsed;
        LaguerrePanel.Visibility = Kind == BasinExplorerKind.Laguerre ? Visibility.Visible : Visibility.Collapsed;
        SecantPanel.Visibility = Kind == BasinExplorerKind.Secant ? Visibility.Visible : Visibility.Collapsed;
        ((ComboBoxItem)InfinityHandlingBox.Items[0]).Content = rational
            ? "Авто — по степеням P и Q"
            : "Авто — уход, цвет фона";

        if (UsesRoots)
        {
            FillOptions(ColoringModeBox,
            [
                (BasinColoringMode.Basins, "Цвет корня"),
                (BasinColoringMode.ConvergenceSpeed, "Цвет + яркость по скорости сходимости"),
                (BasinColoringMode.OrbitOutcome, "Диагностика: исход орбиты"),
                (BasinColoringMode.IterationCount, "Тепловая карта итераций")
            ]);
            FillOptions(MarkerModeBox,
            [
                (BasinMarkerMode.Hidden, "Скрыть"),
                (BasinMarkerMode.Markers, "Корни и опорные точки"),
                (BasinMarkerMode.MarkersWithLabels, "Корни, точки и координаты")
            ]);
        }
        else
        {
            FillOptions(ColoringModeBox,
            [
                (BasinColoringMode.Basins, "Цвет аттрактора"),
                (BasinColoringMode.ConvergenceSpeed, "Цвет + яркость по скорости сходимости"),
                (BasinColoringMode.CyclePhase, "Фаза внутри цикла"),
                (BasinColoringMode.Period, "Цвет по периоду цикла"),
                (BasinColoringMode.OrbitOutcome, "Диагностика: исход орбиты"),
                (BasinColoringMode.IterationCount, "Тепловая карта итераций")
            ]);
            FillOptions(MarkerModeBox,
            [
                (BasinMarkerMode.Hidden, "Скрыть"),
                (BasinMarkerMode.Markers, "Точки циклов"),
                (BasinMarkerMode.MarkersWithLabels, "Точки циклов и подписи"),
                (BasinMarkerMode.MarkersWithCriticalPoints, "Циклы, подписи и критические точки")
            ]);
        }
    }

    /// <summary>Заполняет элементы управления состоянием и применяет его формулу.</summary>
    private void ApplyState(BasinExplorerState state, bool keepPalette, bool showMessage)
    {
        _renderCts?.Cancel();
        _updatingUi = true;
        try
        {
            PopulateExtendedSettings(state);
            PopulatePlanarSettings(state.Planar);
            FormulaBox.Text = state.Formula;
            NumeratorBox.Text = state.Numerator;
            DenominatorBox.Text = state.Denominator;
            SetComplex(ParameterCRealBox, ParameterCImaginaryBox, state.ParameterC);
            IterationsBox.Text = Math.Clamp(state.MaxIterations, 1, 100_000).ToString(CultureInfo.InvariantCulture);

            RootSearchModeBox.SelectedIndex = Math.Clamp((int)state.RootSearchMode, 0, 2);
            RootToleranceBox.Text = FormatNumber(Math.Clamp(state.RootTolerance, 1e-12, 0.1));
            RootSearchRadiusBox.Text = FormatNumber(Math.Clamp(state.RootSearchRadius, 0.01, 1e9));

            MullerSeedModeBox.SelectedIndex = Math.Clamp((int)state.MullerSeedMode, 0, 2);
            SetComplex(MullerOffsetRealBox, MullerOffsetImaginaryBox, state.MullerOffset);
            SetComplex(MullerAnchorARealBox, MullerAnchorAImaginaryBox, state.MullerAnchorA);
            SetComplex(MullerAnchorBRealBox, MullerAnchorBImaginaryBox, state.MullerAnchorB);

            LaguerreAutoDegreeBox.IsChecked = state.LaguerreAutoDegree;
            LaguerreDegreeBox.Text = FormatNumber(Math.Clamp(state.LaguerreDegree, 1, 1024));
            LaguerreComparisonBox.SelectedIndex = Math.Clamp((int)state.LaguerreComparison, 0, 2);

            SecantPlaneModeBox.SelectedIndex = Math.Clamp((int)state.SecantPlaneMode, 0, 2);
            SetComplex(SecantFirstRealBox, SecantFirstImaginaryBox, state.SecantFirstPoint);
            SetComplex(SecantOffsetRealBox, SecantOffsetImaginaryBox, state.SecantOffset);
            SecantHorizontalAxisBox.SelectedIndex = Math.Clamp((int)state.SecantHorizontalAxis, 0, 3);
            SecantVerticalAxisBox.SelectedIndex = Math.Clamp((int)state.SecantVerticalAxis, 0, 3);
            SetComplex(SecantBaseX0RealBox, SecantBaseX0ImaginaryBox, state.SecantBaseX0);
            SetComplex(SecantBaseX1RealBox, SecantBaseX1ImaginaryBox, state.SecantBaseX1);

            MaxPeriodBox.Text = Math.Clamp(state.MaxPeriod, 1, 64).ToString(CultureInfo.InvariantCulture);
            CycleToleranceBox.Text = FormatNumber(Math.Clamp(state.CycleTolerance, 1e-12, 0.1));
            AttractorSearchRadiusBox.Text = FormatNumber(Math.Clamp(state.AttractorSearchRadius, 0.01, 1e6));
            EscapeRadiusBox.Text = FormatNumber(Math.Clamp(state.EscapeRadius, 2, 1e150));
            InfinityHandlingBox.SelectedIndex = Math.Clamp((int)state.InfinityHandling, 0, 2);
            _pendingPeriodFilter = Math.Max(0, state.PeriodFilter);

            if (!SelectOption(ColoringModeBox, state.ColoringMode))
                SelectOption(ColoringModeBox, BasinColoringMode.ConvergenceSpeed);
            ShadingScaleBox.Text = FormatNumber(Math.Clamp(state.ShadingScale, 0.1, 1e6));
            if (!SelectOption(MarkerModeBox, state.MarkerMode)) SelectOption(MarkerModeBox, BasinMarkerMode.Hidden);

            double zoom = double.IsFinite(state.Zoom) && state.Zoom > 0 ? state.Zoom : 1;
            _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            _centerX = double.IsFinite(state.CenterX) ? state.CenterX : 0;
            _centerY = double.IsFinite(state.CenterY) ? state.CenterY : 0;
            SetZoomText();
        }
        finally
        {
            _updatingUi = false;
        }

        if (!keepPalette) _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        _orbitTrace = [];
        RootSearchRadiusPanel.IsEnabled = SelectedRootSearchMode != NewtonRootSearchMode.ManualOnly;
        UpdateMethodControls();
        UpdatePreviewTransform();

        IReadOnlyList<Complex>? savedRoots = UsesRoots &&
                                             (state.Roots.Count > 0 || state.RootSearchMode == NewtonRootSearchMode.ManualOnly)
            ? state.Roots
            : null;
        IReadOnlyList<BasinAttractor>? savedAttractors = !UsesRoots && state.UseSavedAttractors ? state.Attractors : null;
        if (UsesPlanar) ApplyPlanar(showMessage, true, state.UseSavedPlanarAttractors ? state.PlanarAttractors : null);
        else ApplyFormula(showMessage, savedRoots, savedAttractors);
    }

    private void UpdateMethodControls()
    {
        switch (Kind)
        {
            case BasinExplorerKind.Muller:
            {
                MullerSeedMode mode = SelectedMullerSeedMode;
                MullerOffsetPanel.Visibility = mode == MullerSeedMode.FixedAnchors ? Visibility.Collapsed : Visibility.Visible;
                MullerAnchorsPanel.Visibility = mode == MullerSeedMode.FixedAnchors ? Visibility.Visible : Visibility.Collapsed;
                MullerHintText.Text = mode switch
                {
                    MullerSeedMode.Trailing =>
                        "Точки лежат на луче от пикселя против h: направление шага задаёт анизотропию бассейнов.",
                    MullerSeedMode.FixedAnchors =>
                        "Две точки тройки фиксированы, пиксель — третья. Бассейны зависят от положения якорей; на полотне они отмечены квадратами.",
                    _ => "При малом |h| тройка стягивается в пиксель и метод ведёт себя почти как Ньютон; большие h ломают границы бассейнов."
                };
                break;
            }
            case BasinExplorerKind.Laguerre:
                LaguerreDegreePanel.IsEnabled = LaguerreAutoDegreeBox.IsChecked != true || _engine.PolynomialDegree == 0;
                LaguerreHintText.Text = SelectedLaguerreComparison switch
                {
                    LaguerreComparisonMode.Newton => "Та же формула методом Ньютона — для переключения «до/после».",
                    LaguerreComparisonMode.Disagreement =>
                        "Приглушённый цвет — оба метода пришли к одному корню, яркий — к разным (цвет корня Лагерра), белый — сошёлся только один. Режим раскраски здесь не используется.",
                    _ => _engine.PolynomialDegree > 0 || LaguerreAutoDegreeBox.IsChecked != true
                        ? "При n = 1 шаг совпадает с Ньютоном; n больше степени делает шаг осторожнее."
                        : "Формула не полином: n берётся из поля «Параметр n»."
                };
                break;
            case BasinExplorerKind.Secant:
            {
                SecantPlaneMode mode = SelectedSecantPlaneMode;
                SecantFirstPointPanel.Visibility = mode == SecantPlaneMode.FixedFirstPoint ? Visibility.Visible : Visibility.Collapsed;
                SecantOffsetPanel.Visibility = mode == SecantPlaneMode.OffsetPair ? Visibility.Visible : Visibility.Collapsed;
                SecantSlicePanel.Visibility = mode == SecantPlaneMode.StateSlice ? Visibility.Visible : Visibility.Collapsed;
                SecantHintText.Text = mode switch
                {
                    SecantPlaneMode.OffsetPair => "При h → 0 пара стягивается в одну точку и метод секущих превращается в метод Ньютона.",
                    SecantPlaneMode.StateSlice =>
                        "Полотно — плоскость двух выбранных вещественных координат состояния (x₀, x₁); две другие берутся из базы. Там, где x₀ = x₁, шаг вырожден.",
                    _ => "Первая точка фиксирована, пиксель — вторая. Удачный выбор x₀ — вдали от критических точек f."
                };
                break;
            }
        }
        UpdateColoringHint();
        UpdateExtendedLayout();
    }

    private void UpdateColoringHint()
    {
        BasinColoringMode mode = SelectedColoringMode;
        ShadingScalePanel.IsEnabled = mode is BasinColoringMode.ConvergenceSpeed or BasinColoringMode.CyclePhase or BasinColoringMode.Period;
        ColoringHintText.Text = mode switch
        {
            BasinColoringMode.Basins => UsesRoots
                ? "Каждый корень — цвет палитры; несошедшиеся точки — цвет фона."
                : "Каждый аттрактор — цвет палитры; уходящие и нераспознанные орбиты — цвет фона.",
            BasinColoringMode.CyclePhase =>
                "Оттенок сдвигается по точке цикла, в которую приходит орбита: у бассейна цикла периода p видны p типов компонент.",
            BasinColoringMode.Period => "Бассейны окрашиваются по периоду цикла, а не по конкретному циклу.",
            BasinColoringMode.OrbitOutcome => UsesRoots
                ? "Корни — палитра; циклы 2–8 — отдельные цвета; жёлтый — вырожденный шаг, синий — уход, розовый — NaN/переполнение, серый — лимит итераций."
                : "Аттракторы — палитра; нераспознанные циклы 1–8 — отдельные цвета; синий — уход, розовый — переполнение, серый — лимит итераций.",
            BasinColoringMode.IterationCount =>
                "Логарифмическая шкала числа итераций до сходимости или ухода: синий — быстро, красный — медленно.",
            _ => "Яркость падает вдвое за «шкалу» итераций (для цикла — на период), дальше логарифмически."
        };
    }

    #endregion

    #region Formula, roots and attractors

    private void PresetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = PresetBox.SelectedIndex;
        if (_updatingUi || index < 0 || index >= _presets.Count) return;
        ApplyState(_presets[index], keepPalette: true, showMessage: true);
    }

    private void ApplyFormulaButton_OnClick(object sender, RoutedEventArgs e) => ApplyFormula(showMessage: true);

    private void EnsureFormulaApplied()
    {
        if (_formulaDirty && !ApplyFormula(showMessage: false, scheduleRender: false))
            throw new InvalidOperationException(StatusText.Text);
    }

    /// <summary>
    /// Применяет формулу из полей: разбирает её, ищет корни или аттракторы (либо принимает
    /// переданный сохранённый список) и обновляет списки на панели.
    /// </summary>
    private bool ApplyFormula(bool showMessage, IReadOnlyList<Complex>? suppliedRoots = null,
        IReadOnlyList<BasinAttractor>? suppliedAttractors = null, bool scheduleRender = true)
    {
        if (UsesPlanar) return ApplyPlanar(showMessage, scheduleRender);
        if (UsesPhysics) return ApplyPhysics(showMessage, scheduleRender);
        _formulaDirty = false;
        Complex parameterC = Complex.Zero;
        try
        {
            _engine.MaxIterations = ReadIntLenient(IterationsBox, 200, 1, 100_000);
            if (UsesRoots)
            {
                _engine.RootTolerance = ReadDouble(RootToleranceBox, "Точность корней", 1e-12, 0.1);
                _engine.RootSearchRadius = ReadDouble(RootSearchRadiusBox, "Радиус поиска", 0.01, 1e9);
                _engine.RootSearchMode = SelectedRootSearchMode;
            }
            else
            {
                parameterC = ReadComplex(ParameterCRealBox, ParameterCImaginaryBox, "Параметр c");
                _engine.ParameterC = parameterC;
                _engine.MaxPeriod = ReadInt(MaxPeriodBox, "Максимальный период", 1, 64);
                _engine.CycleTolerance = ReadDouble(CycleToleranceBox, "Допуск цикла", 1e-12, 0.1);
                _engine.AttractorSearchRadius = ReadDouble(AttractorSearchRadiusBox, "Радиус затравок", 0.01, 1e6);
                _engine.EscapeRadius = ReadDouble(EscapeRadiusBox, "Радиус ухода", 2, 1e150);
                _engine.InfinityHandling = SelectedInfinityHandling;
                _engine.LogisticPlane = IsLogisticParameter ? LogisticPlaneMode.Parameter : LogisticPlaneMode.InitialValues;
                _engine.LogisticSeed = ReadComplex(LogisticSeedRealBox, LogisticSeedImaginaryBox, "Начальное z₀");
            }
        }
        catch (InvalidOperationException ex)
        {
            _formulaDirty = true;
            StatusText.Text = ex.Message;
            if (showMessage) MessageBox.Show(this, ex.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        bool ok;
        string debug;
        var stopwatch = Stopwatch.StartNew();
        using (new WaitCursorScope())
        {
            if (UsesRoots)
            {
                ok = _engine.SetRootFormula(FormulaBox.Text.Trim(), out debug, suppliedRoots is null);
                if (ok && suppliedRoots is not null)
                {
                    _engine.ReplaceRoots(suppliedRoots);
                    debug += $"{Environment.NewLine}Загружено/задано корней: {_engine.Roots.Count}";
                }
            }
            else
            {
                ok = Kind == BasinExplorerKind.ComplexLogistic
                    ? _engine.SetLogisticMap(out debug, suppliedAttractors is null)
                    : Kind == BasinExplorerKind.RationalMap
                    ? _engine.SetRationalMap(NumeratorBox.Text.Trim(), DenominatorBox.Text.Trim(), out debug, suppliedAttractors is null)
                    : _engine.SetMapFormula(FormulaBox.Text.Trim(), out debug, suppliedAttractors is null);
                if (ok && suppliedAttractors is not null && !IsLogisticParameter)
                {
                    _engine.ReplaceAttractors(suppliedAttractors);
                    debug = _engine.DebugInfo + $"{Environment.NewLine}Использован сохранённый список: {_engine.Attractors.Count} аттракторов";
                }
            }
        }

        _orbitTrace = [];
        if (!ok)
        {
            _formulaDirty = true;
            DebugOutput.Text = debug;
            StatusText.Text = "Ошибка формулы";
            RefreshTargetsUi();
            if (showMessage)
            {
                string reason = debug.Split('\n').Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;
                MessageBox.Show(this, $"Проверьте формулу.{Environment.NewLine}{reason}", "Ошибка формулы",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        _appliedFormula = FormulaBox.Text.Trim();
        _appliedNumerator = NumeratorBox.Text.Trim();
        _appliedDenominator = DenominatorBox.Text.Trim();
        _appliedParameterC = parameterC;
        DebugOutput.Text = debug;
        RefreshTargetsUi();
        UpdateMethodControls();
        if (IsLogisticParameter)
        {
            StatusText.Text = "Плоскость λ: цвет обозначает период притягивающего цикла. Двойной щелчок открывает его бассейны.";
        }
        else if (UsesRoots)
        {
            StatusText.Text = _engine.Roots.Count == 0
                ? "Формула корректна, но корни не найдены. Увеличьте радиус или добавьте корень вручную."
                : $"Формула применена. {_engine.RootSearchStrategy}";
        }
        else
        {
            int cycles = _engine.Attractors.Count(attractor => !attractor.IsInfinity);
            StatusText.Text = $"Формула применена за {stopwatch.ElapsedMilliseconds} мс. Притягивающих циклов: {cycles}" +
                              (_engine.InfinityIsAttractor ? " + ∞." : ".") +
                              (cycles == 0 ? " Попробуйте другой c, больший период или поиск в текущем виде." : string.Empty);
        }
        if (scheduleRender) ScheduleRender();
        return true;
    }

    private void RefreshTargetsUi()
    {
        if (UsesRoots)
        {
            RootCountText.Text = $"Корней: {_engine.Roots.Count} · {_engine.RootSearchStrategy}";
            RootListBox.ItemsSource = _engine.Roots
                .Select((root, index) => $"{index + 1}. {BasinExplorerFormatting.Complex(root)}")
                .ToArray();
        }
        else
        {
            AttractorCountText.Text = $"Аттракторов: {_engine.Attractors.Count} · критических точек: {_engine.CriticalPoints.Count}";
            AttractorListBox.ItemsSource = _engine.Attractors.Select((attractor, index) => attractor.Describe(index)).ToArray();
            InfinityText.Text = _engine.InfinityDescription;
            RebuildPeriodFilter();
        }
        UpdateOverlay();
    }

    private void RebuildPeriodFilter()
    {
        int requested = _pendingPeriodFilter ?? SelectedPeriodFilter;
        _pendingPeriodFilter = null;
        bool previous = _updatingUi;
        _updatingUi = true;
        try
        {
            PeriodFilterBox.Items.Clear();
            PeriodFilterBox.Items.Add(new ComboBoxItem { Content = "Все периоды", Tag = 0 });
            foreach (int period in IsLogisticParameter ? Enumerable.Range(1, _engine.MaxPeriod) : _engine.Attractors.Where(attractor => !attractor.IsInfinity)
                         .Select(attractor => attractor.Period).Distinct().Order())
                PeriodFilterBox.Items.Add(new ComboBoxItem { Content = $"Только период {period}", Tag = period });
            PeriodFilterBox.SelectedItem = PeriodFilterBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is int period && period == requested) ?? PeriodFilterBox.Items[0];
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private void ParameterC_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        _formulaDirty = true;
        ScheduleRender();
    }

    private void RootSearchModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        RootSearchRadiusPanel.IsEnabled = SelectedRootSearchMode != NewtonRootSearchMode.ManualOnly;
    }

    private void AddRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseComplex(ManualRootBox.Text, out Complex root))
        {
            MessageBox.Show(this, "Введите комплексное число, например 1+2*i или -0.5*i.",
                "Добавление корня", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _engine.ReplaceRoots([.. _engine.Roots, root]);
        ManualRootBox.Clear();
        RefreshTargetsUi();
        StatusText.Text = $"Корень {BasinExplorerFormatting.Complex(root)} добавлен вручную.";
        ScheduleRender();
    }

    private void RemoveRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        int index = RootListBox.SelectedIndex;
        if (index < 0 || index >= _engine.Roots.Count)
        {
            MessageBox.Show(this, "Выберите корень в списке.", "Удаление корня", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Complex removed = _engine.Roots[index];
        _engine.ReplaceRoots(_engine.Roots.Where((_, position) => position != index));
        RefreshTargetsUi();
        StatusText.Text = $"Корень {BasinExplorerFormatting.Complex(removed)} удалён.";
        ScheduleRender();
    }

    private void InfinityHandlingBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        _engine.InfinityHandling = SelectedInfinityHandling;
        RefreshTargetsUi();
        ScheduleRender();
    }

    private bool PrepareAttractorSearch()
    {
        try { EnsureFormulaApplied(); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Поиск циклов", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!_engine.IsReady)
        {
            MessageBox.Show(this, "Сначала примените корректную формулу.", "Поиск циклов", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try
        {
            _engine.MaxIterations = ReadIntLenient(IterationsBox, 200, 1, 100_000);
            _engine.MaxPeriod = ReadInt(MaxPeriodBox, "Максимальный период", 1, 64);
            _engine.CycleTolerance = ReadDouble(CycleToleranceBox, "Допуск цикла", 1e-12, 0.1);
            _engine.EscapeRadius = ReadDouble(EscapeRadiusBox, "Радиус ухода", 2, 1e150);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Поиск циклов", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void SearchFromSeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseComplex(SeedBox.Text, out Complex seed))
        {
            MessageBox.Show(this, "Введите комплексное число или щёлкните правой кнопкой по полотну.",
                "Поиск цикла", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!PrepareAttractorSearch()) return;
        int added;
        using (new WaitCursorScope()) added = _engine.DiscoverAttractors([seed], keepExisting: true, includeDefaultSeeds: false);
        RefreshTargetsUi();
        StatusText.Text = added > 0
            ? $"Из точки {BasinExplorerFormatting.Complex(seed)} найден новый притягивающий цикл."
            : $"Из точки {BasinExplorerFormatting.Complex(seed)} новых притягивающих циклов не найдено: орбита уходит, хаотична, слишком медленна или ведёт к известному циклу.";
        if (added > 0) ScheduleRender();
    }

    private void SearchInViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!PrepareAttractorSearch()) return;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _engine.CenterX = _centerX;
        _engine.CenterY = _centerY;
        _engine.Zoom = _zoom;
        int added;
        using (new WaitCursorScope()) added = _engine.DiscoverAttractorsInView(surface.PixelWidth, surface.PixelHeight);
        RefreshTargetsUi();
        StatusText.Text = added > 0
            ? $"В текущем виде найдено новых циклов: {added}."
            : "В текущем виде новых притягивающих циклов не найдено.";
        if (added > 0) ScheduleRender();
    }

    private void RemoveAttractorButton_OnClick(object sender, RoutedEventArgs e)
    {
        int index = AttractorListBox.SelectedIndex;
        if (index < 0 || index >= _engine.Attractors.Count)
        {
            MessageBox.Show(this, "Выберите цикл в списке.", "Удаление цикла", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_engine.Attractors[index].IsInfinity)
        {
            MessageBox.Show(this, "∞ управляется выбором в списке «Бесконечность».", "Удаление цикла",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _engine.ReplaceAttractors(_engine.Attractors.Where((_, position) => position != index));
        RefreshTargetsUi();
        StatusText.Text = "Цикл удалён: его бассейн будет окрашен цветом фона.";
        ScheduleRender();
    }

    private void RandomFormulaButton_OnClick(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        PresetBox.SelectedIndex = -1;
        _updatingUi = false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string formula = GenerateRandomPolynomial();
            if (!new BasinExplorerEngine(Kind).SetRootFormula(formula, out _, discoverRoots: false)) continue;
            FormulaBox.Text = formula;
            ApplyFormula(showMessage: true);
            return;
        }
    }

    private string GenerateRandomPolynomial()
    {
        string[] offsets = ["-1", "+1", "-0.5", "+0.5", "-i", "+i", "-0.5*i", "+0.5*i", "-(1+i)", "+(1-i)"];
        string[] constants = ["- 1", "+ 1", "- 2", "+ 2", "- i", "+ i", "- (1+i)", "+ (1-i)"];
        string[] coefficients = ["0.5", "0.75", "1.5", "2", "3", "(1+i)", "(0.5-i)"];
        switch (_random.Next(3))
        {
            case 0:
                return $"z^{_random.Next(3, 9)} {constants[_random.Next(constants.Length)]}";
            case 1:
            {
                int high = _random.Next(4, 8);
                int low = _random.Next(1, high - 1);
                return $"z^{high} {(_random.Next(2) == 0 ? "+" : "-")} {coefficients[_random.Next(coefficients.Length)]}*z^{low} {constants[_random.Next(constants.Length)]}";
            }
            default:
                return string.Join("*", Enumerable.Range(0, _random.Next(3, 6)).Select(_ => $"(z{offsets[_random.Next(offsets.Length)]})"));
        }
    }

    #endregion

    #region Handlers of simple parameters

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi) return;
        if (Kind == BasinExplorerKind.ComplexLogistic && sender == MaxPeriodBox) _formulaDirty = true;
        ScheduleRender();
    }

    private void MethodMode_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        UpdateMethodControls();
        _orbitTrace = [];
        UpdateOverlay();
        ScheduleRender();
    }

    private void ColoringModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        UpdateColoringHint();
        UpdateExtendedLayout();
        ScheduleRender();
    }

    private void MarkerModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        UpdateOverlay();
    }

    private void ZoomBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        if (!TryReadDouble(ZoomBox.Text.Trim(), out double zoom) || !(zoom > 0) || !double.IsFinite(zoom)) return;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private MullerSeedMode SelectedMullerSeedMode => (MullerSeedMode)Math.Clamp(MullerSeedModeBox.SelectedIndex, 0, 2);
    private LaguerreComparisonMode SelectedLaguerreComparison => (LaguerreComparisonMode)Math.Clamp(LaguerreComparisonBox.SelectedIndex, 0, 2);
    private SecantPlaneMode SelectedSecantPlaneMode => (SecantPlaneMode)Math.Clamp(SecantPlaneModeBox.SelectedIndex, 0, 2);
    private BasinInfinityHandling SelectedInfinityHandling => (BasinInfinityHandling)Math.Clamp(InfinityHandlingBox.SelectedIndex, 0, 2);
    private NewtonRootSearchMode SelectedRootSearchMode => (NewtonRootSearchMode)Math.Clamp(RootSearchModeBox.SelectedIndex, 0, 2);
    private BasinColoringMode SelectedColoringMode => SelectedOption(ColoringModeBox, BasinColoringMode.ConvergenceSpeed);
    private BasinMarkerMode SelectedMarkerMode => SelectedOption(MarkerModeBox, BasinMarkerMode.Hidden);
    private int SelectedPeriodFilter => PeriodFilterBox.SelectedItem is ComboBoxItem { Tag: int period } ? period : 0;

    private bool ViewIsComplexPlane => !IsLogisticParameter && (Kind != BasinExplorerKind.Secant || SelectedSecantPlaneMode != SecantPlaneMode.StateSlice);

    #endregion

    #region Palette, saves, export

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        try { EnsureFormulaApplied(); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_engine.IsReady)
        {
            MessageBox.Show(this, "Сначала примените корректную формулу.", "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewtonPaletteWindow dialog;
        if (UsesPlanar)
        {
            dialog = new NewtonPaletteWindow(_paletteManager,
                _engine.PlanarAttractors.Select(a => a.Points[0]).ToArray(),
                _engine.PlanarAttractors.Select((a, i) => a.Describe(i)).ToArray(),
                "Цвет фона — уход, лимит и нераспознанные траектории.", showGradientOption: false);
        }
        else if (UsesPhysics || IsLogisticParameter)
        {
            int count = UsesPhysics ? _forceCenters.Count : ReadIntLenient(MaxPeriodBox, 16, 1, 64);
            dialog = new NewtonPaletteWindow(_paletteManager,
                Enumerable.Range(0, count).Select(i => UsesPhysics ? new Complex(_forceCenters[i].X, _forceCenters[i].Y) : Complex.Zero).ToArray(),
                Enumerable.Range(1, count).Select(i => UsesPhysics ? $"Центр {i}" : $"Период {i}").ToArray(),
                "Цвет фона — уход и нераспознанные орбиты.", showGradientOption: false);
        }
        else if (UsesRoots)
        {
            dialog = new NewtonPaletteWindow(_paletteManager, _engine.Roots, null, $"Найдено корней: {_engine.Roots.Count}",
                showGradientOption: false);
        }
        else
        {
            IReadOnlyList<BasinAttractor> attractors = _engine.Attractors;
            dialog = new NewtonPaletteWindow(_paletteManager,
                attractors.Select(attractor => attractor.IsInfinity || attractor.Points.Count == 0 ? Complex.Zero : attractor.Points[0]).ToArray(),
                attractors.Select((attractor, index) => attractor.ShortLabel(index)).ToArray(),
                $"Аттракторов: {attractors.Count}. Цвет фона — уходящие и нераспознанные орбиты.",
                showGradientOption: false);
        }
        dialog.Owner = this;
        dialog.PaletteApplied += (_, _) =>
        {
            UpdateOverlay();
            ScheduleRender();
        };
        dialog.ShowDialog();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForBasinExplorer(this, _saveStore));

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        BasinExplorerState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = _definition.ExportFilePrefix,
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    #endregion

    #region Rendering

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => _ = RenderPreviewAsync();
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        if (_isPanning || _draggedCenter >= 0) return;
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

        if (_formulaDirty && !ApplyFormula(showMessage: false, scheduleRender: false)) return;
        BasinExplorerState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var stopwatch = Stopwatch.StartNew();
        SetRenderingState(true, "Рендеринг бассейнов...");
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int factor = SelectedPreviewSsaaFactor;
            int divisor = UsesPhysics && PhysicsResolutionBox.SelectedItem is ComboBoxItem { Tag: string resolution } ? int.Parse(resolution) : 1;
            if (UsesPlanar && PlanarResolutionBox.SelectedItem is ComboBoxItem { Tag: string planarResolution }) divisor = int.Parse(planarResolution);
            int renderWidth = Math.Max(1, checked(surface.PixelWidth * factor) / divisor);
            int renderHeight = Math.Max(1, checked(surface.PixelHeight * factor) / divisor);
            TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(renderWidth, renderHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            BasinExplorerEngine engine = CreateEngine(state);
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
            _renderedCenterX = state.CenterX;
            _renderedCenterY = state.CenterY;
            _renderedZoom = state.Zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            RenderOverlay.EndSession();
            _activeSession = null;
            string targets = UsesPlanar ? $"Аттракторов: {engine.TargetCount}" : UsesPhysics ? $"Центров: {engine.TargetCount}" : IsLogisticParameter ? $"Периоды 1…{engine.MaxPeriod}" : UsesRoots ? $"Корней: {engine.Roots.Count}" : $"Аттракторов: {engine.Attractors.Count}";
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. {targets}. " +
                              $"Зум {state.Zoom.ToString("G6", CultureInfo.InvariantCulture)}. Стратегия: {strategy}";
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

    private static async Task RenderTilesAsync(BasinExplorerEngine engine, IReadOnlyList<MandelbrotRenderTile> tiles,
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
            else if (entry.Pixels is not null && ProgressiveRenderBitmap.WriteTile(session.Bitmap, entry.Tile, entry.Pixels))
            {
                RenderOverlay.CompleteTile(entry.Tile);
                session.CompletedTiles++;
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
            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
        }
        catch (InvalidOperationException)
        {
            // Во время сворачивания или смены размера разметка может быть кратко недоступна.
        }

        CanvasImage.Source = null;
        _visualizationTimer.Stop();
        RenderOverlay.EndSession();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private async Task<BitmapSource> RenderBitmapAsync(BasinExplorerState state, int width, int height, int ssaaFactor,
        CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaaFactor, 1, 4);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] buffer = new byte[checked(stride * renderHeight)];
        BasinExplorerEngine engine = await Task.Run(() => CreateEngine(state, token), token);
        int threads = GetThreadCount();
        await Task.Run(() => engine.RenderToBuffer(buffer, renderWidth, renderHeight, stride, threads, token,
            value => progress?.Report(factor == 1 ? value : value * 90 / 100)), token);

        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
        source.Freeze();
        if (factor == 1 || token.IsCancellationRequested) return source;
        return await Task.Run(() => BitmapResampler.ResizeLanczos3(source, width, height, token, value => progress?.Report(value)));
    }

    private int GetThreadCount() => ThreadsBox.SelectedItem?.ToString() == "Auto"
        ? Environment.ProcessorCount
        : Math.Max(1, Convert.ToInt32(ThreadsBox.SelectedItem, CultureInfo.InvariantCulture));

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

    #endregion

    #region Canvas navigation

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

        double previousZoom = _zoom;
        double step = WheelZoomStep;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? step : 1 / step), MinZoom, MaxZoom);

        // Точка под курсором остаётся на месте.
        double viewWidthDelta = BaseScale / previousZoom - BaseScale / _zoom;
        _centerX += fractionX * viewWidthDelta;
        _centerY += fractionY / width * viewWidthDelta;

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

    private void SetZoomText()
    {
        bool previous = _updatingUi;
        _updatingUi = true;
        try
        {
            ZoomBox.Text = _zoom.ToString("G8", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleExtendedMouseDown(e)) return;
        _renderTimer.Stop();
        _isPanning = true;
        CommitAndBakePreview();
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (MoveForceCenter(e)) return;
        if (!_isPanning) return;
        Point current = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double viewWidth = BaseScale / _zoom;
        _centerX += (_lastPanPoint.X - current.X) / width * viewWidth;
        _centerY += (current.Y - _lastPanPoint.Y) / width * viewWidth;
        _lastPanPoint = current;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (EndForceCenterDrag()) return;
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    /// <summary>
    /// Правая кнопка: итог орбиты точки под курсором в строке состояния и сама орбита поверх
    /// полотна; в режимах отображений точка заодно становится затравкой для поиска цикла.
    /// </summary>
    private void CanvasHost_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_isPanning) ShowOrbitAt(e.GetPosition(CanvasHost));
    }

    private void ShowOrbitAt(Point canvasPoint)
    {
        (double planeX, double planeY) = ScreenToPlane(canvasPoint);
        BasinExplorerEngine engine;
        try { engine = CreateEngine(CaptureState("orbit")); }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        BasinOrbitResult result = engine.AnalyzePoint(planeX, planeY);
        _orbitTrace = engine.TraceOrbit(planeX, planeY, UsesPhysics || UsesPlanar ? 512 : 160);
        UpdateOverlay();
        StatusText.Text = DescribeOrbit(engine, result, planeX, planeY) +
            (SelectedMarkerMode == BasinMarkerMode.Hidden
                ? " Траектория скрыта: включите маркеры и линии вверху панели."
                : " Esc — скрыть орбиту.");
        if (!UsesRoots) SeedBox.Text = FormatComplexInput(new Complex(planeX, planeY));
    }

    private (double X, double Y) ScreenToPlane(Point point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double unitsPerDip = BaseScale / _zoom / width;
        return (_centerX + (point.X - width / 2) * unitsPerDip, _centerY - (point.Y - height / 2) * unitsPerDip);
    }

    private Point PlaneToScreen(Complex point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double dipsPerUnit = width / (BaseScale / _zoom);
        return new Point(width / 2 + (point.Real - _centerX) * dipsPerUnit, height / 2 - (point.Imaginary - _centerY) * dipsPerUnit);
    }

    private string DescribeOrbit(BasinExplorerEngine engine, BasinOrbitResult result, double planeX, double planeY)
    {
        if (UsesPlanar) return DescribePlanarOrbit(engine, result, planeX, planeY);
        if (UsesPhysics) return $"Точка ({planeX:G5}; {planeY:G5}): " +
            (result.TargetIndex >= 0 ? $"центр {result.TargetIndex + 1}" : result.Outcome == BasinOrbitOutcome.Escaped ? "уход" : "не захвачена") +
            $", время {result.SmoothIterations:G5}, шагов {result.Iterations}.";
        if (IsLogisticParameter) return $"λ = {BasinExplorerFormatting.Complex(new Complex(planeX, planeY))}: " +
            (result.Outcome == BasinOrbitOutcome.Converged ? $"притягивающий период {result.CyclePeriod}" : result.Outcome == BasinOrbitOutcome.Escaped ? "уход" : "цикл не распознан") +
            $", итераций {result.Iterations}.";
        string origin;
        if (Kind == BasinExplorerKind.Secant && engine.SecantPlaneMode == SecantPlaneMode.StateSlice)
        {
            (Complex x0, Complex x1) = engine.SecantSeeds(planeX, planeY);
            origin = $"Состояние (x₀, x₁) = ({BasinExplorerFormatting.Complex(x0)}; {BasinExplorerFormatting.Complex(x1)})";
        }
        else origin = $"Орбита из {BasinExplorerFormatting.Complex(new Complex(planeX, planeY))}";

        string iterations = result.SmoothIterations.ToString("0.#", CultureInfo.InvariantCulture);
        string outcome = result.Outcome switch
        {
            BasinOrbitOutcome.Converged when UsesRoots && result.TargetIndex >= 0 =>
                $"сошлась к корню {result.TargetIndex + 1} ({BasinExplorerFormatting.Complex(engine.Roots[result.TargetIndex])}) за {iterations} итераций.",
            BasinOrbitOutcome.Converged when UsesRoots => "попала в точный ноль функции вне списка корней.",
            BasinOrbitOutcome.Converged when result.TargetIndex >= 0 && engine.Attractors[result.TargetIndex].IsInfinity =>
                $"уходит на ∞ (аттрактор {result.TargetIndex + 1}), радиус пройден за {iterations} итераций.",
            BasinOrbitOutcome.Converged when result.TargetIndex >= 0 =>
                $"приходит в цикл {result.TargetIndex + 1} периода {result.CyclePeriod} (фаза {result.Phase}) за {iterations} итераций.",
            BasinOrbitOutcome.Cycle => $"застряла в нераспознанном цикле периода {result.CyclePeriod}.",
            BasinOrbitOutcome.Degenerate => $"вырожденный шаг (нулевой знаменатель) на итерации {result.Iterations}.",
            BasinOrbitOutcome.Escaped => $"уходит за радиус на итерации {result.Iterations}.",
            BasinOrbitOutcome.NonFinite => $"переполнение или NaN на итерации {result.Iterations}.",
            _ => $"не сошлась за {result.Iterations} итераций."
        };
        return $"{origin}: {outcome}";
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleControlsButton, 310);
        ScheduleRender();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape)
        {
            if (_isFullscreen) ToggleFullscreen();
            else if (_orbitTrace.Count > 0)
            {
                _orbitTrace = [];
                UpdateOverlay();
            }
        }
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

    private void UpdatePreviewTransform()
    {
        UpdateOverlay();
        if (!_hasRenderedFrame || !(_renderedZoom > 0) || CanvasHost.ActualWidth <= 0) return;
        double width = CanvasHost.ActualWidth;
        double currentScale = BaseScale / _zoom;
        double scale = _zoom / _renderedZoom;
        double translationX = (_renderedCenterX - _centerX) / currentScale * width;
        double translationY = (_centerY - _renderedCenterY) / currentScale * width;
        if (!double.IsFinite(scale) || !double.IsFinite(translationX) || !double.IsFinite(translationY)) return;
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = translationX;
        _previewTranslation.Y = translationY;
    }

    #endregion

    #region Overlay

    /// <summary>
    /// Маркеры корней, опорных точек и циклов плюс орбита последней точки под правой кнопкой.
    /// Всё хранится в координатах плоскости и пересчитывается в экранные при каждом сдвиге вида.
    /// </summary>
    private void UpdateOverlay()
    {
        if (MarkerOverlay is null) return;
        MarkerOverlay.Children.Clear();
        if (MarkerOverlay.ActualWidth <= 0 || MarkerOverlay.ActualHeight <= 0 || !_engine.IsReady) return;

        BasinMarkerMode mode = SelectedMarkerMode;
        if (mode == BasinMarkerMode.Hidden) return;
        bool labels = mode is BasinMarkerMode.MarkersWithLabels or BasinMarkerMode.MarkersWithCriticalPoints;
        if (mode != BasinMarkerMode.Hidden && ViewIsComplexPlane)
        {
            if (UsesPlanar) DrawPlanarMarkers(labels);
            else if (UsesPhysics) DrawForceCenters(labels);
            else if (UsesRoots) DrawRootMarkers(labels);
            else DrawAttractorMarkers(labels, mode == BasinMarkerMode.MarkersWithCriticalPoints);
        }
        if (_orbitTrace.Count > 1 && ViewIsComplexPlane) DrawOrbitTrace();
    }

    private void DrawRootMarkers(bool labels)
    {
        IReadOnlyList<Complex> roots = _engine.Roots;
        List<Color> colors = NewtonPaletteManager.AdjustColors(_paletteManager.ActivePalette, roots.Count);
        for (int index = 0; index < roots.Count; index++)
        {
            Color color = colors[index % colors.Count];
            AddCircleMarker(roots[index], color, 13, labels ? $"r{index + 1} = {BasinExplorerFormatting.Complex(roots[index])}" : null);
        }

        if (Kind == BasinExplorerKind.Muller && SelectedMullerSeedMode == MullerSeedMode.FixedAnchors)
        {
            AddSquareMarker(ReadComplexLenient(MullerAnchorARealBox, MullerAnchorAImaginaryBox, new Complex(-1, 0)), "a", labels);
            AddSquareMarker(ReadComplexLenient(MullerAnchorBRealBox, MullerAnchorBImaginaryBox, Complex.One), "b", labels);
        }
        else if (Kind == BasinExplorerKind.Secant && SelectedSecantPlaneMode == SecantPlaneMode.FixedFirstPoint)
        {
            AddSquareMarker(ReadComplexLenient(SecantFirstRealBox, SecantFirstImaginaryBox, new Complex(1, 1)), "x₀", labels);
        }
    }

    private void DrawAttractorMarkers(bool labels, bool criticalPoints)
    {
        IReadOnlyList<BasinAttractor> attractors = _engine.Attractors;
        List<Color> colors = NewtonPaletteManager.AdjustColors(_paletteManager.ActivePalette, attractors.Count);
        for (int index = 0; index < attractors.Count; index++)
        {
            BasinAttractor attractor = attractors[index];
            if (attractor.IsInfinity || attractor.Points.Count == 0) continue;
            Color color = colors[index % colors.Count];
            if (attractor.Points.Count > 1)
            {
                var polygon = new Polygon
                {
                    Stroke = new SolidColorBrush(BasinExplorerFormatting.WithAlpha(color, 220)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = [4, 3],
                    Points = new PointCollection(attractor.Points.Select(point => ClampToOverlay(PlaneToScreen(point))))
                };
                MarkerOverlay.Children.Add(polygon);
            }
            for (int point = 0; point < attractor.Points.Count; point++)
            {
                string? label = labels && point == 0
                    ? $"#{index + 1} · p = {attractor.Period} · {BasinExplorerFormatting.Complex(attractor.Points[0])}"
                    : null;
                AddCircleMarker(attractor.Points[point], color, point == 0 ? 12 : 9, label);
            }
        }

        if (!criticalPoints) return;
        foreach (Complex critical in _engine.CriticalPoints)
        {
            Point screen = PlaneToScreen(critical);
            if (!IsNearOverlay(screen)) continue;
            foreach ((double dx, double dy) in new[] { (1.0, 1.0), (1.0, -1.0) })
            {
                MarkerOverlay.Children.Add(new Line
                {
                    X1 = screen.X - 5 * dx, Y1 = screen.Y - 5 * dy, X2 = screen.X + 5 * dx, Y2 = screen.Y + 5 * dy,
                    Stroke = Brushes.White, StrokeThickness = 2
                });
            }
        }
    }

    private void DrawOrbitTrace()
    {
        var brush = new SolidColorBrush(Color.FromArgb(235, 255, 235, 120));
        brush.Freeze();
        var polyline = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 1.3,
            Points = new PointCollection(_orbitTrace.Where(point => double.IsFinite(point.Real) && double.IsFinite(point.Imaginary))
                .Select(point => ClampToOverlay(PlaneToScreen(point))))
        };
        MarkerOverlay.Children.Add(polyline);
        for (int index = 0; index < _orbitTrace.Count; index++)
        {
            Point screen = PlaneToScreen(_orbitTrace[index]);
            if (!IsNearOverlay(screen)) continue;
            double size = index == 0 ? 8 : 4;
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = index == 0 ? Brushes.White : brush,
                Stroke = index == 0 ? Brushes.Black : null,
                StrokeThickness = 1
            };
            Canvas.SetLeft(dot, screen.X - size / 2);
            Canvas.SetTop(dot, screen.Y - size / 2);
            MarkerOverlay.Children.Add(dot);
        }
    }

    private void AddCircleMarker(Complex point, Color color, double size, string? label)
    {
        Point screen = PlaneToScreen(point);
        if (!IsNearOverlay(screen)) return;
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = 2
        };
        Canvas.SetLeft(marker, screen.X - size / 2);
        Canvas.SetTop(marker, screen.Y - size / 2);
        MarkerOverlay.Children.Add(marker);
        if (label is not null) AddLabel(screen, label, color);
    }

    private void AddSquareMarker(Complex point, string name, bool withCoordinates)
    {
        Point screen = PlaneToScreen(point);
        if (!IsNearOverlay(screen)) return;
        var marker = new System.Windows.Shapes.Rectangle
        {
            Width = 11,
            Height = 11,
            Fill = Brushes.Black,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };
        Canvas.SetLeft(marker, screen.X - 5.5);
        Canvas.SetTop(marker, screen.Y - 5.5);
        MarkerOverlay.Children.Add(marker);
        AddLabel(screen, withCoordinates ? $"{name} = {BasinExplorerFormatting.Complex(point)}" : name, Colors.White);
    }

    private void AddLabel(Point screen, string text, Color accent)
    {
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 24)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock { Foreground = Brushes.White, FontSize = 11, Text = text }
        };
        Canvas.SetLeft(label, screen.X + 9);
        Canvas.SetTop(label, screen.Y - 11);
        MarkerOverlay.Children.Add(label);
    }

    private bool IsNearOverlay(Point screen) =>
        screen.X >= -80 && screen.X <= MarkerOverlay.ActualWidth + 80 &&
        screen.Y >= -30 && screen.Y <= MarkerOverlay.ActualHeight + 30;

    /// <summary>Далёкие точки орбиты обрезаются, чтобы WPF не строил геометрию в миллионы пикселей.</summary>
    private Point ClampToOverlay(Point screen) => new(
        Math.Clamp(screen.X, -4 * MarkerOverlay.ActualWidth, 5 * MarkerOverlay.ActualWidth),
        Math.Clamp(screen.Y, -4 * MarkerOverlay.ActualHeight, 5 * MarkerOverlay.ActualHeight));

    #endregion

    #region Parsing helpers

    private static void FillOptions<T>(ComboBox box, IEnumerable<(T Value, string Text)> options) where T : struct, Enum
    {
        box.Items.Clear();
        foreach ((T value, string text) in options) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedIndex = box.Items.Count > 1 ? 1 : 0;
    }

    private static T SelectedOption<T>(ComboBox box, T fallback) where T : struct, Enum =>
        box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;

    private static bool SelectOption<T>(ComboBox box, T value) where T : struct, Enum
    {
        ComboBoxItem? item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => candidate.Tag is T tag && tag.Equals(value));
        if (item is null) return false;
        box.SelectedItem = item;
        return true;
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ReadDouble(TextBox box, string name, double minimum, double maximum)
    {
        if (TryReadDouble(box.Text, out double value) && double.IsFinite(value) && value >= minimum && value <= maximum)
            return value;
        throw new InvalidOperationException(
            $"{name}: введите число от {FormatNumber(minimum)} до {FormatNumber(maximum)}.");
    }

    private static double ReadDoubleLenient(TextBox box, double fallback, double minimum, double maximum) =>
        TryReadDouble(box.Text, out double value) && double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static int ReadInt(TextBox box, string name, int minimum, int maximum)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
            value >= minimum && value <= maximum)
            return value;
        throw new InvalidOperationException($"{name}: введите целое число от {minimum} до {maximum}.");
    }

    private static int ReadIntLenient(TextBox box, int fallback, int minimum, int maximum) =>
        int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static Complex ReadComplex(TextBox real, TextBox imaginary, string name)
    {
        if (TryReadDouble(real.Text, out double re) && TryReadDouble(imaginary.Text, out double im) &&
            double.IsFinite(re) && double.IsFinite(im))
            return new Complex(re, im);
        throw new InvalidOperationException($"{name}: вещественная и мнимая части должны быть конечными числами.");
    }

    private static Complex ReadNonZeroComplex(TextBox real, TextBox imaginary, string name)
    {
        Complex value = ReadComplex(real, imaginary, name);
        if (value == Complex.Zero) throw new InvalidOperationException($"{name} не должен быть нулевым: все начальные точки совпадут.");
        return value;
    }

    private static Complex ReadComplexLenient(TextBox real, TextBox imaginary, Complex fallback) =>
        TryReadDouble(real.Text, out double re) && TryReadDouble(imaginary.Text, out double im) &&
        double.IsFinite(re) && double.IsFinite(im)
            ? new Complex(re, im)
            : fallback;

    private static void SetComplex(TextBox real, TextBox imaginary, Complex value)
    {
        real.Text = FormatNumber(double.IsFinite(value.Real) ? value.Real : 0);
        imaginary.Text = FormatNumber(double.IsFinite(value.Imaginary) ? value.Imaginary : 0);
    }

    /// <summary>Обычная запись для умеренных чисел и короткая научная (1e-6, 1e+6) для остальных.</summary>
    private static string FormatNumber(double value)
    {
        double magnitude = Math.Abs(value);
        if (value == 0 || (magnitude >= 1e-4 && magnitude < 1e5))
            return value.ToString("0.##########", CultureInfo.InvariantCulture);
        return value.ToString("0.#########e+0", CultureInfo.InvariantCulture);
    }

    private static string FormatComplexInput(Complex value) =>
        $"{value.Real.ToString("G10", CultureInfo.InvariantCulture)}{(value.Imaginary < 0 ? "-" : "+")}" +
        $"{Math.Abs(value.Imaginary).ToString("G10", CultureInfo.InvariantCulture)}*i";

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

    #endregion

    private sealed class WaitCursorScope : IDisposable
    {
        private readonly Cursor? _previous = Mouse.OverrideCursor;

        public WaitCursorScope() => Mouse.OverrideCursor = Cursors.Wait;

        public void Dispose() => Mouse.OverrideCursor = _previous;
    }

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
