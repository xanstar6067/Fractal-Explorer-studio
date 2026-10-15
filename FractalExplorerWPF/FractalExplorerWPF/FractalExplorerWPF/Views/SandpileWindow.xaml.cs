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

public partial class SandpileWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly FullscreenToggle _fullscreen = new();
    private readonly SandpileSaveStore _store = new();
    private readonly List<(int X, int Y, int Radius, int Grains)> _brush = [];
    private SandpileState _state = new();
    private SandpileState? _shown;
    private SandpileSimulation? _simulation;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private Task _workerIdle = Task.CompletedTask;
    private bool _syncing = true, _busy, _resetting, _running, _closed, _dirty, _controlsVisible = true;
    private bool _painting, _panning;
    private int _generation;
    private Point _panStart;
    private (int X, int Y)? _lastBrush;

    public SandpileWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = SandpilePresets.All;
        PaletteBox.ItemsSource = SandpilePalettes.All;
        for (int i = 0; i < 6; i++)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = $"{i}", VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Tag = i, Margin = new Thickness(2), ToolTip = $"Цвет клетки с {i} песчинками" };
            button.Click += Color_OnClick; ColorButtons.Children.Add(button);
        }
        ApplyState(_state);
        _timer.Tick += async (_, _) =>
        {
            if (!_running || _busy || _simulation is null) return;
            // Stable fields stay ready for painting without allocating idle frames.
            if (_simulation.IsStable && !_state.AutoDrop && _brush.Count == 0 && !_dirty) return;
            await ProduceFrameAsync(_state.OperationsPerFrame, true);
        };
        Loaded += async (_, _) => await ResetAsync(_state.Clone(), true);
    }

    public SandpileState CaptureState(string name)
    {
        if (_shown is null) throw new InvalidOperationException("Дождитесь первого кадра песочной кучи.");
        var state = _shown.Clone(name); state.Timestamp = DateTime.Now; return state;
    }
    public void LoadState(SandpileState state) => _ = ResetAsync(state.Clone(), false);
    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, FrameImage);
    public Task<BitmapSource> RenderStatePreviewAsync(SandpileState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        SandpileRenderer.RenderStateAsync(state.Clone(), width, height, token, progress);

    private void ApplyState(SandpileState state)
    {
        _syncing = true;
        _state = state.Clone(); _state.Checkpoint = null;
        LatticeBox.SelectedIndex = (int)state.Lattice; SeedBox.SelectedIndex = (int)state.Seed;
        SizeBox.Text = state.Size.ToString(CultureInfo.InvariantCulture); InitialBox.Text = state.InitialGrains.ToString(CultureInfo.InvariantCulture);
        BrushGrainsBox.Text = state.BrushGrains.ToString(CultureInfo.InvariantCulture); BrushRadiusBox.Text = state.BrushRadius.ToString(CultureInfo.InvariantCulture);
        DropBox.Text = state.DropGrains.ToString(CultureInfo.InvariantCulture); AutoDropBox.IsChecked = state.AutoDrop;
        SpeedSlider.Value = state.OperationsPerFrame; ColoringBox.SelectedIndex = (int)state.Coloring;
        PaletteBox.SelectedItem = SandpilePalettes.All.FirstOrDefault(p => p.Colors.SequenceEqual(state.Colors));
        PresetBox.SelectedItem = SandpilePresets.All.FirstOrDefault(p =>
            p.Lattice == state.Lattice && p.Seed == state.Seed && p.Grains == state.InitialGrains && p.Size == state.Size);
        RuleText.Text = $"При {_state.Degree} песчинках клетка отдаёт по одной каждому соседу. Песок за краем поля теряется. Итог лавины не зависит от порядка обрушений.";
        _syncing = false; UpdateColors();
    }

    private async Task ResetAsync(SandpileState state, bool run)
    {
        if (_closed) return;
        int generation = ++_generation;
        SetRunning(false); _resetting = true; UpdateButtons();
        _cts?.Cancel(); _brush.Clear(); _dirty = false;
        StatusText.Text = "Подготовка поля…";
        try
        {
            await _workerIdle;
            if (_closed || generation != _generation) return;
            _cts?.Dispose(); _cts = new CancellationTokenSource(); var token = _cts.Token;
            var simulation = await Task.Run(() => new SandpileSimulation(state), token);
            if (_closed || generation != _generation) return;
            _simulation = simulation; ApplyState(state); _resetting = false;
            await ProduceFrameAsync(0);
            if (_closed || generation != _generation) return;
            SetRunning(run);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && generation == _generation) { StatusText.Text = $"Не удалось создать поле: {ex.Message}"; CrashLogger.Log("Sandpile.Reset", ex); }
        }
        finally
        {
            if (!_closed && generation == _generation) { _resetting = false; UpdateButtons(); }
        }
    }

    private (int Width, int Height) FrameSize()
    {
        double width = Math.Max(1, CanvasHost.ActualWidth - 2), height = Math.Max(1, CanvasHost.ActualHeight - 2);
        if (width < 10 || height < 10) return (720, 720);
        double ratio = Math.Min(1, 1100 / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
    }

    private async Task ProduceFrameAsync(int budget, bool autoDrop = false)
    {
        if (_closed || _busy || _resetting || _simulation is null || _cts is null) return;
        _busy = true; _dirty = false; UpdateButtons();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _workerIdle = completion.Task;
        int generation = _generation;
        var simulation = _simulation; var state = _state.Clone(); var token = _cts.Token;
        var brush = _brush.ToArray(); _brush.Clear(); var size = FrameSize();
        try
        {
            var frame = await Task.Run(() =>
            {
                foreach (var dab in brush) simulation.AddBrush(dab.X, dab.Y, dab.Radius, dab.Grains);
                if (autoDrop && state.AutoDrop && simulation.IsStable) simulation.Add(state.Size / 2, state.Size / 2, state.DropGrains);
                simulation.Advance(budget, token);
                var snapshot = simulation.Snapshot();
                return (Snapshot: snapshot, Pixels: SandpileRenderer.RenderFrame(snapshot, state, size.Width, size.Height, token));
            }, token);
            if (_closed || generation != _generation || token.IsCancellationRequested) return;
            state.Checkpoint = frame.Snapshot; _shown = state;
            if (_bitmap is null || _bitmap.PixelWidth != size.Width || _bitmap.PixelHeight != size.Height)
            {
                _bitmap = new WriteableBitmap(size.Width, size.Height, 96, 96, PixelFormats.Bgra32, null); FrameImage.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, size.Width, size.Height), frame.Pixels, size.Width * 4, 0);
            MassText.Text = $"В поле: {frame.Snapshot.Added - frame.Snapshot.Lost:N0} · ушло: {frame.Snapshot.Lost:N0}";
            StatusText.Text = $"Активных клеток: {frame.Snapshot.Queue.Length:N0} · обработано: {frame.Snapshot.Operations:N0}\n" +
                (simulation.IsStable ? "Поле устойчиво. Подсыпьте песок или включите автоподсыпку." : _running ? "Идёт лавина…" : "Лавина на паузе.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && generation == _generation)
            {
                SetRunning(false); StatusText.Text = ex.Message; CrashLogger.Log("Sandpile.Frame", ex);
            }
        }
        finally
        {
            _busy = false; completion.TrySetResult();
            if (!_closed && generation == _generation)
            {
                UpdateButtons();
                if (_dirty || _brush.Count > 0) _ = ProduceFrameAsync(0);
            }
        }
    }

    private void SetRunning(bool run)
    {
        _running = run && !_closed;
        if (_running) _timer.Start(); else _timer.Stop();
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        RunButton.Content = _running ? "Пауза" : "Продолжить";
        RunButton.IsEnabled = !_resetting && _simulation is not null;
        StepButton.IsEnabled = !_resetting && !_busy && !_running && _simulation is not null;
        ResetButton.IsEnabled = !_resetting;
        SavesButton.IsEnabled = ExportButton.IsEnabled = !_resetting && _shown is not null;
    }
    private void Repaint() { _dirty = true; if (!_busy) _ = ProduceFrameAsync(0); }

    private async void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedItem is not SandpilePreset preset) return;
        var state = preset.CreateState(); state.Colors = [.. _state.Colors]; state.OperationsPerFrame = _state.OperationsPerFrame;
        await ResetAsync(state, true);
    }
    private async void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = _state.Clone(); state.Size = int.Parse(SizeBox.Text, CultureInfo.InvariantCulture);
            state.InitialGrains = long.Parse(InitialBox.Text, CultureInfo.InvariantCulture);
            state.Lattice = (SandpileLattice)LatticeBox.SelectedIndex; state.Seed = (SandpileSeed)SeedBox.SelectedIndex;
            state.Zoom = 1; state.PanX = state.PanY = 0; state.Validate();
            _syncing = true; PresetBox.SelectedItem = null; _syncing = false;
            await ResetAsync(state, true);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { StatusText.Text = ex.Message; }
    }
    private void Run_OnClick(object sender, RoutedEventArgs e) => SetRunning(!_running);
    private async void Step_OnClick(object sender, RoutedEventArgs e) => await ProduceFrameAsync(_state.OperationsPerFrame);
    private void LiveOptions_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            var state = _state.Clone();
            state.BrushGrains = int.Parse(BrushGrainsBox.Text, CultureInfo.InvariantCulture);
            state.BrushRadius = int.Parse(BrushRadiusBox.Text, CultureInfo.InvariantCulture);
            state.DropGrains = int.Parse(DropBox.Text, CultureInfo.InvariantCulture); state.AutoDrop = AutoDropBox.IsChecked == true;
            state.Validate(); _state = state; Repaint();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { StatusText.Text = "Проверьте подсыпку (1–1 000 000) и радиус кисти (0–20)."; }
    }
    private void Speed_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!_syncing) { _state.OperationsPerFrame = (int)SpeedSlider.Value; Repaint(); } }
    private void Appearance_OnChanged(object sender, SelectionChangedEventArgs e)
    { if (!_syncing) { _state.Coloring = (SandpileColoring)Math.Max(0, ColoringBox.SelectedIndex); Repaint(); } }
    private void Palette_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PaletteBox.SelectedItem is not SandpilePalette palette) return;
        _state.Colors = palette.Colors.ToArray(); UpdateColors(); Repaint();
    }
    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        int i = (int)((Button)sender).Tag;
        if (!ColorSelectionService.Default.TrySelectColor(this, _state.Colors[i], out Color color)) return;
        _state.Colors[i] = Color.FromRgb(color.R, color.G, color.B);
        _syncing = true; PaletteBox.SelectedItem = null; _syncing = false; UpdateColors(); Repaint();
    }
    private void UpdateColors()
    {
        CanvasHost.Background = new SolidColorBrush(_state.Colors[0]);
        for (int i = 0; i < ColorButtons.Children.Count; i++)
        {
            var button = (Button)ColorButtons.Children[i]; button.IsEnabled = i < _state.Degree;
            ((Border)((StackPanel)button.Content).Children[0]).Background = new SolidColorBrush(_state.Colors[i]);
            button.ToolTip = $"{i} песчинок: {_state.Colors[i]} · выбрать цвет";
        }
    }
    private void AddCenter_OnClick(object sender, RoutedEventArgs e) => QueueBrush(_state.Size / 2, _state.Size / 2);
    private void QueueBrush(int x, int y)
    {
        if (_resetting || _closed || (uint)x >= _state.Size || (uint)y >= _state.Size || _brush.Count >= 2048) return;
        _brush.Add((x, y, _state.BrushRadius, _state.BrushGrains)); Repaint();
    }
    private void Paint(Point p)
    {
        var cell = SandpileRenderer.CellAt(p.X - 1, p.Y - 1, Math.Max(1, CanvasHost.ActualWidth - 2), Math.Max(1, CanvasHost.ActualHeight - 2), _state);
        if (_lastBrush == cell) return;
        _lastBrush = cell; QueueBrush(cell.X, cell.Y);
    }
    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_resetting) return;
        _panStart = e.GetPosition(CanvasHost); _lastBrush = null;
        _painting = e.ChangedButton == MouseButton.Left; _panning = e.ChangedButton is MouseButton.Right or MouseButton.Middle;
        if (_painting || _panning) { CanvasHost.CaptureMouse(); if (_painting) Paint(_panStart); e.Handled = true; }
    }
    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(CanvasHost);
        if (_painting && e.LeftButton == MouseButtonState.Pressed) Paint(p);
        if (!_panning) return;
        var before = SandpileRenderer.ToLattice(_panStart.X, _panStart.Y, CanvasHost.ActualWidth, CanvasHost.ActualHeight, _state);
        var after = SandpileRenderer.ToLattice(p.X, p.Y, CanvasHost.ActualWidth, CanvasHost.ActualHeight, _state);
        double dx = before.X - after.X, dy = before.Y - after.Y;
        _state.PanX += dx + (_state.Lattice == SandpileLattice.Triangular ? .5 * dy : 0);
        _state.PanY += dy * (_state.Lattice == SandpileLattice.Triangular ? Math.Sqrt(3) / 2 : 1);
        _panStart = p; Repaint();
    }
    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e) { _painting = _panning = false; CanvasHost.ReleaseMouseCapture(); }
    private void Canvas_OnLostCapture(object sender, MouseEventArgs e) { _painting = _panning = false; _lastBrush = null; }
    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point p = e.GetPosition(CanvasHost);
        var before = SandpileRenderer.ToLattice(p.X, p.Y, CanvasHost.ActualWidth, CanvasHost.ActualHeight, _state);
        _state.Zoom = Math.Clamp(_state.Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), .25, 32);
        var after = SandpileRenderer.ToLattice(p.X, p.Y, CanvasHost.ActualWidth, CanvasHost.ActualHeight, _state);
        double dx = before.X - after.X, dy = before.Y - after.Y;
        _state.PanX += dx + (_state.Lattice == SandpileLattice.Triangular ? .5 * dy : 0);
        _state.PanY += dy * (_state.Lattice == SandpileLattice.Triangular ? Math.Sqrt(3) / 2 : 1);
        Repaint(); e.Handled = true;
    }
    private void Canvas_OnSizeChanged(object sender, SizeChangedEventArgs e) { if (!_syncing) Repaint(); }
    private void Fit_OnClick(object sender, RoutedEventArgs e) { _state.PanX = _state.PanY = 0; _state.Zoom = 1; Repaint(); }
    private async Task FreezeAsync()
    {
        SetRunning(false);
        do { await _workerIdle; } while (_busy);
        await ProduceFrameAsync(0);
        do { await _workerIdle; } while (_busy);
    }
    private async void Saves_OnClick(object sender, RoutedEventArgs e)
    {
        await FreezeAsync();
        if (!_closed && !_resetting && _shown is not null) SaveManagerWindow.Open(this, SaveManagerConfigurations.ForSandpile(this, _store));
    }
    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        await FreezeAsync();
        if (_closed || _resetting || _shown is null) return;
        var state = CaptureState("export"); var surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "sandpile", WindowTitle = "Экспорт песочной кучи", InitialWidth = surface.PixelWidth, InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false, MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => SandpileRenderer.RenderStateAsync(state, request.Width, request.Height, token, progress)
        });
    }
    private void Toggle_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleButton, 340);
    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox) { SetRunning(!_running); e.Handled = true; }
        else if (e.Key == Key.F11 || e.Key == Key.Escape && _fullscreen.IsActive) { _fullscreen.Toggle(this); e.Handled = true; }
    }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    { _closed = true; _generation++; _timer.Stop(); _cts?.Cancel(); _cts?.Dispose(); }
}
