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
using Color = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class GrayScottWindow : Window
{
    private readonly DispatcherTimer _frameTimer = new();
    private readonly GrayScottPaletteManager _paletteManager = new();
    private readonly GrayScottSaveStore _saveStore = new();
    private readonly ConcurrentQueue<(double X, double Y)> _injections = new();
    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private readonly Stopwatch _scheduleWatch = Stopwatch.StartNew();
    private IGrayScottEngine? _simulation;
    private GrayScottSnapshot? _presented;
    private GrayScottState? _activeState;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _simulationCts;
    private Task _frameIdleTask = Task.CompletedTask;
    private Task _resetIdleTask = Task.CompletedTask;
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _closed, _resetting;
    private int _generation, _appearanceVersion;
    private int _executionVersion;
    private bool _executionPending, _executionResume;
    private string? _backendNotice;
    private int _presentedFrames;
    private double _measuredFps;
    private double _nextFrameAtMilliseconds;
    private bool _frameBusy;
    private bool _running = true;
    private bool _syncing;
    private bool _painting;
    private bool _controlsVisible = true;
    private bool _fullScreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    public GrayScottWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = GrayScottPresets.All;
        _frameTimer.Tick += FrameTimer_OnTick;
        ApplyState(GrayScottPresets.All[0].State.Clone());
        _sizeTimer.Tick += (_, _) => { _sizeTimer.Stop(); UpdateFrameHint(); RequestRepaint(); };
        CanvasHost.SizeChanged += (_, _) => { _sizeTimer.Stop(); _sizeTimer.Start(); };
        DpiChanged += (_, _) => { _sizeTimer.Stop(); _sizeTimer.Start(); };
        Loaded += async (_, _) => { if (_simulation is null && !_resetting) await ResetSimulationAsync(startAfterReset: true); };
    }

    public GrayScottState CaptureState(string name)
    {
        if (_activeState is null || _presented is null || _resetting) throw new InvalidOperationException("Дождитесь подготовки поля.");
        var state = _activeState.Clone(name, false); state.Timestamp = DateTime.Now; state.Checkpoint = _presented.Copy(); return state;
    }

    public void LoadState(GrayScottState state)
    {
        _ = InstallEngineAsync(state.Clone(), state.Checkpoint is null);
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, FrameImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        GrayScottState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        GrayScottRenderer.RenderPreviewAsync(state.Clone(), width, height, token, progress);

    private bool TryCaptureState(string name, out GrayScottState state, out string error)
    {
        state = new GrayScottState();
        if (!ReadFiniteDouble(DiffusionUBox.Text, out double diffusionU) || diffusionU is <= 0 or > 1 ||
            !ReadFiniteDouble(DiffusionVBox.Text, out double diffusionV) || diffusionV is <= 0 or > 1)
        {
            error = "Коэффициенты диффузии должны быть больше 0 и не больше 1.";
            return false;
        }
        if (!ReadFiniteDouble(FeedBox.Text, out double feed) || feed is < 0 or > 0.2 ||
            !ReadFiniteDouble(KillBox.Text, out double kill) || kill is < 0 or > 0.2)
        {
            error = "Параметры F и K должны лежать в диапазоне 0–0.2.";
            return false;
        }
        if (!ReadFiniteDouble(DeltaTimeBox.Text, out double deltaTime) || deltaTime is < 0.05 or > 1.5)
        {
            error = "Шаг времени должен быть от 0.05 до 1.5.";
            return false;
        }
        if (!int.TryParse(GridSizeBox.Text, out int gridSize) || gridSize is < 32 or > GrayScottState.MaxGridSize)
        {
            error = "Размер сетки должен быть от 32 до 2048.";
            return false;
        }
        if (!int.TryParse(StepsPerFrameBox.Text, out int stepsPerFrame) || stepsPerFrame is < 1 or > 64)
        {
            error = "Число шагов на кадр должно быть от 1 до 64.";
            return false;
        }
        if (!int.TryParse(RandomSeedBox.Text, out int randomSeed) ||
            !int.TryParse(SeedCountBox.Text, out int seedCount) || seedCount is < 1 or > 500 ||
            !int.TryParse(SeedRadiusBox.Text, out int seedRadius) || seedRadius is < 1 or > 128 ||
            !int.TryParse(BrushRadiusBox.Text, out int brushRadius) || brushRadius is < 1 or > 128)
        {
            error = "Проверьте seed, число и радиусы затравок (радиусы 1–128).";
            return false;
        }
        if (!ReadFiniteDouble(RangeMinimumBox.Text, out double rangeMinimum) ||
            !ReadFiniteDouble(RangeMaximumBox.Text, out double rangeMaximum) || rangeMaximum <= rangeMinimum)
        {
            error = "Максимум цветового диапазона должен быть больше минимума.";
            return false;
        }

        int targetFps = TargetFpsBox.SelectedIndex == 1 ? 60 : 30;
        state = new GrayScottState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            PresetId = (PresetBox.SelectedItem as GrayScottPreset)?.Id,
            DiffusionU = diffusionU,
            DiffusionV = diffusionV,
            Feed = feed,
            Kill = kill,
            DeltaTime = deltaTime,
            GridSize = gridSize,
            Backend = BackendBox.SelectedIndex == 1 ? GrayScottBackend.Cpu : GrayScottBackend.Gpu,
            AutoFrameSize = _activeState?.AutoFrameSize ?? true,
            FrameWidth = _activeState?.FrameWidth ?? 1024, FrameHeight = _activeState?.FrameHeight ?? 1024,
            StepsPerFrame = stepsPerFrame,
            TargetFps = targetFps,
            RandomSeed = randomSeed,
            SeedMode = (GrayScottSeedMode)Math.Clamp(SeedModeBox.SelectedIndex, 0, 3),
            SeedCount = seedCount,
            SeedRadius = seedRadius,
            BrushRadius = brushRadius,
            FieldMode = (GrayScottFieldMode)Math.Clamp(FieldModeBox.SelectedIndex, 0, 2),
            RangeMinimum = rangeMinimum,
            RangeMaximum = rangeMaximum,
            ReversePalette = ReversePaletteBox.IsChecked == true,
            Palette = _paletteManager.ActivePalette.Clone()
        };
        try { state.Validate(); } catch (ArgumentException ex) { error = ex.Message; return false; }
        error = string.Empty;
        return true;
    }

    private void ApplyState(GrayScottState state)
    {
        _syncing = true;
        try
        {
            DiffusionUBox.Text = Format(state.DiffusionU);
            DiffusionVBox.Text = Format(state.DiffusionV);
            FeedBox.Text = Format(state.Feed);
            KillBox.Text = Format(state.Kill);
            DeltaTimeBox.Text = Format(state.DeltaTime);
            GridSizeBox.Text = state.GridSize.ToString(CultureInfo.InvariantCulture);
            BackendBox.SelectedIndex = state.Backend == GrayScottBackend.Gpu ? 0 : 1;
            AutoFrameBox.IsChecked = state.AutoFrameSize; FrameWidthBox.Text = state.FrameWidth.ToString(CultureInfo.InvariantCulture);
            FrameHeightBox.Text = state.FrameHeight.ToString(CultureInfo.InvariantCulture); FrameSizeError.Text = GridSizeError.Text = string.Empty;
            StepsPerFrameBox.Text = state.StepsPerFrame.ToString(CultureInfo.InvariantCulture);
            TargetFpsBox.SelectedIndex = state.TargetFps >= 60 ? 1 : 0;
            RandomSeedBox.Text = state.RandomSeed.ToString(CultureInfo.InvariantCulture);
            SeedModeBox.SelectedIndex = (int)state.SeedMode;
            SeedCountBox.Text = state.SeedCount.ToString(CultureInfo.InvariantCulture);
            SeedRadiusBox.Text = state.SeedRadius.ToString(CultureInfo.InvariantCulture);
            BrushRadiusBox.Text = state.BrushRadius.ToString(CultureInfo.InvariantCulture);
            FieldModeBox.SelectedIndex = (int)state.FieldMode;
            RangeMinimumBox.Text = Format(state.RangeMinimum);
            RangeMaximumBox.Text = Format(state.RangeMaximum);
            ReversePaletteBox.IsChecked = state.ReversePalette;
            GrayScottPalette? palette = _paletteManager.Palettes.FirstOrDefault(item =>
                item.Name.Equals(state.Palette.Name, StringComparison.OrdinalIgnoreCase) && item.Colors.SequenceEqual(state.Palette.Colors) && item.Gamma == state.Palette.Gamma && item.IsGradient == state.Palette.IsGradient);
            if (palette is null)
            {
                palette = state.Palette.Clone();
                _paletteManager.Palettes.Add(palette);
            }
            _paletteManager.ActivePalette = palette;
            PresetBox.SelectedItem = GrayScottPresets.All.FirstOrDefault(item => item.Id == state.PresetId);
        }
        finally
        {
            _syncing = false;
        }
        SetTimerInterval(state.TargetFps);
        UpdatePalettePreview();
        PendingText.Visibility = Visibility.Collapsed;
        UpdateFrameHint(state);
    }

    private async void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedItem is not GrayScottPreset preset) return;
        try
        {
            var state = preset.State.Clone();
            if (_activeState is { } current)
            {
                state.GridSize = current.GridSize; state.Backend = current.Backend;
                state.AutoFrameSize = current.AutoFrameSize; state.FrameWidth = current.FrameWidth; state.FrameHeight = current.FrameHeight;
            }
            await InstallEngineAsync(state, true);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось применить пресет: {exception.Message}";
        }
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_syncing) PendingText.Visibility = Visibility.Visible;
    }

    private void TargetFps_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        SetTimerInterval(TargetFpsBox.SelectedIndex == 1 ? 60 : 30);
        PendingText.Visibility = Visibility.Visible;
    }

    private async void PaletteMapping_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (_activeState is not null) _activeState.ReversePalette = ReversePaletteBox.IsChecked == true;
        await RenderCurrentFieldAsync();
    }

    private void Appearance_OnChanged(object sender, EventArgs e)
    {
        if (_syncing || _activeState is null) return;
        if (!ReadFiniteDouble(RangeMinimumBox.Text, out double min) || !ReadFiniteDouble(RangeMaximumBox.Text, out double max) || max <= min || !double.IsFinite(max - min)) return;
        _activeState.RangeMinimum = min; _activeState.RangeMaximum = max;
        _activeState.FieldMode = (GrayScottFieldMode)Math.Clamp(FieldModeBox.SelectedIndex, 0, 2);
        RequestRepaint();
    }

    private async void Backend_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _resetting || _presented is null) return;
        await ChangeExecutionAsync(_activeState!.GridSize, BackendBox.SelectedIndex == 1 ? GrayScottBackend.Cpu : GrayScottBackend.Gpu);
    }

    private async void GridSize_OnApply(object sender, RoutedEventArgs e)
    {
        if (_resetting || _activeState is null) return;
        if (!int.TryParse(GridSizeBox.Text, out int size) || size is < 32 or > GrayScottState.MaxGridSize)
        { GridSizeError.Text = "Введите целое число от 32 до 2048."; return; }
        GridSizeError.Text = string.Empty;
        await ChangeExecutionAsync(size, BackendBox.SelectedIndex == 1 ? GrayScottBackend.Cpu : GrayScottBackend.Gpu);
    }

    private async Task ChangeExecutionAsync(int size, GrayScottBackend backend)
    {
        if (_activeState is null || _presented is null || _closed || _resetting) return;
        if (size == _activeState.GridSize && backend == _activeState.Backend && !_executionPending) return;
        if (!_executionPending) { _executionPending = true; _executionResume = _running; }
        int version = ++_executionVersion; SetRunning(false); await _frameIdleTask;
        if (_closed || _resetting || version != _executionVersion) return;
        bool resume = _executionResume; _executionPending = false;
        var state = CaptureState("transferred"); state.GridSize = size; state.Backend = backend;
        if (state.Checkpoint!.Size != size) state.Checkpoint = state.Checkpoint.Resize(size);
        await InstallEngineAsync(state, resume, false);
    }

    private void FrameSize_OnApply(object sender, RoutedEventArgs e)
    {
        if (_syncing || _resetting || _activeState is null) return;
        bool automatic = AutoFrameBox.IsChecked == true;
        if (!automatic)
        {
            if (!int.TryParse(FrameWidthBox.Text, out int width) || !int.TryParse(FrameHeightBox.Text, out int height) ||
                width is < 32 or > 8192 || height is < 32 or > 8192 || (long)width * height > 16_777_216)
            { FrameSizeInputs.IsEnabled = true; FrameSizeError.Text = "Введите 32–8192 по стороне, всего до 16 млн пикселей."; return; }
            _activeState.FrameWidth = width; _activeState.FrameHeight = height;
        }
        else
        {
            FrameWidthBox.Text = _activeState.FrameWidth.ToString(CultureInfo.InvariantCulture);
            FrameHeightBox.Text = _activeState.FrameHeight.ToString(CultureInfo.InvariantCulture);
        }
        _activeState.AutoFrameSize = automatic; FrameSizeError.Text = string.Empty; UpdateFrameHint(); RequestRepaint();
    }

    private double DisplayAspect => CanvasHost.ActualWidth > 0 && CanvasHost.ActualHeight > 0 ? CanvasHost.ActualWidth / CanvasHost.ActualHeight : 1;
    private (int Width, int Height) ResolveFrameSize(GrayScottState state)
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
    private void UpdateFrameHint(GrayScottState? state = null)
    {
        state ??= _activeState; if (state is null) return;
        var frame = ResolveFrameSize(state); FrameSizeInputs.IsEnabled = !state.AutoFrameSize;
        FrameSizeHint.Text = state.AutoFrameSize ? $"Авто: {frame.Width} × {frame.Height} пикселей, с учётом DPI." : $"Вручную: {frame.Width} × {frame.Height} пикселей.";
    }
    private void RequestRepaint()
    { _appearanceVersion++; if (!_frameBusy && !_resetting && !_executionPending) _ = ProduceFrameAsync(0); }

    private async void Reset_OnClick(object sender, RoutedEventArgs e) =>
        await ResetSimulationAsync(startAfterReset: _running);

    private async Task ResetSimulationAsync(bool startAfterReset)
    {
        if (!TryCaptureState("preview", out GrayScottState state, out string error)) { StatusText.Text = error; return; }
        await InstallEngineAsync(state, startAfterReset);
    }

    private async Task InstallEngineAsync(GrayScottState state, bool run, bool applyControls = true)
    {
        if (_closed) return;
        try { state.Validate(); } catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        int generation = ++_generation; _executionVersion++; _executionPending = false;
        SetRunning(false); _resetting = true; UpdateRunState();
        var previousCts = _simulationCts; previousCts?.Cancel();
        Retire(previousCts, _frameIdleTask, _resetIdleTask, _simulation); _simulation = null;
        var cts = new CancellationTokenSource(); _simulationCts = cts;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _resetIdleTask = idle.Task;
        ControlsHost.IsEnabled = false; StatusText.Text = "Подготовка движка…";
        while (_injections.TryDequeue(out _)) { }
        var frame = ResolveFrameSize(state); double aspect = DisplayAspect; int appearance = _appearanceVersion;
        try
        {
            var result = await Task.Run(() =>
            {
                IGrayScottEngine engine = GrayScottEngineFactory.Create(state, out string? fallback);
                try { return (Engine: engine, Snapshot: engine.Snapshot(), Pixels: engine.RenderFrame(state, frame.Width, frame.Height, cts.Token, aspect), Fallback: fallback); }
                catch { engine.Dispose(); throw; }
            }, cts.Token);
            if (_closed || generation != _generation || cts.IsCancellationRequested) { result.Engine.Dispose(); return; }
            _simulation = result.Engine; _presented = result.Snapshot; state.Backend = _simulation.Backend;
            _activeState = state.Clone(includeCheckpoint: false); _backendNotice = result.Fallback;
            if (applyControls) ApplyState(state);
            else
            {
                _syncing = true; BackendBox.SelectedIndex = state.Backend == GrayScottBackend.Gpu ? 0 : 1;
                GridSizeBox.Text = state.GridSize.ToString(CultureInfo.InvariantCulture); _syncing = false;
            }
            BackendHint.Text = _backendNotice ?? (_simulation.Backend == GrayScottBackend.Gpu ? _simulation.DeviceName : "Расчёт на процессоре. ГП можно включить на текущем поле.");
            PresentFrame(result.Pixels, _presented.StepCount, frame.Width, frame.Height);
            _resetting = false; SetRunning(run);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_closed && generation == _generation) { SetRunning(false); StatusText.Text = $"Не удалось подготовить симуляцию: {exception.Message}"; }
        }
        finally
        {
            idle.TrySetResult();
            if (!_closed && generation == _generation)
            {
                _resetting = false; ControlsHost.IsEnabled = true; UpdateRunState(); UpdateFrameHint();
                if (appearance != _appearanceVersion) RequestRepaint();
            }
        }
    }

    private static async void Retire(CancellationTokenSource? cts, Task frame, Task reset, IGrayScottEngine? engine)
    { try { await Task.WhenAll(frame, reset); } finally { engine?.Dispose(); cts?.Dispose(); } }

    private void RunPause_OnClick(object sender, RoutedEventArgs e) => SetRunning(!_running);

    private void SetRunning(bool running)
    {
        _running = running && !_closed && !_resetting && _simulation is not null;
        if (_running)
        {
            _nextFrameAtMilliseconds = _scheduleWatch.Elapsed.TotalMilliseconds;
            _frameTimer.Start();
        }
        else
        {
            _frameTimer.Stop();
        }
        UpdateRunState();
        UpdateBadge();
    }

    private void UpdateRunState()
    {
        RunButton.Content = _running ? "Пауза" : "Продолжить";
        RunButton.IsEnabled = !_resetting && !_executionPending && _simulation is not null;
        StepButton.IsEnabled = !_running && !_frameBusy && !_resetting && !_executionPending && _simulation is not null;
        ResetButton.IsEnabled = !_resetting;
    }

    private async void Step_OnClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        await ProduceFrameAsync();
    }

    private async void FrameTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_running || _frameBusy) return;
        int targetFps = _activeState?.TargetFps ?? (TargetFpsBox.SelectedIndex == 1 ? 60 : 30);
        double now = _scheduleWatch.Elapsed.TotalMilliseconds;
        if (now < _nextFrameAtMilliseconds) return;
        double period = 1000d / Math.Clamp(targetFps, 1, 120);
        _nextFrameAtMilliseconds = Math.Max(_nextFrameAtMilliseconds + period, now);
        await ProduceFrameAsync();
    }

    private async Task ProduceFrameAsync(int? stepOverride = null)
    {
        if (_closed || _resetting || _frameBusy || _simulation is null || _activeState is null || _simulationCts is null) return;
        _frameBusy = true;
        var frameIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _frameIdleTask = frameIdle.Task;
        IGrayScottEngine simulation = _simulation;
        GrayScottState state = _activeState.Clone(includeCheckpoint: false);
        state.Palette = _paletteManager.ActivePalette.Clone();
        state.ReversePalette = ReversePaletteBox.IsChecked == true;
        int steps = stepOverride ?? state.StepsPerFrame;
        var frame = ResolveFrameSize(state); double aspect = DisplayAspect;
        int generation = _generation, appearance = _appearanceVersion;
        GrayScottState? recovery = null; string? failure = null;
        CancellationToken token = _simulationCts.Token;
        var pendingInjections = new List<(double X, double Y)>();
        while (_injections.TryDequeue(out (double X, double Y) point)) pendingInjections.Add(point);

        try
        {
            var result = await Task.Run(() =>
            {
                foreach ((double x, double y) in pendingInjections)
                    simulation.Inject(x, y, state.BrushRadius);
                if (steps > 0) simulation.Advance(steps, token);
                GrayScottSnapshot snapshot = simulation.Snapshot();
                return (Snapshot: snapshot, Pixels: simulation.RenderFrame(state, frame.Width, frame.Height, token, aspect));
            }, token);
            if (_closed || generation != _generation || !ReferenceEquals(simulation, _simulation) || token.IsCancellationRequested) return;
            _presented = result.Snapshot; PresentFrame(result.Pixels, _presented.StepCount, frame.Width, frame.Height);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_closed && generation == _generation)
            {
                SetRunning(false); StatusText.Text = exception.Message;
                if (simulation.Backend == GrayScottBackend.Gpu && _presented is not null)
                {
                    recovery = CaptureState("recovery"); recovery.Backend = GrayScottBackend.Cpu;
                    failure = $"ГП остановлен. Последнее показанное поле восстановлено на ЦП: {exception.Message}";
                }
            }
        }
        finally
        {
            _frameBusy = false;
            frameIdle.TrySetResult(true);
            if (!_closed)
            {
                UpdateRunState();
                if (recovery is null && generation == _generation && !_running && (appearance != _appearanceVersion || !_injections.IsEmpty)) RequestRepaint();
            }
        }
        if (recovery is not null && !_closed && generation == _generation)
        { await InstallEngineAsync(recovery, false, false); BackendHint.Text = failure; }
    }

    private async Task RenderCurrentFieldAsync()
    {
        if (_simulation is null || _activeState is null || _simulationCts is null) return;
        try
        {
            await _frameIdleTask;
            _activeState.Palette = _paletteManager.ActivePalette.Clone();
            _activeState.ReversePalette = ReversePaletteBox.IsChecked == true;
            await ProduceFrameAsync(0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetRunning(false);
            StatusText.Text = $"Не удалось обновить изображение: {exception.Message}";
        }
    }

    private void PresentFrame(byte[] pixels, long stepCount, int width, int height)
    {
        if (_activeState is null) return;
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        { _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); FrameImage.Source = _bitmap; }
        _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight),
            pixels, _bitmap.PixelWidth * 4, 0);
        _presentedFrames++;
        if (_fpsWatch.Elapsed.TotalSeconds >= 0.75)
        {
            _measuredFps = _presentedFrames / _fpsWatch.Elapsed.TotalSeconds;
            _presentedFrames = 0;
            _fpsWatch.Restart();
        }
        double simulatedTime = stepCount * _activeState.DeltaTime;
        UpdateBadge();
        StatusText.Text = $"{(_simulation?.Backend == GrayScottBackend.Gpu ? "ГП" : "ЦП")} · поле {_activeState.GridSize} × {_activeState.GridSize} · буфер {width} × {height}\nВремя модели: {simulatedTime:N1} · шагов/кадр: {_activeState.StepsPerFrame} · " +
                          $"фактически {_measuredFps:F1} FPS";
    }

    private void UpdateBadge() => FrameBadgeText.Text = (_running ? $"{_measuredFps:F1} FPS" : "Пауза") + $" · шаг {_presented?.StepCount ?? 0:N0}";

    private async void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        RandomSeedBox.Text = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);
        await ResetSimulationAsync(startAfterReset: true);
    }

    private void Palette_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new GrayScottPaletteWindow(_paletteManager) { Owner = this };
        window.PaletteApplied += async (_, _) =>
        {
            UpdatePalettePreview();
            await RenderCurrentFieldAsync();
        };
        window.ShowDialog();
    }

    private void UpdatePalettePreview()
    {
        List<Color> colors = _paletteManager.ActivePalette.Colors;
        if (colors.Count == 0)
        {
            PalettePreview.Background = MediaBrushes.Transparent;
            return;
        }
        if (colors.Count == 1)
        {
            PalettePreview.Background = new SolidColorBrush(colors[0]);
            return;
        }
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < colors.Count; index++)
            brush.GradientStops.Add(new GradientStop(colors[index], index / (double)(colors.Count - 1)));
        PalettePreview.Background = brush;
    }

    private async void Saves_OnClick(object sender, RoutedEventArgs e)
    {
        bool resume = _running; int generation = _generation; SetRunning(false); await _frameIdleTask;
        if (_closed || _resetting || _presented is null) return;
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForGrayScott(this, _saveStore));
        if (generation == _generation) SetRunning(resume);
    }

    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        bool resume = _running;
        SetRunning(false);
        await _frameIdleTask;
        if (_closed || _resetting) return;
        if (_simulation is null)
        {
            MessageBox.Show(this, "Симуляция ещё не инициализирована.", "Gray–Scott",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SetRunning(resume);
            return;
        }
        if (_closed || _resetting || _presented is null) return;
        GrayScottState state = CaptureState("export");
        GrayScottSnapshot snapshot = _presented.Copy();
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "gray_scott",
            WindowTitle = "Экспорт текущего кадра Gray–Scott",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => GrayScottRenderer.RenderSnapshotAsync(
                snapshot, state, request.Width, request.Height, token, progress)
        });
        SetRunning(resume);
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_resetting || _simulation is null) return;
        if (e.OriginalSource is DependencyObject source)
            for (DependencyObject? parent = source; parent is not null; parent = parent is Visual ? VisualTreeHelper.GetParent(parent) : null)
                if (parent is Button) return;
        _painting = true;
        CanvasHost.CaptureMouse();
        QueueInjection(e.GetPosition(CanvasHost));
        e.Handled = true;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_painting || e.LeftButton != MouseButtonState.Pressed) return;
        QueueInjection(e.GetPosition(CanvasHost));
    }

    private async void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_painting) return;
        _painting = false;
        CanvasHost.ReleaseMouseCapture();
        if (!_running) await ProduceFrameAsync(0);
    }

    private void QueueInjection(Point point)
    {
        if (_simulation is null) return;
        double hostWidth = Math.Max(1, CanvasHost.ActualWidth);
        double hostHeight = Math.Max(1, CanvasHost.ActualHeight);
        double side = Math.Min(hostWidth, hostHeight);
        double left = (hostWidth - side) * 0.5;
        double top = (hostHeight - side) * 0.5;
        double x = (point.X - left) / side;
        double y = (point.Y - top) / side;
        if (x is < 0 or > 1 || y is < 0 or > 1) return;
        if (_injections.Count >= 4096) return;
        _injections.Enqueue((x, y));
        if (!_running) RequestRepaint();
    }

    private void SetTimerInterval(int targetFps)
    {
        _frameTimer.Interval = TimeSpan.FromMilliseconds(4);
        _nextFrameAtMilliseconds = _scheduleWatch.Elapsed.TotalMilliseconds;
    }

    private void Toggle_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleButton, 330);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Space && RunButton.IsEnabled)
        {
            SetRunning(!_running);
            e.Handled = true;
        }
        else if (e.Key == Key.F11 || e.Key == Key.Escape && _fullScreen)
        {
            ToggleFullScreen();
        }
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
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
        _fullScreen = !_fullScreen;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true; _generation++; _frameTimer.Stop(); _sizeTimer.Stop(); _simulationCts?.Cancel();
        Retire(_simulationCts, _frameIdleTask, _resetIdleTask, _simulation); _simulationCts = null; _simulation = null;
    }

    private static bool ReadFiniteDouble(string text, out double value)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return parsed && double.IsFinite(value);
    }

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
