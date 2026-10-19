using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorer.Engines;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class SerpinskyWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly SerpinskyPaletteManager _paletteManager = new();
    private readonly SerpinskySaveStore _saveStore = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private readonly bool _chaosOnly;
    private CancellationTokenSource? _renderCts;
    private bool _isRendering;
    private bool _isPanning;
    private bool _controlsVisible = true;
    private readonly FullscreenToggle _fullscreen = new();
    private Point _lastPanPoint;
    private double _centerX;
    private double _centerY;
    private double _zoom = 1.0;
    private bool _hasRenderedFrame;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedZoom = 1;

    public SerpinskyWindow(bool chaosOnly = false)
    {
        _chaosOnly = chaosOnly;
        InitializeComponent();
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        CanvasImage.RenderTransformOrigin = new Point(0.5, 0.5);
        CanvasImage.RenderTransform = _previewTransform;
        UpdateCanvasBackground();
        _renderTimer.Tick += RenderTimer_OnTick;
        RenderModeBox.SelectedIndex = chaosOnly ? 1 : 0;
        IterationsBox.Text = chaosOnly ? "250000" : "8";
        ZoomBox.Text = "1";

        if (chaosOnly)
        {
            Title = "Серпинский — игра хаоса";
            RenderModeLabel.Visibility = Visibility.Collapsed;
            RenderModeBox.Visibility = Visibility.Collapsed;
            IterationsLabel.Text = "Количество точек";
        }

        RenderThreadsBox.Fill(ThreadsBox);

        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public SerpinskySaveState CaptureState(string saveName)
    {
        NumericInput.Prepare(this);
        return new()
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            RenderMode = SelectedRenderMode,
            Iterations = ReadIterations(),
            Zoom = _zoom,
            CenterX = _centerX,
            CenterY = _centerY,
            FractalColor = _paletteManager.ActivePalette.FractalColor,
            BackgroundColor = _paletteManager.ActivePalette.BackgroundColor
        };
    }

    public void LoadState(SerpinskySaveState state)
    {
        _renderCts?.Cancel();
        RenderModeBox.SelectedIndex = _chaosOnly || state.RenderMode == SerpinskyRenderMode.Chaos ? 1 : 0;
        int iterations = _chaosOnly && state.RenderMode != SerpinskyRenderMode.Chaos
            ? 250_000
            : state.Iterations;
        IterationsBox.Text = iterations.ToString(CultureInfo.InvariantCulture);
        _zoom = Math.Clamp(state.Zoom, 0.01, 10_000_000);
        _centerX = state.CenterX;
        _centerY = state.CenterY;
        ZoomBox.Text = _zoom.ToString("0.####", CultureInfo.InvariantCulture);

        _paletteManager.ActivePalette = new SerpinskyPalette
        {
            Name = $"Загружено: {state.SaveName}",
            FractalColor = state.FractalColor,
            BackgroundColor = state.BackgroundColor
        };
        UpdateCanvasBackground();
        UpdatePreviewTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        SerpinskySaveState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, state.Iterations, 1, token, progress);

    private SerpinskyRenderMode SelectedRenderMode =>
        RenderModeBox.SelectedIndex == 1
            ? SerpinskyRenderMode.Chaos
            : SerpinskyRenderMode.Geometric;

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (sender == RenderModeBox)
        {
            IterationsBox.Text = SelectedRenderMode == SerpinskyRenderMode.Geometric ? "8" : "50000";
        }
        ScheduleRender();
    }

    private void ZoomBox_OnTextChanged(object sender, EventArgs e)
    {
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (TryReadDouble(ZoomBox.Text, out double zoom))
        {
            _zoom = Math.Clamp(zoom, 0.01, 10_000_000);
            UpdatePreviewTransform();
            ScheduleRender();
        }
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SerpinskyPaletteWindow(_paletteManager) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            UpdateCanvasBackground();
            ScheduleRender();
        }
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForSerpinsky(this, _saveStore));
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        _renderCts?.Cancel();
        SerpinskySaveState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "serpinski",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = state.RenderMode == SerpinskyRenderMode.Geometric ? 4 : 1,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, state.Iterations, request.SsaaFactor, token, progress)
        });
    }

    private void ScheduleRender()
    {
        if (!IsLoaded)
        {
            return;
        }

        _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
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

        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        int width = surface.PixelWidth;
        int height = surface.PixelHeight;
        SerpinskySaveState state;
        try
        {
            state = CaptureState("preview");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var stopwatch = Stopwatch.StartNew();
        SetRenderingState(true, "Рендеринг...");

        try
        {
            BitmapSource bitmap = await RenderBitmapAsync(
                state,
                width,
                height,
                state.Iterations,
                1,
                token,
                new Progress<int>(value => RenderProgress.Value = value),
                surface.Dpi.PixelsPerInchX,
                surface.Dpi.PixelsPerInchY);
            if (token.IsCancellationRequested)
            {
                StatusText.Text = "Рендер отменён";
                return;
            }
            PresentBitmap(bitmap, state);
            stopwatch.Stop();
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Рендер отменён";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка рендера";
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetRenderingState(false);
        }
    }

    private async Task<BitmapSource> RenderBitmapAsync(
        SerpinskySaveState state,
        int width,
        int height,
        int iterations,
        int ssaaFactor,
        CancellationToken token,
        IProgress<int>? progress,
        double dpiX = 96,
        double dpiY = 96)
    {
        int factor = state.RenderMode == SerpinskyRenderMode.Geometric
            ? Math.Clamp(ssaaFactor, 1, 4)
            : 1;
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] buffer = new byte[checked(stride * renderHeight)];
        var engine = CreateEngine(state, iterations);
        int threadCount = GetThreadCount();

        await Task.Run(
            () => engine.RenderToBuffer(
                buffer,
                renderWidth,
                renderHeight,
                stride,
                4,
                threadCount,
                token,
                value => progress?.Report(value)));

        BitmapSource source = BitmapSource.Create(
            renderWidth,
            renderHeight,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            null,
            buffer,
            stride);
        source.Freeze();

        if (factor == 1)
        {
            return source;
        }

        var scaled = new TransformedBitmap(source, new ScaleTransform(1.0 / factor, 1.0 / factor));
        scaled.Freeze();
        return scaled;
    }

    private static FractalSerpinskyEngine CreateEngine(SerpinskySaveState state, int iterations) =>
        new()
        {
            RenderMode = state.RenderMode,
            Iterations = iterations,
            Zoom = state.Zoom,
            CenterX = state.CenterX,
            CenterY = state.CenterY,
            ColorMode = SerpinskyColorMode.CustomColor,
            FractalColor = ToDrawingColor(state.FractalColor),
            BackgroundColor = ToDrawingColor(state.BackgroundColor)
        };

    private static DrawingColor ToDrawingColor(MediaColor color) =>
        DrawingColor.FromArgb(color.A, color.R, color.G, color.B);

    private int ReadIterations()
    {
        if (!int.TryParse(IterationsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new InvalidOperationException("Введите целое количество итераций.");
        }

        int minimum = SelectedRenderMode == SerpinskyRenderMode.Geometric ? 0 : 1_000;
        int maximum = SelectedRenderMode == SerpinskyRenderMode.Geometric ? 20 : int.MaxValue;
        return Math.Clamp(value, minimum, maximum);
    }

    private int GetThreadCount() => RenderThreadsBox.Read(ThreadsBox);

    private void SetRenderingState(bool rendering, string? status = null)
    {
        _isRendering = rendering;
        CancelButton.IsEnabled = rendering;
        if (!rendering)
        {
            RenderProgress.Value = 0;
        }
        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 250, ScheduleRender);

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mouse = e.GetPosition(CanvasHost);
        Point worldBefore = ScreenToWorld(mouse);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1.0 / 1.2), 0.01, 10_000_000);
        Point worldAfter = ScreenToWorld(mouse);
        _centerX += worldBefore.X - worldAfter.X;
        _centerY += worldBefore.Y - worldAfter.Y;
        ZoomBox.Text = _zoom.ToString("0.####", CultureInfo.InvariantCulture);
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderCts?.Cancel();
        _isPanning = true;
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.Cursor = Cursors.SizeAll;
        if (!CanvasHost.CaptureMouse())
        {
            _isPanning = false;
            CanvasHost.Cursor = null;
        }
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPanning();
            return;
        }

        Point current = e.GetPosition(CanvasHost);
        Point worldBefore = ScreenToWorld(_lastPanPoint);
        Point worldAfter = ScreenToWorld(current);
        _centerX += worldBefore.X - worldAfter.X;
        _centerY += worldBefore.Y - worldAfter.Y;
        _lastPanPoint = current;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPanning();
    }

    private void CanvasHost_OnLostMouseCapture(object sender, MouseEventArgs e) => StopPanning();

    private void StopPanning(bool scheduleRender = true)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (CanvasHost.IsMouseCaptured)
        {
            CanvasHost.ReleaseMouseCapture();
        }

        CanvasHost.Cursor = null;
        if (scheduleRender)
        {
            ScheduleRender();
        }
    }

    private void PresentBitmap(BitmapSource bitmap, SerpinskySaveState state)
    {
        CanvasImage.Source = bitmap;
        _renderedCenterX = state.CenterX;
        _renderedCenterY = state.CenterY;
        _renderedZoom = state.Zoom;
        _hasRenderedFrame = true;
        UpdatePreviewTransform();
    }

    private void UpdateCanvasBackground() =>
        CanvasHost.Background = new SolidColorBrush(_paletteManager.ActivePalette.BackgroundColor);

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || CanvasHost.ActualWidth <= 0 || CanvasHost.ActualHeight <= 0)
        {
            return;
        }

        double scale = _zoom / Math.Max(1e-12, _renderedZoom);
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = (_renderedCenterX - _centerX) * _zoom * CanvasHost.ActualHeight;
        _previewTranslation.Y = (_centerY - _renderedCenterY) * _zoom * CanvasHost.ActualHeight;
        bool isIdentity = Math.Abs(scale - 1) < 1e-9 &&
                          Math.Abs(_previewTranslation.X) < 0.01 &&
                          Math.Abs(_previewTranslation.Y) < 0.01;
        RenderOptions.SetBitmapScalingMode(CanvasImage,
            isIdentity ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
    }

    private Point ScreenToWorld(Point point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double viewHeight = 1.0 / _zoom;
        double viewWidth = viewHeight * width / height;
        return new Point(
            _centerX - viewWidth / 2.0 + point.X / width * viewWidth,
            _centerY + viewHeight / 2.0 - point.Y / height * viewHeight);
    }

    private static bool TryReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            _fullscreen.Toggle(this);
        }
        else if (e.Key == Key.Escape && _fullscreen.IsActive)
        {
            _fullscreen.Toggle(this);
        }
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopPanning(scheduleRender: false);
        _renderTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }
}
