using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Универсальное окно трёхмерных фракталов: Мандельбульб, Жюлиабульб, Мандельбокс, губка Менгера,
/// тетраэдр Серпинского и кватернионное Жюлиа. Вид задаётся при создании окна, разметка показывает
/// только параметры выбранной формы. Кадр целиком считает GPU (<see cref="Fractal3DRenderer"/>),
/// поэтому отдельного тайлового прогресса нет: прогресс идёт по горизонтальным полосам кадра.
/// Во время вращения и перемещения кадр считается в половинном разрешении, полный — после остановки.
/// </summary>
public partial class Fractal3DWindow : Window
{
    private const double PanelWidth = 340;
    private const double InteractiveScale = 0.5;
    private const int MaxSsaa = 4;

    private readonly DispatcherTimer _renderTimer = new();
    private readonly Fractal3DRenderer _renderer = new();
    private readonly Fractal3DSaveStore _saveStore;
    private readonly Fractal3DDefinition _definition;
    private readonly IReadOnlyList<Fractal3DState> _presets;

    private CancellationTokenSource? _renderCts;
    private bool _isRendering;
    private bool _updatingUi = true;
    private bool _controlsVisible = true;
    private bool _isFullscreen;
    private bool _isClosing;
    private bool _orbiting;
    private bool _panning;
    private Point _lastPoint;

    private double _yaw;
    private double _pitch;
    private double _distance = 3;
    private double _fieldOfView = 55;
    private Vector3 _target;

    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    public Fractal3DWindow(Fractal3DKind kind)
    {
        InitializeComponent();
        Kind = kind;
        _definition = Fractal3DCatalog.GetDefinition(kind);
        _saveStore = new Fractal3DSaveStore(kind);
        _presets = Fractal3DCatalog.GetPresets(kind);

        Title = _definition.Title;
        PanelTitleText.Text = _definition.PanelTitle;
        KindDescriptionText.Text = _definition.Description;
        ConfigureKindLayout();

        PresetBox.ItemsSource = _presets.Select(preset => preset.SaveName).ToArray();
        PresetBox.SelectedIndex = 0;
        _renderTimer.Tick += RenderTimer_OnTick;
        _updatingUi = false;

        ApplyState(_presets[0]);
        Loaded += (_, _) => ScheduleRender(immediate: true);
    }

    public Fractal3DKind Kind { get; }

    public string DisplayTitle => _definition.Title;

    #region Менеджер сохранений и экспорт

    public Fractal3DState CaptureState(string saveName) => new()
    {
        SaveName = saveName,
        Timestamp = DateTime.Now,
        Kind = Kind,
        Iterations = ReadInt(IterationsBox, "Итерации", 1, 64),
        Power = ReadDouble(PowerBox, "Степень", -32, 32),
        Bailout = ReadDouble(BailoutBox, "Радиус вылета", 1.01, 1e6),
        JuliaCX = ReadDouble(JuliaCXBox, "Первая координата C", -8, 8),
        JuliaCY = ReadDouble(JuliaCYBox, "Вторая координата C", -8, 8),
        JuliaCZ = ReadDouble(JuliaCZBox, "Третья координата C", -8, 8),
        JuliaCW = ReadDouble(JuliaCWBox, "Четвёртая координата C", -8, 8),
        QuaternionSlice = ReadDouble(SliceBox, "Координата среза", -4, 4),
        BoxScale = ReadDouble(BoxScaleBox, "Масштаб свёртки", -8, 8),
        BoxMinRadius = ReadDouble(BoxMinRadiusBox, "Минимальный радиус сферы", 0.01, 4),
        BoxFoldingLimit = ReadDouble(BoxFoldingBox, "Предел свёртки по кубу", 0.1, 8),
        SierpinskiScale = ReadDouble(SierpinskiScaleBox, "Масштаб складывания", 1.05, 8),

        CameraYaw = _yaw,
        CameraPitch = _pitch,
        CameraDistance = _distance,
        TargetX = _target.X,
        TargetY = _target.Y,
        TargetZ = _target.Z,
        FieldOfView = _fieldOfView,

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
        SurfaceColor = SurfaceColorSelector.SelectedColor,
        ColorA = ColorASelector.SelectedColor,
        ColorB = ColorBSelector.SelectedColor,
        ColorScale = ReadDouble(ColorScaleBox, "Масштаб цвета", 0.01, 100),
        ColorOffset = ReadDouble(ColorOffsetBox, "Сдвиг цвета", -100, 100),
        BackgroundTop = BackgroundTopSelector.SelectedColor,
        BackgroundBottom = BackgroundBottomSelector.SelectedColor
    };

