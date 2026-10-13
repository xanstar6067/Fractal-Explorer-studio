using System.Diagnostics;
using System.Globalization;
using System.Windows;
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

public partial class DlaWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DlaSaveStore _saveStore = new();
    private readonly ColorSelectionService _colorPicker = ColorSelectionService.Default;
    private readonly TransformGroup _imageTransform = new();
    private readonly ScaleTransform _imageScale = new(1, 1);
    private readonly TranslateTransform _translation = new();
    private CancellationTokenSource? _renderCts;
    private bool _rendering;
    private bool _panning;
    private bool _controlsVisible = true;
    private bool _hasStableFrame;
    private bool _syncing;
    private Point _panStart;
    private double _centerX;
    private double _centerY;
    private double _viewWidth = 2.2;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedViewWidth = 2.2;
    private Color _startColor = Color.FromRgb(125, 211, 252);
    private Color _endColor = Color.FromRgb(99, 102, 241);
    private Color _backgroundColor = Color.FromRgb(3, 7, 18);
    private string? _presetId;
    private readonly FullscreenToggle _fullscreen = new();

    public DlaWindow()
    {
        InitializeComponent();
        _imageTransform.Children.Add(_imageScale);
        _imageTransform.Children.Add(_translation);
        StableImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StableImage.RenderTransform = _imageTransform;
        PresetBox.ItemsSource = DlaPresets.All;
        ApplyState(DlaPresets.All[0].State, DlaPresets.All[0]);

        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            _ = RenderAsync();
        };
        Loaded += (_, _) => ScheduleRender();
    }

    public DlaState CaptureState(string name)
    {
        if (TryCaptureState(name, out DlaState state, out string error)) return state;
        throw new InvalidOperationException(error);
    }

    private bool TryCaptureState(string name, out DlaState state, out string error)
    {
        state = new DlaState();
        if (!int.TryParse(ParticlesBox.Text, out int particles) || particles is < 500 or > 100_000)
        {
            error = "Число частиц должно быть от 500 до 100 000.";
            return false;
        }
        if (!int.TryParse(GridSizeBox.Text, out int gridSize) || gridSize is < 201 or > 1_401 || gridSize % 2 == 0)
        {
            error = "Размер сетки должен быть нечётным числом от 201 до 1401.";
            return false;
        }
        if (!int.TryParse(MaxStepsBox.Text, out int maximumSteps) || maximumSteps is < 100 or > 100_000)
        {
            error = "Число шагов блуждания должно быть от 100 до 100 000.";
            return false;
        }
        if (!int.TryParse(RandomSeedBox.Text, out int randomSeed))
        {
            error = "Seed генератора должен быть целым числом.";
            return false;
        }
        if (!ReadFiniteDouble(StickinessBox.Text, out double stickiness) || stickiness is < 0 or > 1)
        {
            error = "Вероятность прилипания должна быть от 0 до 1.";
            return false;
        }
        if (!ReadFiniteDouble(DriftXBox.Text, out double driftX) ||
            !ReadFiniteDouble(DriftYBox.Text, out double driftY) ||
            driftX is < -2 or > 2 || driftY is < -2 or > 2)
        {
            error = "Компоненты дрейфа должны быть от −2 до 2.";
            return false;
        }
        if (!ReadFiniteDouble(ParticleRadiusBox.Text, out double particleRadius) || particleRadius is < 0.25 or > 8)
        {
            error = "Радиус частицы должен быть от 0.25 до 8.";
            return false;
        }
        if (!ReadFiniteDouble(CenterXBox.Text, out double centerX) ||
            !ReadFiniteDouble(CenterYBox.Text, out double centerY) ||
            !ReadFiniteDouble(ViewWidthBox.Text, out double viewWidth) || viewWidth is < 0.02 or > 10)
        {
            error = "Проверьте центр и ширину вида (0.02–10).";
            return false;
        }

        state = new DlaState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            PresetId = _presetId,
            ParticleCount = particles,
            GridSize = gridSize,
            MaxStepsPerWalker = maximumSteps,
            RandomSeed = randomSeed,
            Stickiness = stickiness,
            DriftX = driftX,
            DriftY = driftY,
            CenterX = centerX,
            CenterY = centerY,
            ViewWidth = viewWidth,
            ParticleRadius = particleRadius,
            SeedMode = (DlaSeedMode)Math.Clamp(SeedModeBox.SelectedIndex, 0, 2),
            ColoringMode = (DlaColoringMode)Math.Clamp(ColoringBox.SelectedIndex, 0, 2),
            StartColor = _startColor,
            EndColor = _endColor,
            BackgroundColor = _backgroundColor
        };
        error = string.Empty;
        return true;
    }

    public void LoadState(DlaState state)
    {
        DlaPreset? preset = DlaPresets.All.FirstOrDefault(item => item.Id == state.PresetId);
        ApplyState(state, preset);
        UpdateStableTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StableImage, CurrentImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        DlaState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state.Clone(), width, height, token, progress);

    private void ApplyState(DlaState state, DlaPreset? preset)
    {
        _renderCts?.Cancel();
        _syncing = true;
        try
        {
            _presetId = state.PresetId;
            ParticlesBox.Text = state.ParticleCount.ToString(CultureInfo.InvariantCulture);
            GridSizeBox.Text = state.GridSize.ToString(CultureInfo.InvariantCulture);
            MaxStepsBox.Text = state.MaxStepsPerWalker.ToString(CultureInfo.InvariantCulture);
            RandomSeedBox.Text = state.RandomSeed.ToString(CultureInfo.InvariantCulture);
            StickinessBox.Text = Format(state.Stickiness);
            DriftXBox.Text = Format(state.DriftX);
            DriftYBox.Text = Format(state.DriftY);
            ParticleRadiusBox.Text = Format(state.ParticleRadius);
            SeedModeBox.SelectedIndex = (int)state.SeedMode;
            ColoringBox.SelectedIndex = (int)state.ColoringMode;
            _centerX = state.CenterX;
            _centerY = state.CenterY;
            _viewWidth = state.ViewWidth <= 0 ? 2.2 : state.ViewWidth;
            SyncViewportBoxes();
            _startColor = state.StartColor;
            _endColor = state.EndColor;
            _backgroundColor = state.BackgroundColor;
            PresetBox.SelectedItem = preset;
        }
        finally
        {
            _syncing = false;
        }
        UpdateSwatches();
    }

    private void Preset_OnChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedItem is not DlaPreset preset) return;
        ApplyState(preset.State.Clone(), preset);
        ScheduleRender();
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_syncing) ScheduleRender();
    }

    private void Viewport_OnChanged(object sender, EventArgs e)
    {
        if (_syncing) return;
        if (ReadFiniteDouble(CenterXBox.Text, out double x)) _centerX = x;
        if (ReadFiniteDouble(CenterYBox.Text, out double y)) _centerY = y;
        if (ReadFiniteDouble(ViewWidthBox.Text, out double width) && width is >= 0.02 and <= 10) _viewWidth = width;
        UpdateStableTransform();
        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void Render_OnClick(object sender, RoutedEventArgs e)
    {
        _renderTimer.Stop();
        _renderCts?.Cancel();
        _ = RenderAsync();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private async Task RenderAsync()
    {
        if (_rendering)
        {
            ScheduleRender();
            return;
        }

        if (!TryCaptureState("preview", out DlaState state, out string validationError))
        {
            ProgressText.Text = "Проверьте параметры";
            StatusText.Text = validationError;
            return;
        }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        _rendering = true;
        CancelButton.IsEnabled = true;
        GrowthBadge.Visibility = Visibility.Visible;
        var stopwatch = Stopwatch.StartNew();
        WriteableBitmap? activeBitmap = null;

        try
        {
            ClearDisplayedFrame();
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int width = surface.PixelWidth;
            int height = surface.PixelHeight;
            var bitmap = new WriteableBitmap(width, height,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Bgra32, null);
            activeBitmap = bitmap;
            var renderer = new DlaRenderer(state);
            byte[] initialFrame = await Task.Run(
                () => renderer.CreateFrame(width, height, CancellationToken.None));
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), initialFrame, width * 4, 0);
            CurrentImage.Source = bitmap;
            int batch = Math.Clamp(state.ParticleCount / 70, 25, 220);

            while (!renderer.Complete && !token.IsCancellationRequested)
            {
                await Task.Run(() => renderer.Grow(batch, token), token);
                byte[] frame = await Task.Run(() => renderer.CreateFrame(width, height, token), token);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), frame, width * 4, 0);
                int percent = Math.Min(100, (int)(renderer.ParticleCount * 100d / state.ParticleCount));
                ProgressBar.Value = percent;
                ProgressText.Text = $"Рост кластера: {percent}%";
                GrowthBadgeText.Text = $"{renderer.ParticleCount:N0} / {state.ParticleCount:N0} частиц";
                StatusText.Text = $"Глубина ветвей: {renderer.MaximumDepth:N0} · ушло блуждателей: {renderer.FailedWalkers:N0}";
            }

            if (token.IsCancellationRequested)
            {
                CommitFrame(bitmap, state);
                StatusText.Text = "Рост остановлен";
                return;
            }

            byte[] finalFrame = await Task.Run(() => renderer.CreateFrame(width, height, token), token);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), finalFrame, width * 4, 0);
            CommitFrame(bitmap, state);
            ProgressBar.Value = state.Stickiness <= 0 ? 0 : 100;
            ProgressText.Text = state.Stickiness <= 0
                ? "Прилипание равно 0: рост невозможен"
                : renderer.ParticleCount >= state.ParticleCount
                    ? "Агрегация завершена"
                    : "Рост остановлен: частицы больше не достигают кластера";
            StatusText.Text = $"{renderer.ParticleCount:N0} частиц за {stopwatch.Elapsed.TotalSeconds:F3} сек. · " +
                              $"глубина {renderer.MaximumDepth:N0}";
        }
        catch (OperationCanceledException)
        {
            if (activeBitmap is not null) CommitFrame(activeBitmap, state);
            StatusText.Text = "Рост остановлен";
        }
        catch (Exception exception)
        {
            if (activeBitmap is not null) CommitFrame(activeBitmap, state);
            MessageBox.Show(this, exception.Message, "DLA", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _rendering = false;
            CancelButton.IsEnabled = false;
            GrowthBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void StartColor_OnClick(object sender, RoutedEventArgs e) => PickColor(ref _startColor);
    private void EndColor_OnClick(object sender, RoutedEventArgs e) => PickColor(ref _endColor);
    private void BackgroundColor_OnClick(object sender, RoutedEventArgs e) => PickColor(ref _backgroundColor);

    private void PickColor(ref Color target)
    {
        if (!_colorPicker.TrySelectColor(this, target, out Color selected)) return;
        target = selected;
        UpdateSwatches();
        ScheduleRender();
    }

    private void UpdateSwatches()
    {
        StartColorSwatch.Background = new SolidColorBrush(_startColor);
        EndColorSwatch.Background = new SolidColorBrush(_endColor);
        BackgroundColorSwatch.Background = new SolidColorBrush(_backgroundColor);
        CanvasHost.Background = new SolidColorBrush(_backgroundColor);
    }

    private void ClearDisplayedFrame()
    {
        CurrentImage.Source = null;
        StableImage.Source = null;
        _hasStableFrame = false;
        _imageScale.ScaleX = 1;
        _imageScale.ScaleY = 1;
        _translation.X = 0;
        _translation.Y = 0;
    }

    private void CommitFrame(WriteableBitmap bitmap, DlaState state)
    {
        BitmapSource completed = bitmap.Clone();
        completed.Freeze();
        StableImage.Source = completed;
        CurrentImage.Source = null;
        _renderedCenterX = state.CenterX;
        _renderedCenterY = state.CenterY;
        _renderedViewWidth = state.ViewWidth;
        _hasStableFrame = true;
        UpdateStableTransform();
    }

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        _centerX = 0;
        _centerY = 0;
        _viewWidth = 2.2;
        SyncViewportBoxes();
        UpdateStableTransform();
        ScheduleRender();
    }

    private void Saves_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForDla(this, _saveStore));

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        if (!TryCaptureState("export", out DlaState exportState, out string validationError))
        {
            MessageBox.Show(this, validationError, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "dla",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) =>
                RenderBitmapAsync(exportState.Clone(), request.Width, request.Height, token, progress)
        });
    }

    private static async Task<BitmapSource> RenderBitmapAsync(
        DlaState state, int width, int height, CancellationToken token, IProgress<int>? progress)
    {
        var renderer = new DlaRenderer(state);
        int batch = Math.Clamp(state.ParticleCount / 35, 50, 400);
        while (!renderer.Complete && !token.IsCancellationRequested)
        {
            await Task.Run(() => renderer.Grow(batch, token), token);
            progress?.Report(Math.Min(99, (int)(renderer.ParticleCount * 100d / state.ParticleCount)));
        }
        token.ThrowIfCancellationRequested();
        byte[] pixels = await Task.Run(() => renderer.CreateFrame(width, height, token), token);
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        progress?.Report(100);
        return bitmap;
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStableTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        (double x, double y) before = ScreenToWorld(point);
        _viewWidth = Math.Clamp(_viewWidth * (e.Delta > 0 ? 0.84 : 1.19), 0.02, 10);
        (double x, double y) after = ScreenToWorld(point);
        _centerX += before.x - after.x;
        _centerY += before.y - after.y;
        SyncViewportBoxes();
        UpdateStableTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderCts?.Cancel();
        _panning = true;
        _panStart = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        Point current = e.GetPosition(CanvasHost);
        (double x, double y) from = ScreenToWorld(_panStart);
        (double x, double y) to = ScreenToWorld(current);
        _centerX += from.x - to.x;
        _centerY += from.y - to.y;
        _panStart = current;
        SyncViewportBoxes();
        UpdateStableTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        CanvasHost.ReleaseMouseCapture();
        ScheduleRender();
    }

    private (double x, double y) ScreenToWorld(Point point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double worldHeight = _viewWidth * height / width;
        return (_centerX + (point.X / width - 0.5) * _viewWidth,
            _centerY - (point.Y / height - 0.5) * worldHeight);
    }

    private void SyncViewportBoxes()
    {
        bool wasSyncing = _syncing;
        _syncing = true;
        CenterXBox.Text = Format(_centerX);
        CenterYBox.Text = Format(_centerY);
        ViewWidthBox.Text = Format(_viewWidth);
        _syncing = wasSyncing;
    }

    private void UpdateStableTransform()
    {
        if (!_hasStableFrame || CanvasHost.ActualWidth <= 0) return;
        double width = CanvasHost.ActualWidth;
        double height = CanvasHost.ActualHeight;
        double worldHeight = _viewWidth * height / width;
        _imageScale.ScaleX = _imageScale.ScaleY = _renderedViewWidth / _viewWidth;
        _translation.X = (_renderedCenterX - _centerX) / _viewWidth * width;
        _translation.Y = (_centerY - _renderedCenterY) / worldHeight * height;
    }

    private void Toggle_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost, ToggleButton, 310, ScheduleRender);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static bool ReadFiniteDouble(string text, out double value)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return parsed && double.IsFinite(value);
    }

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
