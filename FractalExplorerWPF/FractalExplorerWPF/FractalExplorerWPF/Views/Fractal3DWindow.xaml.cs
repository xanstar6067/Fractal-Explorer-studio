using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Универсальное окно трёхмерных фракталов: Мандельбульб, Горящий корабль 3D и их Julia-варианты,
/// гибрид Мандельбульба и Мандельбокса, губка Менгера,
/// тетраэдр Серпинского, кватернионное Жюлиа и аполлонова упаковка сфер. Вид задаётся при создании окна, разметка показывает
/// только параметры выбранной формы. Кадр целиком считает GPU (<see cref="Fractal3DRenderer"/>),
/// поэтому отдельного тайлового прогресса нет: прогресс идёт по горизонтальным полосам кадра.
/// Превью живое: кадровый цикл считает черновик подобранного размера столько раз, сколько успевает,
/// а после остановки достраивает полный кадр и сглаживание. Навигация — в <c>.Navigation.cs</c>.
/// </summary>
public partial class Fractal3DWindow : Window
{
    private const double PanelWidth = 340;
    private const int MaxSsaa = 4;

    /// <summary>Сколько миллисекунд ждать после правки параметра, чтобы не считать каждый символ.</summary>
    private const double ParameterSettleMs = 160;

    /// <summary>Пауза перед ступенью уточнения: даёт шанс продолжить движение без лишнего кадра.</summary>
    private const double RefineDelayMs = 90;

    /// <summary>Во сколько миллисекунд целится живой кадр; от этого подбирается его размер.</summary>
    private const double TargetFrameMs = 33;

    private const double MinDraftScale = 0.15;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Fractal3DRenderer _renderer = new();
    private readonly Fractal3DSaveStore _saveStore;
    private readonly Fractal3DPaletteManager _paletteManager;
    private readonly Fractal3DDefinition _definition;
    private readonly IReadOnlyList<Fractal3DState> _presets;

    private CancellationTokenSource? _renderCts;
    private bool _isRendering;
    private bool _updatingUi = true;
    private bool _controlsVisible = true;
    private bool _isFullscreen;
    private bool _isClosing;

    private bool _loopAttached;
    private bool _wasMoving;
    private bool _suspended;
    private bool _frameRequested;
    private FrameQuality _requestedQuality;
    private FrameQuality _renderingQuality;
    private double _frameDueMs;
    private double _lastLoopMs;
    private double _draftScale = 0.5;
    private Fractal3DState? _lastGoodState;
    private Fractal3DPalette _palette = Fractal3DPalettes.Classic();
    private byte[]? _draftBuffer;
    private byte[]? _fullBuffer;
    private WriteableBitmap? _draftBitmap;
    private WriteableBitmap? _fullBitmap;

    private Quaternion _orientation = Quaternion.Identity;
    private double _distance = 3;
    private double _fieldOfView = 55;
    private Vector3 _target;

    /// <summary>Положение камеры целиком; запись раскладывает его обратно по полям окна.</summary>
    private Fractal3DPose Pose
    {
        get => new(_orientation, _distance, _target);
        set
        {
            _orientation = value.Orientation;
            _distance = Math.Clamp(value.Distance, Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance);
            _target = value.Target;
        }
    }

    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    public Fractal3DWindow(Fractal3DKind kind)
    {
        InitializeComponent();
        Kind = kind;
        _paletteManager = kind == Fractal3DKind.Ifs3D
            ? new Ifs3DPaletteManager()
            : new Fractal3DPaletteManager();
        _definition = Fractal3DCatalog.GetDefinition(kind);
        _saveStore = new Fractal3DSaveStore(kind);
        _presets = Fractal3DCatalog.GetPresets(kind);

        Title = _definition.Title;
        PanelTitleText.Text = _definition.PanelTitle;
        KindDescriptionText.Text = _definition.Description;
        ConfigureKindLayout();

        PresetBox.ItemsSource = _presets.Select(preset => preset.SaveName).ToArray();
        PresetBox.SelectedIndex = 0;
        _updatingUi = false;

        ApplyState(_presets[0]);
        Loaded += (_, _) =>
        {
            ScheduleRender(immediate: true);
            ScheduleJuliabulbMapRender();
        };
    }

    public Fractal3DKind Kind { get; }

    public string DisplayTitle => _definition.Title;

    #region Менеджер сохранений и экспорт

