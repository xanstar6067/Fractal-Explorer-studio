using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class DomainColoringWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DomainColoringSaveStore _saveStore = new();
    private readonly DomainColoringPaletteManager _paletteManager = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private CancellationTokenSource? _renderCts;
    private RenderSession? _activeSession;
    private bool _isRendering;
    private bool _panning;
    private bool _isFullscreen;
    private bool _controlsVisible = true;
    private bool _hasRenderedFrame;
    private bool _updatingControls;
    private Point _lastPanPoint;
    private double _centerX;
    private double _centerY;
    private double _zoom = 1;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedZoom = 1;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    public DomainColoringWindow()
    {
        InitializeComponent();
        _updatingControls = true;
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StablePreviewImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StablePreviewImage.RenderTransform = _previewTransform;

        _visualizationTimer.Tick += (_, _) =>
        {
            try
            {
                if (_activeSession is not null) FlushVisualizationEvents(_activeSession, false);
            }
            catch (Exception ex)
            {
                _visualizationTimer.Stop();
                StatusText.Text = "Ошибка визуализации прогресса рендера.";
                CrashLogger.Log("DomainColoringWindow.VisualizationTimer", ex);
            }
        };
        _renderTimer.Tick += RenderTimer_OnTick;

        FormulaBox.Text = "(z^3-1)/(z^3+1)";
        PresetBox.SelectedIndex = 5;
        ColoringModeBox.SelectedIndex = (int)DomainColoringMode.PolarGrid;
        HueCyclesBox.Text = "1";
        MagnitudeExposureBox.Text = "1";
        RingDensityBox.Text = "1";
        PhaseSectorsBox.Text = "12";
        ContourStrengthBox.Text = "0.55";
        SaturationBox.Text = "0.9";
        ZoomBox.Text = "1";
        InvalidColorSelector.SelectedColor = Colors.White;
        ShowAxesBox.IsChecked = false;
        SsaaBox.SelectedIndex = 0;
        for (int count = 1; count <= Environment.ProcessorCount; count++) ThreadsBox.Items.Add(count);
        ThreadsBox.Items.Add("Auto");
        ThreadsBox.SelectedItem = "Auto";
        UpdateColoringControls();
        _updatingControls = false;

        Loaded += (_, _) => ScheduleRender();
    }

    public DomainColoringState CaptureState(string name)
    {
        return new DomainColoringState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            Formula = FormulaBox.Text.Trim(),
            CenterX = _centerX,
            CenterY = _centerY,
            Zoom = _zoom,
            ColoringMode = (DomainColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
                (int)DomainColoringMode.ArgumentOnly),
            HueCycles = ReadDouble(HueCyclesBox.Text, "обороты оттенка", 0.05, 32),
            MagnitudeExposure = ReadDouble(MagnitudeExposureBox.Text, "экспозиция модуля", 0.01, 20),
            RingDensity = ReadDouble(RingDensityBox.Text, "плотность колец", 0.05, 32),
            PhaseSectors = ReadInt(PhaseSectorsBox.Text, "число фазовых секторов", 1, 256),
            ContourStrength = ReadDouble(ContourStrengthBox.Text, "сила контуров", 0, 1),
            Saturation = ReadDouble(SaturationBox.Text, "насыщенность", 0, 1),
            ShowAxes = ShowAxesBox.IsChecked == true,
            InvalidColor = InvalidColorSelector.SelectedColor,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name)
        };
    }

    public void LoadState(DomainColoringState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _updatingControls = true;

        _centerX = double.IsFinite(state.CenterX) ? state.CenterX : 0;
        _centerY = double.IsFinite(state.CenterY) ? state.CenterY : 0;
        _zoom = double.IsFinite(state.Zoom) ? Math.Clamp(state.Zoom, 1e-12, 1e12) : 1;
        string formula = string.IsNullOrWhiteSpace(state.Formula) ? "z" : state.Formula.Trim();
        PresetBox.SelectedIndex = FindPresetIndex(formula);
        FormulaBox.Text = formula;
        ColoringModeBox.SelectedIndex = Math.Clamp((int)state.ColoringMode, 0,
            (int)DomainColoringMode.ArgumentOnly);
        HueCyclesBox.Text = Format(ClampFinite(state.HueCycles, 0.05, 32, 1));
        MagnitudeExposureBox.Text = Format(ClampFinite(state.MagnitudeExposure, 0.01, 20, 1));
        RingDensityBox.Text = Format(ClampFinite(state.RingDensity, 0.05, 32, 1));
        PhaseSectorsBox.Text = Math.Clamp(state.PhaseSectors, 1, 256).ToString(CultureInfo.InvariantCulture);
        ContourStrengthBox.Text = Format(ClampFinite(state.ContourStrength, 0, 1, 0.55));
        SaturationBox.Text = Format(ClampFinite(state.Saturation, 0, 1, 0.9));
        ShowAxesBox.IsChecked = state.ShowAxes;
        InvalidColorSelector.SelectedColor = state.InvalidColor;
        ZoomBox.Text = Format(_zoom);
        _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");

        UpdateColoringControls();
        _updatingControls = false;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        DomainColoringState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    private int FindPresetIndex(string formula)
    {
        for (int index = 0; index < PresetBox.Items.Count; index++)
        {
            if (PresetBox.Items[index] is ComboBoxItem { Tag: string preset } &&
                preset != "custom" && string.Equals(preset, formula, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return PresetBox.Items.Count - 1;
    }

    private void PresetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ComboBoxItem { Tag: string formula } || formula == "custom") return;
        FormulaBox.Text = formula;
        if (!_updatingControls) ScheduleRender();
    }

    private void FormulaBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingControls) ScheduleRender();
    }

    private void ColoringModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColoringControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void UpdateColoringControls()
    {
        if (ColoringModeBox is null || MagnitudePanel is null) return;
        DomainColoringMode mode = (DomainColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
            (int)DomainColoringMode.ArgumentOnly);
        MagnitudePanel.Visibility = mode == DomainColoringMode.SmoothMagnitude
            ? Visibility.Visible : Visibility.Collapsed;
        RingPanel.Visibility = mode is DomainColoringMode.LogarithmicRings or DomainColoringMode.PolarGrid
            ? Visibility.Visible : Visibility.Collapsed;
        PhasePanel.Visibility = mode is DomainColoringMode.PhaseContours or DomainColoringMode.PolarGrid
            ? Visibility.Visible : Visibility.Collapsed;
        ContourPanel.Visibility = mode is DomainColoringMode.LogarithmicRings or
            DomainColoringMode.PhaseContours or DomainColoringMode.PolarGrid
            ? Visibility.Visible : Visibility.Collapsed;
        ColoringDescriptionText.Text = mode switch
        {
            DomainColoringMode.LogarithmicRings =>
                "Оттенок показывает arg f(z), тёмные кольца — уровни log₂|f(z)|.",
            DomainColoringMode.PhaseContours =>
                "Изолинии аргумента делят образ функции на равные угловые секторы.",
            DomainColoringMode.PolarGrid =>
                "Совмещает логарифмические кольца модуля и фазовые линии.",
            DomainColoringMode.ArgumentOnly =>
                "Чистая цветовая карта аргумента без модуля и контуров.",
            _ => "Оттенок показывает аргумент, яркость плавно зависит от log₂|f(z)|."
        };
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingControls) ScheduleRender();
    }

    private void ZoomBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || !TryReadDouble(ZoomBox.Text, out double zoom) || zoom <= 0) return;
        _zoom = Math.Clamp(zoom, 1e-12, 1e12);
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => _ = RenderPreviewAsync();
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        _centerX = 0;
        _centerY = 0;
        _zoom = 1;
        SetZoomText();
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForDomainColoring(this, _saveStore));

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new DomainColoringPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        DomainColoringState state;
        try
        {
            state = CaptureState("export");
            _ = new DomainColoringRenderer(state);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "domain-coloring",
            WindowTitle = "Экспорт Domain Coloring",
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
        if (_isRendering) CommitAndBakePreview();
        else _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

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
        RenderSession? session = _activeSession;
        _renderCts?.Cancel();
        if (session is null) return;

        FlushVisualizationEvents(session, true);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(SavePreviewLayer);
        try
        {
            var baked = new RenderTargetBitmap(surface.PixelWidth, surface.PixelHeight,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            baked.Render(SavePreviewLayer);
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
            // Разметка бывает недоступна на свёртывании окна и в момент изменения размера.
        }
        CanvasImage.Source = null;
        RenderOverlay.EndSession();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }


    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isRendering)
        {
            ScheduleRender();
            return;
        }

        DomainColoringState state;
        DomainColoringRenderer renderer;
        try
        {
            state = CaptureState("preview");
            renderer = new DomainColoringRenderer(state);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;
        var watch = Stopwatch.StartNew();
        SetRendering(true, $"Рендеринг f(z) = {renderer.ParsedFormula}...");

        try
        {
            int factor = SsaaBox.SelectedItem is ComboBoxItem item
                ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) : 1;
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int renderWidth = checked(surface.PixelWidth * factor);
            int renderHeight = checked(surface.PixelHeight * factor);
            TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(
                renderWidth, renderHeight, surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY);
            var session = new RenderSession(bitmap, tiles.Count, renderWidth, renderHeight);
            _activeSession = session;
            CanvasImage.Source = bitmap;
            RenderOverlay.BeginSession(renderWidth, renderHeight);
            _visualizationTimer.Start();

            await RenderTilesAsync(renderer, tiles, session, GetThreadCount(), token);
            FlushVisualizationEvents(session, true);
            token.ThrowIfCancellationRequested();

            BitmapSource completed = session.Bitmap.Clone();
            completed.Freeze();
            StablePreviewImage.Source = completed;
            CanvasImage.Source = null;
            _renderedCenterX = state.CenterX;
            _renderedCenterY = state.CenterY;
            _renderedZoom = state.Zoom;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; f(z) = {renderer.ParsedFormula}.";
        }
        catch (OperationCanceledException)
        {
            CanvasImage.Source = null;
            StatusText.Text = "Рендер отменён";
        }
        catch (Exception ex)
        {
            CanvasImage.Source = null;
            StatusText.Text = "Ошибка рендера";
            MessageBox.Show(this, ex.Message, "Domain Coloring", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _visualizationTimer.Stop();
            RenderOverlay.EndSession();
            _activeSession = null;
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
            SetRendering(false);
        }
    }

    private static async Task RenderTilesAsync(
        DomainColoringRenderer renderer,
        IReadOnlyList<MandelbrotRenderTile> tiles,
        RenderSession session,
        int threadCount,
        CancellationToken token)
    {
        var queue = new ConcurrentQueue<MandelbrotRenderTile>(tiles);
        Task[] workers = Enumerable.Range(0, Math.Clamp(threadCount, 1, Environment.ProcessorCount))
            .Select(_ => Task.Run(() =>
            {
                while (queue.TryDequeue(out MandelbrotRenderTile tile))
                {
                    if (token.IsCancellationRequested) return;
                    session.Events.Enqueue(new TileRenderEvent(true, tile, null));
                    byte[]? pixels = renderer.RenderTile(tile, session.RenderWidth, session.RenderHeight, token);
                    if (pixels is null || token.IsCancellationRequested) return;
                    session.Events.Enqueue(new TileRenderEvent(false, tile, pixels));
                }
            }, token)).ToArray();
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

    private async Task<BitmapSource> RenderBitmapAsync(
        DomainColoringState state,
        int width,
        int height,
        int ssaa,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaa, 1, 4);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] pixels = new byte[checked(stride * renderHeight)];
        var renderer = new DomainColoringRenderer(state);
        int threads = GetThreadCount();
        await Task.Run(() => renderer.Render(pixels, renderWidth, renderHeight, stride, threads, token,
            value => progress?.Report(factor == 1 ? value : value * 90 / 100)), token);
        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return factor == 1 || token.IsCancellationRequested
            ? source
            : await Task.Run(() => BitmapResampler.ResizeLanczos3(source, width, height, token,
                value => progress?.Report(value)), token);
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

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CommitAndBakePreview();
        Point mouse = e.GetPosition(CanvasHost);
        (double x, double y) = ScreenToWorld(mouse);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 1e-12, 1e12);
        (double nextX, double nextY) = ScreenToWorld(mouse);
        _centerX += x - nextX;
        _centerY += y - nextY;
        SetZoomText();
        UpdatePreviewTransform();
        ScheduleRender();
        e.Handled = true;
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
        Point current = e.GetPosition(CanvasHost);
        (double x, double y) = ScreenToWorld(current);
        CoordinateText.Text = $"z = {x:G7} {(y < 0 ? '−' : '+')} {Math.Abs(y):G7}i";
        if (!_panning) return;

        (double beforeX, double beforeY) = ScreenToWorld(_lastPanPoint);
        _centerX += beforeX - x;
        _centerY += beforeY - y;
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

    private (double X, double Y) ScreenToWorld(Point point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double scale = DomainColoringRenderer.BaseScale / _zoom;
        return (_centerX + (point.X - width / 2) * scale / width,
            _centerY + (Math.Max(1, CanvasHost.ActualHeight) / 2 - point.Y) * scale / width);
    }

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || _renderedZoom <= 0 || _zoom <= 0 || CanvasHost.ActualWidth <= 0) return;
        double scale = _zoom / _renderedZoom;
        double currentScale = DomainColoringRenderer.BaseScale / _zoom;
        double width = CanvasHost.ActualWidth;
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = (_renderedCenterX - _centerX) / currentScale * width;
        _previewTranslation.Y = (_centerY - _renderedCenterY) / currentScale * width;
    }

    private void SetZoomText()
    {
        _updatingControls = true;
        ZoomBox.Text = _zoom.ToString("G8", CultureInfo.InvariantCulture);
        _updatingControls = false;
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 320);
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
        _visualizationTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        Mouse.OverrideCursor = null;
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ReadDouble(string text, string name, double minimum, double maximum)
    {
        if (!TryReadDouble(text, out double value) || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{name}» должен быть от {minimum:G8} до {maximum:G8}.");
        return value;
    }

    private static int ReadInt(string text, string name, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{name}» должен быть целым числом от {minimum} до {maximum}.");
        return value;
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

    private sealed class RenderSession(WriteableBitmap bitmap, int tileCount, int renderWidth, int renderHeight)
    {
        public WriteableBitmap Bitmap { get; } = bitmap;
        public int TileCount { get; } = tileCount;
        public int RenderWidth { get; } = renderWidth;
        public int RenderHeight { get; } = renderHeight;
        public int CompletedTiles { get; set; }
        public ConcurrentQueue<TileRenderEvent> Events { get; } = new();
    }

    private readonly record struct TileRenderEvent(bool IsStart, MandelbrotRenderTile Tile, byte[]? Pixels);
}
