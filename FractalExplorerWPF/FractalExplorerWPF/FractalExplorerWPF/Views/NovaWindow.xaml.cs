using System.Diagnostics;
using System.Globalization;
using System.IO;
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
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class NovaWindow : Window
{
    private const decimal BaseScale = 4m;
    private readonly NovaVariant _variant;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _mapTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly NovaPaletteManager _paletteManager = new();
    private readonly NovaSaveStore _saveStore;
    private readonly DecimalDeepZoomViewport _viewport = new((double)BaseScale, MinZoom, MaxZoom, DeepZoomThreshold);
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _mapCts;
    private TiledRenderSession? _activeSession;
    private bool _isRendering, _controlsVisible = true;

    /// <summary>
    /// Окно само заполняет поле зума после колеса. Без этого флага обработчик изменения текста
    /// тут же прочитал бы округлённое до восьми цифр значение обратно в зум вида —
    /// уже после того, как по прежнему зуму посчитан сдвиг центра.
    /// </summary>
    private bool _updatingControls;

    /// <summary>
    /// Зум, начиная с которого окно ведёт центр в <see cref="BigFloat"/>. Совпадает с порогом
    /// включения пертурбационного движка в <c>NovaRenderer.DeepZoom</c>: смысла вести точный
    /// центр раньше нет, а после — обязательно, иначе движку неоткуда взять положение области
    /// с нужным числом знаков.
    /// </summary>
    private const double DeepZoomThreshold = NovaRenderer.DeepZoomThreshold;

    /// <summary>
    /// Потолок зума. Прежде здесь стояло 1e15 — не предел точности, а рубеж, за которым
    /// прежняя ступень на <see cref="FractalExplorer.Utilities.ComplexDecimal"/> начинала
    /// заметно квантовать координаты (28 знаков decimal при центре порядка единицы).
    ///
    /// Теперь выше <see cref="DeepZoomThreshold"/> кадр считает пертурбационный движок, и
    /// предел задаёт уже он. Отклонение δ ведётся в double, как у Феникса, но ведёт себя
    /// заметно лучше по двум причинам: ребазирование у Nova полноценное (опорная орбита
    /// сходится в неподвижную точку, поэтому доступна целиком и перенос в её начало всегда
    /// возможен), а сама формула — ньютоновская, то есть сжимающая, так что ошибка δ по
    /// орбите не нарастает.
    ///
    /// Замер по спуску вдоль границы, сравнение с прямой BigFloat-итерацией того же кадра
    /// (64×44, 400 итераций): на всех глубинах от 1e12 до 2e56 расхождение остаётся на
    /// уровне отдельных пикселей границы — 0…10 из 2816 — и с глубиной не растёт. Потолок
    /// взят с запасом внутри измеренного диапазона; выше него начинает мешать и другое —
    /// арифметика самого центра в окне идёт на
    /// <see cref="BigFloat.MinimumPrecisionBits"/> (≈115 десятичных цифр).
    /// </summary>
    private const double MaxZoom = 1e50;

    private const double MinZoom = 0.000000000000001;
    private readonly FullscreenToggle _fullscreen = new();

    public NovaWindow(NovaVariant variant)
    {
        _variant = variant;
        _saveStore = new NovaSaveStore(variant);
        InitializeComponent();
        Title = variant == NovaVariant.Julia ? "Фрактал Nova Julia" : "Фрактал Nova Mandelbrot";
        HeaderText.Text = variant == NovaVariant.Julia ? "Параметры Nova Julia" : "Параметры Nova Mandelbrot";
        JuliaParametersPanel.Visibility = JuliaMapPanel.Visibility = variant == NovaVariant.Julia ? Visibility.Visible : Visibility.Collapsed;
        _viewport.AttachPreview(StablePreviewImage, CanvasHost);
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); _ = RenderPreviewAsync(); };
        _mapTimer.Tick += (_, _) => { _mapTimer.Stop(); _ = RenderJuliaMapAsync(); };
        PRealBox.Text = "3"; PImaginaryBox.Text = "0"; Z0RealBox.Text = "1"; Z0ImaginaryBox.Text = "0";
        CRealBox.Text = "0"; CImaginaryBox.Text = "1"; MBox.Text = "1"; IterationsBox.Text = "100";
        ThresholdBox.Text = "10"; ZoomBox.Text = "1"; ColoringBox.SelectedIndex = 1; SsaaBox.SelectedIndex = 0;
        RenderThreadsBox.Fill(ThreadsBox);
        Loaded += (_, _) => { ScheduleRender(); ScheduleMapRender(); };
        InitializeNumericInput();
    }

    public NovaState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        if (!TryRead(PRealBox.Text, out decimal pRe) || !TryRead(PImaginaryBox.Text, out decimal pIm) || pRe is < -10 or > 10 || pIm is < -10 or > 10)
            throw new InvalidOperationException("Компоненты степени P должны быть от −10 до 10.");
        if (!TryRead(Z0RealBox.Text, out decimal zRe) || !TryRead(Z0ImaginaryBox.Text, out decimal zIm) || zRe is < -10 or > 10 || zIm is < -10 or > 10)
            throw new InvalidOperationException("Компоненты Z₀ должны быть от −10 до 10.");
        if (!TryRead(CRealBox.Text, out decimal cRe) || !TryRead(CImaginaryBox.Text, out decimal cIm))
            throw new InvalidOperationException("Введите корректную константу C.");
        if (!TryRead(MBox.Text, out decimal m) || m is < 0.1m or > 5m) throw new InvalidOperationException("Релаксация m должна быть от 0,1 до 5.");
        if (!int.TryParse(IterationsBox.Text, out int iterations) || iterations is < 10 or > 100_000) throw new InvalidOperationException("Итерации должны быть от 10 до 100000.");
        if (!TryRead(ThresholdBox.Text, out decimal threshold) || threshold is < 2 or > 1000) throw new InvalidOperationException("Порог должен быть от 2 до 1000.");
        (decimal centerX, decimal centerY, string? exactX, string? exactY) = _viewport.CaptureCenter();
        return new NovaState
        {
            SaveName = name, Timestamp = DateTime.Now, Variant = _variant,
            FractalType = _variant == NovaVariant.Julia ? "NovaJulia" : "NovaMandelbrot",
            CenterX = centerX, CenterY = centerY, CenterXExact = exactX, CenterYExact = exactY,
            Zoom = _viewport.Zoom.ToDouble(), Threshold = threshold, Iterations = iterations,
            PReal = pRe, PImaginary = pIm, Z0Real = zRe, Z0Imaginary = zIm, M = m, CReal = cRe, CImaginary = cIm,
            UseSmoothColoring = ColoringBox.SelectedIndex == 1,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name)
        };
    }

    public void LoadState(NovaState state)
    {
        _renderCts?.Cancel();
        _viewport.Restore(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
        PRealBox.Text = Format(state.PReal); PImaginaryBox.Text = Format(state.PImaginary); Z0RealBox.Text = Format(state.Z0Real); Z0ImaginaryBox.Text = Format(state.Z0Imaginary);
        CRealBox.Text = Format(state.CReal); CImaginaryBox.Text = Format(state.CImaginary); MBox.Text = Format(state.M);
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture); ThresholdBox.Text = Format(state.Threshold);
        _updatingControls = true; ZoomBox.Text = FormatZoom(_viewport.Zoom.ToDouble()); _updatingControls = false;
        ColoringBox.SelectedIndex = state.UseSmoothColoring ? 1 : 0; _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        UpdatePreviewTransform(); ScheduleRender(); ScheduleMapRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        NovaState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);
    private void Parameter_OnChanged(object sender, EventArgs e) => ScheduleRender();
    private void MapFormulaParameter_OnChanged(object sender, EventArgs e) { ScheduleRender(); ScheduleMapRender(); }
    private void JuliaMapParameter_OnChanged(object sender, EventArgs e) { ScheduleRender(); DrawMapMarker(); }
    private void ZoomBox_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (_updatingControls) return;
        if (!double.TryParse(ZoomBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double zoom) &&
            !double.TryParse(ZoomBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out zoom)) return;
        _viewport.SetZoom(zoom);
        UpdatePreviewTransform();
        ScheduleRender();
    }
    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();
    private void PaletteButton_OnClick(object sender, RoutedEventArgs e) { var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this }; dialog.PaletteApplied += (_, _) => ScheduleRender(); dialog.ShowDialog(); }
    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForNova(this, _saveStore, _variant)); }

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
    private void ScheduleMapRender() { if (!IsLoaded || _variant != NovaVariant.Julia) return; _mapCts?.Cancel(); _mapTimer.Stop(); _mapTimer.Start(); }
    private void JuliaMapHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawMapMarker();
        if (e.NewSize.Width > 1 && e.NewSize.Height > 1) ScheduleMapRender();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isRendering) { ScheduleRender(); return; }
        NovaState state; try { state = CaptureState("preview"); } catch (Exception ex) { StatusText.Text = ex.Message; return; }
        _renderCts?.Dispose(); _renderCts = new CancellationTokenSource(); CancellationToken token = _renderCts.Token;
        var watch = Stopwatch.StartNew(); SetRendering(true, "Рендеринг Nova...");
        TiledRenderSession? session = null;
        try
        {
            int factor = SsaaBox.SelectedItem is ComboBoxItem item ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) : 1;
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int width = checked(surface.PixelWidth * factor);
            int height = checked(surface.PixelHeight * factor);
            TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(width, height, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
            _activeSession = session; CanvasImage.Source = bitmap;
            await session.RenderTilesAsync(tiles, GetThreadCount(),
                (tile, tileToken) => NovaRenderer.RenderTile(state, width, height, tile, tileToken), token);
            if (token.IsCancellationRequested) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; return; }
            session.Flush();
            StablePreviewImage.Source = session.Snapshot(); CanvasImage.Source = null;
            _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
            UpdatePreviewTransform();
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек. Стратегия: {strategy}.";
        }
        catch (OperationCanceledException) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; }
        catch (Exception ex) { CanvasImage.Source = null; MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { session?.End(); _activeSession = null; SetRendering(false); }
    }

    private async Task RenderJuliaMapAsync()
    {
        if (_variant != NovaVariant.Julia || JuliaMapHost.ActualWidth <= 2 || JuliaMapHost.ActualHeight <= 2) return;
        NovaState state; try { state = CaptureState("map"); } catch { return; }
        state.Variant = NovaVariant.Mandelbrot; state.CenterX = 0; state.CenterY = 0; state.Zoom = 1; state.Iterations = 100;
        state.CenterXExact = null; state.CenterYExact = null;
        _mapCts?.Dispose(); _mapCts = new CancellationTokenSource(); CancellationToken token = _mapCts.Token;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(JuliaMapHost);
        int width = Math.Max(160, surface.PixelWidth);
        int height = Math.Max(100, surface.PixelHeight);
        int stride = width * 4; byte[] pixels = new byte[stride * height];
        try
        {
            await Task.Run(() =>
            {
                var tile = new MandelbrotRenderTile(0, 0, width, height, 0, 0);
                byte[]? rendered = NovaRenderer.RenderTile(state, width, height, tile, token, true);
                if (rendered is null) return;
                Buffer.BlockCopy(rendered, 0, pixels, 0, pixels.Length);
            });
            if (token.IsCancellationRequested) return;
            BitmapSource bitmap = BitmapSource.Create(width, height, surface.Dpi.PixelsPerInchX,
                surface.Dpi.PixelsPerInchY, PixelFormats.Bgra32, null, pixels, stride); bitmap.Freeze();
            JuliaMapPreviewImage.Source = bitmap; DrawMapMarker();
        }
        catch (OperationCanceledException) { }
    }

    private void JuliaMapPreview_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        NovaState state; try { state = CaptureState("selector"); } catch (Exception ex) { StatusText.Text = ex.Message; return; }
        state.CenterXExact = null; state.CenterYExact = null;
        var selector = new NovaParameterSelectorWindow(state) { Owner = this };
        selector.CoordinatesSelected += (real, imaginary) => { CRealBox.Text = Format(real); CImaginaryBox.Text = Format(imaginary); DrawMapMarker(); ScheduleRender(); };
        selector.ShowDialog();
    }

    private void DrawMapMarker()
    {
        JuliaMapMarker.Children.Clear(); if (!TryRead(CRealBox.Text, out decimal real) || !TryRead(CImaginaryBox.Text, out decimal imaginary)) return;
        double width = JuliaMapMarker.ActualWidth; double height = JuliaMapMarker.ActualHeight; if (width <= 0 || height <= 0) return;
        double x = ((double)real + 2) / 4 * width; double y = (2 - (double)imaginary) / 4 * height;
        var brush = new SolidColorBrush(Colors.Lime); brush.Freeze();
        JuliaMapMarker.Children.Add(new Line { X1 = x - 7, X2 = x + 7, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 2 });
        JuliaMapMarker.Children.Add(new Line { X1 = x, X2 = x, Y1 = y - 7, Y2 = y + 7, Stroke = brush, StrokeThickness = 2 });
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        NovaState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = _variant == NovaVariant.Julia ? "nova_julia" : "nova_mandelbrot",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    private Task<BitmapSource> RenderBitmapAsync(NovaState state, int width, int height, int ssaa, CancellationToken token, IProgress<int>? progress)
    {
        // Элементы окна принадлежат UI-потоку: число потоков читается до расчёта.
        int threads = GetThreadCount();
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4, (pixels, renderWidth, renderHeight, stride, report) =>
            NovaRenderer.Render(state, pixels, renderWidth, renderHeight, stride, threads, token, report), token, progress);
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
        _updatingControls = true; ZoomBox.Text = FormatZoom(_viewport.Zoom.ToDouble()); _updatingControls = false;
        ScheduleRender();
        e.Handled = true;
    }
    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { CommitAndBakePreview(); _viewport.BeginPan(e.GetPosition(CanvasHost)); CanvasHost.CaptureMouse(); Mouse.OverrideCursor = Cursors.SizeAll; }
    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport.PanTo(e.GetPosition(CanvasHost), CanvasHost.ActualWidth)) UpdatePreviewTransform();
    }
    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (!_viewport.EndPan()) return; CanvasHost.ReleaseMouseCapture(); Mouse.OverrideCursor = null; ScheduleRender(); }

    private void UpdatePreviewTransform() => _viewport.UpdatePreview();
    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) => FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleControlsButton, 300, ScheduleRender);
    private void Window_OnKeyDown(object sender, KeyEventArgs e) { _fullscreen.HandleKey(this, e.Key); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { _renderTimer.Stop(); _mapTimer.Stop(); _activeSession?.End(); _renderCts?.Cancel(); _mapCts?.Cancel(); _renderCts?.Dispose(); _mapCts?.Dispose(); }
    private static bool TryRead(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    private static string Format(decimal value) => value.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>
    /// Зум показывается восемью значащими цифрами: большое значение уходит в
    /// экспоненциальную запись (8.1707708E+09) и помещается в поле целиком.
    /// </summary>
    private static string FormatZoom(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
}