    public Fractal3DState CaptureState(string saveName) => new()
    {
        SaveName = saveName,
        Timestamp = DateTime.Now,
        Kind = Kind,
        Iterations = ReadInt(IterationsBox,
            Kind == Fractal3DKind.ApollonianPacking ? "Поколения сфер" : "Итерации",
            Kind == Fractal3DKind.Ifs3D ? 10_000 : 1,
            Kind == Fractal3DKind.ApollonianPacking ? ApollonianSpherePacking.MaxGeneration :
            Kind == Fractal3DKind.Ifs3D ? 10_000_000 : 64),
        IfsTransforms = CaptureIfsTransforms(),
        Terrain = CaptureTerrain(),
        Power = Fractal3DCatalog.IsBurningShip(Kind) || Kind == Fractal3DKind.Phoenix
            ? ReadInt(PowerBox, "Степень", 2, Kind == Fractal3DKind.Phoenix ? 6 : 16)
            : ReadDouble(PowerBox, "Степень", Kind == Fractal3DKind.BulbBoxHybrid ? 2 : -32,
                Kind == Fractal3DKind.BulbBoxHybrid ? 12 : 32),
        BurningShipFormula = SelectedBurningShipFormula,
        Bailout = ReadDouble(BailoutBox, "Радиус вылета", 1.01, 1e6),
        JuliaCX = ReadDouble(JuliaCXBox, "Первая координата C", -8, 8),
        JuliaCY = ReadDouble(JuliaCYBox, "Вторая координата C", -8, 8),
        JuliaCZ = ReadDouble(JuliaCZBox, "Третья координата C", -8, 8),
        JuliaCW = ReadDouble(JuliaCWBox, "Четвёртая координата C", -8, 8),
        PhoenixSecondaryPower = ReadInt(PhoenixSecondaryPowerBox, "Степень при C₁", 0, 1),
        PhoenixMemoryX = ReadDouble(PhoenixMemoryXBox, "Вещественная часть C₂", -8, 8),
        PhoenixMemoryY = ReadDouble(PhoenixMemoryYBox, "Координата i константы C₂", -8, 8),
        PhoenixMemoryZ = ReadDouble(PhoenixMemoryZBox, "Координата j константы C₂", -8, 8),
        QuaternionSlice = ReadDouble(SliceBox, "Координата среза", -4, 4),
        BoxScale = ReadDouble(BoxScaleBox, "Масштаб свёртки", -8, 8),
        BoxMinRadius = ReadDouble(BoxMinRadiusBox, "Минимальный радиус инверсии", 0.01, 4),
        BoxFoldingLimit = ReadDouble(BoxFoldingBox, "Предел свёртки по кубу", 0.1, 8),
        BoxInversionShape = SelectedBoxInversionShape,
        BoxInversionStretch = BoxInversionStretchSlider.Value,
        BoxInversionPower = BoxInversionPowerSlider.Value,
        HybridMix = ReadDouble(HybridMixBox, "Доля второй операции", 0, 1),
        HybridBulbSteps = ReadInt(HybridBulbStepsBox, "Итерации Мандельбульба подряд", 1, 6),
        HybridBoxSteps = ReadInt(HybridBoxStepsBox, "Итерации Мандельбокса подряд", 1, 6),
        HybridOrder = SelectedHybridOrder,
        SierpinskiScale = ReadDouble(SierpinskiScaleBox, "Масштаб складывания", 1.05, 8),
        CubeThickness = ReadDouble(CubeThicknessBox, "Толщина элементов", 0.5, 1.5),

        CameraYaw = CameraAngles.Yaw,
        CameraPitch = CameraAngles.Pitch,
        CameraRoll = CameraAngles.Roll,
        CameraDistance = _distance,
        TargetX = _target.X,
        TargetY = _target.Y,
        TargetZ = _target.Z,
        FieldOfView = _fieldOfView,

        MotionQuality = SelectedMotionQuality,
        MotionResolution = SelectedMotionResolution,
        NavigationMode = SelectedNavigationMode,
        GameMovementSpeed = GameSpeedSlider.Value,
        GameMouseSensitivity = GameSensitivitySlider.Value,
        RotationInertia = RotationInertiaBox.IsChecked == true,
        AutoRotate = AutoRotateBox.IsChecked == true,
        AutoRotateSpeed = ReadDouble(AutoRotateSpeedBox, "Скорость автовращения", -720, 720),

        MaxSteps = ReadInt(MaxStepsBox, "Шагов луча", 16, 1024),
        Detail = ReadDouble(DetailBox, "Детализация", 0.05, 8),
        MaxDistance = ReadDouble(MaxDistanceBox, "Дальность трассировки", 1, 1000),
        Ssaa = SelectedSsaa,

        LightYaw = ReadDouble(LightYawBox, "Азимут света", -720, 720),
        LightPitch = ReadDouble(LightPitchBox, "Высота света", -89.9, 89.9),
        Ambient = ReadDouble(AmbientBox, "Фоновый свет", 0, 2),
        Specular = ReadDouble(SpecularBox, "Блики", 0, 4),
        SoftShadows = SoftShadowsBox.IsChecked == true,
        ShadowSharpness = ReadDouble(ShadowSharpnessBox, "Жёсткость теней", 1, 128),
        AmbientOcclusion = AmbientOcclusionBox.IsChecked == true,
        AoStrength = ReadDouble(AoStrengthBox, "Сила затенения", 0, 1),

        ColoringMode = SelectedColoringMode,
        ShadingStyle = SelectedShadingStyle,
        EffectStrength = ReadDouble(EffectStrengthBox, "Сила эффекта", 0, 8),
        SurfaceColor = SurfaceColorSelector.SelectedColor,
        Palette = _palette.Clone(),

        // Устаревшие цвета пишутся по краям палитры: файл, открытый сборкой без палитр,
        // покажет тот же градиент из двух цветов, а не чёрно-белую заглушку.
        ColorA = _palette.Colors.Count > 0 ? _palette.Colors[0] : Colors.Black,
        ColorB = _palette.Colors.Count > 0 ? _palette.Colors[^1] : Colors.White,

        ColorScale = ReadDouble(ColorScaleBox, "Масштаб цвета", 0.01, 100),
        ColorOffset = ReadDouble(ColorOffsetBox, "Сдвиг цвета", -100, 100),
        ColorRepeat = SelectedColorRepeat,
        BackgroundTop = BackgroundTopSelector.SelectedColor,
        BackgroundBottom = BackgroundBottomSelector.SelectedColor,
        LightColor = LightColorSelector.SelectedColor,
        SkyLightMix = ReadDouble(SkyLightMixBox, "Влияние неба на свет", 0, 1)
    };

    public void LoadState(Fractal3DState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _renderCts?.Cancel();
        _transition = null;
        ApplyState(state);
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        Fractal3DState state, int width, int height, CancellationToken token, IProgress<int>? progress) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    #endregion

    #region Состояние и элементы управления

