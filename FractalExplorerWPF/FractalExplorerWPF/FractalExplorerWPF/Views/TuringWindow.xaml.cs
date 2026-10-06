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
using Brushes = System.Windows.Media.Brushes;

namespace FractalExplorerWPF.Views;

public partial class TuringWindow : Window
{
    private static readonly TuringPreset CustomPreset = new("custom", "Свой узор", "Ваш вариант. Меняйте форму на текущем поле или выберите готовый вид для нового старта.", 1, 5, 1, false, 1729);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly DispatcherTimer _shapeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TuringSaveStore _saveStore = new();
    private readonly DynamicPaletteStore _paletteStore = new("turing_palettes.json", TuringPalettes.All());
    private List<DynamicPalette> _palettes;
    private readonly List<StrokePoint> _strokes = [];
    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private readonly Stopwatch _scheduleWatch = Stopwatch.StartNew();
    private double _nextFrameAtMilliseconds;
    private TuringState _state = new();
    private TuringState? _undo;
    private TuringCheckpoint? _presented;
    private ITuringEngine? _simulation;
    private string? _backendNotice;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private Task _workerIdle = Task.CompletedTask, _resetIdle = Task.CompletedTask;
    private bool _syncing = true, _closed, _busy, _resetting, _running, _painting, _panning, _shapePending;
    private bool _controlsVisible = true, _fullScreen;
    private bool _qualityPending, _qualityResume;
    private volatile bool _stopPreparation;
    private int _generation, _appearanceVersion, _frameCount, _qualityVersion;
    private double _measuredFps;
    private Point _panStart;
    private Point? _lastStroke;
    private WindowStyle _previousStyle;
    private WindowState _previousState;
    private sealed record StrokePoint(double X, double Y, double Radius, double Strength, TuringBrush Brush);

    public TuringWindow()
    {
        InitializeComponent();
        _palettes = _paletteStore.Load(); PaletteBox.ItemsSource = _palettes;
        PresetBox.ItemsSource = TuringPresets.All.Append(CustomPreset).ToList();
        _timer.Tick += async (_, _) =>
        {
            if (!_running || _busy) return;
            double now = _scheduleWatch.Elapsed.TotalMilliseconds;
            if (now < _nextFrameAtMilliseconds) return;
            _nextFrameAtMilliseconds = Math.Max(_nextFrameAtMilliseconds + 1000d / 30, now);
            await ProduceFrameAsync(_state.StepsPerFrame);
        };
        _shapeTimer.Tick += (_, _) => { _shapeTimer.Stop(); ApplyShape(); };
        _sizeTimer.Tick += (_, _) => { _sizeTimer.Stop(); UpdateFrameHint(); RequestRepaint(); };
        CanvasHost.SizeChanged += (_, _) => { _sizeTimer.Stop(); _sizeTimer.Start(); };
        DpiChanged += (_, _) => { _sizeTimer.Stop(); _sizeTimer.Start(); };
        ApplyState(TuringPresets.All[0].CreateState());
        Loaded += async (_, _) => { if (_simulation is null && !_resetting) await ResetAsync(_state, true); };
    }