    public void LoadState(Fractal3DState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _renderCts?.Cancel();
        _renderTimer.Stop();
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

        _yaw = state.CameraYaw;
        _pitch = Math.Clamp(state.CameraPitch, Fractal3DCamera.MinPitch, Fractal3DCamera.MaxPitch);
        _distance = Math.Max(state.CameraDistance, Fractal3DCamera.MinDistance);
        _fieldOfView = Math.Clamp(state.FieldOfView, 5, 160);
        _target = new Vector3((float)state.TargetX, (float)state.TargetY, (float)state.TargetZ);

        PowerBox.Text = Format(state.Power);
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        BailoutBox.Text = Format(state.Bailout);
        JuliaCXBox.Text = Format(state.JuliaCX);
        JuliaCYBox.Text = Format(state.JuliaCY);
        JuliaCZBox.Text = Format(state.JuliaCZ);
        JuliaCWBox.Text = Format(state.JuliaCW);
        SliceBox.Text = Format(state.QuaternionSlice);
        BoxScaleBox.Text = Format(state.BoxScale);
        BoxMinRadiusBox.Text = Format(state.BoxMinRadius);
        BoxFoldingBox.Text = Format(state.BoxFoldingLimit);
        SierpinskiScaleBox.Text = Format(state.SierpinskiScale);

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
        SurfaceColorSelector.SelectedColor = state.SurfaceColor;
        ColorASelector.SelectedColor = state.ColorA;
        ColorBSelector.SelectedColor = state.ColorB;
        ColorScaleBox.Text = Format(state.ColorScale);
        ColorOffsetBox.Text = Format(state.ColorOffset);
        BackgroundTopSelector.SelectedColor = state.BackgroundTop;
        BackgroundBottomSelector.SelectedColor = state.BackgroundBottom;

        SyncCameraBoxes();
        _updatingUi = false;

        UpdateColoringPanels();
        UpdateCameraText();
        ScheduleRender();
    }

    private void ConfigureKindLayout()
    {
        PowerPanel.Visibility = Collapse(Fractal3DCatalog.UsesPower(Kind));
        JuliaPanel.Visibility = Collapse(Fractal3DCatalog.UsesJuliaConstant(Kind));
        QuaternionPanel.Visibility = Collapse(Kind == Fractal3DKind.QuaternionJulia);
        BoxPanel.Visibility = Collapse(Kind == Fractal3DKind.Mandelbox);
        SierpinskiPanel.Visibility = Collapse(Kind == Fractal3DKind.SierpinskiTetrahedron);
        BailoutPanel.Visibility = Collapse(
            Kind is not (Fractal3DKind.MengerSponge or Fractal3DKind.SierpinskiTetrahedron));
    }

    private static Visibility Collapse(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Fractal3DColoringMode SelectedColoringMode =>
        (Fractal3DColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0, (int)Fractal3DColoringMode.Depth);

    private int SelectedSsaa => SsaaBox.SelectedItem is ComboBoxItem item
        ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture)
        : 1;

    private void UpdateColoringPanels()
    {
        if (MaterialColorPanel is null) return;
        Fractal3DColoringMode mode = SelectedColoringMode;
        MaterialColorPanel.Visibility = Collapse(mode == Fractal3DColoringMode.Material);
        GradientColorPanel.Visibility = Collapse(
            mode is Fractal3DColoringMode.OrbitTrap or Fractal3DColoringMode.Depth);
    }

    private void UpdateCameraText() =>
        CameraText.Text = $"Азимут {_yaw:F1}°, наклон {_pitch:F1}°\n" +
                          $"Расстояние {_distance:G6}, обзор {_fieldOfView:F0}°\n" +
                          $"Цель {_target.X:G5}; {_target.Y:G5}; {_target.Z:G5}";

    private void SyncCameraBoxes()
    {
        YawBox.Text = Format(Math.Round(_yaw, 3));
        PitchBox.Text = Format(Math.Round(_pitch, 3));
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
        if (!_updatingUi) ScheduleRender();
    }