    private void ApplyState(Fractal3DState state)
    {
        _updatingUi = true;

        Pose = Fractal3DCamera.Pose(state);
        _fieldOfView = Math.Clamp(state.FieldOfView, 5, 160);
        StopCameraMotion();
        _surfaceDistance = double.NaN;

        MotionQualityBox.SelectedIndex = (int)state.MotionQuality;
        MotionResolutionBox.SelectedIndex = (int)state.MotionResolution;
        NavigationModeBox.SelectedIndex = (int)state.NavigationMode;
        GameSpeedSlider.Value = double.IsFinite(state.GameMovementSpeed)
            ? Math.Clamp(state.GameMovementSpeed, GameSpeedSlider.Minimum, GameSpeedSlider.Maximum) : 1;
        GameSensitivitySlider.Value = double.IsFinite(state.GameMouseSensitivity)
            ? Math.Clamp(state.GameMouseSensitivity, GameSensitivitySlider.Minimum, GameSensitivitySlider.Maximum) : 1;
        UpdateGameSliderLabels();
        UpdateNavigationHelp();
        RotationInertiaBox.IsChecked = state.RotationInertia;
        AutoRotateBox.IsChecked = state.AutoRotate;
        AutoRotateSpeedBox.Text = Format(state.AutoRotateSpeed);

        PowerBox.Text = Format(state.Power);
        BurningShipFormulaBox.SelectedIndex = (int)state.BurningShipFormula;
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        BailoutBox.Text = Format(state.Bailout);
        JuliaCXBox.Text = Format(state.JuliaCX);
        JuliaCYBox.Text = Format(state.JuliaCY);
        JuliaCZBox.Text = Format(state.JuliaCZ);
        JuliaCWBox.Text = Format(state.JuliaCW);
        PhoenixSecondaryPowerBox.Text = state.PhoenixSecondaryPower.ToString(CultureInfo.InvariantCulture);
        PhoenixMemoryXBox.Text = Format(state.PhoenixMemoryX);
        PhoenixMemoryYBox.Text = Format(state.PhoenixMemoryY);
        PhoenixMemoryZBox.Text = Format(state.PhoenixMemoryZ);
        SliceBox.Text = Format(state.QuaternionSlice);
        BoxScaleBox.Text = Format(state.BoxScale);
        BoxMinRadiusBox.Text = Format(state.BoxMinRadius);
        BoxFoldingBox.Text = Format(state.BoxFoldingLimit);
        BoxInversionShapeBox.SelectedIndex = Math.Clamp((int)state.BoxInversionShape, 0,
            (int)BoxInversionShape.RoundedCube);
        BoxInversionStretchSlider.Value = double.IsFinite(state.BoxInversionStretch)
            ? Math.Clamp(state.BoxInversionStretch, 0.5, 2) : 1;
        BoxInversionPowerSlider.Value = double.IsFinite(state.BoxInversionPower)
            ? Math.Clamp(state.BoxInversionPower, 2, 16) : 4;
        UpdateBoxInversionPanels();
        HybridMixBox.Text = Format(state.HybridMix);
        HybridBulbStepsBox.Text = state.HybridBulbSteps.ToString(CultureInfo.InvariantCulture);
        HybridBoxStepsBox.Text = state.HybridBoxSteps.ToString(CultureInfo.InvariantCulture);
        HybridOrderBox.SelectedIndex = (int)state.HybridOrder;
        SierpinskiScaleBox.Text = Format(state.SierpinskiScale);
        CubeThicknessBox.Text = Format(state.CubeThickness);
        LoadIfsTransforms(state.IfsTransforms);
        LoadTerrain(state.Terrain);

        MaxStepsBox.Text = state.MaxSteps.ToString(CultureInfo.InvariantCulture);
        DetailBox.Text = Format(state.Detail);
        MaxDistanceBox.Text = Format(state.MaxDistance);
        SsaaBox.SelectedIndex = Math.Max(0, SsaaBox.Items.OfType<ComboBoxItem>()
            .ToList()
            .FindIndex(item => Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == state.Ssaa));

        LightYawBox.Text = Format(state.LightYaw);
        LightPitchBox.Text = Format(state.LightPitch);
        AmbientBox.Text = Format(state.Ambient);
        SpecularBox.Text = Format(state.Specular);
        SoftShadowsBox.IsChecked = state.SoftShadows;
        ShadowSharpnessBox.Text = Format(state.ShadowSharpness);
        AmbientOcclusionBox.IsChecked = state.AmbientOcclusion;
        AoStrengthBox.Text = Format(state.AoStrength);

        ColoringModeBox.SelectedIndex = (int)state.ColoringMode;
        ShadingStyleBox.SelectedIndex = (int)state.ShadingStyle;
        EffectStrengthBox.Text = Format(state.EffectStrength);
        SurfaceColorSelector.SelectedColor = state.SurfaceColor;
        _palette = state.ResolvePalette();
        RefreshPaletteBox();
        ColorRepeatBox.SelectedIndex = (int)state.ColorRepeat;
        ColorScaleBox.Text = Format(state.ColorScale);
        ColorOffsetBox.Text = Format(state.ColorOffset);
        BackgroundTopSelector.SelectedColor = state.BackgroundTop;
        BackgroundBottomSelector.SelectedColor = state.BackgroundBottom;
        LightColorSelector.SelectedColor = state.LightColor;
        SkyLightMixBox.Text = Format(state.SkyLightMix);

        SyncCameraBoxes();
        UpdateAxisTriad();
        _updatingUi = false;

        UpdateColoringPanels();
        UpdateCameraText();
        ScheduleRender();
        ScheduleJuliabulbMapRender();
    }

