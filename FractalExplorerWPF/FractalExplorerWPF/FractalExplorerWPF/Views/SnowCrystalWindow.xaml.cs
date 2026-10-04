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
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class SnowCrystalWindow : Window
{
    private const int FrameSize = 640;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly SnowCrystalSaveStore _saveStore = new();
    private SnowCrystalState _state = new();
    private SnowCrystalSimulation? _simulation;
    private SnowCrystalCheckpoint? _presented;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private Task _workerIdle = Task.CompletedTask;
    private bool _busy, _running, _syncing = true, _closed, _resetting;
    private bool _controlsVisible = true, _panning, _fullScreen;
    private int _generation, _appearanceVersion;
    private Point _panStart;
    private WindowStyle _previousStyle;
    private WindowState _previousWindowState;

    public SnowCrystalWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = SnowCrystalPresets.All;
        _timer.Tick += async (_, _) => { if (_running) await ProduceFrameAsync(_state.StepsPerFrame); };
        ApplyState(SnowCrystalPresets.All[0].CreateState());
        Loaded += async (_, _) => await ResetAsync(_state, true);
    }

    public SnowCrystalState CaptureState(string name)
    {
        if (_presented is null) throw new InvalidOperationException("Дождитесь первого кадра кристалла.");
        // Save the displayed complete step, including all diffusion mass, rather than replaying parameters.
        SnowCrystalState state = _state.Clone(name);
        state.Timestamp = DateTime.Now;
        state.Checkpoint = _presented.Clone();
        return state;
    }

    public void LoadState(SnowCrystalState state) => _ = ResetAsync(state.Clone(), false);

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, FrameImage);

    public Task<BitmapSource> RenderStatePreviewAsync(SnowCrystalState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null) =>
        SnowCrystalRenderer.RenderStateAsync(state.Clone(), width, height, token, progress);

    private void ApplyState(SnowCrystalState state)
    {
        _syncing = true;
        _state = state.Clone(); _state.Checkpoint = null;
        DiffusionBox.Text = Format(state.Diffusion); VaporBox.Text = Format(state.Vapor);
        DepositionBox.Text = Format(state.Deposition); RadiusBox.Text = state.Radius.ToString(CultureInfo.InvariantCulture);
        SeedRadiusBox.Text = state.SeedRadius.ToString(CultureInfo.InvariantCulture);
        SpeedSlider.Value = state.StepsPerFrame; ColoringBox.SelectedIndex = (int)state.Coloring;
        VaporVisibleBox.IsChecked = state.ShowVapor;
        PaletteBox.SelectedIndex = state.CenterColor == Rgb(36, 115, 194) && state.TipColor == Rgb(224, 251, 255) && state.BackgroundColor == Rgb(3, 10, 24) ? 0 : -1;
        PresetBox.SelectedItem = SnowCrystalPresets.All.FirstOrDefault(p => p.Id == state.PresetId);
        PendingText.Visibility = Visibility.Collapsed;
        _syncing = false; UpdateColors();
    }

    private bool TryReadConditions(bool newField, out SnowCrystalState state)
    {
        state = _state.Clone();
        try
        {
            state.Diffusion = Parse(DiffusionBox.Text); state.Vapor = Parse(VaporBox.Text);
            state.Deposition = Parse(DepositionBox.Text);
            if (newField)
            {
                state.Radius = int.Parse(RadiusBox.Text, CultureInfo.InvariantCulture);
                state.SeedRadius = int.Parse(SeedRadiusBox.Text, CultureInfo.InvariantCulture);
            }
            state.Validate(); return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        { StatusText.Text = ex is ArgumentException ? ex.Message : "Проверьте числовые параметры."; return false; }
    }

    private async Task ResetAsync(SnowCrystalState state, bool run)
    {
        if (_closed) return;
        int generation = ++_generation;
        SetRunning(false); _resetting = true;
        _cts?.Cancel(); _cts?.Dispose(); _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        ResetButton.IsEnabled = StepButton.IsEnabled = RunButton.IsEnabled = false;
        try
        {
            // Build independently: switching presets cannot mutate the old worker's field.
            var simulation = await Task.Run(() => new SnowCrystalSimulation(state), token);
            SnowCrystalCheckpoint snapshot = simulation.Snapshot();
            byte[] pixels = await Task.Run(() => SnowCrystalRenderer.RenderFrame(snapshot, state, FrameSize, FrameSize, token, simulation.Lattice), token);
            if (_closed || generation != _generation || token.IsCancellationRequested) return;
            _simulation = simulation; ApplyState(state);
            _bitmap = new WriteableBitmap(FrameSize, FrameSize, 96, 96, PixelFormats.Bgra32, null);
            FrameImage.Source = _bitmap; _presented = snapshot;
            Present(pixels, simulation); SetRunning(run && !simulation.BoundaryReached);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (!_closed && generation == _generation) StatusText.Text = $"Не удалось загрузить поле: {ex.Message}"; }
        finally
        {
            if (!_closed && generation == _generation)
            { _resetting = false; ResetButton.IsEnabled = true; UpdateRunState(); }
        }
    }

    private async Task ProduceFrameAsync(int steps)
    {
        if (_closed || _busy || _resetting || _simulation is null || _cts is null) return;
        _busy = true;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workerIdle = idle.Task;
        UpdateRunState();
        int generation = _generation, appearance = _appearanceVersion;
        SnowCrystalSimulation simulation = _simulation;
        SnowCrystalState state = _state.Clone(); CancellationToken token = _cts.Token;
        try
        {
            var frame = await Task.Run(() =>
            {
                simulation.Advance(steps, state, token);
                SnowCrystalCheckpoint snapshot = simulation.Snapshot();
                return (Snapshot: snapshot, Pixels: SnowCrystalRenderer.RenderFrame(snapshot, state, FrameSize, FrameSize, token, simulation.Lattice));
            }, token);
            if (_closed || generation != _generation || token.IsCancellationRequested) return;
            _presented = frame.Snapshot;
            Present(frame.Pixels, simulation);
            if (simulation.BoundaryReached) SetRunning(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && generation == _generation) { SetRunning(false); StatusText.Text = ex.Message; }
        }
        finally
        {
            _busy = false;
            idle.TrySetResult();
            if (!_closed && generation == _generation)
            {
                UpdateRunState();
                // A colour/view change made while a frame was computing still reaches a paused image.
                if (appearance != _appearanceVersion && !_running) _ = ProduceFrameAsync(0);
            }
        }
    }

    private void Present(byte[] pixels, SnowCrystalSimulation simulation)
    {
        _bitmap?.WritePixels(new Int32Rect(0, 0, FrameSize, FrameSize), pixels, FrameSize * 4, 0);
        GrowthText.Text = $"Шаг {simulation.StepCount:N0} · {simulation.FrozenCount:N0} ячеек льда";
        UpdateStatus();
    }

    private void SetRunning(bool running)
    {
        _running = running && !_closed && _simulation?.BoundaryReached != true;
        if (_running) _timer.Start(); else _timer.Stop();
        UpdateRunState();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_simulation is null) return;
        StatusText.Text = _simulation.BoundaryReached ? "Кристалл достиг границы поля. Для нового роста увеличьте радиус."
            : $"Радиус кристалла: {_simulation.CrystalRadius} / {_state.Radius} · {(_running ? "растёт" : "пауза")}";
    }

    private void UpdateRunState()
    {
        RunButton.Content = _running ? "Пауза" : "Продолжить";
        RunButton.IsEnabled = !_resetting && _simulation is not null && !_simulation.BoundaryReached;
        StepButton.IsEnabled = !_resetting && !_busy && !_running && _simulation is not null && !_simulation.BoundaryReached;
    }

    private async void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedItem is not SnowCrystalPreset preset) return;
        var state = preset.CreateState(); state.Radius = _state.Radius;
        state.StepsPerFrame = _state.StepsPerFrame;
        await ResetAsync(state, true);
    }

    private void Conditions_OnChanged(object sender, TextChangedEventArgs e)
    { if (!_syncing) PendingText.Visibility = Visibility.Visible; }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_resetting || !TryReadConditions(false, out SnowCrystalState state)) return;
        state.PresetId = null; _state = state; PendingText.Visibility = Visibility.Collapsed;
        _syncing = true; PresetBox.SelectedItem = null; _syncing = false;
        StatusText.Text = "Условия изменены. Достигнутая форма сохранена.";
        RequestRepaint();
    }

    private async void Reset_OnClick(object sender, RoutedEventArgs e)
    { if (TryReadConditions(true, out SnowCrystalState state)) await ResetAsync(state, true); }

    private void Run_OnClick(object sender, RoutedEventArgs e) => SetRunning(!_running);
    private async void Step_OnClick(object sender, RoutedEventArgs e) { if (!_running) await ProduceFrameAsync(1); }

    private void Speed_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!_syncing) _state.StepsPerFrame = (int)Math.Round(SpeedSlider.Value); }

    private void Appearance_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _state.Coloring = (SnowCrystalColoring)Math.Max(0, ColoringBox.SelectedIndex);
        _state.ShowVapor = VaporVisibleBox.IsChecked == true; RequestRepaint();
    }

    private void Palette_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PaletteBox.SelectedIndex < 0) return;
        (_state.CenterColor, _state.TipColor, _state.BackgroundColor) = PaletteBox.SelectedIndex switch
        {
            1 => (Rgb(61, 39, 146), Rgb(158, 255, 198), Rgb(8, 5, 28)),
            2 => (Rgb(148, 61, 24), Rgb(255, 239, 164), Rgb(20, 8, 5)),
            3 => (Rgb(152, 177, 191), Rgb(255, 255, 255), Rgb(5, 10, 15)),
            _ => (Rgb(36, 115, 194), Rgb(224, 251, 255), Rgb(3, 10, 24))
        };
        UpdateColors(); RequestRepaint();
    }

    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        Color initial = sender == CenterColorButton ? _state.CenterColor : sender == TipColorButton ? _state.TipColor : _state.BackgroundColor;
        if (!ColorSelectionService.Default.TrySelectColor(this, initial, out Color selected)) return;
        if (sender == CenterColorButton) _state.CenterColor = selected;
        else if (sender == TipColorButton) _state.TipColor = selected;
        else _state.BackgroundColor = selected;
        _syncing = true; PaletteBox.SelectedIndex = -1; _syncing = false;
        UpdateColors(); RequestRepaint();
    }

    private void UpdateColors()
    {
        CanvasHost.Background = new SolidColorBrush(_state.BackgroundColor);
        CenterColorButton.ToolTip = $"Цвет центра: {_state.CenterColor}";
        TipColorButton.ToolTip = $"Цвет края: {_state.TipColor}";
        BackgroundButton.ToolTip = $"Цвет фона: {_state.BackgroundColor}";
    }

    private void RequestRepaint()
    { _appearanceVersion++; if (!_running) _ = ProduceFrameAsync(0); }

    private void Fit_OnClick(object sender, RoutedEventArgs e)
    {
        _state.PanX = _state.PanY = 0;
        _state.Zoom = Math.Clamp(_state.Radius / (double)Math.Max(12, _simulation?.CrystalRadius ?? _state.Radius), .25, 20);
        RequestRepaint();
    }

    private Point ScreenToWorld(Point p)
    {
        double side = Math.Max(1, Math.Min(CanvasHost.ActualWidth, CanvasHost.ActualHeight));
        double scale = side * .46 * _state.Zoom / _state.Radius;
        return new Point((p.X - CanvasHost.ActualWidth / 2) / scale + _state.PanX,
            -(p.Y - CanvasHost.ActualHeight / 2) / scale + _state.PanY);
    }

    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point p = e.GetPosition(CanvasHost), before = ScreenToWorld(p);
        _state.Zoom = Math.Clamp(_state.Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), .25, 20);
        Point after = ScreenToWorld(p);
        _state.PanX += before.X - after.X; _state.PanY += before.Y - after.Y;
        RequestRepaint(); e.Handled = true;
    }

    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    { _panning = true; _panStart = e.GetPosition(CanvasHost); CanvasHost.CaptureMouse(); }

    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning || e.LeftButton != MouseButtonState.Pressed) return;
        Point p = e.GetPosition(CanvasHost), before = ScreenToWorld(_panStart), after = ScreenToWorld(p);
        _state.PanX += before.X - after.X; _state.PanY += before.Y - after.Y; _panStart = p;
        RequestRepaint();
    }

    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    { _panning = false; CanvasHost.ReleaseMouseCapture(); }
    private void Canvas_OnLostCapture(object sender, MouseEventArgs e) => _panning = false;

    private async void Saves_OnClick(object sender, RoutedEventArgs e)
    {
        SetRunning(false);
        await RefreshPausedAsync();
        if (_closed || _resetting || _presented is null) return;
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForSnowCrystal(this, _saveStore));
    }

    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        SetRunning(false);
        await RefreshPausedAsync();
        if (_closed || _resetting || _presented is null) return;
        SnowCrystalState state = CaptureState("export");
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "snow_crystal", WindowTitle = "Экспорт снежного кристалла",
            InitialWidth = surface.PixelWidth, InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false, MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => SnowCrystalRenderer.RenderStateAsync(state, request.Width, request.Height, token, progress)
        });
    }

    private async Task RefreshPausedAsync()
    {
        // Repaint requests can queue another frame while the previous frame finishes.
        // Drain those too so captured state, visible preview and export describe the same image.
        while (!_closed && !_resetting)
        {
            await _workerIdle;
            if (_busy) continue;
            await ProduceFrameAsync(0);
            if (!_busy) return;
        }
    }

    private void Toggle_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleButton, 330);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox)
        { SetRunning(!_running); e.Handled = true; }
        else if (e.Key == Key.F11 || e.Key == Key.Escape && _fullScreen)
        {
            if (!_fullScreen) { _previousStyle = WindowStyle; _previousWindowState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
            else { WindowStyle = _previousStyle; WindowState = _previousWindowState; }
            _fullScreen = !_fullScreen;
        }
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    { _closed = true; _generation++; _timer.Stop(); _cts?.Cancel(); _cts?.Dispose(); }

    private static double Parse(string text) => double.TryParse(text.Replace(',', '.'), NumberStyles.Float,
        CultureInfo.InvariantCulture, out double value) ? value : throw new FormatException();
    private static string Format(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
