using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
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
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace FractalExplorerWPF.Views;

public partial class TranscendentalJuliaWindow : Window
{
    private readonly DoubleDeepZoomViewport _viewport = new(TranscendentalJuliaRenderer.BaseViewWidth, 1e-6, 1e10, double.PositiveInfinity);
    private readonly DoubleDeepZoomViewport _parameterViewport = new(TranscendentalJuliaRenderer.BaseViewWidth, 1e-6, 1e10, double.PositiveInfinity);
    private TranscendentalJuliaPlane _plane;
    private DoubleDeepZoomViewport ActiveView => _plane == TranscendentalJuliaPlane.Parameter ? _parameterViewport : _viewport;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly NewtonPaletteManager _palettes = new();
    private readonly FullscreenToggle _fullscreen = new();
    private CancellationTokenSource? _renderCts, _orbitCts;
    private TiledRenderSession? _session;
    private bool _updating = true, _rendering, _closed, _controlsVisible = true;
    public TranscendentalJuliaKind Kind { get; private set; }

    public TranscendentalJuliaWindow(TranscendentalJuliaKind kind = TranscendentalJuliaKind.Exponential)
    {
        InitializeComponent();
        ActiveView.AttachPreview(StablePreviewImage, CanvasHost);
        RenderThreadsBox.Fill(ThreadsBox);
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderPreviewAsync(); };
        LoadState(TranscendentalJuliaCatalog.CreateDefault(kind));
        Loaded += (_, _) => ScheduleRender();
    }

    public TranscendentalJuliaState CaptureState(string name)
    {
        var state = new TranscendentalJuliaState
        {
            SaveName = name, Timestamp = DateTime.Now, Kind = Kind,
            LambdaReal = PanelFields.ReadDouble(LambdaRealBox, "Re λ", -1e6, 1e6),
            LambdaImaginary = PanelFields.ReadDouble(LambdaImaginaryBox, "Im λ", -1e6, 1e6),
            CenterX = _viewport.CenterX, CenterY = _viewport.CenterY, Zoom = _viewport.Zoom.ToDouble(),
            ParameterCenterX = _parameterViewport.CenterX, ParameterCenterY = _parameterViewport.CenterY,
            ParameterZoom = _parameterViewport.Zoom.ToDouble(), Plane = _plane,
            MaxIterations = PanelFields.ReadInt(IterationsBox, "итерации", 1, 10000),
            MaxPeriod = PanelFields.ReadInt(PeriodBox, "макс. период", 1, 32),
            Tolerance = PanelFields.ReadDouble(ToleranceBox, "допуск", 1e-12, 1e-3),
            InverseDepth = PanelFields.ReadInt(DepthBox, "глубина прообразов", 1, 80),
            BranchRadius = PanelFields.ReadInt(BranchesBox, "радиус ветвей", 1, 24),
            PointBudget = PanelFields.ReadInt(BudgetBox, "бюджет прообразов", 1000, 10_000_000),
            Layer = (TranscendentalJuliaLayer)LayerBox.SelectedIndex,
            SamplingSeed = _samplingSeed,
            Drawing = (TranscendentalJuliaDrawing)DrawingBox.SelectedIndex,
            RayLimit = PanelFields.ReadInt(RayLimitBox, "частей лучей", 8, 10000),
            RayAccuracy = PanelFields.ReadDouble(RayAccuracyBox, "точность лучей", 0.05, 2),
            Palette = _palettes.ActivePalette.Clone(_palettes.ActivePalette.Name)
        };
        // Не сохраняем прежний центр при неоконченном/неверном вводе нового.
        _ = PanelFields.ReadDouble(CenterXBox, "центр X", -1e10, 1e10);
        _ = PanelFields.ReadDouble(CenterYBox, "центр Y", -1e10, 1e10);
        _ = PanelFields.ReadDouble(ZoomBox, "приближение", 1e-6, 1e10);
        state.Validate();
        return state;
    }
    private int _samplingSeed = 137;

    public void LoadState(TranscendentalJuliaState state)
    {
        state.Validate();
        BakePreview(); _timer.Stop(); ClearOrbit();
        EndNavigation();
        _updating = true;
        if (_plane != state.Plane || Kind != state.Kind) StablePreviewImage.Source = null;
        _plane = state.Plane;
        PlaneBox.SelectedIndex = (int)_plane;
        Kind = state.Kind;
        KindBox.SelectedIndex = (int)Kind;
        Title = TranscendentalJuliaCatalog.Title(Kind);
        FormulaText.Text = TranscendentalJuliaCatalog.Formula(Kind);
        LambdaRealBox.Text = FormatValue(state.LambdaReal);
        LambdaImaginaryBox.Text = FormatValue(state.LambdaImaginary);
        IterationsBox.Text = state.MaxIterations.ToString(CultureInfo.InvariantCulture);
        PeriodBox.Text = state.MaxPeriod.ToString(CultureInfo.InvariantCulture);
        ToleranceBox.Text = FormatValue(state.Tolerance);
        DepthBox.Text = state.InverseDepth.ToString(CultureInfo.InvariantCulture);
        BranchesBox.Text = state.BranchRadius.ToString(CultureInfo.InvariantCulture);
        BudgetBox.Text = state.PointBudget.ToString(CultureInfo.InvariantCulture);
        LayerBox.SelectedIndex = (int)state.Layer;
        DrawingBox.SelectedIndex = (int)state.Drawing;
        RayLimitBox.Text = state.RayLimit.ToString(CultureInfo.InvariantCulture);
        RayAccuracyBox.Text = FormatValue(state.RayAccuracy);
        _samplingSeed = state.SamplingSeed;
        _palettes.ActivePalette = state.Palette.Clone(state.Palette.Name);
        _viewport.Restore(state.CenterX, state.CenterY, null, null, state.Zoom);
        _parameterViewport.Restore(state.ParameterCenterX, state.ParameterCenterY, null, null, state.ParameterZoom);
        ActiveView.AttachPreview(StablePreviewImage, CanvasHost);
        UpdatePlaneControls();
        SetViewText();
        _updating = false;
        ActiveView.UpdatePreview();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);
    public Task<BitmapSource> RenderStatePreviewAsync(TranscendentalJuliaState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null) =>
        TranscendentalJuliaRenderer.RenderStateAsync(state, width, height, token, progress,
            threads: RenderThreadsBox.Read(ThreadsBox));

    private void Kind_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && KindBox.SelectedIndex >= 0)
            LoadState(TranscendentalJuliaCatalog.CreateDefault((TranscendentalJuliaKind)KindBox.SelectedIndex));
    }
    private void Parameter_OnChanged(object sender, EventArgs e) { if (!_updating) ScheduleRender(); }
    private void Plane_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PlaneBox.SelectedIndex < 0) return;
        BakePreview(); ClearOrbit();
        EndNavigation();
        _plane = (TranscendentalJuliaPlane)PlaneBox.SelectedIndex;
        StablePreviewImage.Source = null;
        ActiveView.AttachPreview(StablePreviewImage, CanvasHost);
        SetViewText(); UpdatePlaneControls(); ScheduleRender();
    }
    private void UpdatePlaneControls()
    {
        bool dynamic = _plane == TranscendentalJuliaPlane.Dynamic;
        LambdaRealBox.IsEnabled = LambdaImaginaryBox.IsEnabled = LayerBox.IsEnabled = DrawingBox.IsEnabled = dynamic;
        RayLimitBox.IsEnabled = RayAccuracyBox.IsEnabled = DepthBox.IsEnabled = BranchesBox.IsEnabled = BudgetBox.IsEnabled = dynamic;
        PlaneDescriptionText.Text = dynamic
            ? "Лучам задаются согласованные обратные ветви и точность в пикселях. Разрывы и критические точки разделяют кривые."
            : "Цвет показывает период притягивающего цикла. Для синуса учитываются обе орбиты ±λ. Правая кнопка: исходы орбит; двойной щелчок: открыть Жюлиа.";
    }
    private void EndNavigation()
    {
        _viewport.EndPan(); _parameterViewport.EndPan();
        CanvasHost.ReleaseMouseCapture();
    }

    public void ExploreParameter(double real, double imaginary)
    {
        var state = CaptureState("parameter");
        state.LambdaReal = real; state.LambdaImaginary = imaginary;
        state.Plane = TranscendentalJuliaPlane.Dynamic;
        var defaults = TranscendentalJuliaCatalog.CreateDefault(Kind);
        state.CenterX = defaults.CenterX; state.CenterY = defaults.CenterY; state.Zoom = defaults.Zoom;
        LoadState(state);
    }
    private void View_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        try
        {
            double x = PanelFields.ReadDouble(CenterXBox, "центр X", -1e10, 1e10);
            double y = PanelFields.ReadDouble(CenterYBox, "центр Y", -1e10, 1e10);
            double zoom = PanelFields.ReadDouble(ZoomBox, "приближение", 1e-6, 1e10);
            BakePreview();
            ActiveView.Restore(x, y, null, null, zoom);
            ActiveView.UpdatePreview();
            ScheduleRender();
        }
        catch (InvalidOperationException ex) { _renderCts?.Cancel(); _timer.Stop(); StatusText.Text = ex.Message; }
    }

    private void ScheduleRender()
    {
        if (!IsLoaded || _closed) return;
        BakePreview(); ClearOrbit();
        _timer.Stop(); _timer.Start();
    }
    private void BakePreview()
    {
        _renderCts?.Cancel();
        PreparationOverlay.Visibility = Visibility.Collapsed;
        PreparationOverlay.IsBusy = false;
        if (_session is not { } session) return;
        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(SavePreviewLayer) is { } baked)
        {
            StablePreviewImage.Source = baked;
            ActiveView.MarkRendered();
            ActiveView.UpdatePreview();
        }
        CanvasImage.Source = null;
        session.End(); _session = null;
    }

    private async Task RenderPreviewAsync()
    {
        _timer.Stop();
        if (_closed) return;
        if (_rendering) { ScheduleRender(); return; }
        TranscendentalJuliaState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        var renderer = new TranscendentalJuliaRenderer(state);
        using var cts = new CancellationTokenSource();
        _renderCts = cts;
        _rendering = true; CancelButton.IsEnabled = true;
        StatusText.Text = "Поиск циклов и подготовка прообразов Жюлиа…";
        PreparationOverlay.Heading = Title;
        PreparationOverlay.Message = state.Plane == TranscendentalJuliaPlane.Parameter
            ? "Подготовка карты параметра λ…"
            : "Поиск циклов и построение структуры Жюлиа…";
        PreparationOverlay.Detail = "Вычисления идут. После подготовки изображение начнёт появляться по частям.";
        PreparationOverlay.ActionText = "Отменить";
        PreparationOverlay.IsBusy = true;
        if (StablePreviewImage.Source is not null)
            PreparationOverlay.Background = Brushes.Transparent;
        else
            PreparationOverlay.SetResourceReference(BackgroundProperty, "Theme.PanelBackgroundBrush");
        PreparationOverlay.Visibility = Visibility.Visible;
        var watch = Stopwatch.StartNew();
        TiledRenderSession? session = null;
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int factor = Convert.ToInt32(((ComboBoxItem)SsaaBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
            int width = checked(surface.PixelWidth * factor), height = checked(surface.PixelHeight * factor);
            await Task.Run(() => renderer.Prepare(width, height, cts.Token), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var tiles = MandelbrotTileScheduler.Create(width, height, 16 * factor, RenderPatternSettings.SelectedPattern);
            var bitmap = ProgressiveRenderBitmap.CreateOverlay(width, height, surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
            _session = session; CanvasImage.Source = bitmap;
            PreparationOverlay.Visibility = Visibility.Collapsed;
            PreparationOverlay.IsBusy = false;
            StatusText.Text = renderer.Description;
            await session.RenderTilesAsync(tiles, RenderThreadsBox.Read(ThreadsBox),
                (tile, token) => renderer.RenderTile(tile, token), cts.Token);
            session.Flush(); cts.Token.ThrowIfCancellationRequested();
            StablePreviewImage.Source = session.Snapshot();
            CanvasImage.Source = null;
            ActiveView.MarkRendered(state.ViewCenterX, state.ViewCenterY, null, null, state.ViewZoom);
            ActiveView.UpdatePreview();
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F2} с. {renderer.Description}";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Рендер отменён."; }
        catch (Exception ex)
        {
            if (!_closed)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                PreparationOverlay.Message = StatusText.Text;
                PreparationOverlay.Detail = "Проверьте параметры и запустите рендер снова.";
                PreparationOverlay.IsBusy = false;
                PreparationOverlay.ActionText = "Закрыть";
                PreparationOverlay.Visibility = Visibility.Visible;
            }
            CrashLogger.Log("TranscendentalJulia.Render", ex);
        }
        finally
        {
            if (PreparationOverlay.IsBusy)
            {
                PreparationOverlay.Visibility = Visibility.Collapsed;
                PreparationOverlay.IsBusy = false;
            }
            session?.End();
            if (ReferenceEquals(_session, session)) _session = null;
            CanvasImage.Source = null;
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            _rendering = false; CancelButton.IsEnabled = false; RenderProgress.Value = 0;
        }
    }

    private void Render_OnClick(object sender, RoutedEventArgs e) { BakePreview(); _ = RenderPreviewAsync(); }
    private void Cancel_OnClick(object sender, RoutedEventArgs e) { _timer.Stop(); BakePreview(); }
    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        BakePreview();
        var defaults = TranscendentalJuliaCatalog.CreateDefault(Kind);
        ActiveView.Reset(_plane == TranscendentalJuliaPlane.Parameter ? 0 : defaults.CenterX,
            _plane == TranscendentalJuliaPlane.Parameter ? 0 : defaults.CenterY,
            _plane == TranscendentalJuliaPlane.Parameter ? (FloatExp)3 : FloatExp.One);
        SetViewText(); ActiveView.UpdatePreview(); ScheduleRender();
    }
    private static string FormatValue(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void SetViewText()
    {
        bool previous = _updating; _updating = true;
        CenterXBox.Text = FormatValue(ActiveView.CenterX);
        CenterYBox.Text = FormatValue(ActiveView.CenterY);
        ZoomBox.Text = FormatValue(ActiveView.Zoom.ToDouble());
        _updating = previous;
    }
    private void Saves_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForTranscendentalJulia(this, new(Kind)));
    private void Palette_OnClick(object sender, RoutedEventArgs e)
    {
        _palettes.ReloadCustomPalettes();
        string[] labels = ["Жюлиа: лучи и точки", "Нераспознанные орбиты", "Численный предел", "Цикл 1", "Цикл 2", "Цикл 3", "Цикл 4"];
        var dialog = new NewtonPaletteWindow(_palettes, Enumerable.Repeat(Complex.Zero, labels.Length).ToArray(),
            labels, "Цвета слоёв и исходов орбиты", true, "Палитра трансцендентных Жюлиа") { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }
    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = CaptureState("export");
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int threads = RenderThreadsBox.Read(ThreadsBox);
            BakePreview();
            ImageExportManagerWindow.Open(this, new ImageExportConfiguration
            {
                FileNamePrefix = TranscendentalJuliaCatalog.LaunchKey(Kind), WindowTitle = $"Экспорт: {Title}",
                InitialWidth = surface.PixelWidth, InitialHeight = surface.PixelHeight, MaxSsaaFactor = 4,
                RenderAsync = (request, token, progress) => TranscendentalJuliaRenderer.RenderStateAsync(state,
                    request.Width, request.Height, token, progress, request.SsaaFactor, threads)
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Canvas_OnSizeChanged(object sender, SizeChangedEventArgs e) { ActiveView.UpdatePreview(); ScheduleRender(); }
    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        BakePreview();
        ActiveView.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight, ActiveView.WheelFactor(e.Delta));
        SetViewText(); ActiveView.UpdatePreview(); ScheduleRender(); e.Handled = true;
    }
    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_plane == TranscendentalJuliaPlane.Parameter && e.ClickCount == 2)
        {
            (double real, double imaginary) = ActiveView.ScreenToWorld(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight);
            try { ExploreParameter(real, imaginary); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            e.Handled = true; return;
        }
        BakePreview(); _timer.Stop(); ClearOrbit();
        ActiveView.BeginPan(e.GetPosition(CanvasHost)); CanvasHost.CaptureMouse();
    }
    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        (double x, double y) = ActiveView.ScreenToWorld(point, CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        CoordinateText.Text = $"{(_plane == TranscendentalJuliaPlane.Parameter ? "λ" : "z")} = {x:G8} {(y < 0 ? '−' : '+')} {Math.Abs(y):G8}i";
        if (ActiveView.PanTo(point, CanvasHost.ActualWidth)) { SetViewText(); ActiveView.UpdatePreview(); }
    }
    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ActiveView.EndPan()) return;
        CanvasHost.ReleaseMouseCapture(); ScheduleRender();
    }
    private void Canvas_OnLostMouseCapture(object sender, MouseEventArgs e) { if (ActiveView.EndPan()) ScheduleRender(); }

    private async void Canvas_OnOrbit(object sender, MouseButtonEventArgs e)
    {
        ClearOrbit();
        TranscendentalJuliaState state;
        try { state = CaptureState("orbit"); }
        catch (Exception ex) { OrbitText.Text = ex.Message; return; }
        (double x, double y) = ActiveView.ScreenToWorld(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        e.Handled = true;
        using var cts = new CancellationTokenSource(); _orbitCts = cts;
        OrbitText.Text = "Расчёт орбиты…";
        try
        {
            if (_plane == TranscendentalJuliaPlane.Parameter)
            {
                var parameter = await Task.Run(() => TranscendentalParameterMap.Analyze(Kind, new(x, y), state.MaxIterations,
                    state.MaxPeriod, state.Tolerance, cts.Token), cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                string Describe(string name, SingularOrbitResult orbit) => $"{name}: " + (orbit.Outcome switch
                {
                    TranscendentalOrbitOutcome.Converged => $"притягивающий цикл периода {orbit.Period}; множитель {orbit.Multiplier:G5}",
                    TranscendentalOrbitOutcome.NumericalLimit => "численный предел",
                    _ => "исход не распознан"
                }) + $"; шагов: {orbit.Iterations}.";
                OrbitText.Text = $"λ = {x:G9} + ({y:G9})i\n" + Describe(Kind == TranscendentalJuliaKind.Sine ? "+λ" : "0", parameter.First) +
                    (parameter.Second is { } second ? "\n" + Describe("−λ", second) : "");
                return;
            }
            var result = await Task.Run(() =>
            {
                var dynamics = new TranscendentalJuliaDynamics(state);
                var cycles = dynamics.FindAttractors(cts.Token);
                return (Trace: dynamics.Trace(new(x, y), cts.Token), Orbit: dynamics.Classify(new(x, y), cycles, cts.Token));
            }, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            string outcome = result.Orbit.Outcome switch
            {
                TranscendentalOrbitOutcome.Converged => "Сходимость к найденному циклу",
                TranscendentalOrbitOutcome.NumericalLimit => "Достигнут численный предел",
                _ => "Исход не распознан за лимит итераций"
            };
            var text = new StringBuilder($"{outcome}; шагов: {result.Orbit.Iterations}.\n");
            for (int i = 0; i < Math.Min(12, result.Trace.Count); i++) text.AppendLine($"{i}: {result.Trace[i].Real:G7} {result.Trace[i].Imaginary:+0.######;-0.######;+0}i");
            OrbitText.Text = text.ToString();
            DrawOrbit(result.Trace, state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) OrbitText.Text = ex.Message; }
        finally { if (ReferenceEquals(_orbitCts, cts)) _orbitCts = null; }
        e.Handled = true;
    }
    private void DrawOrbit(IReadOnlyList<Complex> points, TranscendentalJuliaState state)
    {
        double width = CanvasHost.ActualWidth, height = CanvasHost.ActualHeight;
        double scale = width * state.Zoom / TranscendentalJuliaRenderer.BaseViewWidth;
        Point? previous = null;
        foreach (Complex z in points.Take(256))
        {
            Point point = new(width / 2 + (z.Real - state.CenterX) * scale, height / 2 - (z.Imaginary - state.CenterY) * scale);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > width * 10 || Math.Abs(point.Y) > height * 10)
            { previous = null; continue; }
            if (previous is { } p) OrbitCanvas.Children.Add(new Line { X1 = p.X, Y1 = p.Y, X2 = point.X, Y2 = point.Y,
                Stroke = Brushes.White, StrokeThickness = 1, Opacity = 0.65 });
            var marker = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(marker, point.X - 2.5); Canvas.SetTop(marker, point.Y - 2.5); OrbitCanvas.Children.Add(marker);
            previous = point;
        }
    }
    private void ClearOrbit() { _orbitCts?.Cancel(); OrbitCanvas.Children.Clear(); OrbitText.Text = ""; }
    private void ClearOrbit_OnClick(object sender, RoutedEventArgs e) => ClearOrbit();
    private void Toggle_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleControlsButton, 330); ScheduleRender();
    }
    private void Window_OnKeyDown(object sender, KeyEventArgs e) => _fullscreen.HandleKey(this, e.Key);
    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _closed = true; _timer.Stop(); _renderCts?.Cancel(); _orbitCts?.Cancel();
        _session?.End(); _session = null; CanvasHost.ReleaseMouseCapture();
    }
}
