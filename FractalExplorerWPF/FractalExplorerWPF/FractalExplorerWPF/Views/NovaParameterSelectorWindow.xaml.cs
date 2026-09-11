using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Canvas = System.Windows.Controls.Canvas;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;

namespace FractalExplorerWPF.Views;

public partial class NovaParameterSelectorWindow : Window
{
    private const decimal BaseScale = 4m;
    private readonly NovaState _mapState;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _visualTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly TransformGroup _transform = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translation = new();
    private CancellationTokenSource? _cts;
    private Session? _session;
    private decimal _centerX, _centerY, _zoom = 1, _renderedCenterX, _renderedCenterY, _renderedZoom = 1;
    private decimal _selectedReal, _selectedImaginary;
    private bool _hasFrame, _panning, _rendering;
    private Point _panStart;

    public event Action<decimal, decimal>? CoordinatesSelected;

    public NovaParameterSelectorWindow(NovaState source)
    {
        InitializeComponent();
        _selectedReal = source.CReal; _selectedImaginary = source.CImaginary;
        _mapState = new NovaState
        {
            Variant = NovaVariant.Mandelbrot, PReal = source.PReal, PImaginary = source.PImaginary,
            Z0Real = source.Z0Real, Z0Imaginary = source.Z0Imaginary, M = source.M,
            Threshold = 20, Iterations = 100, Zoom = 1, Palette = source.Palette
        };
        _transform.Children.Add(_scale); _transform.Children.Add(_translation);
        StableImage.RenderTransformOrigin = new Point(0.5, 0.5); StableImage.RenderTransform = _transform;
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); _ = RenderAsync(); };
        _visualTimer.Tick += (_, _) => { if (_session is not null) Flush(_session, false); };
        Loaded += (_, _) => ScheduleRender();
    }

    private void ScheduleRender() { if (!IsLoaded) return; _cts?.Cancel(); _renderTimer.Stop(); _renderTimer.Start(); }

    private async Task RenderAsync()
    {
        if (_rendering) { ScheduleRender(); return; }
        _rendering = true; _cts?.Dispose(); _cts = new CancellationTokenSource(); CancellationToken token = _cts.Token;
        _mapState.CenterX = _centerX; _mapState.CenterY = _centerY; _mapState.Zoom = (double)_zoom;
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int width = surface.PixelWidth;
            int height = surface.PixelHeight;
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(width, height, 16, RenderPatternSettings.SelectedPattern);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            var session = new Session(bitmap, tiles.Count, width, height); _session = session; CurrentImage.Source = bitmap;
            RenderOverlay.BeginSession(width, height); _visualTimer.Start();
            var queue = new ConcurrentQueue<MandelbrotRenderTile>(tiles);
            Task[] workers = Enumerable.Range(0, Math.Max(1, Environment.ProcessorCount)).Select(_ => Task.Run(() =>
            {
                while (queue.TryDequeue(out MandelbrotRenderTile tile))
                {
                    if (token.IsCancellationRequested) return;
                    session.Events.Enqueue(new TileEvent(true, tile, null));
                    byte[]? pixels = NovaRenderer.RenderTile(_mapState, width, height, tile, token, true);
                    if (pixels is null || token.IsCancellationRequested) return;
                    session.Events.Enqueue(new TileEvent(false, tile, pixels));
                }
            })).ToArray();
            await Task.WhenAll(workers);
            if (token.IsCancellationRequested) return;
            Flush(session, true);
            BitmapSource completed = bitmap.Clone(); completed.Freeze(); StableImage.Source = completed; CurrentImage.Source = null;
            _renderedCenterX = _centerX; _renderedCenterY = _centerY; _renderedZoom = _zoom; _hasFrame = true; UpdateTransform(); DrawOverlays();
            StatusText.Text = $"C = {_selectedReal:G8} {(_selectedImaginary < 0 ? '−' : '+')} {Math.Abs(_selectedImaginary):G8}i";
        }
        catch (OperationCanceledException) { CurrentImage.Source = null; }
        finally { _visualTimer.Stop(); RenderOverlay.EndSession(); _session = null; _rendering = false; }
    }

    private void Flush(Session session, bool drain)
    {
        int count = 0; bool changed = false;
        while ((drain || count < 512) && session.Events.TryDequeue(out TileEvent entry))
        {
            if (entry.Start) RenderOverlay.StartTile(entry.Tile);
            else if (entry.Pixels is not null)
            {
                if (ProgressiveRenderBitmap.WriteTile(session.Bitmap, entry.Tile, entry.Pixels))
                    RenderOverlay.CompleteTile(entry.Tile);
            }
            count++; changed = true;
        }
        if (changed) RenderOverlay.Refresh();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning) return;
        (decimal real, decimal imaginary) = ScreenToWorld(e.GetPosition(CanvasHost));
        if (real is < -2 or > 2 || imaginary is < -2 or > 2) return;
        _selectedReal = real; _selectedImaginary = imaginary; DrawOverlays();
        StatusText.Text = $"C = {real:G8} {(imaginary < 0 ? '−' : '+')} {Math.Abs(imaginary):G8}i";
        CoordinatesSelected?.Invoke(real, imaginary);
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mouse = e.GetPosition(CanvasHost); var before = ScreenToWorld(mouse);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2m : 1m / 1.2m), 0.000001m, 1_000_000m);
        var after = ScreenToWorld(mouse); _centerX += before.X - after.X; _centerY += before.Y - after.Y;
        UpdateTransform(); DrawOverlays(); ScheduleRender();
    }

    private void CanvasHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return; _panning = true; _panStart = e.GetPosition(CanvasHost); CanvasHost.CaptureMouse();
    }
    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return; Point current = e.GetPosition(CanvasHost); var before = ScreenToWorld(_panStart); var after = ScreenToWorld(current);
        _centerX += before.X - after.X; _centerY += before.Y - after.Y; _panStart = current; UpdateTransform(); DrawOverlays();
    }
    private void CanvasHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || e.ChangedButton != MouseButton.Middle) return; _panning = false; CanvasHost.ReleaseMouseCapture(); ScheduleRender();
    }
    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) { UpdateTransform(); DrawOverlays(); ScheduleRender(); }

    private (decimal X, decimal Y) ScreenToWorld(Point point)
    {
        decimal width = (decimal)Math.Max(1, CanvasHost.ActualWidth), scale = BaseScale / _zoom;
        return (_centerX + ((decimal)point.X - width / 2) * scale / width,
            _centerY + ((decimal)Math.Max(1, CanvasHost.ActualHeight) / 2 - (decimal)point.Y) * scale / width);
    }

    private Point WorldToScreen(decimal real, decimal imaginary)
    {
        double width = CanvasHost.ActualWidth, scale = (double)(BaseScale / _zoom);
        return new Point(width / 2 + ((double)real - (double)_centerX) * width / scale,
            CanvasHost.ActualHeight / 2 - ((double)imaginary - (double)_centerY) * width / scale);
    }

    private void UpdateTransform()
    {
        if (!_hasFrame || CanvasHost.ActualWidth <= 0) return; double scale = (double)(_zoom / _renderedZoom), width = CanvasHost.ActualWidth;
        decimal currentScale = BaseScale / _zoom; _scale.ScaleX = _scale.ScaleY = scale;
        _translation.X = (double)((_renderedCenterX - _centerX) / currentScale) * width;
        _translation.Y = (double)((_centerY - _renderedCenterY) / currentScale) * width;
    }

    private void DrawOverlays()
    {
        MarkerCanvas.Children.Clear();
        Point topLeft = WorldToScreen(-2, 2), bottomRight = WorldToScreen(2, -2);
        var border = new Rectangle { Width = Math.Max(0, bottomRight.X - topLeft.X), Height = Math.Max(0, bottomRight.Y - topLeft.Y), Stroke = Brushes.Red, StrokeThickness = 1 };
        Canvas.SetLeft(border, topLeft.X); Canvas.SetTop(border, topLeft.Y); MarkerCanvas.Children.Add(border);
        Point marker = WorldToScreen(_selectedReal, _selectedImaginary);
        MarkerCanvas.Children.Add(new Line { X1 = marker.X - 9, X2 = marker.X + 9, Y1 = marker.Y, Y2 = marker.Y, Stroke = Brushes.Lime, StrokeThickness = 2 });
        MarkerCanvas.Children.Add(new Line { X1 = marker.X, X2 = marker.X, Y1 = marker.Y - 9, Y2 = marker.Y + 9, Stroke = Brushes.Lime, StrokeThickness = 2 });
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { _renderTimer.Stop(); _visualTimer.Stop(); _cts?.Cancel(); _cts?.Dispose(); }
    private sealed class Session(WriteableBitmap bitmap, int count, int width, int height) { public WriteableBitmap Bitmap { get; } = bitmap; public int Count { get; } = count; public int Width { get; } = width; public int Height { get; } = height; public ConcurrentQueue<TileEvent> Events { get; } = new(); }
    private readonly record struct TileEvent(bool Start, MandelbrotRenderTile Tile, byte[]? Pixels);
}