    private void ConfigureKindLayout()
    {
        IterationsLabel.Text = Kind == Fractal3DKind.ApollonianPacking
            ? "Поколения сфер (1–5)"
            : Kind == Fractal3DKind.Ifs3D ? "Точки орбиты (10 000–10 000 000)" : "Итерации";
        if (Kind == Fractal3DKind.ApollonianPacking)
        {
            ((ComboBoxItem)ColoringModeBox.Items[(int)Fractal3DColoringMode.OrbitTrap]).Content = "По диаметру сферы";
            ((ComboBoxItem)ColoringModeBox.Items[(int)Fractal3DColoringMode.CrossTrap]).Content = "По положению сферы";
            ((ComboBoxItem)ColoringModeBox.Items[(int)Fractal3DColoringMode.IterationIndex]).Content = "По масштабу сферы";
            ((ComboBoxItem)ColoringModeBox.Items[(int)Fractal3DColoringMode.Escape]).Content = "По радиусу сферы";
        }
        if (Kind is Fractal3DKind.Ifs3D or Fractal3DKind.Terrain)
        {
            // These four sources require orbit/escape metadata that a density volume does not contain.
            foreach (Fractal3DColoringMode mode in new[]
            {
                Fractal3DColoringMode.OrbitTrap,
                Fractal3DColoringMode.CrossTrap,
                Fractal3DColoringMode.IterationIndex,
                Fractal3DColoringMode.Escape
            })
                ((ComboBoxItem)ColoringModeBox.Items[(int)mode]).Visibility = Visibility.Collapsed;
        }
        TerrainPanel.Visibility = Collapse(Kind == Fractal3DKind.Terrain);
        IterationsLabel.Visibility = IterationsBox.Visibility = Collapse(Kind != Fractal3DKind.Terrain);
        if (Kind == Fractal3DKind.Terrain)
            foreach (Fractal3DShadingStyle style in new[] { Fractal3DShadingStyle.Glow, Fractal3DShadingStyle.Density, Fractal3DShadingStyle.Translucent })
                ((ComboBoxItem)ShadingStyleBox.Items[(int)style]).Visibility = Visibility.Collapsed;
        PowerPanel.Visibility = Collapse(Fractal3DCatalog.UsesPower(Kind));
        PhoenixPowerPanel.Visibility = PhoenixMemoryPanel.Visibility = Collapse(Kind == Fractal3DKind.Phoenix);
        BurningShipFormulaPanel.Visibility = Collapse(Fractal3DCatalog.IsBurningShip(Kind));
        PowerLabel.Text = Kind == Fractal3DKind.Phoenix ? "Основная степень p (целая, 2–6)" :
            Fractal3DCatalog.IsBurningShip(Kind) ? "Степень n (целая, 2–16)" :
            Kind == Fractal3DKind.BulbBoxHybrid ? "Степень Мандельбульба (2–12)" : "Степень n";
        JuliaPanel.Visibility = Collapse(Fractal3DCatalog.UsesJuliaConstant(Kind));
        if (Kind == Fractal3DKind.Phoenix)
            ((TextBlock)JuliaPanel.Children[0]).Text = "Константа C₁ (вещественная; i; j)";
        QuaternionPanel.Visibility = Collapse(Kind == Fractal3DKind.QuaternionJulia);
        JuliabulbPickerPanel.Visibility = Collapse(Kind is Fractal3DKind.Juliabulb or Fractal3DKind.BurningShipJulia);
        if (Kind == Fractal3DKind.BurningShipJulia)
            JuliaPickerButton.Content = "Выбрать C на Горящем корабле 3D";
        BoxPanel.Visibility = Collapse(Kind is Fractal3DKind.Mandelbox or Fractal3DKind.BulbBoxHybrid);
        HybridPanel.Visibility = Collapse(Kind == Fractal3DKind.BulbBoxHybrid);
        SierpinskiPanel.Visibility = Collapse(Kind == Fractal3DKind.SierpinskiTetrahedron);
        CubeThicknessPanel.Visibility = Collapse(Kind is Fractal3DKind.Vicsek or Fractal3DKind.CantorDust);
        IfsPanel.Visibility = Collapse(Kind == Fractal3DKind.Ifs3D);
        RayQualityGrid.Visibility = Collapse(Kind is not (Fractal3DKind.Ifs3D or Fractal3DKind.Terrain));
        MaxDistanceLabel.Visibility = Collapse(Kind != Fractal3DKind.Ifs3D);
        MaxDistanceBox.Visibility = Collapse(Kind != Fractal3DKind.Ifs3D);
        if (Kind == Fractal3DKind.Ifs3D)
            PaletteManagerButton.ToolTip = "Отдельный редактор палитр конструктора объёмных IFS";
        BailoutPanel.Visibility = Collapse(
            Kind is not (Fractal3DKind.MengerSponge or Fractal3DKind.Vicsek or Fractal3DKind.CantorDust or Fractal3DKind.SierpinskiTetrahedron or Fractal3DKind.ApollonianPacking or Fractal3DKind.Ifs3D or Fractal3DKind.Terrain));
    }

    private static Visibility Collapse(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Fractal3DColoringMode SelectedColoringMode =>
        (Fractal3DColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0, (int)Fractal3DColoringMode.Steps);

    private Fractal3DShadingStyle SelectedShadingStyle =>
        (Fractal3DShadingStyle)Math.Clamp(ShadingStyleBox.SelectedIndex, 0, (int)Fractal3DShadingStyle.Translucent);

    private Fractal3DColorRepeat SelectedColorRepeat =>
        (Fractal3DColorRepeat)Math.Clamp(ColorRepeatBox.SelectedIndex, 0, (int)Fractal3DColorRepeat.Mirror);

    private BurningShip3DFormula SelectedBurningShipFormula =>
        (BurningShip3DFormula)Math.Clamp(BurningShipFormulaBox.SelectedIndex, 0,
            (int)BurningShip3DFormula.SphericalFullFold);

    private Hybrid3DOrder SelectedHybridOrder =>
        (Hybrid3DOrder)Math.Clamp(HybridOrderBox.SelectedIndex, 0, (int)Hybrid3DOrder.BoxFirst);

    private BoxInversionShape SelectedBoxInversionShape =>
        (BoxInversionShape)Math.Clamp(BoxInversionShapeBox.SelectedIndex, 0,
            (int)BoxInversionShape.RoundedCube);

    private void BoxInversionShape_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBoxInversionPanels();
        Parameter_OnChanged(sender, e);
    }

