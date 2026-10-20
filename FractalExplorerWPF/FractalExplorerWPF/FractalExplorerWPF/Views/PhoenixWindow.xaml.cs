using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Serialization;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class PhoenixWindow : Window
{
    private const decimal BaseScale = 4m;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly MandelbrotPaletteManager _paletteManager = new();
    private readonly PhoenixSaveStore _saveStore = new();
    private CancellationTokenSource? _renderCts;
    private bool _isRendering, _controlsVisible = true;

    /// <summary>
    /// Окно само заполняет поле зума после колеса. Без этого флага обработчик изменения текста
    /// тут же прочитал бы округлённое до восьми цифр значение обратно в зум вида —
    /// уже после того, как по прежнему зуму посчитан сдвиг центра.
    /// </summary>
    private bool _updatingControls;

    /// <summary>
    /// Зум, начиная с которого окно ведёт центр в <see cref="BigFloat"/>. Совпадает с порогом
    /// включения пертурбационного движка в <c>PhoenixRenderer.DeepZoom</c>: смысла вести
    /// точный центр раньше нет, а после — обязательно, иначе движку неоткуда взять положение
    /// области с нужным числом знаков.
    /// </summary>
    private const double DeepZoomThreshold = PhoenixRenderer.DeepZoomThreshold;

    private static readonly FloatExp MinZoom = FloatExp.FromDouble(0.000001);

    /// <summary>
    /// Потолок зума — 1e1000, как у семейства Мандельброта.
    ///
    /// Прежний потолок 1e24 считался измеренным пределом точности, но был свойством проверочного
    /// кадра: там вся область вылетала за радиус на одном шаге, а разброс |z|² по кадру на шаге
    /// решения был меньше 2⁻⁵² относительно — такой кадр не различает никакой рендер, ведущий z
    /// в double. На кадрах со структурой точность пертурбации относительная и с глубиной не
    /// падает; упор был в представлении чисел. Теперь зум и сетка кадра — <see cref="FloatExp"/>,
    /// δ за порогом переходит в FloatExp (см. <c>PhoenixRenderer.Extended</c>), а центр
    /// ведётся в <see cref="BigFloat"/> с адаптивной точностью (<see cref="DeepZoomViewport{T}.CenterPrecisionBits"/>).
    /// </summary>
    private static readonly FloatExp MaxZoom = FloatExp.Pow10(1000);

    private readonly DecimalDeepZoomViewport _viewport = new((double)BaseScale, MinZoom, MaxZoom, DeepZoomThreshold);
    private TiledRenderSession? _activeSession;
    private readonly FullscreenToggle _fullscreen = new();

    public PhoenixWindow()
    {
        InitializeComponent();
        _viewport.AttachPreview(StablePreviewImage, CanvasHost);
        _renderTimer.Tick += RenderTimer_OnTick;
        C1RealBox.Text = "0.56667"; C1ImaginaryBox.Text = "0"; C2RealBox.Text = "-0.5"; C2ImaginaryBox.Text = "0";
        PrimaryPowerBox.Text = "2"; SecondaryPowerBox.Text = "0";
        InitialZRealBox.Text = "0"; InitialZImaginaryBox.Text = "0";
        PreviousRealBox.Text = "0"; PreviousImaginaryBox.Text = "0";
        IterationsBox.Text = "300"; ThresholdBox.Text = "4"; ZoomBox.Text = "1";
        OrbitTrapRadiusBox.Text = "0.5"; OrbitTrapStrengthBox.Text = "1.5";
        StripeFrequencyBox.Text = "3"; StripeStrengthBox.Text = "0.65";
        CycleToleranceBox.Text = "0.0000001"; MaximumPeriodBox.Text = "32";
        RenderThreadsBox.Fill(ThreadsBox);
        PlaneModeBox.SelectedIndex = 0; VariantBox.SelectedIndex = 0; ColoringBox.SelectedIndex = 1;
        OrbitTrapBox.SelectedIndex = 0; SsaaBox.SelectedIndex = 0;
        UpdatePlaneUi(); UpdateColoringPanels();
        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public PhoenixState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        if (!TryRead(C1RealBox.Text, out decimal c1r) || !TryRead(C1ImaginaryBox.Text, out decimal c1i) ||
            !TryRead(C2RealBox.Text, out decimal c2r) || !TryRead(C2ImaginaryBox.Text, out decimal c2i) ||
            !TryRead(InitialZRealBox.Text, out decimal initialZr) || !TryRead(InitialZImaginaryBox.Text, out decimal initialZi) ||
            !TryRead(PreviousRealBox.Text, out decimal previousZr) || !TryRead(PreviousImaginaryBox.Text, out decimal previousZi) ||
            !TryRead(ThresholdBox.Text, out decimal threshold) || threshold is < 2 or > 1000 ||
            !int.TryParse(IterationsBox.Text, out int iterations) || iterations is < 10 or > 100_000 ||
            !int.TryParse(PrimaryPowerBox.Text, out int primaryPower) || primaryPower is < 2 or > 12 ||
            !int.TryParse(SecondaryPowerBox.Text, out int secondaryPower) || secondaryPower is < 0 or > 12 ||
            !TryRead(OrbitTrapRadiusBox.Text, out decimal trapRadius) || trapRadius is < 0 or > 1000 ||
            !TryRead(OrbitTrapStrengthBox.Text, out decimal trapStrength) || trapStrength is <= 0 or > 1000 ||
            !TryRead(StripeFrequencyBox.Text, out decimal stripeFrequency) || stripeFrequency is < 0 or > 1000 ||
            !TryRead(StripeStrengthBox.Text, out decimal stripeStrength) || stripeStrength is < 0 or > 1 ||
            !TryRead(CycleToleranceBox.Text, out decimal cycleTolerance) || cycleTolerance is <= 0 or > 0.1m ||
            !int.TryParse(MaximumPeriodBox.Text, out int maximumPeriod) || maximumPeriod is < 1 or > 64)
            throw new InvalidOperationException("Проверьте C1/C2, степени a (2–12) и b (0–12), итерации, радиус выхода и параметры окраски.");
        (decimal centerX, decimal centerY, string? exactX, string? exactY) = _viewport.CaptureCenter();
        return new PhoenixState
        {
            SaveName = name, Timestamp = DateTime.Now,
            CenterX = centerX, CenterY = centerY, CenterXExact = exactX, CenterYExact = exactY,
            Zoom = _viewport.Zoom,
            Threshold = threshold, Iterations = iterations, C1Real = c1r, C1Imaginary = c1i, C2Real = c2r, C2Imaginary = c2i,
            PlaneMode = GetSelectedEnum(PlaneModeBox, PhoenixPlaneMode.Julia),
            Variant = GetSelectedEnum(VariantBox, PhoenixVariant.Classic),
            PrimaryPower = primaryPower, SecondaryPower = secondaryPower,
            InitialZReal = initialZr, InitialZImaginary = initialZi,
            InitialPreviousReal = previousZr, InitialPreviousImaginary = previousZi,
            ColoringMode = GetSelectedEnum(ColoringBox, PhoenixColoringMode.Smooth),
            OrbitTrapMode = GetSelectedEnum(OrbitTrapBox, PhoenixOrbitTrapMode.Axes),
            OrbitTrapRadius = (double)trapRadius, OrbitTrapStrength = (double)trapStrength,
            StripeFrequency = (double)stripeFrequency, StripeStrength = (double)stripeStrength,
            CycleTolerance = (double)cycleTolerance, MaximumDetectedPeriod = maximumPeriod,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name)
        };
    }

    public void LoadState(PhoenixState state)
    {
        _renderCts?.Cancel();
        _viewport.Restore(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
        C1RealBox.Text = Format(state.C1Real); C1ImaginaryBox.Text = Format(state.C1Imaginary); C2RealBox.Text = Format(state.C2Real); C2ImaginaryBox.Text = Format(state.C2Imaginary);
        PrimaryPowerBox.Text = state.PrimaryPower.ToString(CultureInfo.InvariantCulture); SecondaryPowerBox.Text = state.SecondaryPower.ToString(CultureInfo.InvariantCulture);
        InitialZRealBox.Text = Format(state.InitialZReal); InitialZImaginaryBox.Text = Format(state.InitialZImaginary);
        PreviousRealBox.Text = Format(state.InitialPreviousReal); PreviousImaginaryBox.Text = Format(state.InitialPreviousImaginary);
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture); ThresholdBox.Text = Format(state.Threshold);
        SetZoomText();
        OrbitTrapRadiusBox.Text = state.OrbitTrapRadius.ToString("G15", CultureInfo.InvariantCulture);
        OrbitTrapStrengthBox.Text = state.OrbitTrapStrength.ToString("G15", CultureInfo.InvariantCulture);
        StripeFrequencyBox.Text = state.StripeFrequency.ToString("G15", CultureInfo.InvariantCulture);
        StripeStrengthBox.Text = state.StripeStrength.ToString("G15", CultureInfo.InvariantCulture);
        CycleToleranceBox.Text = state.CycleTolerance.ToString("G15", CultureInfo.InvariantCulture);
        MaximumPeriodBox.Text = state.MaximumDetectedPeriod.ToString(CultureInfo.InvariantCulture);
        SelectByTag(PlaneModeBox, state.PlaneMode); SelectByTag(VariantBox, state.Variant);
        SelectByTag(ColoringBox, state.ColoringMode); SelectByTag(OrbitTrapBox, state.OrbitTrapMode);
        MandelbrotPalette palette = ResolveSavedPalette(state);
        _paletteManager.ActivePalette = palette.Clone(palette.Name);
        UpdatePlaneUi(); UpdateColoringPanels();
        UpdatePreviewTransform(); ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        PhoenixState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(PreparePreviewState(state), width, height, 1, token, progress);

    private PhoenixState PreparePreviewState(PhoenixState state)
    {
        PhoenixState preview = state.Clone();
        MandelbrotPalette palette = ResolveSavedPalette(state);
        preview.Palette = palette.Clone(palette.Name);
        return preview;
    }

    private MandelbrotPalette ResolveSavedPalette(PhoenixState state)
    {
        MandelbrotPalette palette = state.Palette;
        if (!string.IsNullOrWhiteSpace(state.PaletteName) &&
            (palette.Colors.Count == 0 || palette.Name == "Новая палитра"))
            return _paletteManager.Palettes.FirstOrDefault(candidate =>
                candidate.Name.Equals(state.PaletteName, StringComparison.OrdinalIgnoreCase)) ?? _paletteManager.Palettes[0];
        return palette;
    }

    private void ParameterSelector_OnClick(object sender, RoutedEventArgs e)
    {
        PhoenixState state;
        try { state = CaptureState("parameter-explorer"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Параметры Phoenix", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dialog = new PhoenixParameterExplorerWindow(state) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        C1RealBox.Text = Format(dialog.SelectedC1Real); C1ImaginaryBox.Text = Format(dialog.SelectedC1Imaginary);
        C2RealBox.Text = Format(dialog.SelectedC2Real); C2ImaginaryBox.Text = Format(dialog.SelectedC2Imaginary);
        if (dialog.OpenC1AsJulia)
        {
            SelectByTag(PlaneModeBox, PhoenixPlaneMode.Julia);
            ResetView(); UpdatePlaneUi();
        }
        ScheduleRender();
    }

    private void PlaneModeBox_OnChanged(object sender, SelectionChangedEventArgs e) { UpdatePlaneUi(); ScheduleRender(); }
    private void ColoringBox_OnChanged(object sender, SelectionChangedEventArgs e) { UpdateColoringPanels(); ScheduleRender(); }
    private void UpdatePlaneUi()
    {
        if (NavigationHint is null) return;
        bool parameterPlane = GetSelectedEnum(PlaneModeBox, PhoenixPlaneMode.Julia) == PhoenixPlaneMode.ParameterC1;
        NavigationHint.Text = parameterPlane
            ? "Колесо: масштаб (Ctrl — ×10, Shift — точно). ЛКМ: перемещение. Двойной щелчок открывает выбранный C1 как динамическую плоскость. F11: полный экран." +
              (UsesAutomaticParameterStartFromInputs()
                  ? " При b > 0 и нулевых z₀/z₋₁ для невырожденной карты автоматически используется z₀ = 1."
                  : string.Empty)
            : "Колесо: масштаб (Ctrl — ×10, Shift — точно). ЛКМ: перемещение. F11: полноэкранный режим.";
    }

    private bool UsesAutomaticParameterStartFromInputs() =>
        int.TryParse(SecondaryPowerBox.Text, out int secondaryPower) && secondaryPower > 0 &&
        TryRead(InitialZRealBox.Text, out decimal initialZr) && initialZr == 0 &&
        TryRead(InitialZImaginaryBox.Text, out decimal initialZi) && initialZi == 0 &&
        TryRead(PreviousRealBox.Text, out decimal previousZr) && previousZr == 0 &&
        TryRead(PreviousImaginaryBox.Text, out decimal previousZi) && previousZi == 0;

    private void UpdateColoringPanels()
    {
        if (OrbitTrapPanel is null) return;
        PhoenixColoringMode mode = GetSelectedEnum(ColoringBox, PhoenixColoringMode.Smooth);
        OrbitTrapPanel.Visibility = mode == PhoenixColoringMode.OrbitTrap ? Visibility.Visible : Visibility.Collapsed;
        StripePanel.Visibility = mode == PhoenixColoringMode.StripeAverage ? Visibility.Visible : Visibility.Collapsed;
        PeriodPanel.Visibility = mode == PhoenixColoringMode.Period ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Parameter_OnChanged(object sender, EventArgs e) { if (_updatingControls) return; UpdatePlaneUi(); RefreshParameterAtlas(); ScheduleRender(); }
    private void ZoomBox_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (!NumericInput.IsValid(ZoomBox)) return;
        // Выход до всего остального: поле заполняет само окно после колеса, и обратное чтение
        // округлило бы зум уже после того, как по прежнему значению посчитан сдвиг центра.
        if (_updatingControls) return;
        if (FloatExp.TryParse(ZoomBox.Text.Trim().Replace(',', '.'), out FloatExp zoom) && zoom.Sign > 0)
        {
            _viewport.SetZoom(zoom);
            UpdatePreviewTransform();
            ScheduleRender();
        }
    }
    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender(); dialog.ShowDialog();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForPhoenix(this, _saveStore)); }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        PhoenixState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "phoenix",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void ScheduleRender() { if (!IsLoaded) return; if (_isRendering) CommitAndBakePreview(); else _renderCts?.Cancel(); _renderTimer.Stop(); _renderTimer.Start(); }

    /// <summary>
    /// Останавливает текущий рендер и «запекает» то, что уже видно на холсте (сдвинутый
    /// прежний кадр плюс успевшие лечь тайлы), в стабильный предпросмотр. Снимок после
    /// этого считается отрисованным для текущего вида, поэтому дальнейшие зум и
    /// перетаскивание двигают именно его.
    ///
    /// Без этого при зуме терялся уже посчитанный кадр, а при перетаскивании рендер
    /// продолжал идти поверх уезжающего фона.
    /// </summary>
    private void CommitAndBakePreview()
    {
        TiledRenderSession? session = _activeSession;
        _renderCts?.Cancel();
        if (session is null) return;

        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(SavePreviewLayer) is { } baked)
        {
            StablePreviewImage.Source = baked;
            _viewport.MarkRendered();
            UpdatePreviewTransform();
        }
        CanvasImage.Source = null;
        session.End();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }
    private void RenderTimer_OnTick(object? sender, EventArgs e) { _renderTimer.Stop(); _ = RenderPreviewAsync(); }

    private async Task RenderPreviewAsync()
    {
        if (_isRendering) { ScheduleRender(); return; }
        PhoenixState state; try { state = CaptureState("preview"); } catch (Exception ex) { StatusText.Text = ex.Message; return; }
        _renderCts?.Dispose(); _renderCts = new CancellationTokenSource(); CancellationToken token = _renderCts.Token;
        var watch = Stopwatch.StartNew();
        string planeName = state.PlaneMode == PhoenixPlaneMode.Julia ? "динамической плоскости" : "параметрической плоскости C1";
        SetRendering(true, $"Рендеринг {planeName}...");
        TiledRenderSession? session = null;
        try
        {
            int factor = SsaaBox.SelectedItem is ComboBoxItem item ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) : 1;
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int pixelWidth = surface.PixelWidth;
            int pixelHeight = surface.PixelHeight;
            int renderWidth = checked(pixelWidth * factor);
            int renderHeight = checked(pixelHeight * factor);
            TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(renderWidth, renderHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
            _activeSession = session;
            CanvasImage.Source = bitmap;
            await session.RenderTilesAsync(tiles, GetThreadCount(),
                (tile, tileToken) => PhoenixRenderer.RenderTile(state, renderWidth, renderHeight, tile, tileToken), token);
            if (token.IsCancellationRequested) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; return; }
            session.Flush();
            StablePreviewImage.Source = session.Snapshot();
            CanvasImage.Source = null;
            // Центр берём из состояния, которым кадр посчитан, а не из текущего: пока шёл
            // рендер, пользователь мог уже сдвинуть вид.
            _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
            UpdatePreviewTransform();
            session.End();
            _activeSession = null;
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек. {state.PlaneMode}, {state.Variant}, a={state.PrimaryPower}, b={state.SecondaryPower}, зум {FloatExpJsonConverter.ToDisplay(state.Zoom)}. Стратегия: {strategy}.";
        }
        catch (OperationCanceledException) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; }
        catch (Exception ex) { StatusText.Text = "Ошибка рендера"; MessageBox.Show(this, ex.Message, "Phoenix", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { session?.End(); _activeSession = null; SetRendering(false); }
    }

    private Task<BitmapSource> RenderBitmapAsync(PhoenixState state, int width, int height, int ssaa, CancellationToken token, IProgress<int>? progress)
    {
        // Элементы окна принадлежат UI-потоку: число потоков читается до расчёта.
        int threadCount = GetThreadCount();
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4, (pixels, renderWidth, renderHeight, stride, report) =>
            PhoenixRenderer.Render(state, pixels, renderWidth, renderHeight, stride, threadCount, token, report), token, progress);
    }

    private int GetThreadCount() => RenderThreadsBox.Read(ThreadsBox);
    private void SetRendering(bool value, string? status = null) { _isRendering = value; CancelButton.IsEnabled = value; if (!value) RenderProgress.Value = 0; if (status is not null) StatusText.Text = status; }
    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) { UpdatePreviewTransform(); ScheduleRender(); }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Запекаем до изменения зума: снимок должен соответствовать прежнему виду.
        CommitAndBakePreview();
        _viewport.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight,
            _viewport.WheelFactor(e.Delta));
        UpdatePreviewTransform();
        SetZoomText();
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitAndBakePreview();
        Point point = e.GetPosition(CanvasHost);
        if (e.ClickCount >= 2 && GetSelectedEnum(PlaneModeBox, PhoenixPlaneMode.Julia) == PhoenixPlaneMode.ParameterC1)
        {
            (decimal X, decimal Y) selected = _viewport.ScreenToWorld(point, CanvasHost.ActualWidth, CanvasHost.ActualHeight);
            C1RealBox.Text = Format(selected.X); C1ImaginaryBox.Text = Format(selected.Y);
            SelectByTag(PlaneModeBox, PhoenixPlaneMode.Julia);
            ResetView();
            UpdatePlaneUi(); UpdatePreviewTransform(); ScheduleRender(); e.Handled = true;
            return;
        }
        _viewport.BeginPan(point); CanvasHost.CaptureMouse(); Mouse.OverrideCursor = Cursors.SizeAll;
    }
    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport.PanTo(e.GetPosition(CanvasHost), CanvasHost.ActualWidth)) UpdatePreviewTransform();
    }
    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (!_viewport.EndPan()) return; CanvasHost.ReleaseMouseCapture(); Mouse.OverrideCursor = null; ScheduleRender(); }

    private void UpdatePreviewTransform() => _viewport.UpdatePreview();
    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) => FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleControlsButton, 310, ScheduleRender);
    private void Window_OnKeyDown(object sender, KeyEventArgs e) { _fullscreen.HandleKey(this, e.Key); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { _parameterAtlas?.Close(); _renderTimer.Stop(); _activeSession?.End(); _renderCts?.Cancel(); _renderCts?.Dispose(); }
    /// <summary>Возврат к исходному виду: центр в нуле, зум 1.</summary>
    private void ResetView()
    {
        _viewport.Reset(0, 0, FloatExp.One);
        _updatingControls = true; ZoomBox.Text = "1"; _updatingControls = false;
    }

    private static bool TryRead(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    private static string Format(decimal value) => value.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>
    /// Зум показывается восемью значащими цифрами: большое значение уходит в экспоненциальную
    /// запись (8.1707708E+09, за пределами double — 1.2345678e+1000) и помещается в поле.
    /// </summary>
    private void SetZoomText()
    {
        _updatingControls = true;
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_viewport.Zoom);
        _updatingControls = false;
    }

    private static TEnum GetSelectedEnum<TEnum>(ComboBox comboBox, TEnum fallback) where TEnum : struct, Enum
    {
        string? tag = (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse(tag, out TEnum result) ? result : fallback;
    }

    private static void SelectByTag<TEnum>(ComboBox comboBox, TEnum value) where TEnum : struct, Enum
    {
        string expected = value.ToString();
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && string.Equals(comboItem.Tag?.ToString(), expected, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }
    }
}
