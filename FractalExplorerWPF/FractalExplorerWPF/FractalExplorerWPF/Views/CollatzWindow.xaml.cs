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
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class CollatzWindow : Window
{
    private const decimal BaseScale = 4m;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly CollatzPaletteManager _paletteManager = new();
    private readonly CollatzSaveStore _saveStore = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private CancellationTokenSource? _renderCts;
    private TiledRenderSession? _activeSession;
    private bool _isRendering, _panning, _isFullscreen, _controlsVisible = true, _hasRenderedFrame,
        _updatingControls;
    private Point _lastPanPoint;
    private decimal _centerX, _centerY;
    private double _zoom = 1;
    private double _renderedZoom = 1;

    // Ступень BigFloat: выше DeepZoomThreshold центр ведётся в произвольной точности, а
    // _centerX/_centerY остаются ближайшим decimal — для показа и для старого формата
    // сохранений. Центр отрисованного кадра нужен в той же точности, иначе разность
    // «текущий центр − отрисованный» тонет в 28 цифрах и грубый предпросмотр перестаёт
    // двигаться на глубине.
    private BigFloat _centerXExact, _centerYExact;
    private BigFloat _renderedCenterXExact, _renderedCenterYExact;
    private bool _deepZoomEngaged;

    /// <summary>Зум, с которого включается ступень BigFloat — тот же порог, что и у рендера.</summary>
    private const double DeepZoomThreshold = 1e10;

    private const double MinZoom = 1e-15;

    /// <summary>
    /// Потолок зума. Прежний 1e15 был недостижим на практике: и double-, и decimal-ступень
    /// считали тригонометрию через Math.Cos/Math.Sin, поэтому картинка разваливалась уже
    /// около 1e10. Ступень BigFloat считает и координаты, и саму формулу с точностью,
    /// подобранной под глубину; ограничение теперь не в точности, а во времени кадра —
    /// каждый шаг орбиты стоит десятки микросекунд.
    /// </summary>
    private const double MaxZoom = 1e50;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    public CollatzWindow()
    {
        InitializeComponent();
        _updatingControls = true;
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StablePreviewImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StablePreviewImage.RenderTransform = _previewTransform;
        _renderTimer.Tick += RenderTimer_OnTick;
        IterationsBox.Text = "150";
        ThresholdBox.Text = "100";
        ZoomBox.Text = "1";
        PParameterBox.Text = "3";
        QRealParameterBox.Text = "0";
        QImaginaryParameterBox.Text = "0";
        VariationBox.SelectedIndex = 0;
        ColoringModeBox.SelectedIndex = 0;
        EscapeSmoothingBox.SelectedIndex = 1;
        ArgumentCyclesBox.Text = "1";
        MagnitudeScaleBox.Text = "1";
        TrapScaleBox.Text = "4";
        CycleToleranceBox.Text = "1e-6";
        MaximumPeriodBox.Text = "32";
        DensityExposureBox.Text = "1";
        DensitySampleStepBox.Text = "2";
        DensityEscapedOnlyBox.IsChecked = true;
        InteriorFillModeBox.SelectedIndex = 0;
        CustomInteriorColorSelector.SelectedColor = Colors.Black;
        SsaaBox.SelectedIndex = 0;
        for (int count = 1; count <= Environment.ProcessorCount; count++) ThreadsBox.Items.Add(count);
        ThreadsBox.Items.Add("Auto");
        ThreadsBox.SelectedItem = "Auto";
        UpdateVariationControls();
        UpdateColoringControls();
        _updatingControls = false;
        Loaded += (_, _) => ScheduleRender();
    }

    public CollatzState CaptureState(string name)
    {
        if (!int.TryParse(IterationsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations is < 10 or > 100_000)
            throw new InvalidOperationException("Итерации должны быть целым числом от 10 до 100000.");
        if (!TryRead(ThresholdBox.Text, out decimal threshold) || threshold is < 2 or > 10_000)
            throw new InvalidOperationException("Порог выхода должен быть от 2 до 10000.");
        if (!TryRead(PParameterBox.Text, out decimal p) || p is < -100 or > 100)
            throw new InvalidOperationException("Параметр p должен быть от −100 до 100.");
        if (!TryRead(QRealParameterBox.Text, out decimal qReal) || qReal is < -100 or > 100)
            throw new InvalidOperationException("Re(q) должна быть от −100 до 100.");
        if (!TryRead(QImaginaryParameterBox.Text, out decimal qImaginary) || qImaginary is < -100 or > 100)
            throw new InvalidOperationException("Im(q) должна быть от −100 до 100.");
        double argumentCycles = ReadDouble(ArgumentCyclesBox.Text, "обороты аргумента", 0.1, 20);
        double magnitudeScale = ReadDouble(MagnitudeScaleBox.Text, "масштаб модуля", 0.01, 20);
        double trapScale = ReadDouble(TrapScaleBox.Text, "чувствительность ловушки", 0.01, 100);
        double cycleTolerance = ReadDouble(CycleToleranceBox.Text, "допуск цикла", 1e-12, 0.1);
        int maximumPeriod = ReadInt(MaximumPeriodBox.Text, "максимальный период", 1, 64);
        double densityExposure = ReadDouble(DensityExposureBox.Text, "экспозиция плотности", 0.1, 10);
        int densitySampleStep = ReadInt(DensitySampleStepBox.Text, "шаг начальных точек", 1, 8);
        return new CollatzState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            CenterX = _deepZoomEngaged ? _centerXExact.ToDecimalClamped() : _centerX,
            CenterY = _deepZoomEngaged ? _centerYExact.ToDecimalClamped() : _centerY,
            CenterXExact = _deepZoomEngaged ? _centerXExact.ToInvariantString() : null,
            CenterYExact = _deepZoomEngaged ? _centerYExact.ToInvariantString() : null,
            Zoom = _zoom,
            Threshold = threshold,
            Iterations = iterations,
            Variation = (CollatzVariation)Math.Clamp(VariationBox.SelectedIndex, 0,
                (int)CollatzVariation.GeneralizedPQ),
            PParameter = p,
            QRealParameter = qReal,
            QImaginaryParameter = qImaginary,
            ColoringMode = (CollatzColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
                (int)CollatzColoringMode.PeriodDetection),
            UseSmoothColoring = EscapeSmoothingBox.SelectedIndex == 1,
            ArgumentCycles = argumentCycles,
            MagnitudeScale = magnitudeScale,
            TrapScale = trapScale,
            CycleTolerance = cycleTolerance,
            MaximumDetectedPeriod = maximumPeriod,
            OrbitDensityExposure = densityExposure,
            OrbitDensitySampleStep = densitySampleStep,
            OrbitDensityEscapedOnly = DensityEscapedOnlyBox.IsChecked == true,
            InteriorFillMode = (CollatzInteriorFillMode)Math.Clamp(InteriorFillModeBox.SelectedIndex, 0,
                (int)CollatzInteriorFillMode.Custom),
            CustomInteriorColor = CustomInteriorColorSelector.SelectedColor,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name)
        };
    }

    public void LoadState(CollatzState state)
    {
        _renderCts?.Cancel();
        _updatingControls = true;
        _centerX = state.CenterX;
        _centerY = state.CenterY;
        _zoom = Math.Clamp(state.Zoom, MinZoom, MaxZoom);
        _deepZoomEngaged = false;
        if (state.CenterXExact is { Length: > 0 } exactX && state.CenterYExact is { Length: > 0 } exactY)
        {
            try
            {
                _centerXExact = BigFloat.Parse(exactX);
                _centerYExact = BigFloat.Parse(exactY);
                _deepZoomEngaged = _zoom >= DeepZoomThreshold;
                if (_deepZoomEngaged)
                {
                    _centerX = _centerXExact.ToDecimalClamped();
                    _centerY = _centerYExact.ToDecimalClamped();
                }
            }
            catch (FormatException)
            {
                // Испорченная строка центра не должна мешать открыть сохранение: остаётся
                // decimal-центр, точности которого хватает примерно до 1e24.
                _deepZoomEngaged = false;
            }
        }
        if (!_deepZoomEngaged) SyncDeepZoomState();
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        ThresholdBox.Text = Format(state.Threshold);
        ZoomBox.Text = FormatZoom(_zoom);
        PParameterBox.Text = Format(state.PParameter);
        QRealParameterBox.Text = Format(state.QRealParameter);
        QImaginaryParameterBox.Text = Format(state.QImaginaryParameter);
        VariationBox.SelectedIndex = Math.Clamp((int)state.Variation, 0,
            (int)CollatzVariation.GeneralizedPQ);
        ColoringModeBox.SelectedIndex = Math.Clamp((int)state.ColoringMode, 0,
            (int)CollatzColoringMode.PeriodDetection);
        EscapeSmoothingBox.SelectedIndex = state.UseSmoothColoring ? 1 : 0;
        ArgumentCyclesBox.Text = Format(state.ArgumentCycles);
        MagnitudeScaleBox.Text = Format(state.MagnitudeScale);
        TrapScaleBox.Text = Format(state.TrapScale);
        CycleToleranceBox.Text = state.CycleTolerance.ToString("G8", CultureInfo.InvariantCulture);
        MaximumPeriodBox.Text = state.MaximumDetectedPeriod.ToString(CultureInfo.InvariantCulture);
        DensityExposureBox.Text = Format(state.OrbitDensityExposure);
        DensitySampleStepBox.Text = state.OrbitDensitySampleStep.ToString(CultureInfo.InvariantCulture);
        DensityEscapedOnlyBox.IsChecked = state.OrbitDensityEscapedOnly;
        InteriorFillModeBox.SelectedIndex = Math.Clamp((int)state.InteriorFillMode, 0,
            (int)CollatzInteriorFillMode.Custom);
        CustomInteriorColorSelector.SelectedColor = state.CustomInteriorColor;
        _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        UpdateVariationControls();
        UpdateColoringControls();
        _updatingControls = false;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        CollatzState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    private void VariationBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVariationControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void UpdateVariationControls()
    {
        CollatzVariation variation = (CollatzVariation)Math.Clamp(VariationBox.SelectedIndex, 0,
            (int)CollatzVariation.GeneralizedPQ);
        if (PParameterPanel is not null)
            PParameterPanel.Visibility = variation is CollatzVariation.ParityBranchVariation or
                CollatzVariation.GeneralizedP or CollatzVariation.GeneralizedPQ
                ? Visibility.Visible : Visibility.Collapsed;
        if (QParameterPanel is not null)
            QParameterPanel.Visibility = variation == CollatzVariation.GeneralizedPQ
                ? Visibility.Visible : Visibility.Collapsed;
        if (VariationHintText is not null)
            VariationHintText.Text = VariationDescription(variation);
    }

    private void ColoringMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColoringControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void InteriorFillMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInteriorFillControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void UpdateColoringControls()
    {
        CollatzColoringMode mode = (CollatzColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
            (int)CollatzColoringMode.PeriodDetection);
        if (EscapeColoringPanel is not null)
            EscapeColoringPanel.Visibility = mode == CollatzColoringMode.EscapeTime
                ? Visibility.Visible : Visibility.Collapsed;
        if (ArgumentColoringPanel is not null)
            ArgumentColoringPanel.Visibility = mode == CollatzColoringMode.FinalArgument
                ? Visibility.Visible : Visibility.Collapsed;
        if (MagnitudeColoringPanel is not null)
            MagnitudeColoringPanel.Visibility = mode == CollatzColoringMode.FinalMagnitude
                ? Visibility.Visible : Visibility.Collapsed;
        if (TrapColoringPanel is not null)
            TrapColoringPanel.Visibility = mode is CollatzColoringMode.IntegerTrap or
                CollatzColoringMode.RealAxisTrap ? Visibility.Visible : Visibility.Collapsed;
        if (CycleColoringPanel is not null)
            CycleColoringPanel.Visibility = mode is CollatzColoringMode.CycleBasins or
                CollatzColoringMode.PeriodDetection ? Visibility.Visible : Visibility.Collapsed;
        if (DensityColoringPanel is not null)
            DensityColoringPanel.Visibility = mode == CollatzColoringMode.OrbitDensity
                ? Visibility.Visible : Visibility.Collapsed;
        if (ColoringDescriptionText is not null)
            ColoringDescriptionText.Text = ColoringModeDescription(mode);
        UpdateInteriorFillControls();
    }

    private void UpdateInteriorFillControls()
    {
        if (CustomInteriorColorSelector is null) return;
        CollatzInteriorFillMode mode = (CollatzInteriorFillMode)Math.Clamp(
            InteriorFillModeBox.SelectedIndex, 0, (int)CollatzInteriorFillMode.Custom);
        CustomInteriorColorSelector.Visibility = mode == CollatzInteriorFillMode.Custom
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingControls) ScheduleRender();
    }

    private void ZoomBox_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || !TryReadDouble(ZoomBox.Text, out double zoom) ||
            !double.IsFinite(zoom) || zoom <= 0) return;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        SyncDeepZoomState();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => _ = RenderPreviewAsync();
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForCollatz(this, _saveStore));

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        if (_isRendering) CommitAndBakePreview();
        else _renderCts?.Cancel();
        CollatzState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "collatz",
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
        // Запекание само выставляет трансформацию в единичную, но вызвать её надо в любом
        // случае: _isRendering сбрасывается только в finally уже отменённого рендера, и до
        // тех пор повторное запекание — пустая операция.
        if (_isRendering) CommitAndBakePreview();
        UpdatePreviewTransform();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isRendering) { ScheduleRender(); return; }
        CollatzState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var watch = Stopwatch.StartNew();
        SetRendering(true, $"Рендеринг: {VariationDisplayName(state.Variation)} · " +
                           $"{ColoringModeDisplayName(state.ColoringMode)}...");
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
            BitmapSource completed;
            string renderDetails;
            if (state.ColoringMode == CollatzColoringMode.OrbitDensity)
            {
                CanvasImage.Source = null;
                completed = await RenderBitmapAsync(state, pixelWidth, pixelHeight, factor, token,
                    new Progress<int>(value => RenderProgress.Value = value));
                renderDetails = "глобальное накопление орбит";
            }
            else
            {
                TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
                IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                    renderWidth, renderHeight, 16 * factor, strategy);
                WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(
                    renderWidth, renderHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
                session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
                _activeSession = session;
                CanvasImage.Source = bitmap;
                await session.RenderTilesAsync(tiles, GetThreadCount(),
                    (tile, tileToken) => CollatzRenderer.RenderTile(state, renderWidth, renderHeight, tile, tileToken), token);
                session.Flush();
                completed = session.Snapshot();
                renderDetails = $"стратегия: {strategy}";
            }

            if (token.IsCancellationRequested)
            {
                CanvasImage.Source = null;
                StatusText.Text = "Рендер отменён";
                return;
            }
            StablePreviewImage.Source = completed;
            CanvasImage.Source = null;
            _renderedCenterXExact = state.CenterXExact is { Length: > 0 } renderedX
                ? BigFloat.Parse(renderedX)
                : BigFloat.FromDecimal(state.CenterX);
            _renderedCenterYExact = state.CenterYExact is { Length: > 0 } renderedY
                ? BigFloat.Parse(renderedY)
                : BigFloat.FromDecimal(state.CenterY);
            _renderedZoom = state.Zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            // На ступени BigFloat время кадра определяется выбранной точностью, поэтому
            // её видно в статусе — иначе непонятно, почему кадр вдруг стал дороже.
            if (CollatzRenderer.UsesBigFloat(state))
                renderDetails += $"; точность {CollatzRenderer.PlanPrecisionBits(state)} бит";
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; {renderDetails}.";
        }
        catch (OperationCanceledException) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; }
        catch (Exception ex) { StatusText.Text = "Ошибка рендера"; MessageBox.Show(this, ex.Message, "Коллатц", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            session?.End();
            _activeSession = null;
            SetRendering(false);
        }
    }

    private async Task<BitmapSource> RenderBitmapAsync(CollatzState state, int width, int height, int ssaa,
        CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaa, 1, 4);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] pixels = new byte[checked(stride * renderHeight)];
        int threads = GetThreadCount();
        await Task.Run(() => CollatzRenderer.Render(state, pixels, renderWidth, renderHeight, stride, threads, token,
            value => progress?.Report(factor == 1 ? value : value * 90 / 100)));
        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return factor == 1 || token.IsCancellationRequested ? source : await Task.Run(() => BitmapResampler.ResizeLanczos3(source, width, height, token,
            value => progress?.Report(value)));
    }

    private int GetThreadCount() => ThreadsBox.SelectedItem?.ToString() == "Auto"
        ? Environment.ProcessorCount
        : Math.Max(1, Convert.ToInt32(ThreadsBox.SelectedItem, CultureInfo.InvariantCulture));

    private void SetRendering(bool value, string? status = null)
    {
        _isRendering = value;
        CancelButton.IsEnabled = value;
        if (!value) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) { UpdatePreviewTransform(); ScheduleRender(); }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Запекаем до изменения зума: снимок должен соответствовать прежнему виду, иначе
        // грубый предпросмотр будет двигать не то, что нарисовано.
        CommitAndBakePreview();
        Point mouse = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double fractionX = mouse.X / width - 0.5;
        double fractionY = 0.5 - mouse.Y / height;

        double previousZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), MinZoom, MaxZoom);

        // Точка под курсором остаётся на месте: сдвиг центра — доля от разности ширин
        // области. Это прежняя формула «мир до минус мир после», записанная явно; сдвиг
        // мал и укладывается в double, а ApplyCenterShift кладёт его в BigFloat-центр на
        // глубине и в decimal — как раньше — на мелком зуме.
        double previousViewWidth = (double)BaseScale / previousZoom;
        double currentViewWidth = (double)BaseScale / _zoom;
        double shiftX = fractionX * (previousViewWidth - currentViewWidth);
        double shiftY = fractionY * (previousViewWidth - currentViewWidth) * height / width;

        SyncDeepZoomState();
        ApplyCenterShift(shiftX, shiftY);

        _updatingControls = true;
        ZoomBox.Text = FormatZoom(_zoom);
        _updatingControls = false;
        ScheduleRender();
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitAndBakePreview();
        _panning = true;
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        Point current = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double viewWidth = (double)BaseScale / _zoom;
        ApplyCenterShift((_lastPanPoint.X - current.X) / width * viewWidth,
            (current.Y - _lastPanPoint.Y) / width * viewWidth);
        _lastPanPoint = current;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    /// <summary>
    /// Останавливает текущий рендер и «запекает» то, что уже видно на холсте (сдвинутый
    /// прежний кадр плюс успевшие лечь тайлы), в стабильный предпросмотр. После этого
    /// снимок считается отрисованным для текущего вида, поэтому дальнейшие зум и
    /// перетаскивание двигают именно его.
    ///
    /// Без этого при зуме терялся уже посчитанный кадр, а при перетаскивании рендер
    /// продолжал идти поверх уезжающего фона.
    /// </summary>
    private void CommitAndBakePreview()
    {
        TiledRenderSession? session = _activeSession;
        _renderCts?.Cancel();
        // Полнокадровые режимы (Orbit Density) сессию тайлов не заводят: запекать нечего,
        // достаточно отменить рендер.
        if (session is null) return;

        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(SavePreviewLayer) is { } baked)
        {
            StablePreviewImage.Source = baked;
            _renderedCenterXExact = _deepZoomEngaged ? _centerXExact : BigFloat.FromDecimal(_centerX);
            _renderedCenterYExact = _deepZoomEngaged ? _centerYExact : BigFloat.FromDecimal(_centerY);
            _renderedZoom = _zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
        }
        CanvasImage.Source = null;
        session.End();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    /// <summary>
    /// Прибавляет к центру небольшой сдвиг в мировых координатах. На глубине сдвиг уходит в
    /// BigFloat-центр (decimal-приближение обновляется следом), на мелком зуме — в decimal.
    /// </summary>
    private void ApplyCenterShift(double shiftX, double shiftY)
    {
        if (_deepZoomEngaged)
        {
            _centerXExact += BigFloat.FromDouble(shiftX);
            _centerYExact += BigFloat.FromDouble(shiftY);
            _centerX = _centerXExact.ToDecimalClamped();
            _centerY = _centerYExact.ToDecimalClamped();
        }
        else
        {
            _centerX += (decimal)shiftX;
            _centerY += (decimal)shiftY;
        }
    }

    /// <summary>
    /// Заводит или глушит ступень BigFloat по текущему зуму. Вверх через порог центр
    /// переносится из decimal в BigFloat, вниз decimal снова становится источником истины.
    /// </summary>
    private void SyncDeepZoomState()
    {
        bool shouldEngage = _zoom >= DeepZoomThreshold;
        if (shouldEngage && !_deepZoomEngaged)
        {
            _centerXExact = BigFloat.FromDecimal(_centerX);
            _centerYExact = BigFloat.FromDecimal(_centerY);
            _deepZoomEngaged = true;
        }
        else if (!shouldEngage && _deepZoomEngaged)
        {
            _centerX = _centerXExact.ToDecimalClamped();
            _centerY = _centerYExact.ToDecimalClamped();
            _deepZoomEngaged = false;
        }
    }

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || _renderedZoom <= 0 || _zoom <= 0 || CanvasHost.ActualWidth <= 0) return;
        double scale = _zoom / _renderedZoom;
        double currentScale = (double)BaseScale / _zoom;
        double width = CanvasHost.ActualWidth;
        BigFloat currentCenterX = _deepZoomEngaged ? _centerXExact : BigFloat.FromDecimal(_centerX);
        BigFloat currentCenterY = _deepZoomEngaged ? _centerYExact : BigFloat.FromDecimal(_centerY);
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = (_renderedCenterXExact - currentCenterX).ToDouble() / currentScale * width;
        _previewTranslation.Y = (currentCenterY - _renderedCenterYExact).ToDouble() / currentScale * width;
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 290);
        ScheduleRender();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || e.Key == Key.Escape && _isFullscreen) ToggleFullscreen();
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
        _activeSession?.End();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static string VariationDisplayName(CollatzVariation variation) => variation switch
    {
        CollatzVariation.SineVariation => "синусная арт-вариация",
        CollatzVariation.ParityBranchVariation => "арт-вариация с ветвями 1 / (p−1)n",
        CollatzVariation.GeneralizedP => "обобщённая Cₚ",
        CollatzVariation.GeneralizedPQ => "семейство C(p,q)",
        _ => "стандартная вариация"
    };

    private static string VariationDescription(CollatzVariation variation) => variation switch
    {
        CollatzVariation.SineVariation => "Художественная вариация; не сохраняет отображение Коллатца на целых.",
        CollatzVariation.ParityBranchVariation => "Прежний режим Generalized P: чётное n → 1, нечётное n → (p−1)n.",
        CollatzVariation.GeneralizedP => "Аналитическое продолжение: чётное n → n/2, нечётное n → pn+1.",
        CollatzVariation.GeneralizedPQ => "То же отображение на целых; комплексный q меняет только продолжение между ними.",
        _ => "Классическое продолжение Коллатца (p = 3)."
    };

    private static string ColoringModeDisplayName(CollatzColoringMode mode) => mode switch
    {
        CollatzColoringMode.FinalArgument => "Final Argument",
        CollatzColoringMode.FinalMagnitude => "Final Magnitude",
        CollatzColoringMode.CycleBasins => "Cycle Basins",
        CollatzColoringMode.IntegerTrap => "Integer Trap",
        CollatzColoringMode.RealAxisTrap => "Real Axis Trap",
        CollatzColoringMode.OrbitDensity => "Orbit Density",
        CollatzColoringMode.PeriodDetection => "Period Detection",
        _ => "Escape Time"
    };

    private static string ColoringModeDescription(CollatzColoringMode mode) => mode switch
    {
        CollatzColoringMode.FinalArgument => "Цвет определяется аргументом последнего значения z.",
        CollatzColoringMode.FinalMagnitude => "Цвет определяется логарифмом модуля последнего значения z.",
        CollatzColoringMode.CycleBasins => "Одинаковым найденным циклам назначается одинаковый цвет.",
        CollatzColoringMode.IntegerTrap => "Яркость показывает минимальное расстояние орбиты до вещественного целого.",
        CollatzColoringMode.RealAxisTrap => "Яркость показывает минимальное расстояние орбиты до вещественной оси.",
        CollatzColoringMode.OrbitDensity => "Накопление посещений пикселей всеми траекториями, как в Buddhabrot.",
        CollatzColoringMode.PeriodDetection => "Цвет определяется найденным периодом орбиты.",
        _ => "Текущий режим: цвет по числу итераций до выхода."
    };

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool TryRead(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ReadDouble(string text, string parameterName, double minimum, double maximum)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        if (!parsed || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameterName}» должен быть от {minimum:G8} до {maximum:G8}.");
        return value;
    }

    private static int ReadInt(string text, string parameterName, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameterName}» должен быть целым числом от {minimum} до {maximum}.");
        return value;
    }

    /// <summary>
    /// Зум показывается восемью значащими цифрами: так большое значение уходит в
    /// экспоненциальную запись (8.1707708E+09) и помещается в поле целиком, а не обрезается.
    /// </summary>
    private static string FormatZoom(double value) => value.ToString("G8", CultureInfo.InvariantCulture);

    private static string Format(decimal value) => value.ToString("G15", CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