    private void UpdateBoxInversionPanels()
    {
        if (BoxInversionStretchPanel is null || BoxInversionPowerPanel is null) return;
        BoxInversionStretchPanel.Visibility = Collapse(SelectedBoxInversionShape is
            BoxInversionShape.Ellipsoid or BoxInversionShape.RoundedCube);
        BoxInversionPowerPanel.Visibility = Collapse(SelectedBoxInversionShape == BoxInversionShape.RoundedCube);
    }

    private (double Yaw, double Pitch, double Roll) CameraAngles => Fractal3DCamera.Angles(_orientation);

    private Fractal3DMotionQuality SelectedMotionQuality =>
        (Fractal3DMotionQuality)Math.Clamp(MotionQualityBox.SelectedIndex, 0, (int)Fractal3DMotionQuality.Draft);

    private Fractal3DMotionResolution SelectedMotionResolution =>
        (Fractal3DMotionResolution)Math.Clamp(MotionResolutionBox.SelectedIndex, 0, (int)Fractal3DMotionResolution.Half);

    private int SelectedSsaa => SsaaBox.SelectedItem is ComboBoxItem item
        ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture)
        : 1;

    private void UpdateColoringPanels()
    {
        if (MaterialColorPanel is null) return;
        Fractal3DColoringMode mode = SelectedColoringMode;
        MaterialColorPanel.Visibility = Collapse(mode == Fractal3DColoringMode.Material);
        PalettePanel.Visibility = Collapse(Fractal3DCatalog.UsesPalette(mode) ||
            SelectedShadingStyle is Fractal3DShadingStyle.Glow or Fractal3DShadingStyle.Density);
        UpdatePalettePreview();
        UpdateShadingHint();
    }

    /// <summary>Полоска под списком палитр: те же цвета, что уйдут в шейдер.</summary>
    private void UpdatePalettePreview()
    {
        List<Color> colors = _palette.Colors;
        if (colors.Count == 0)
        {
            PalettePreview.Background = Brushes.Transparent;
            return;
        }
        if (colors.Count == 1)
        {
            PalettePreview.Background = new SolidColorBrush(colors[0]);
            return;
        }
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < colors.Count; index++)
        {
            if (_palette.IsGradient)
            {
                brush.GradientStops.Add(new GradientStop(colors[index], index / (double)(colors.Count - 1)));
                continue;
            }
            brush.GradientStops.Add(new GradientStop(colors[index], index / (double)colors.Count));
            brush.GradientStops.Add(new GradientStop(colors[index], (index + 1) / (double)colors.Count));
        }
        PalettePreview.Background = brush;
    }

    private void UpdateShadingHint() => ShadingStyleHint.Text = SelectedShadingStyle switch
    {
        Fractal3DShadingStyle.Clay => "Мягкий обёрнутый свет без бликов; складки затенены сильнее обычного.",
        Fractal3DShadingStyle.Metal => "В поверхности отражается небо, поэтому цвета фона становятся частью фигуры.",
        Fractal3DShadingStyle.Glow => "Луч по дороге копит близость к поверхности: складки светятся и в пустоте.",
        Fractal3DShadingStyle.Density => "Луч проходит фигуру насквозь — поверхности нет, есть накопленная плотность.",
        Fractal3DShadingStyle.Studio => "Свет берётся от нормали в осях камеры: фигура читается с любой стороны.",
        Fractal3DShadingStyle.Toon => "Свет ступенями и тёмная обводка силуэта; сила эффекта задаёт число ступеней.",
        Fractal3DShadingStyle.Translucent => "Свет, пришедший с изнанки, подсвечивает тонкие места насквозь.",
        _ => "Рассеянный свет, блик, мягкая тень и затенение складок — вид по умолчанию."
    };

    /// <summary>
    /// Список палитр окна. Палитра открытого вида может быть не из библиотеки — например,
    /// пришла из старого сохранения; тогда она становится первой строкой, чтобы список не
    /// показывал пустоту вместо того, что видно на экране.
    /// </summary>
    private void RefreshPaletteBox()
    {
        var items = new List<Fractal3DPalette>(_paletteManager.Palettes);
        Fractal3DPalette? listed = items.FirstOrDefault(
            palette => palette.Name.Equals(_palette.Name, StringComparison.OrdinalIgnoreCase));
        if (listed is null)
        {
            listed = _palette;
            items.Insert(0, listed);
        }

        bool updating = _updatingUi;
        _updatingUi = true;
        PaletteBox.ItemsSource = items;
        PaletteBox.SelectedItem = listed;
        _updatingUi = updating;
        UpdatePalettePreview();
    }

    /// <summary>Ставит палитру на вид и, если она задаёт окружение, заодно фон, свет и материал.</summary>
    private void ApplyPalette(Fractal3DPalette palette)
    {
        _palette = palette.Clone();
        bool updating = _updatingUi;
        _updatingUi = true;
        if (_palette.OverridesEnvironment)
        {
            BackgroundTopSelector.SelectedColor = _palette.BackgroundTop;
            BackgroundBottomSelector.SelectedColor = _palette.BackgroundBottom;
            LightColorSelector.SelectedColor = _palette.LightColor;
            SurfaceColorSelector.SelectedColor = _palette.SurfaceColor;
        }
        _updatingUi = updating;
        RefreshPaletteBox();
        if (!_updatingUi) ScheduleRender();
    }

    private void UpdateCameraText()
    {
        (double yaw, double pitch, double roll) = CameraAngles;
        CameraText.Text = $"Азимут {yaw:F1}°, наклон {pitch:F1}°, крен {roll:F1}°\n" +
                          $"Расстояние {_distance:G6}, обзор {_fieldOfView:F0}°\n" +
                          $"Цель {_target.X:G5}; {_target.Y:G5}; {_target.Z:G5}\n" +
                          (double.IsNaN(_surfaceDistance)
                              ? "Под курсором фон"
                              : $"До поверхности под курсором {_surfaceDistance:G5}");
    }

    private void SyncCameraBoxes()
    {
        (double yaw, double pitch, double roll) = CameraAngles;
        YawBox.Text = Format(Math.Round(yaw, 3));
        PitchBox.Text = Format(Math.Round(pitch, 3));
        RollBox.Text = Format(Math.Round(roll, 3));
        DistanceBox.Text = Format(Math.Round(_distance, 6));
        FieldOfViewBox.Text = Format(Math.Round(_fieldOfView, 3));
        TargetXBox.Text = Format(Math.Round(_target.X, 6));
        TargetYBox.Text = Format(Math.Round(_target.Y, 6));
        TargetZBox.Text = Format(Math.Round(_target.Z, 6));
    }

    #endregion

    #region Обработчики панели

    private void PresetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || PresetBox.SelectedIndex < 0 || PresetBox.SelectedIndex >= _presets.Count) return;
        ApplyState(_presets[PresetBox.SelectedIndex].Clone());
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ShadingStyleBox)) UpdateColoringPanels();
        if (!_updatingUi)
        {
            ScheduleRender();
            if (ReferenceEquals(sender, PowerBox) || ReferenceEquals(sender, IterationsBox) ||
                ReferenceEquals(sender, BailoutBox) || ReferenceEquals(sender, BurningShipFormulaBox))
                ScheduleJuliabulbMapRender();
            if (ReferenceEquals(sender, JuliaCXBox) || ReferenceEquals(sender, JuliaCYBox) ||
                ReferenceEquals(sender, JuliaCZBox))
                ScheduleJuliabulbMapRender();
        }
    }

    private void ColoringModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColoringPanels();
        if (!_updatingUi) ScheduleRender();
    }

    private void PaletteBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || PaletteBox.SelectedItem is not Fractal3DPalette palette) return;
        if (ReferenceEquals(palette, _palette)) return;
        ApplyPalette(palette);
    }

    private void PaletteManagerButton_OnClick(object sender, RoutedEventArgs e) => SuspendLive(() =>
    {
        Fractal3DPaletteWindow window = Kind == Fractal3DKind.Ifs3D
            ? new Ifs3DPaletteWindow((Ifs3DPaletteManager)_paletteManager, _palette, RenderPalettePreviewAsync)
            : new Fractal3DPaletteWindow(_paletteManager, _palette, RenderPalettePreviewAsync);
        window.Owner = this;
        window.PaletteApplied += (_, palette) => ApplyPalette(palette);
        window.ShowDialog();
        RefreshPaletteBox();
    });

    /// <summary>
    /// Кадр для менеджера палитр: та же фигура, тот же ракурс и те же настройки, что в окне, но
    /// с примеряемой палитрой. Живой цикл на это время остановлен, поэтому устройство свободно.
    /// </summary>
    private Task<BitmapSource> RenderPalettePreviewAsync(
        Fractal3DPalette palette, int width, int height, CancellationToken token)
    {
        Fractal3DState state = CaptureState("palette");
        state.Palette = palette.Clone();
        state.Ssaa = 1;
        if (palette.OverridesEnvironment)
        {
            state.BackgroundTop = palette.BackgroundTop;
            state.BackgroundBottom = palette.BackgroundBottom;
            state.LightColor = palette.LightColor;
            state.SurfaceColor = palette.SurfaceColor;
        }
        return _renderer.RenderAsync(state, width, height, null, token);
    }

    private void Camera_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        (double yaw, double pitch, double roll) = CameraAngles;
        if (TryReadDouble(YawBox.Text, out double typedYaw)) yaw = typedYaw;
        if (TryReadDouble(PitchBox.Text, out double typedPitch)) pitch = Math.Clamp(typedPitch, -90, 90);
        if (TryReadDouble(RollBox.Text, out double typedRoll)) roll = typedRoll;
        _orientation = Fractal3DCamera.Orientation(yaw, pitch, roll);
        if (TryReadDouble(DistanceBox.Text, out double distance) && distance > 0) _distance = distance;
        if (TryReadDouble(FieldOfViewBox.Text, out double fieldOfView) && fieldOfView is >= 5 and <= 160)
            _fieldOfView = fieldOfView;
        if (TryReadDouble(TargetXBox.Text, out double x)) _target.X = (float)x;
        if (TryReadDouble(TargetYBox.Text, out double y)) _target.Y = (float)y;
        if (TryReadDouble(TargetZBox.Text, out double z)) _target.Z = (float)z;
        StopCameraMotion();
        InvalidateCursorHit();

        UpdateCameraText();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => ScheduleRender(immediate: true);

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        Fractal3DState defaults = Fractal3DCatalog.CreateDefaultState(Kind);
        _fieldOfView = Math.Clamp(defaults.FieldOfView, 5, 160);
        BeginTransition(Fractal3DCamera.Pose(defaults));
    }

    private void LevelHorizonButton_OnClick(object sender, RoutedEventArgs e) => LevelHorizon();

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SuspendLive(() => SaveManagerWindow.Open(this, SaveManagerConfigurations.ForFractal3D(this, _saveStore)));

    /// <summary>
    /// Останавливает живое превью на время модального окна: менеджер сохранений и экспорт считают
    /// свои кадры тем же устройством, и делить его с кадровым циклом незачем.
    /// </summary>
    private void SuspendLive(Action action)
    {
        _suspended = true;
        DetachLoop();
        _renderCts?.Cancel();
        try
        {
            action();
        }
        finally
        {
            _suspended = false;
            RequestFrame(FrameQuality.Draft);
        }
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        Fractal3DState state;
        try
        {
            state = CaptureState("export");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Параметры экспорта",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        SuspendLive(() => ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = _definition.ExportPrefix,
            WindowTitle = $"Экспорт: {_definition.Title}",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = MaxSsaa,
            RenderAsync = (request, token, progress) =>
                RenderBitmapAsync(state, request.Width, request.Height, request.SsaaFactor, token, progress)
        }));
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, PanelWidth);
        ScheduleRender();
    }

    #endregion

    #region Рендер

    /// <summary>Ступени уточнения: черновик по ходу движения, затем полный кадр и сглаживание.</summary>
    private enum FrameQuality
    {
        Draft,
        Full,
        Antialiased
    }

    /// <summary>
    /// Просит пересчитать кадр. Запросы сливаются: побеждает более ранний срок и более грубая
    /// ступень, поэтому движение всегда прерывает начатое уточнение.
    /// </summary>
    private void RequestFrame(FrameQuality quality, double delayMs = 0)
    {
        if (!IsLoaded || _isClosing || _suspended) return;

        // Движение прерывает только уточнение: длинный полный кадр не должен держать поворот
        // мыши. А уже начатый живой кадр обязан досчитаться — иначе каждое событие мыши отменяло
        // бы кадр, который начался от предыдущего, и картинка стояла бы до паузы в движении.
        if (_isRendering && quality == FrameQuality.Draft && _renderingQuality != FrameQuality.Draft)
            _renderCts?.Cancel();

        double due = _clock.Elapsed.TotalMilliseconds + delayMs;
        if (_frameRequested)
        {
            _requestedQuality = (FrameQuality)Math.Min((int)_requestedQuality, (int)quality);
            _frameDueMs = Math.Min(_frameDueMs, due);
        }
        else
        {
            _requestedQuality = quality;
            _frameDueMs = due;
            _frameRequested = true;
        }
        AttachLoop();
    }

    private void ScheduleRender(bool immediate = false) =>
        RequestFrame(FrameQuality.Draft, immediate ? 0 : ParameterSettleMs);

    private void AttachLoop()
    {
        if (_loopAttached || _isClosing) return;
        _loopAttached = true;
        _lastLoopMs = _clock.Elapsed.TotalMilliseconds;
        CompositionTarget.Rendering += Loop_OnRendering;
    }

    private void DetachLoop()
    {
        if (!_loopAttached) return;
        _loopAttached = false;
        CompositionTarget.Rendering -= Loop_OnRendering;
    }

    /// <summary>
    /// Кадровый цикл живого превью: на каждом такте композиции двигает анимацию камеры и, как
    /// только предыдущий кадр посчитан, сразу запускает следующий. Когда считать нечего, цикл
    /// отцепляется и окно перестаёт что-либо тратить.
    /// </summary>
    private void Loop_OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        double seconds = Math.Clamp((now - _lastLoopMs) / 1000, 0, 0.1);
        _lastLoopMs = now;

        AdvanceAnimation(seconds);

        // Движение кончилось — доводим кадр. Мышь отпускают не только кнопкой: так же кончаются
        // инерция, перелёт и автовращение, и в каждом случае показанным остаётся живой кадр.
        bool moving = IsMoving;
        if (_wasMoving && !moving) RequestFrame(FrameQuality.Full, RefineDelayMs);
        _wasMoving = moving;

        if (_isRendering) return;
        if (!_frameRequested)
        {
            if (!IsMoving) DetachLoop();
            return;
        }
        if (now < _frameDueMs) return;

        FrameQuality quality = _requestedQuality;
        _frameRequested = false;
        _ = RenderFrameAsync(quality);
    }

    private async Task RenderFrameAsync(FrameQuality quality)
    {
        if (_isClosing) return;

        Fractal3DState state;
        try
        {
            state = CaptureState("preview");
            _lastGoodState = state;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        bool moving = IsMoving;

        // Черновик во весь холст, да ещё и без движения, ничем не отличается от полного кадра:
        // считаем его сразу полным, чтобы не гонять ту же работу дважды и честно назвать результат.
        double draftScale = DraftScale;
        if (quality == FrameQuality.Draft && !moving && draftScale >= 1) quality = FrameQuality.Full;
        _renderingQuality = quality;

        var watch = Stopwatch.StartNew();
        SetRendering(true, quality == FrameQuality.Draft ? null : "Рендеринг...");

        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int ssaa = Math.Clamp(state.Ssaa, 1, MaxSsaa);
            double scale = quality == FrameQuality.Draft ? draftScale : 1;
            bool simplified = false;
            int width = Math.Max(1, (int)Math.Round(surface.PixelWidth * scale));
            int height = Math.Max(1, (int)Math.Round(surface.PixelHeight * scale));

            if (quality == FrameQuality.Antialiased)
            {
                var progress = new Progress<int>(value => RenderProgress.Value = value);
                BitmapSource bitmap = await RenderBitmapAsync(state, width, height, ssaa, cts.Token, progress);
                cts.Token.ThrowIfCancellationRequested();
                RenderOptions.SetBitmapScalingMode(CanvasImage, BitmapScalingMode.HighQuality);
                CanvasImage.Source = bitmap;
                StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; кадр {width}×{height}, " +
                                  $"сглаживание {ssaa}×.";
            }
            else
            {
                bool draft = quality == FrameQuality.Draft;
                Fractal3DState frameState = draft && moving ? ApplyMotionQuality(state) : state;
                simplified = !ReferenceEquals(frameState, state);
                IProgress<int>? progress = draft
                    ? null
                    : new Progress<int>(value => RenderProgress.Value = value);

                Fractal3DPixels frame = await _renderer.RenderPixelsAsync(
                    frameState, width, height, draft ? _draftBuffer : _fullBuffer, progress, cts.Token);
                byte[] buffer = frame.Buffer;

                WriteableBitmap target;
                if (draft)
                {
                    _draftBuffer = buffer;
                    _draftBitmap = EnsureBitmap(_draftBitmap, width, height);
                    target = _draftBitmap;
                }
                else
                {
                    _fullBuffer = buffer;
                    _fullBitmap = EnsureBitmap(_fullBitmap, width, height);
                    target = _fullBitmap;
                }

                // Кадр уступил место движению камеры: показывать половину нечего, следующий уже
                // в очереди. Это обычный ход, поэтому ни исключения, ни сообщения здесь нет.
                if (!frame.Completed) return;

                target.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
                RenderOptions.SetBitmapScalingMode(CanvasImage,
                    draft ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
                CanvasImage.Source = target;

                double elapsedMs = watch.Elapsed.TotalMilliseconds;
                if (draft)
                {
                    if (moving && SelectedMotionResolution == Fractal3DMotionResolution.Adaptive)
                        AdaptDraftScale(elapsedMs);
                    StatusText.Text = $"Живой кадр {width}×{height} · {elapsedMs:F0} мс " +
                                      $"({1000 / Math.Max(elapsedMs, 1):F0} к/с)";
                }
                else
                {
                    StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; кадр {width}×{height}.";
                }
            }

            RequestRefinement(quality, scale, ssaa, simplified);
        }
        catch (OperationCanceledException)
        {
            // Прерванное ради движения уточнение — рабочий ход, а не событие для пользователя.
            if (!_frameRequested) StatusText.Text = "Рендер отменён";
        }
        catch (Exception exception)
        {
            if (_isClosing) return;
            StatusText.Text = "Ошибка рендера";
            CrashLogger.Log("Fractal3DWindow.RenderFrameAsync", exception);
            MessageBox.Show(this, exception.Message, _definition.Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
            SetRendering(false);
        }
    }

    /// <summary>
    /// Следующая ступень лесенки, если камера уже стоит и показанному кадру есть что добавить:
    /// он мельче холста или посчитан с упрощённым в движении светом.
    /// </summary>
    private void RequestRefinement(FrameQuality quality, double scale, int ssaa, bool simplified)
    {
        if (IsMoving) return;
        if (quality == FrameQuality.Draft && (scale < 1 || simplified))
            RequestFrame(FrameQuality.Full, RefineDelayMs);
        else if (quality != FrameQuality.Antialiased && ssaa > 1)
            RequestFrame(FrameQuality.Antialiased, RefineDelayMs);
    }

    /// <summary>Доля холста для живого кадра: подобранная по времени или заданная в разделе «Навигация».</summary>
    private double DraftScale => SelectedMotionResolution switch
    {
        Fractal3DMotionResolution.Full => 1,
        Fractal3DMotionResolution.ThreeQuarters => 0.75,
        Fractal3DMotionResolution.Half => 0.5,
        _ => _draftScale
    };

    /// <summary>
    /// Размер живого кадра подбирается по времени предыдущего: цель — <see cref="TargetFrameMs"/>.
    /// Шаг округляется до 0.05, иначе растровое полотно пересоздавалось бы на каждом кадре.
    /// </summary>
    private void AdaptDraftScale(double frameMs)
    {
        double factor = Math.Clamp(Math.Sqrt(TargetFrameMs / Math.Max(frameMs, 1)), 0.55, 1.6);
        double scale = Math.Clamp(_draftScale * factor, MinDraftScale, 1);
        _draftScale = Math.Round(scale * 20) / 20;
    }

    /// <summary>Чем жертвует живой кадр в движении; выбирается в разделе «Навигация».</summary>
    private Fractal3DState ApplyMotionQuality(Fractal3DState state)
    {
        Fractal3DMotionQuality quality = SelectedMotionQuality;
        if (quality == Fractal3DMotionQuality.Full) return state;

        Fractal3DState draft = state.Clone();
        draft.SoftShadows = false;
        if (quality == Fractal3DMotionQuality.Draft)
        {
            draft.AmbientOcclusion = false;
            if (Kind == Fractal3DKind.Ifs3D) return draft;
            draft.MaxSteps = Math.Max(48, (int)(state.MaxSteps * 0.6));
            draft.Detail = Math.Min(8, state.Detail * 1.5);
        }
        return draft;
    }

    private static WriteableBitmap EnsureBitmap(WriteableBitmap? cache, int width, int height) =>
        cache is not null && cache.PixelWidth == width && cache.PixelHeight == height
            ? cache
            : new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

    private async Task<BitmapSource> RenderBitmapAsync(
        Fractal3DState state, int width, int height, int ssaa, CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaa, 1, MaxSsaa);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        IProgress<int>? scaled = progress is null
            ? null
            : new Progress<int>(value => progress.Report(factor == 1 ? value : value * 90 / 100));

        BitmapSource bitmap = await _renderer.RenderAsync(state, renderWidth, renderHeight, scaled, token);
        if (factor == 1 || token.IsCancellationRequested) return bitmap;

        return await Task.Run(() => BitmapResampler.ResizeLanczos3(bitmap, width, height, token,
            value => progress?.Report(90 + value / 10)), token);
    }

    private void SetRendering(bool value, string? status = null)
    {
        _isRendering = value;
        if (CancelButton.IsEnabled != value) CancelButton.IsEnabled = value;
        if (!value) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    #endregion

    #region Навигация

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        RequestFrame(FrameQuality.Draft, RefineDelayMs);

    #endregion

    #region Окно

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleGameKeyDown(e)) return;
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _isFullscreen))
        {
            ToggleFullscreen();
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
        if (_drag != DragMode.None) CanvasHost.ReleaseMouseCapture();
        _isClosing = true;
        DetachLoop();
        _renderCts?.Cancel();
        DisposeJuliabulbMap();
        Mouse.OverrideCursor = null;
        // Освобождение ждёт выхода из текущей полосы кадра, поэтому уводим его с UI-потока.
        Fractal3DRenderer renderer = _renderer;
        Task.Run(renderer.Dispose);
    }

    #endregion

    #region Чтение значений

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ReadDouble(TextBox box, string name, double minimum, double maximum)
    {
        if (!TryReadDouble(box.Text, out double value) || !double.IsFinite(value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Параметр «{name}» должен быть числом от {minimum:G8} до {maximum:G8}.");
        }
        return value;
    }

    private static int ReadInt(TextBox box, string name, int minimum, int maximum)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Параметр «{name}» должен быть целым числом от {minimum} до {maximum}.");
        }
        return value;
    }

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

    #endregion
}
