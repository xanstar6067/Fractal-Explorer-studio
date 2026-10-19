using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class DomainColoringWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DomainColoringSaveStore _saveStore = new();
    private readonly DomainColoringPaletteManager _paletteManager = new();
    private readonly DoubleDeepZoomViewport _viewport =
        new(DomainColoringRenderer.BaseScale, 1e-12, 1e12, double.PositiveInfinity);
    private CancellationTokenSource? _renderCts;
    private TiledRenderSession? _activeSession;
    private bool _isRendering;
    private bool _controlsVisible = true;
    private bool _updatingControls;
    private readonly FullscreenToggle _fullscreen = new();

    public DomainColoringWindow()
    {
        InitializeComponent();
        _updatingControls = true;
        _viewport.AttachPreview(StablePreviewImage, CanvasHost);
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
        RenderThreadsBox.Fill(ThreadsBox);
        UpdateColoringControls();
        _updatingControls = false;

        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public DomainColoringState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        return new DomainColoringState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            Formula = FormulaBox.Text.Trim(),
            CenterX = _viewport.CenterX,
            CenterY = _viewport.CenterY,
            Zoom = _viewport.Zoom.ToDouble(),
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

        _viewport.Restore(double.IsFinite(state.CenterX) ? state.CenterX : 0,
            double.IsFinite(state.CenterY) ? state.CenterY : 0, null, null, state.Zoom);
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
        ZoomBox.Text = Format(_viewport.Zoom.ToDouble());
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
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (_updatingControls || !TryReadDouble(ZoomBox.Text, out double zoom) || zoom <= 0) return;
        _viewport.SetZoom(zoom);
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewport.Reset(0, 0, FloatExp.One);
        SetZoomText();
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForDomainColoring(this, _saveStore)); }

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new DomainColoringPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
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
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        try
        {
            renderer = new DomainColoringRenderer(state);
            NumericInput.ShowIssue(FormulaBox, "");
        }
        catch (Exception ex)
        {
            NumericInput.ShowIssue(FormulaBox, ex.Message);
            StatusText.Text = ex.Message;
            return;
        }

        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;
        var watch = Stopwatch.StartNew();
        SetRendering(true, $"Рендеринг f(z) = {renderer.ParsedFormula}...");

        TiledRenderSession? session = null;
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
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
            session.VisualizationFailed += _ => StatusText.Text = "Ошибка визуализации прогресса рендера.";
            _activeSession = session;
            CanvasImage.Source = bitmap;

            await session.RenderTilesAsync(tiles, GetThreadCount(),
                (tile, tileToken) => renderer.RenderTile(tile, renderWidth, renderHeight, tileToken), token);
            session.Flush();
            token.ThrowIfCancellationRequested();

            StablePreviewImage.Source = session.Snapshot();
            CanvasImage.Source = null;
            _viewport.MarkRendered(state.CenterX, state.CenterY, null, null, state.Zoom);
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
            session?.End();
            _activeSession = null;
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
            SetRendering(false);
        }
    }

    private Task<BitmapSource> RenderBitmapAsync(
        DomainColoringState state,
        int width,
        int height,
        int ssaa,
        CancellationToken token,
        IProgress<int>? progress)
    {
        var renderer = new DomainColoringRenderer(state);
        // Элементы окна принадлежат UI-потоку: число потоков читается до расчёта.
        int threads = GetThreadCount();
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4, (pixels, renderWidth, renderHeight, stride, report) =>
            renderer.Render(pixels, renderWidth, renderHeight, stride, threads, token, report), token, progress);
    }

    private int GetThreadCount() => RenderThreadsBox.Read(ThreadsBox);

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
        _viewport.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight,
            _viewport.WheelFactor(e.Delta));
        SetZoomText();
        UpdatePreviewTransform();
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitAndBakePreview();
        _viewport.BeginPan(e.GetPosition(CanvasHost));
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point current = e.GetPosition(CanvasHost);
        (double x, double y) = _viewport.ScreenToWorld(current, CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        CoordinateText.Text = $"z = {x:G7} {(y < 0 ? '−' : '+')} {Math.Abs(y):G7}i";
        if (_viewport.PanTo(current, CanvasHost.ActualWidth)) UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewport.EndPan()) return;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    private void UpdatePreviewTransform() => _viewport.UpdatePreview();

    private void SetZoomText()
    {
        _updatingControls = true;
        ZoomBox.Text = _viewport.Zoom.ToDouble().ToString("G8", CultureInfo.InvariantCulture);
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
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _activeSession?.End();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        Mouse.OverrideCursor = null;
    }

    private static bool TryReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

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
}
