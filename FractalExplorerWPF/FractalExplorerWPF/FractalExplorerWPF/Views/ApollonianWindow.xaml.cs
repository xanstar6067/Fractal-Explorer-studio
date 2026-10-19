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

public partial class ApollonianWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly ApollonianSaveStore _saveStore = new();
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
    private Color _startColor = Color.FromRgb(34, 211, 238);
    private Color _endColor = Color.FromRgb(244, 63, 94);
    private Color _backgroundColor = Color.FromRgb(8, 15, 30);
    private readonly FullscreenToggle _fullscreen = new();

    public ApollonianWindow()
    {
        InitializeComponent();
        _imageTransform.Children.Add(_imageScale);
        _imageTransform.Children.Add(_translation);
        StableImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StableImage.RenderTransform = _imageTransform;

        _syncing = true;
        ColoringBox.SelectedIndex = 0;
        DrawModeBox.SelectedIndex = 0;
        DepthBox.Text = "11";
        CircleLimitBox.Text = "25000";
        MinimumRadiusBox.Text = "0.00001";
        LineWidthBox.Text = "1.25";
        SyncViewportBoxes();
        _syncing = false;
        UpdateSwatches();

        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            _ = RenderAsync();
        };
        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public ApollonianState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        if (!int.TryParse(DepthBox.Text, out int depth) || depth is < 1 or > 18)
            throw new InvalidOperationException("Глубина должна быть от 1 до 18.");
        if (!int.TryParse(CircleLimitBox.Text, out int circleLimit) || circleLimit is < 20 or > 150_000)
            throw new InvalidOperationException("Лимит окружностей должен быть от 20 до 150 000.");
        if (!ReadDouble(MinimumRadiusBox.Text, out double minimumRadius) || minimumRadius is < 1e-9 or > 0.1)
            throw new InvalidOperationException("Минимальный радиус должен быть от 1e-9 до 0.1.");
        if (!ReadDouble(LineWidthBox.Text, out double lineWidth) || lineWidth is < 0.25 or > 20)
            throw new InvalidOperationException("Толщина линии должна быть от 0.25 до 20.");
        if (!ReadDouble(CenterXBox.Text, out double centerX) ||
            !ReadDouble(CenterYBox.Text, out double centerY) ||
            !ReadDouble(ViewWidthBox.Text, out double viewWidth) || viewWidth is < 1e-6 or > 20)
            throw new InvalidOperationException("Проверьте центр и ширину вида (1e-6–20).");

        return new ApollonianState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            MaxDepth = depth,
            MaxCircles = circleLimit,
            MinimumRadius = minimumRadius,
            CenterX = centerX,
            CenterY = centerY,
            ViewWidth = viewWidth,
            LineWidth = lineWidth,
            ColoringMode = (ApollonianColoringMode)Math.Clamp(ColoringBox.SelectedIndex, 0, 2),
            DrawMode = (ApollonianDrawMode)Math.Clamp(DrawModeBox.SelectedIndex, 0, 1),
            StartColor = _startColor,
            EndColor = _endColor,
            BackgroundColor = _backgroundColor
        };
    }

    public void LoadState(ApollonianState state)
    {
        _renderCts?.Cancel();
        _syncing = true;
        try
        {
            DepthBox.Text = state.MaxDepth.ToString(CultureInfo.InvariantCulture);
            CircleLimitBox.Text = state.MaxCircles.ToString(CultureInfo.InvariantCulture);
            MinimumRadiusBox.Text = Format(state.MinimumRadius);
            LineWidthBox.Text = Format(state.LineWidth);
            ColoringBox.SelectedIndex = (int)state.ColoringMode;
            DrawModeBox.SelectedIndex = (int)state.DrawMode;
            _centerX = state.CenterX;
            _centerY = state.CenterY;
            _viewWidth = state.ViewWidth <= 0 ? 2.2 : state.ViewWidth;
            SyncViewportBoxes();
            _startColor = state.StartColor;
            _endColor = state.EndColor;
            _backgroundColor = state.BackgroundColor;
        }
        finally
        {
            _syncing = false;
        }
        UpdateSwatches();
        UpdateStableTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StableImage, CurrentImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        ApollonianState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state.Clone(), width, height, token, progress);

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_syncing) ScheduleRender();
    }

    private void Viewport_OnChanged(object sender, EventArgs e)
    {
        if (_syncing) return;
        if (ReadDouble(CenterXBox.Text, out double x)) _centerX = x;
        if (ReadDouble(CenterYBox.Text, out double y)) _centerY = y;
        if (ReadDouble(ViewWidthBox.Text, out double width) && width is >= 1e-6 and <= 20) _viewWidth = width;
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
        NumericInput.CommitAll(this);
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

        ApollonianState state;
        try
        {
            state = CaptureState("preview");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        _rendering = true;
        CancelButton.IsEnabled = true;
        RenderBadge.Visibility = Visibility.Visible;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int width = surface.PixelWidth;
            int height = surface.PixelHeight;
            var bitmap = new WriteableBitmap(width, height,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Bgra32, null);
            CurrentImage.Source = bitmap;
            var renderer = new ApollonianRenderer(state, width, height);
            int batch = Math.Clamp(state.MaxCircles / 45, 120, 1_500);

            while (!renderer.Complete && !token.IsCancellationRequested)
            {
                await Task.Run(() => renderer.Advance(batch, token), token);
                byte[] frame = await Task.Run(renderer.CreateFrame, token);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), frame, width * 4, 0);
                int percent = Math.Min(99, (int)(renderer.CircleCount * 100d / state.MaxCircles));
                ProgressBar.Value = percent;
                ProgressText.Text = $"Рекурсия: уровень {renderer.CurrentDepth} из {state.MaxDepth}";
                RenderBadgeText.Text = $"{renderer.CircleCount:N0} окружностей";
            }

            if (token.IsCancellationRequested)
            {
                CurrentImage.Source = null;
                StatusText.Text = "Рендер отменён";
                return;
            }

            byte[] finalFrame = await Task.Run(renderer.CreateFrame, token);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), finalFrame, width * 4, 0);
            BitmapSource completed = bitmap.Clone();
            completed.Freeze();
            StableImage.Source = completed;
            CurrentImage.Source = null;
            _renderedCenterX = state.CenterX;
            _renderedCenterY = state.CenterY;
            _renderedViewWidth = state.ViewWidth;
            _hasStableFrame = true;
            UpdateStableTransform();
            ProgressBar.Value = 100;
            ProgressText.Text = $"Готово: {renderer.CircleCount:N0} окружностей";
            StatusText.Text = $"Построено за {stopwatch.Elapsed.TotalSeconds:F3} сек.";
        }
        catch (OperationCanceledException)
        {
            CurrentImage.Source = null;
            StatusText.Text = "Рендер отменён";
        }
        catch (Exception exception)
        {
            CurrentImage.Source = null;
            MessageBox.Show(this, exception.Message, "Аполлонова прокладка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _rendering = false;
            CancelButton.IsEnabled = false;
            RenderBadge.Visibility = Visibility.Collapsed;
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

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        _centerX = 0;
        _centerY = 0;
        _viewWidth = 2.2;
        SyncViewportBoxes();
        UpdateStableTransform();
        ScheduleRender();
    }

    private void Saves_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForApollonian(this, _saveStore)); }

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        try
        {
            _ = CaptureState("export");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "apollonian-gasket",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) =>
                RenderBitmapAsync(CaptureState("export"), request.Width, request.Height, token, progress)
        });
    }

    private static async Task<BitmapSource> RenderBitmapAsync(
        ApollonianState state, int width, int height, CancellationToken token, IProgress<int>? progress)
    {
        var renderer = new ApollonianRenderer(state, width, height);
        int batch = Math.Clamp(state.MaxCircles / 30, 250, 3_000);
        while (!renderer.Complete && !token.IsCancellationRequested)
        {
            await Task.Run(() => renderer.Advance(batch, token), token);
            progress?.Report(Math.Min(99, (int)(renderer.CircleCount * 100d / state.MaxCircles)));
        }
        token.ThrowIfCancellationRequested();
        byte[] pixels = renderer.CreateFrame();
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
        _viewWidth = Math.Clamp(_viewWidth * (e.Delta > 0 ? 0.82 : 1.22), 1e-6, 20);
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

    private static bool ReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