    private void ColoringModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColoringPanels();
        if (!_updatingUi) ScheduleRender();
    }

    private void Camera_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        if (TryReadDouble(YawBox.Text, out double yaw)) _yaw = yaw;
        if (TryReadDouble(PitchBox.Text, out double pitch))
            _pitch = Math.Clamp(pitch, Fractal3DCamera.MinPitch, Fractal3DCamera.MaxPitch);
        if (TryReadDouble(DistanceBox.Text, out double distance) && distance > 0) _distance = distance;
        if (TryReadDouble(FieldOfViewBox.Text, out double fieldOfView) && fieldOfView is >= 5 and <= 160)
            _fieldOfView = fieldOfView;
        if (TryReadDouble(TargetXBox.Text, out double x)) _target.X = (float)x;
        if (TryReadDouble(TargetYBox.Text, out double y)) _target.Y = (float)y;
        if (TryReadDouble(TargetZBox.Text, out double z)) _target.Z = (float)z;

        UpdateCameraText();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => ScheduleRender(immediate: true);

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        Fractal3DState defaults = Fractal3DCatalog.CreateDefaultState(Kind);
        _updatingUi = true;
        _yaw = defaults.CameraYaw;
        _pitch = defaults.CameraPitch;
        _distance = defaults.CameraDistance;
        _fieldOfView = defaults.FieldOfView;
        _target = new Vector3((float)defaults.TargetX, (float)defaults.TargetY, (float)defaults.TargetZ);
        SyncCameraBoxes();
        _updatingUi = false;
        UpdateCameraText();
        ScheduleRender();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForFractal3D(this, _saveStore));

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
        _renderCts?.Cancel();
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = _definition.ExportPrefix,
            WindowTitle = $"Экспорт: {_definition.Title}",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = MaxSsaa,
            RenderAsync = (request, token, progress) =>
                RenderBitmapAsync(state, request.Width, request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, PanelWidth);
        ScheduleRender();
    }

    #endregion

    #region Рендер

    private void ScheduleRender(bool immediate = false)
    {
        if (!IsLoaded || _isClosing) return;
        _renderTimer.Stop();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(immediate ? 1 : _orbiting || _panning ? 30 : 180);
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isClosing) return;
        if (_isRendering)
        {
            ScheduleRender();
            return;
        }

        Fractal3DState state;
        try
        {
            state = CaptureState("preview");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        bool interactive = _orbiting || _panning;
        var watch = Stopwatch.StartNew();
        SetRendering(true, "Рендеринг...");

        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int factor = interactive ? 1 : Math.Clamp(state.Ssaa, 1, MaxSsaa);
            double scale = interactive ? InteractiveScale : 1;
            int width = Math.Max(1, (int)(surface.PixelWidth * scale)) * factor;
            int height = Math.Max(1, (int)(surface.PixelHeight * scale)) * factor;

            var progress = new Progress<int>(value => RenderProgress.Value = value);
            BitmapSource bitmap = await _renderer.RenderAsync(state, width, height, progress, cts.Token);
            if (factor > 1)
            {
                bitmap = await Task.Run(() => BitmapResampler.ResizeLanczos3(
                    bitmap, width / factor, height / factor, cts.Token, null), cts.Token);
            }

            cts.Token.ThrowIfCancellationRequested();
            CanvasImage.Source = bitmap;
            StatusText.Text = interactive
                ? $"Черновой кадр {bitmap.PixelWidth}×{bitmap.PixelHeight} за {watch.Elapsed.TotalSeconds:F3} сек."
                : $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; кадр {bitmap.PixelWidth}×{bitmap.PixelHeight}" +
                  (factor > 1 ? $", сглаживание {factor}×." : ".");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Рендер отменён";
        }
        catch (Exception exception)
        {
            if (_isClosing) return;
            StatusText.Text = "Ошибка рендера";
            CrashLogger.Log("Fractal3DWindow.RenderPreviewAsync", exception);
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
        CancelButton.IsEnabled = value;
        if (!value) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    #endregion

    #region Навигация мышью

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRender();

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.6
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.03
            : 1.15;
        _distance = Math.Clamp(e.Delta > 0 ? _distance / step : _distance * step, 1e-4, 1e5);

        _updatingUi = true;
        SyncCameraBoxes();
        _updatingUi = false;
        UpdateCameraText();
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        BeginInteraction(e.GetPosition(CanvasHost), orbit: true);

    private void CanvasHost_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        BeginInteraction(e.GetPosition(CanvasHost), orbit: false);

    private void CanvasHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) BeginInteraction(e.GetPosition(CanvasHost), orbit: false);
    }

    private void BeginInteraction(Point point, bool orbit)
    {
        _orbiting = orbit;
        _panning = !orbit;
        _lastPoint = point;
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = orbit ? Cursors.ScrollAll : Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbiting && !_panning) return;
        Point current = e.GetPosition(CanvasHost);
        double deltaX = current.X - _lastPoint.X;
        double deltaY = current.Y - _lastPoint.Y;
        _lastPoint = current;

        if (_orbiting)
        {
            _yaw -= deltaX * 0.35;
            _pitch = Math.Clamp(_pitch + deltaY * 0.35,
                Fractal3DCamera.MinPitch, Fractal3DCamera.MaxPitch);
        }
        else
        {
            Fractal3DState state = CaptureCameraOnly();
            Fractal3DCameraBasis camera = Fractal3DCamera.Build(state);
            double unit = Fractal3DCamera.WorldUnitsPerPixel(state, CanvasHost.ActualHeight);
            _target -= camera.Right * (float)(deltaX * unit);
            _target += camera.Up * (float)(deltaY * unit);
        }

        _updatingUi = true;
        SyncCameraBoxes();
        _updatingUi = false;
        UpdateCameraText();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_orbiting && !_panning) return;
        _orbiting = false;
        _panning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender(immediate: true);
    }

    /// <summary>Камера без чтения остальных полей: нужна для панорамирования во время ввода.</summary>
    private Fractal3DState CaptureCameraOnly() => new()
    {
        Kind = Kind,
        CameraYaw = _yaw,
        CameraPitch = _pitch,
        CameraDistance = _distance,
        TargetX = _target.X,
        TargetY = _target.Y,
        TargetZ = _target.Z,
        FieldOfView = _fieldOfView
    };

    #endregion

    #region Окно

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _isFullscreen)) ToggleFullscreen();
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
        _isClosing = true;
        _renderTimer.Stop();
        _renderCts?.Cancel();
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