    public TuringState CaptureState(string name)
    {
        if (_presented is null || _resetting) throw new InvalidOperationException("Дождитесь подготовки поля.");
        var state = _state.Clone(name, false); state.Timestamp = DateTime.Now;
        state.Checkpoint = _presented.Clone(); return state;
    }
    public void LoadState(TuringState state) => _ = ResetAsync(state.Clone(), false);
    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, FrameImage);
    public Task<BitmapSource> RenderStatePreviewAsync(TuringState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        TuringRenderer.RenderStateAsync(state.Clone(), width, height, token, progress);

    private void ApplyState(TuringState state)
    {
        _syncing = true;
        _state = state.Clone(includeCheckpoint: false);
        ReactionEditor.Load(state.Reaction); UpdateReactionControls();
        DetailSlider.Value = state.DetailSize; DepthSlider.Value = Math.Clamp(state.Layers.Count(l => l.Enabled), 1, 6);
        SpeedSlider.Value = state.StepsPerFrame;
        SelectTag(QualityBox, state.GridSize); SelectTag(SymmetryBox, state.Symmetry);
        BackendBox.SelectedIndex = state.Backend == TuringBackend.Gpu ? 0 : 1;
        GridSizeBox.Text = state.GridSize.ToString(CultureInfo.InvariantCulture); GridSizeError.Text = string.Empty;
        AutoFrameBox.IsChecked = state.AutoFrameSize; FrameWidthBox.Text = state.FrameWidth.ToString(CultureInfo.InvariantCulture);
        FrameHeightBox.Text = state.FrameHeight.ToString(CultureInfo.InvariantCulture); FrameSizeError.Text = string.Empty;
        MirrorBox.IsChecked = state.Mirror; BoundaryBox.SelectedIndex = (int)state.Boundary;
        ColoringBox.SelectedIndex = (int)state.Coloring; ContrastSlider.Value = state.Contrast; ReliefSlider.Value = state.Relief;
        ReverseBox.IsChecked = state.ReversePalette;
        BrushBox.SelectedIndex = (int)state.Brush; BrushSizeSlider.Value = state.BrushRadius; BrushStrengthSlider.Value = state.BrushStrength;
        SeedBox.Text = state.RandomSeed.ToString(CultureInfo.InvariantCulture);
        InhibitorSlider.Value = state.InhibitorRatio;
        LayersItems.ItemsSource = state.Layers.Select(l => l.Clone()).ToList();
        ApplyLayersButton.IsEnabled = false; LayersError.Text = string.Empty;
        // A saved palette owns its colours, even if an installed palette has the same name.
        DynamicPalette? selected = _palettes.FirstOrDefault(p => p.Name == state.Palette.Name && p.Colors.SequenceEqual(state.Palette.Colors));
        if (selected is null) { selected = state.Palette.Clone(); _palettes.Add(selected); PaletteBox.Items.Refresh(); }
        PaletteBox.SelectedItem = selected;
        PresetBox.SelectedItem = TuringPresets.All.FirstOrDefault(p => p.Id == state.PresetId) ?? CustomPreset;
        PresetDescription.Text = (PresetBox.SelectedItem as TuringPreset)?.Description ?? "Ваш узор. Изменяйте форму на текущем поле или выберите готовый вид для нового старта.";
        UpdateAppearanceControls(); UpdatePalettePreview(); UpdateView(); UpdateLegend();
        UpdateFrameHint(); UpdateReactionControls();
        _syncing = false;
    }

    private async Task ResetAsync(TuringState state, bool run)
    {
        if (_closed) return;
        try { state.Validate(); } catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        int generation = ++_generation;
        _qualityVersion++; _qualityPending = false;
        _shapeTimer.Stop(); _shapePending = false; SetRunning(false); _resetting = true;
        var priorCts = _cts; priorCts?.Cancel();
        Retire(priorCts, _workerIdle, _resetIdle, _simulation); _simulation = null;
        var cts = new CancellationTokenSource(); _cts = cts; CancellationToken token = cts.Token;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _resetIdle = idle.Task;
        _strokes.Clear(); _stopPreparation = false; EndPointerInteraction();
        BusyOverlay.Visibility = Visibility.Visible; PreparationProgress.Value = 0;
        PreparationText.Text = state.Checkpoint is null ? "Выращиваем начальный узор…" : "Восстанавливаем поле…";
        ControlsHost.IsEnabled = NewButton.IsEnabled = SavesButton.IsEnabled = ExportButton.IsEnabled = UndoButton.IsEnabled = false;
        UpdateRunState();
        var progress = new Progress<int>(value => { if (!_closed && generation == _generation) PreparationProgress.Value = value; });
        var frame = ResolveFrameSize(state); double aspect = DisplayAspect;
        int appearanceVersion = _appearanceVersion;
        try
        {
            var result = await Task.Run(() =>
            {
                ITuringEngine simulation = TuringEngineFactory.Create(state, out string? fallback);
                try
                {
                    if (state.Checkpoint is null)
                        for (int i = 0; i < state.WarmupSteps && !_stopPreparation; i++)
                        {
                            simulation.Advance(1, state, token);
                            if (i % 8 == 0) ((IProgress<int>)progress).Report(i * 90 / Math.Max(1, state.WarmupSteps));
                        }
                    var cp = simulation.Snapshot();
                    return (Simulation: simulation, Checkpoint: cp, Pixels: simulation.RenderFrame(state, frame.Width, frame.Height, token, aspect), Fallback: fallback);
                }
                catch { simulation.Dispose(); throw; }
            }, token);
            if (_closed || generation != _generation || token.IsCancellationRequested) { result.Simulation.Dispose(); return; }
            _simulation = result.Simulation; _presented = result.Checkpoint; state.Backend = _simulation.Backend;
            _backendNotice = result.Fallback; ApplyState(state);
            BackendHint.Text = _backendNotice ?? (_simulation.Backend == TuringBackend.Gpu ? _simulation.DeviceName : "Расчёт на процессоре. ГП можно включить на этом же поле.");
            Present(result.Pixels, frame.Width, frame.Height); _resetting = false; SetRunning(run && !_stopPreparation);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (!_closed && generation == _generation) { ApplyState(_state); StatusText.Text = $"Не удалось подготовить поле: {ex.Message}"; } }
        finally
        {
            idle.TrySetResult();
            if (!_closed && generation == _generation)
            {
                _resetting = false; BusyOverlay.Visibility = Visibility.Collapsed;
                ControlsHost.IsEnabled = NewButton.IsEnabled = true;
                SavesButton.IsEnabled = ExportButton.IsEnabled = _presented is not null;
                UpdateRunState(); UndoButton.IsEnabled = _undo is not null;
                if (appearanceVersion != _appearanceVersion) RequestRepaint();
            }
        }
    }

    private static async void Retire(CancellationTokenSource? cts, Task worker, Task reset, ITuringEngine? engine = null)
    {
        try { await Task.WhenAll(worker, reset); } finally { engine?.Dispose(); cts?.Dispose(); }
    }

    private async Task ProduceFrameAsync(int steps)
    {
        if (_closed || _busy || _resetting || _simulation is null || _cts is null) return;
        _busy = true; UpdateRunState();
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _workerIdle = idle.Task;
        int generation = _generation, appearanceVersion = _appearanceVersion;
        ITuringEngine simulation = _simulation; TuringState state = _state.Clone(includeCheckpoint: false);
        var frame = ResolveFrameSize(state); double aspect = DisplayAspect;
        CancellationToken token = _cts.Token; StrokePoint[] strokes = _strokes.ToArray(); _strokes.Clear();
        TuringState? recovery = null; string? gpuFailure = null;
        try
        {
            var result = await Task.Run(() =>
            {
                foreach (var stroke in strokes) simulation.Paint(stroke.X, stroke.Y, stroke.Radius, stroke.Strength, stroke.Brush, state);
                simulation.Advance(steps, state, token);
                var cp = simulation.Snapshot();
                return (Checkpoint: cp, Pixels: simulation.RenderFrame(state, frame.Width, frame.Height, token, aspect));
            }, token);
            if (_closed || generation != _generation || token.IsCancellationRequested) return;
            _presented = result.Checkpoint; Present(result.Pixels, frame.Width, frame.Height);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && generation == _generation)
            {
                SetRunning(false); StatusText.Text = ex.Message;
                if (simulation.Backend == TuringBackend.Gpu && _presented is not null)
                {
                    recovery = _state.Clone(includeCheckpoint: false); recovery.Backend = TuringBackend.Cpu; recovery.Checkpoint = _presented.Clone();
                    gpuFailure = $"ГП остановлен. Последний показанный узор восстановлен на ЦП: {ex.Message}";
                }
            }
        }
        finally
        {
            _busy = false; idle.TrySetResult();
            if (!_closed)
            {
                UpdateRunState();
                if (recovery is null && generation == _generation && !_running && (appearanceVersion != _appearanceVersion || _strokes.Count > 0)) _ = ProduceFrameAsync(0);
            }
        }
        if (recovery is not null && !_closed && generation == _generation)
        {
            await ResetAsync(recovery, false); BackendHint.Text = gpuFailure;
        }
    }

    private void Present(byte[] pixels, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); FrameImage.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        _frameCount++;
        if (_fpsWatch.Elapsed.TotalSeconds >= 1)
        {
            _measuredFps = _frameCount / _fpsWatch.Elapsed.TotalSeconds;
            _frameCount = 0; _fpsWatch.Restart();
        }
        UpdateStatus();
        FrameBadgeText.Text = $"{(_running ? "Развитие" : "Пауза")} · шаг {_presented?.StepCount ?? 0:N0}";
    }

    private void UpdateStatus()
    {
        if (_presented is null) return;
        StatusText.Text = $"{(_simulation?.Backend == TuringBackend.Gpu ? "ГП" : "ЦП")} · поле {_presented.Size} × {_presented.Size} · буфер {_bitmap?.PixelWidth} × {_bitmap?.PixelHeight}" +
            (_running ? (_measuredFps > 0 ? $" · {_measuredFps:F1} кадров/с" : " · развитие") : " · поле на паузе");
    }

    private void SetRunning(bool running)
    {
        _running = running && !_closed && !_resetting && _simulation is not null;
        if (_running)
        {
            _frameCount = 0; _measuredFps = 0; _fpsWatch.Restart();
            _nextFrameAtMilliseconds = _scheduleWatch.Elapsed.TotalMilliseconds;
            _timer.Start();
        }
        else _timer.Stop();
        UpdateRunState();
        UpdateStatus();
        FrameBadgeText.Text = $"{(_running ? "Развитие" : "Пауза")} · шаг {_presented?.StepCount ?? 0:N0}";
    }
    private void UpdateRunState()
    {
        RunButton.Content = _running ? "Пауза" : "Продолжить";
        RunButton.IsEnabled = !_resetting && !_qualityPending && _simulation is not null;
        StepButton.IsEnabled = !_resetting && !_qualityPending && !_busy && !_running && _simulation is not null;
        RestartButton.IsEnabled = !_resetting;
    }
    private void Run_OnClick(object sender, RoutedEventArgs e) { FlushShape(); SetRunning(!_running); }
    private async void Step_OnClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        FlushShape(); await WaitForFrameIdleAsync();
        if (!_running && !_closed && !_resetting) await ProduceFrameAsync(1);
    }
    private async Task WaitForFrameIdleAsync()
    {
        while (_busy && !_closed) await _workerIdle;
    }
    private void CancelPreparation_OnClick(object sender, RoutedEventArgs e) { _stopPreparation = true; PreparationText.Text = "Завершаем текущий шаг…"; }

    private void Remember()
    {
        if (_presented is null || _resetting) return;
        _undo = CaptureState("previous"); UndoButton.IsEnabled = true;
    }
    private async void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_undo is null || _resetting) return;
        TuringState previous = _undo; _undo = null; UndoButton.IsEnabled = false;
        await ResetAsync(previous, false);
    }
    private async void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _resetting || PresetBox.SelectedItem is not TuringPreset preset || preset == CustomPreset) return;
        Remember(); TuringState state = preset.CreateState();
        state.GridSize = _state.GridSize; state.StepsPerFrame = _state.StepsPerFrame;
        state.Backend = _state.Backend; state.AutoFrameSize = _state.AutoFrameSize; state.FrameWidth = _state.FrameWidth; state.FrameHeight = _state.FrameHeight;
        state.Palette = _state.Palette.Clone(); state.Coloring = _state.Coloring;
        state.Contrast = _state.Contrast; state.Relief = _state.Relief; state.ReversePalette = _state.ReversePalette;
        await ResetAsync(state, true);
    }
    private async void New_OnClick(object sender, RoutedEventArgs e)
    {
        if (_resetting) return; FlushShape(); Remember();
        var state = _state.Clone(includeCheckpoint: false); state.RandomSeed = Random.Shared.Next(); state.Zoom = 1; state.PanX = state.PanY = 0;
        await ResetAsync(state, true);
    }
    private async void Restart_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SeedBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        { StatusText.Text = "Введите целое число для случайного старта."; SeedBox.Focus(); return; }
        FlushShape(); Remember(); var state = _state.Clone(includeCheckpoint: false);
        state.RandomSeed = seed; state.Zoom = 1; state.PanX = state.PanY = 0;
        await ResetAsync(state, true);
    }
    private async void Reaction_OnChanged(object? sender, EventArgs e)
    {
        if (_syncing || _resetting) return;
        FlushShape(); Remember();
        var reaction = ReactionEditor.Settings;
        if (reaction.Model != _state.Reaction.Model)
        {
            var state = _state.Clone(includeCheckpoint: false); state.Reaction = reaction; state.PresetId = "custom";
            state.WarmupSteps = reaction.IsClassical ? 400 : 160;
            if (state.Coloring == TuringColoring.Scales) state.Coloring = TuringColoring.Relief;
            await ResetAsync(state, true);
        }
        else { _state.Reaction = reaction; SetCustomPreset(); UpdateReactionControls(); RequestRepaint(); }
    }

    private void UpdateReactionControls()
    {
        var visibility = _state.Reaction.IsClassical ? Visibility.Collapsed : Visibility.Visible;
        McCabeDepthPanel.Visibility = McCabeLayersPanel.Visibility = visibility;
        LayersExpander.Header = _state.Reaction.IsClassical ? "Поведение на краях" : "Масштабы и края";
        ((ComboBoxItem)ColoringBox.Items[2]).IsEnabled = !_state.Reaction.IsClassical;
        PresetBox.IsEnabled = !_state.Reaction.IsClassical;
        if (_state.Reaction.IsClassical) PresetDescription.Text = _state.Reaction.Name + " · изменяйте параметры или начните новый рисунок.";
    }

    private void Shape_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || _resetting) return;
        if (!_shapePending) Remember();
        _shapePending = true; _shapeTimer.Stop(); _shapeTimer.Start();
    }
    private void FlushShape() { if (_shapePending) { _shapeTimer.Stop(); ApplyShape(); } }
    private void ApplyShape()
    {
        _shapePending = false;
        _state.DetailSize = DetailSlider.Value; _state.Symmetry = TagInt(SymmetryBox, 1);
        _state.Mirror = MirrorBox.IsChecked == true; _state.Boundary = (TuringBoundary)Math.Max(0, BoundaryBox.SelectedIndex);
        SetCustomPreset(); UpdateSymmetryHint(); UpdateLegend(); RequestRepaint();
        StatusText.Text = "Форма изменена на текущем поле. Следующий шаг продолжит развитие с новыми настройками.";
    }
    private void Depth_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _resetting) return;
        Shape_OnChanged(sender, e);
        List<TuringScale> layers = TuringState.DefaultLayers();
        layers.Add(new TuringScale { Radius = 90, Amount = .1 });
        for (int i = 0; i < layers.Count; i++) layers[i].Enabled = i < (int)DepthSlider.Value;
        _state.Layers = layers; _syncing = true; LayersItems.ItemsSource = layers.Select(l => l.Clone()).ToList(); _syncing = false;
        ApplyLayersButton.IsEnabled = false; UpdateLegend();
    }
    private void Speed_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!_syncing) _state.StepsPerFrame = (int)SpeedSlider.Value; }
    private async void Quality_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _resetting || _presented is null) return;
        await ChangeGridSizeAsync(TagInt(QualityBox, _state.GridSize));
    }
    private async Task ChangeGridSizeAsync(int size)
    {
        await ChangeExecutionAsync(size, BackendBox.SelectedIndex == 0 ? TuringBackend.Gpu : TuringBackend.Cpu);
    }
    private async Task ChangeExecutionAsync(int size, TuringBackend backend)
    {
        if (size == _state.GridSize && backend == _state.Backend && !_qualityPending) return;
        if (!_qualityPending) { _qualityPending = true; _qualityResume = _running; }
        int qualityVersion = ++_qualityVersion; SetRunning(false); await WaitForFrameIdleAsync();
        if (_closed || _resetting || qualityVersion != _qualityVersion) return;
        bool run = _qualityResume; _qualityPending = false;
        FlushShape(); Remember();
        var state = CaptureState("resized"); state.GridSize = size; state.Backend = backend;
        if (state.Checkpoint!.Size != size) state.Checkpoint = TuringSimulation.Resize(state.Checkpoint, size);
        await ResetAsync(state, run);
    }
    private async void Backend_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _resetting || _presented is null) return;
        await ChangeExecutionAsync(TagInt(QualityBox, _state.GridSize), BackendBox.SelectedIndex == 0 ? TuringBackend.Gpu : TuringBackend.Cpu);
    }
    private async void GridSize_OnApply(object sender, RoutedEventArgs e)
    {
        if (_resetting || _presented is null) return;
        if (!int.TryParse(GridSizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) || size is < 32 or > TuringState.MaxGridSize)
        { GridSizeError.Text = "Введите целое число от 32 до 2048."; GridSizeBox.Focus(); return; }
        GridSizeError.Text = string.Empty;
        _syncing = true; SelectTag(QualityBox, size); _syncing = false;
        await ChangeGridSizeAsync(size);
    }
    private void FrameSize_OnApply(object sender, RoutedEventArgs e)
    {
        if (_syncing || _resetting) return;
        bool automatic = AutoFrameBox.IsChecked == true;
        if (!automatic)
        {
            if (!int.TryParse(FrameWidthBox.Text, out int width) || !int.TryParse(FrameHeightBox.Text, out int height) ||
                width is < 32 or > 8192 || height is < 32 or > 8192 || (long)width * height > 16_777_216)
            { FrameSizeInputs.IsEnabled = true; FrameSizeError.Text = "Введите 32–8192 пикселей по стороне; не более 16 млн пикселей."; return; }
            _state.FrameWidth = width; _state.FrameHeight = height;
        }
        if (automatic)
        {
            FrameWidthBox.Text = _state.FrameWidth.ToString(CultureInfo.InvariantCulture); FrameHeightBox.Text = _state.FrameHeight.ToString(CultureInfo.InvariantCulture);
        }
        _state.AutoFrameSize = automatic; FrameSizeError.Text = string.Empty; UpdateFrameHint(); RequestRepaint();
    }
    private double DisplayAspect => CanvasHost.ActualWidth > 0 && CanvasHost.ActualHeight > 0 ? CanvasHost.ActualWidth / CanvasHost.ActualHeight : 1;
    private (int Width, int Height) ResolveFrameSize(TuringState state)
    {
        if (!state.AutoFrameSize) return (state.FrameWidth, state.FrameHeight);
        DpiScale dpi = VisualTreeHelper.GetDpi(CanvasHost);
        int width = Math.Clamp((int)Math.Ceiling(CanvasHost.ActualWidth > 0 ? CanvasHost.ActualWidth * dpi.DpiScaleX : state.FrameWidth), 32, 8192);
        int height = Math.Clamp((int)Math.Ceiling(CanvasHost.ActualHeight > 0 ? CanvasHost.ActualHeight * dpi.DpiScaleY : state.FrameHeight), 32, 8192);
        if ((long)width * height > 16_777_216)
        {
            double factor = Math.Sqrt(16_777_216d / ((long)width * height));
            width = (int)Math.Floor(width * factor); height = (int)Math.Floor(height * factor);
        }
        return (width, height);
    }
    private void UpdateFrameHint()
    {
        var frame = ResolveFrameSize(_state); FrameSizeInputs.IsEnabled = !_state.AutoFrameSize;
        FrameSizeHint.Text = _state.AutoFrameSize ? $"Авто: {frame.Width} × {frame.Height} пикселей, с учётом DPI." : $"Вручную: {frame.Width} × {frame.Height} пикселей. Пропорции узора сохраняются.";
    }
    private void Layers_OnEdited(object sender, RoutedEventArgs e)
    { if (!_syncing && (sender is not TextBox box || box.IsLoaded)) { ApplyLayersButton.IsEnabled = true; LayersError.Text = "Есть неприменённые правки масштабов."; } }
    private void ApplyLayers_OnClick(object sender, RoutedEventArgs e)
    {
        var layers = ((IEnumerable<TuringScale>)LayersItems.ItemsSource).Select(l => l.Clone()).ToList();
        var state = _state.Clone(includeCheckpoint: false); state.Layers = layers; state.InhibitorRatio = InhibitorSlider.Value;
        try
        {
            if (HasInvalidBinding(LayersItems)) throw new ArgumentException("Исправьте выделенные числовые значения.");
            state.Validate(); Remember(); _state.Layers = layers; _state.InhibitorRatio = state.InhibitorRatio; SetCustomPreset();
            _syncing = true; DepthSlider.Value = Math.Clamp(layers.Count(l => l.Enabled), 1, 6); _syncing = false;
            ApplyLayersButton.IsEnabled = false; LayersError.Text = "Масштабы применены к текущему полю."; UpdateLegend();
        }
        catch (ArgumentException ex) { LayersError.Text = ex.Message; }
    }
    private void SetCustomPreset()
    {
        _state.PresetId = null; _syncing = true; PresetBox.SelectedItem = CustomPreset; _syncing = false;
        PresetDescription.Text = CustomPreset.Description;
    }
    private static bool HasInvalidBinding(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (HasInvalidBinding(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private void Appearance_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _state.Coloring = (TuringColoring)Math.Max(0, ColoringBox.SelectedIndex); _state.Contrast = ContrastSlider.Value;
        _state.Relief = ReliefSlider.Value; _state.ReversePalette = ReverseBox.IsChecked == true;
        UpdateAppearanceControls(); RequestRepaint();
    }
    private void UpdateAppearanceControls()
    {
        ReliefPanel.Visibility = _state.Coloring == TuringColoring.Relief ? Visibility.Visible : Visibility.Collapsed;
        ScaleLegend.Visibility = _state.Coloring == TuringColoring.Scales ? Visibility.Visible : Visibility.Collapsed;
        PaletteBox.IsEnabled = PaletteEditorButton.IsEnabled = ContrastSlider.IsEnabled = ReverseBox.IsEnabled = _state.Coloring != TuringColoring.Scales;
        PalettePreview.Opacity = _state.Coloring == TuringColoring.Scales ? .4 : 1;
        ColoringHint.Text = _state.Coloring switch
        {
            TuringColoring.Relief => "Свет и тень подчёркивают перепады поля. Это способ окраски, а не трёхмерная геометрия.",
            TuringColoring.Scales => "Цвет показывает масштаб, победивший на последнем шаге. Палитра поля здесь не используется.",
            _ => "Значение поля определяет цвет. Контраст усиливает различия между светлыми и тёмными областями."
        };
        UpdateSymmetryHint();
    }
    private void UpdateSymmetryHint() => SymmetryHint.Text = _state.Symmetry > 1
        ? "Орнамент развивается в круге вокруг центра. Кисть повторяется по всем лучам; симметрия проявляется при следующем шаге."
        : _state.Mirror ? "Поле отражается относительно горизонтальной оси при каждом шаге." : "Поле занимает квадрат. Противоположные края могут взаимодействовать.";
    private void Palette_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PaletteBox.SelectedItem is not DynamicPalette palette) return;
        _state.Palette = palette.Clone(); UpdatePalettePreview(); RequestRepaint();
    }
    private async void PaletteEditor_OnClick(object sender, RoutedEventArgs e)
    {
        bool run = _running; SetRunning(false); await WaitForFrameIdleAsync();
        var editor = new DynamicPaletteWindow(_paletteStore, _palettes, PaletteBox.SelectedItem as DynamicPalette, true)
        { Owner = this, Title = "Палитры узоров Тьюринга" };
        editor.PaletteApplied += (_, _) =>
        {
            if (editor.SelectedPalette is not { } selected) return;
            if (!_palettes.Contains(selected)) { _palettes.Add(selected); PaletteBox.Items.Refresh(); }
            PaletteBox.SelectedItem = selected; _state.Palette = selected.Clone(); UpdatePalettePreview(); RequestRepaint();
        };
        bool accepted = editor.ShowDialog() == true;
        DynamicPalette chosen = accepted && editor.SelectedPalette is { } selection ? selection : _state.Palette;
        _palettes = _paletteStore.Load();
        DynamicPalette? installed = _palettes.FirstOrDefault(p => p.Name == chosen.Name && p.Colors.SequenceEqual(chosen.Colors));
        if (installed is null) { installed = chosen.Clone(); _palettes.Add(installed); }
        PaletteBox.ItemsSource = _palettes; PaletteBox.SelectedItem = installed;
        await ProduceFrameAsync(0); SetRunning(run);
    }
    private void UpdatePalettePreview()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        for (int i = 0; i < _state.Palette.Colors.Count; i++) brush.GradientStops.Add(new GradientStop(_state.Palette.Colors[i], i / (double)(_state.Palette.Colors.Count - 1)));
        PalettePreview.Background = brush;
    }
    private void UpdateLegend()
    {
        int count = _state.Layers.Count(l => l.Enabled);
        DepthValueText.Text = ScaleCountText(count);
        LegendItems.Items.Clear();
        for (int i = 0; i < _state.Layers.Count; i++)
            if (_state.Layers[i].Enabled)
            {
                var chip = new Border { Background = new SolidColorBrush(TuringRenderer.ScaleColors[i]), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2), CornerRadius = new CornerRadius(3),
                    Child = new TextBlock { Text = $"{i + 1} · {_state.Layers[i].Radius * _state.DetailSize:F1}", Foreground = Brushes.Black, FontSize = 11 } };
                LegendItems.Items.Add(chip);
            }
    }
    private static string ScaleCountText(int count) => $"{count} " + (count == 1 ? "масштаб" : count is >= 2 and <= 4 ? "масштаба" : "масштабов");
    private void Brush_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _state.Brush = (TuringBrush)Math.Max(0, BrushBox.SelectedIndex); _state.BrushRadius = BrushSizeSlider.Value; _state.BrushStrength = BrushStrengthSlider.Value;
    }
    private void RequestRepaint()
    { _appearanceVersion++; if (!_busy && !_resetting) _ = ProduceFrameAsync(0); }

    private Point FieldPoint(Point p)
    {
        double side = Math.Max(1, Math.Min(CanvasHost.ActualWidth, CanvasHost.ActualHeight)) * _state.Zoom;
        return new Point((p.X - CanvasHost.ActualWidth / 2) / side + .5 + _state.PanX, (p.Y - CanvasHost.ActualHeight / 2) / side + .5 + _state.PanY);
    }
    private bool InField(Point p) => p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1 &&
        (_state.Symmetry == 1 || (p.X - .5) * (p.X - .5) + (p.Y - .5) * (p.Y - .5) <= .25);
    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_resetting || _simulation is null || IsButtonSource(e.OriginalSource as DependencyObject)) return;
        if (e.ChangedButton == MouseButton.Left)
        {
            Point point = FieldPoint(e.GetPosition(CanvasHost)); if (!InField(point)) return;
            Remember(); _painting = true; _lastStroke = null; QueueStroke(point);
        }
        else if (e.ChangedButton is MouseButton.Right or MouseButton.Middle) { _panning = true; _panStart = e.GetPosition(CanvasHost); }
        else return;
        CanvasHost.CaptureMouse(); e.Handled = true;
    }
    private static bool IsButtonSource(DependencyObject? source)
    {
        while (source is not null) { if (source is Button) return true; source = source is Visual ? VisualTreeHelper.GetParent(source) : null; }
        return false;
    }
    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(CanvasHost), field = FieldPoint(p);
        double diameter = _state.BrushRadius * 2 * Math.Min(CanvasHost.ActualWidth, CanvasHost.ActualHeight) * _state.Zoom;
        BrushCursor.Width = BrushCursor.Height = diameter; Canvas.SetLeft(BrushCursor, p.X - diameter / 2); Canvas.SetTop(BrushCursor, p.Y - diameter / 2);
        BrushCursor.Visibility = !_resetting && !_panning && InField(field) ? Visibility.Visible : Visibility.Collapsed;
        if (_painting) { if (e.LeftButton == MouseButtonState.Pressed && InField(field)) QueueStroke(field); else _lastStroke = null; }
        if (_panning)
        {
            double side = Math.Max(1, Math.Min(CanvasHost.ActualWidth, CanvasHost.ActualHeight)) * _state.Zoom;
            _state.PanX = Math.Clamp(_state.PanX - (p.X - _panStart.X) / side, -.5, .5);
            _state.PanY = Math.Clamp(_state.PanY - (p.Y - _panStart.Y) / side, -.5, .5);
            _panStart = p; UpdateView();
        }
    }
    private void QueueStroke(Point point)
    {
        if (_strokes.Count >= 4096) return;
        Point from = _lastStroke ?? point;
        double distance = (point - from).Length;
        int count = Math.Clamp((int)Math.Ceiling(distance / (_state.BrushRadius * .25)), 1, 200);
        for (int i = 1; i <= count; i++)
        {
            double t = i / (double)count;
            _strokes.Add(new StrokePoint(from.X + (point.X - from.X) * t, from.Y + (point.Y - from.Y) * t, _state.BrushRadius, _state.BrushStrength, _state.Brush));
        }
        _lastStroke = point; RequestRepaint();
    }
    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e) { EndPointerInteraction(); e.Handled = true; }
    private void Canvas_OnMouseLeave(object sender, MouseEventArgs e) { if (!_painting) BrushCursor.Visibility = Visibility.Collapsed; }
    private void Canvas_OnLostCapture(object sender, MouseEventArgs e) { _painting = _panning = false; _lastStroke = null; }
    private void EndPointerInteraction() { _painting = _panning = false; _lastStroke = null; CanvasHost.ReleaseMouseCapture(); BrushCursor.Visibility = Visibility.Collapsed; }
    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_resetting) return;
        Point p = e.GetPosition(CanvasHost), before = FieldPoint(p);
        _state.Zoom = Math.Clamp(_state.Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 1, 12);
        Point after = FieldPoint(p); _state.PanX = Math.Clamp(_state.PanX + before.X - after.X, -.5, .5); _state.PanY = Math.Clamp(_state.PanY + before.Y - after.Y, -.5, .5);
        UpdateView(); e.Handled = true;
    }
    private void Fit_OnClick(object sender, RoutedEventArgs e) { _state.Zoom = 1; _state.PanX = _state.PanY = 0; UpdateView(); }
    private void UpdateView()
    {
        if (FrameImage is null) return;
        ViewText.Text = $"Масштаб {_state.Zoom:P0}";
        if (!_syncing) RequestRepaint();
    }

    private async void Saves_OnClick(object sender, RoutedEventArgs e)
    {
        bool run = _running; int generation = _generation; SetRunning(false); FlushShape(); await WaitForFrameIdleAsync(); await ProduceFrameAsync(0);
        if (_closed || _resetting || _presented is null) return;
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForTuring(this, _saveStore));
        if (generation == _generation) SetRunning(run);
    }
    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        bool run = _running; SetRunning(false); FlushShape(); await WaitForFrameIdleAsync(); await ProduceFrameAsync(0);
        if (_closed || _resetting || _presented is null) return;
        TuringState state = CaptureState("export"); RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "turing_patterns", WindowTitle = "Экспорт текущего узора Тьюринга",
            InitialWidth = surface.PixelWidth, InitialHeight = surface.PixelHeight, HasNativeSsaa = false, MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => TuringRenderer.RenderStateAsync(state, request.Width, request.Height, token, progress)
        });
        SetRunning(run);
    }
    private void Toggle_OnClick(object sender, RoutedEventArgs e) => FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleButton, 360);
    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Space && RunButton.IsEnabled) { Run_OnClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.Right && StepButton.IsEnabled && Keyboard.Modifiers == ModifierKeys.None) { Step_OnClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo_OnClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.Home) { Fit_OnClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.F11 || e.Key == Key.Escape && _fullScreen)
        {
            if (!_fullScreen) { _previousStyle = WindowStyle; _previousState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
            else { WindowStyle = _previousStyle; WindowState = _previousState; }
            _fullScreen = !_fullScreen; e.Handled = true;
        }
    }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true; _generation++; _timer.Stop(); _shapeTimer.Stop(); _sizeTimer.Stop(); _cts?.Cancel(); Retire(_cts, _workerIdle, _resetIdle, _simulation); _cts = null; _simulation = null;
    }
    private static int TagInt(ComboBox box, int fallback) => box.SelectedItem is ComboBoxItem { Tag: string text } && int.TryParse(text, out int value) ? value : fallback;
    private static void SelectTag(ComboBox box, int value)
    {
        ComboBoxItem? item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == value.ToString(CultureInfo.InvariantCulture));
        if (item is null) { item = new ComboBoxItem { Tag = value.ToString(CultureInfo.InvariantCulture), Content = box.Name == "QualityBox" ? $"Свой размер · {value} × {value}" : value == 1 ? "Без симметрии" : $"{value}" }; box.Items.Add(item); }
        box.SelectedItem = item;
    }
}
