using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class McMullenWindow : Window
{
    private readonly DoubleDeepZoomViewport _juliaView = new(McMullenRenderer.BaseViewWidth, 1e-4, 1e10, double.PositiveInfinity);
    private readonly DoubleDeepZoomViewport _parameterView = new(McMullenRenderer.BaseViewWidth, 1e-4, 1e10, double.PositiveInfinity);
    private DoubleDeepZoomViewport ActiveView => _plane == McMullenPlane.Parameter ? _parameterView : _juliaView;
    private McMullenPlane _plane;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly NewtonPaletteManager _palettes = new();
    private readonly FullscreenToggle _fullscreen = new();
    private CancellationTokenSource? _renderCts, _orbitCts;
    private TiledRenderSession? _session;
    private bool _updating = true, _rendering, _closed, _controlsVisible = true;

    public McMullenWindow()
    {
        InitializeComponent();
        RenderThreadsBox.Fill(ThreadsBox);
        PresetBox.ItemsSource = McMullenPresets.All;
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderPreviewAsync(); };
        LoadState(McMullenPresets.All[0]);
        Loaded += (_, _) => ScheduleRender();
    }

    public McMullenState CaptureState(string name)
    {
        // Validate visible navigation fields too: incomplete text must not save an older view.
        _ = Read(CenterXBox, "центр X", -1e6, 1e6); _ = Read(CenterYBox, "центр Y", -1e6, 1e6);
        _ = Read(ZoomBox, "приближение", 1e-4, 1e10);
        var state = new McMullenState
        {
            SaveName = name, Timestamp = DateTime.Now, Plane = _plane,
            P = PanelFields.ReadInt(PBox, "степень p", 2, 12), Q = PanelFields.ReadInt(QBox, "порядок полюса q", 1, 12),
            LambdaReal = Read(LambdaRealBox, "Re λ", -1e6, 1e6), LambdaImaginary = Read(LambdaImaginaryBox, "Im λ", -1e6, 1e6),
            CenterX = _juliaView.CenterX, CenterY = _juliaView.CenterY, Zoom = _juliaView.Zoom.ToDouble(),
            ParameterCenterX = _parameterView.CenterX, ParameterCenterY = _parameterView.CenterY, ParameterZoom = _parameterView.Zoom.ToDouble(),
            MaxIterations = PanelFields.ReadInt(IterationsBox, "итерации", 1, 10000), EscapeRadius = Read(EscapeRadiusBox, "радиус выхода", 2, 1e6),
            Coloring = (McMullenColoring)ColoringBox.SelectedIndex,
            ColorScale = Read(ColorScaleBox, "масштаб цвета", 0.1, 1000), ColorOffset = Read(ColorOffsetBox, "сдвиг цвета", 0, 1),
            TrapRadius = Read(TrapRadiusBox, "радиус ловушки", 0, 100), MaxPeriod = PanelFields.ReadInt(PeriodBox, "период", 1, 64),
            Tolerance = Read(ToleranceBox, "допуск", 1e-12, 1e-3), ShowCriticalPoints = CriticalPointsBox.IsChecked == true,
            Palette = _palettes.ActivePalette.Clone(_palettes.ActivePalette.Name)
        };
        state.Validate(); return state;
    }

    public void LoadState(McMullenState state)
    {
        state.Validate();
        BakePreview(); _timer.Stop(); ClearOrbit(); EndNavigation();
        _updating = true;
        try
        {
            StablePreviewImage.Source = null;
            _plane = state.Plane; PlaneBox.SelectedIndex = (int)_plane;
            PBox.Text = F(state.P); QBox.Text = F(state.Q);
            LambdaRealBox.Text = F(state.LambdaReal); LambdaImaginaryBox.Text = F(state.LambdaImaginary);
            IterationsBox.Text = F(state.MaxIterations); EscapeRadiusBox.Text = F(state.EscapeRadius);
            ColoringBox.SelectedIndex = (int)state.Coloring; ColorScaleBox.Text = F(state.ColorScale); ColorOffsetBox.Text = F(state.ColorOffset);
            TrapRadiusBox.Text = F(state.TrapRadius); PeriodBox.Text = F(state.MaxPeriod); ToleranceBox.Text = F(state.Tolerance);
            CriticalPointsBox.IsChecked = state.ShowCriticalPoints;
            _palettes.ActivePalette = state.Palette.Clone(state.Palette.Name);
            _juliaView.Restore(state.CenterX, state.CenterY, null, null, state.Zoom);
            _parameterView.Restore(state.ParameterCenterX, state.ParameterCenterY, null, null, state.ParameterZoom);
            ActiveView.AttachPreview(StablePreviewImage, CanvasHost);
            SetViewText(); UpdateOptions();
        }
        finally { _updating = false; }
        ActiveView.UpdatePreview(); DrawMarkers(); ScheduleRender();
    }

    private static double Read(TextBox box, string name, double min, double max) => PanelFields.ReadDouble(box, name, min, max);
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void SetViewText()
    {
        bool previous = _updating; _updating = true;
        CenterXBox.Text = F(ActiveView.CenterX); CenterYBox.Text = F(ActiveView.CenterY); ZoomBox.Text = F(ActiveView.Zoom.ToDouble());
        _updating = previous;
    }
    private void UpdateOptions()
    {
        bool julia = _plane == McMullenPlane.Julia;
        LambdaRealBox.IsEnabled = LambdaImaginaryBox.IsEnabled = CriticalPointsBox.IsEnabled = julia;
        TrapRadiusBox.IsEnabled = ColoringBox.SelectedIndex == (int)McMullenColoring.CircleTrap;
        PeriodBox.IsEnabled = ToleranceBox.IsEnabled = ColoringBox.SelectedIndex == (int)McMullenColoring.Period;
        PlaneDescriptionText.Text = julia ? "Каждый пиксель — начальная точка z. Внутренние отверстия ведут через полюс на бесконечность." :
            "Каждый пиксель — λ. Исследуется свободная критическая орбита; модули всех p+q таких орбит совпадают. Двойной щелчок открывает Жюлиа.";
        FormulaText.Text = $"zₙ₊₁ = z^{PBox.Text} + λ / z^{QBox.Text}";
        if (int.TryParse(PBox.Text, out int p) && int.TryParse(QBox.Text, out int q) && p >= 2 && q >= 1)
            FamilyHintText.Text = 1.0 / p + 1.0 / q < 1 ?
                "1/p + 1/q < 1: при подходящих λ возможны канторовы множества колец. Карта не доказывает топологический тип." :
                "1/p + 1/q ≥ 1: условие канторовых колец не выполнено; доступны другие структуры Жюлиа.";
    }
    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (_updating) return;
        UpdateOptions(); DrawMarkers(); ScheduleRender();
    }
    private void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && PresetBox.SelectedItem is McMullenState state) LoadState(state);
    }
    private void Plane_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PlaneBox.SelectedIndex < 0) return;
        BakePreview(); ClearOrbit(); EndNavigation();
        _plane = (McMullenPlane)PlaneBox.SelectedIndex; StablePreviewImage.Source = null;
        ActiveView.AttachPreview(StablePreviewImage, CanvasHost);
        SetViewText(); UpdateOptions(); DrawMarkers(); ScheduleRender();
    }
    public void ExploreParameter(double real, double imaginary)
    {
        var state = CaptureState("Выбранный λ");
        state.LambdaReal = real; state.LambdaImaginary = imaginary; state.Plane = McMullenPlane.Julia;
        state.CenterX = state.CenterY = 0; state.Zoom = 1;
        LoadState(state);
    }
    private void View_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        try
        {
            double x = Read(CenterXBox, "центр X", -1e6, 1e6), y = Read(CenterYBox, "центр Y", -1e6, 1e6);
            double zoom = Read(ZoomBox, "приближение", 1e-4, 1e10);
            BakePreview(); ActiveView.Restore(x, y, null, null, zoom);
            ActiveView.UpdatePreview(); DrawMarkers(); ScheduleRender();
        }
        catch (InvalidOperationException ex) { _timer.Stop(); BakePreview(); StatusText.Text = ex.Message; }
    }
    private void ScheduleRender()
    {
        if (!IsLoaded || _closed) return;
        BakePreview(); ClearOrbit(); _timer.Stop(); _timer.Start();
    }
    private void BakePreview()
    {
        _renderCts?.Cancel();
        if (_session is not { } session) return;
        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(SavePreviewLayer) is { } image)
        {
            StablePreviewImage.Source = image; ActiveView.MarkRendered(); ActiveView.UpdatePreview();
        }
        CanvasImage.Source = null; session.End(); _session = null;
    }
    private async Task RenderPreviewAsync()
    {
        _timer.Stop();
        if (_closed) return;
        if (_rendering) { ScheduleRender(); return; }
        TiledRenderSession? session = null;
        using var cts = new CancellationTokenSource();
        try
        {
            var state = CaptureState("preview");
            var surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int factor = Convert.ToInt32(((ComboBoxItem)SsaaBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
            int width = checked(surface.PixelWidth * factor), height = checked(surface.PixelHeight * factor);
            var renderer = new McMullenRenderer(state, width, height);
            _renderCts = cts; _rendering = true; CancelButton.IsEnabled = true;
            StatusText.Text = "Вычисление орбит…";
            var watch = Stopwatch.StartNew();
            var tiles = MandelbrotTileScheduler.Create(width, height, 16 * factor, RenderPatternSettings.SelectedPattern);
            var bitmap = ProgressiveRenderBitmap.CreateOverlay(width, height, surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress, cts);
            _session = session; CanvasImage.Source = bitmap;
            await session.RenderTilesAsync(tiles, RenderThreadsBox.Read(ThreadsBox), (tile, token) => renderer.RenderTile(tile, token), cts.Token);
            session.Flush(); cts.Token.ThrowIfCancellationRequested();
            StablePreviewImage.Source = session.Snapshot(); CanvasImage.Source = null;
            ActiveView.MarkRendered(state.ViewCenterX, state.ViewCenterY, null, null, state.ViewZoom); ActiveView.UpdatePreview();
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F2} с. p={state.P}, q={state.Q}. " +
                (state.LambdaReal == 0 && state.LambdaImaginary == 0 && state.Plane == McMullenPlane.Julia ? "λ = 0: полюс устраняется, f(z) = zᵖ." : "");
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Рендер отменён."; }
        catch (Exception ex)
        {
            if (!_closed) StatusText.Text = ex.Message;
            if (ex is not (ArgumentException or InvalidOperationException)) CrashLogger.Log("McMullen.Render", ex);
        }
        finally
        {
            session?.End(); if (ReferenceEquals(_session, session)) _session = null;
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            CanvasImage.Source = null; _rendering = false; CancelButton.IsEnabled = false; RenderProgress.Value = 0;
        }
    }
    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);
    public Task<BitmapSource> RenderStatePreviewAsync(McMullenState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        McMullenRenderer.RenderStateAsync(state, width, height, token, progress, threads: RenderThreadsBox.Read(ThreadsBox));
    private void Render_OnClick(object sender, RoutedEventArgs e) { BakePreview(); _ = RenderPreviewAsync(); }
    private void Cancel_OnClick(object sender, RoutedEventArgs e) { _timer.Stop(); BakePreview(); }
    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        BakePreview(); ActiveView.Reset(0, 0, _plane == McMullenPlane.Parameter ? 8 : 1);
        SetViewText(); ActiveView.UpdatePreview(); DrawMarkers(); ScheduleRender();
    }
    private void Saves_OnClick(object sender, RoutedEventArgs e) => SaveManagerWindow.Open(this, SaveManagerConfigurations.ForMcMullen(this, new()));
    private void Palette_OnClick(object sender, RoutedEventArgs e)
    {
        _palettes.ReloadCustomPalettes();
        var dialog = new NewtonPaletteWindow(_palettes, Enumerable.Repeat(Complex.Zero, 6).ToArray(),
            ["Цвет 1", "Цвет 2", "Цвет 3", "Цвет 4", "Цвет 5", "Цвет 6"], "Циклическая шкала", true, "Палитра Макмаллена") { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender(); dialog.ShowDialog();
    }
    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = CaptureState("export"); var surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int threads = RenderThreadsBox.Read(ThreadsBox); BakePreview();
            ImageExportManagerWindow.Open(this, new ImageExportConfiguration
            {
                FileNamePrefix = "McMullen", WindowTitle = "Экспорт: семейство Макмаллена", InitialWidth = surface.PixelWidth,
                InitialHeight = surface.PixelHeight, MaxSsaaFactor = 4,
                RenderAsync = (request, token, progress) => McMullenRenderer.RenderStateAsync(state, request.Width, request.Height,
                    token, progress, request.SsaaFactor, threads)
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void EndNavigation()
    {
        _juliaView.EndPan(); _parameterView.EndPan(); CanvasHost.ReleaseMouseCapture();
    }
    private void Canvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_updating) return;
        ActiveView.UpdatePreview(); DrawMarkers(); ScheduleRender();
    }
    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        BakePreview(); ActiveView.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight, ActiveView.WheelFactor(e.Delta));
        SetViewText(); ActiveView.UpdatePreview(); DrawMarkers(); ScheduleRender(); e.Handled = true;
    }
    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_plane == McMullenPlane.Parameter && e.ClickCount == 2)
        {
            var (x, y) = ActiveView.ScreenToWorld(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight);
            try { ExploreParameter(x, y); } catch (Exception ex) { StatusText.Text = ex.Message; }
            e.Handled = true; return;
        }
        BakePreview(); _timer.Stop(); ClearOrbit(); ActiveView.BeginPan(e.GetPosition(CanvasHost)); CanvasHost.CaptureMouse();
    }
    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        var (x, y) = ActiveView.ScreenToWorld(point, CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        CoordinateText.Text = $"{(_plane == McMullenPlane.Parameter ? "λ" : "z")} = {x:G9} + ({y:G9})i";
        if (ActiveView.PanTo(point, CanvasHost.ActualWidth)) { SetViewText(); ActiveView.UpdatePreview(); DrawMarkers(); }
    }
    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ActiveView.EndPan()) return;
        CanvasHost.ReleaseMouseCapture(); ScheduleRender();
    }
    private void Canvas_OnLostMouseCapture(object sender, MouseEventArgs e) { if (ActiveView.EndPan()) ScheduleRender(); }
    private async void Canvas_OnOrbit(object sender, MouseButtonEventArgs e)
    {
        ClearOrbit(); e.Handled = true;
        using var cts = new CancellationTokenSource(); _orbitCts = cts;
        try
        {
            var state = CaptureState("orbit"); state.Coloring = McMullenColoring.Period;
            var (x, y) = ActiveView.ScreenToWorld(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight);
            var dynamics = new McMullenDynamics(state, _plane == McMullenPlane.Parameter ? new Complex(x, y) : null);
            Complex start = _plane == McMullenPlane.Parameter ? dynamics.CriticalPoint() : new(x, y);
            var result = await Task.Run(() => (Orbit: dynamics.Analyze(start, cts.Token), Points: dynamics.Trace(start)), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            string outcome = result.Orbit.Outcome switch
            {
                McMullenOutcome.Escaped => "Выход во внешнюю область", McMullenOutcome.Pole => "Выход через окрестность полюса",
                McMullenOutcome.Periodic => $"Численно найден период {result.Orbit.Period}",
                McMullenOutcome.NumericalLimit => "Численный предел", _ => "Выход не найден за лимит итераций"
            };
            OrbitText.Text = $"{outcome}; шагов: {result.Orbit.Iterations}.\n" + string.Join("\n", result.Points.Take(12).Select((z, i) => $"{i}: {z.Real:G7} + ({z.Imaginary:G7})i"));
            if (_plane == McMullenPlane.Julia) DrawOrbit(result.Points);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) OrbitText.Text = ex.Message; }
        finally { if (ReferenceEquals(_orbitCts, cts)) _orbitCts = null; }
    }
    private Point ToScreen(Complex z)
    {
        double scale = CanvasHost.ActualWidth * ActiveView.Zoom.ToDouble() / McMullenRenderer.BaseViewWidth;
        return new(CanvasHost.ActualWidth / 2 + (z.Real - ActiveView.CenterX) * scale,
            CanvasHost.ActualHeight / 2 - (z.Imaginary - ActiveView.CenterY) * scale);
    }
    private bool IsNearCanvas(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        Math.Abs(p.X) < 10 * CanvasHost.ActualWidth && Math.Abs(p.Y) < 10 * CanvasHost.ActualHeight;
    private void DrawOrbit(IReadOnlyList<Complex> points)
    {
        Point? previous = null;
        foreach (var z in points)
        {
            var point = ToScreen(z);
            if (!IsNearCanvas(point)) { previous = null; continue; }
            if (previous is { } p) OrbitCanvas.Children.Add(new Line { X1 = p.X, Y1 = p.Y, X2 = point.X, Y2 = point.Y, Stroke = Brushes.White, StrokeThickness = 1 });
            AddMarker(OrbitCanvas, point, false); previous = point;
        }
    }
    private static void AddMarker(Canvas canvas, Point p, bool critical)
    {
        var marker = new Ellipse { Width = 7, Height = 7, Stroke = critical ? Brushes.Gold : Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Black };
        Canvas.SetLeft(marker, p.X - 3.5); Canvas.SetTop(marker, p.Y - 3.5); canvas.Children.Add(marker);
    }
    private void DrawMarkers()
    {
        MarkerCanvas.Children.Clear();
        if (_updating || _plane != McMullenPlane.Julia || CriticalPointsBox.IsChecked != true) return;
        try
        {
            var state = CaptureState("markers"); var dynamics = new McMullenDynamics(state);
            int count = dynamics.Lambda == Complex.Zero ? 1 : state.P + state.Q;
            for (int k = 0; k < count; k++)
            {
                var point = ToScreen(dynamics.CriticalPoint(k)); if (IsNearCanvas(point)) AddMarker(MarkerCanvas, point, true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
    }
    private void Markers_OnClick(object sender, RoutedEventArgs e) => DrawMarkers();
    private void ClearOrbit() { _orbitCts?.Cancel(); OrbitCanvas.Children.Clear(); OrbitText.Text = ""; }
    private void Toggle_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleControlsButton, 340); ScheduleRender();
    }
    private void Window_OnKeyDown(object sender, KeyEventArgs e) => _fullscreen.HandleKey(this, e.Key);
    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _closed = true; _timer.Stop(); _renderCts?.Cancel(); _orbitCts?.Cancel(); _session?.End(); _session = null; EndNavigation();
    }
}
