using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace FractalExplorerWPF.Controls;

/// <summary>Orbit, zoom and pick on a Phoenix parameter section. Both views share one device.</summary>
public partial class PhoenixParameterSliceView : UserControl, IDisposable
{
    private PhoenixParameterSliceRenderer? _renderer;
    private PhoenixState? _state;
    private PhoenixSliceFrame? _frame;
    private Fractal3DPose _pose = DefaultPose;
    private Vector3 _marker;
    private CancellationTokenSource? _cts;
    private bool _c1, _shadows, _running, _requested, _disposed, _active;
    private bool _mouseDown, _dragging;
    private Point _start, _last;
    private int _revision, _frameRevision = -1;
    private int _parameterRevision;
    public event Action<Vector3>? PointSelected;

    private static Fractal3DPose DefaultPose => new(Fractal3DCamera.Orientation(25, 15, 0), 4.8, Vector3.Zero);

    public PhoenixParameterSliceView() => InitializeComponent();

    internal void Initialize(Direct3DDeviceHost host, bool c1)
    {
        _renderer = new(host); _c1 = c1;
        AxesText.Text = c1 ? "X: Re C1 · Y: Im C1 · Z: Re C2" : "X: Re C2 · Y: Im C2 · Z: Re C1";
    }

    internal void SetParameters(PhoenixState state, bool shadows)
    {
        _parameterRevision++;
        _state = state;
        _shadows = shadows;
        _marker = _c1 ? new((float)state.C1Real, (float)state.C1Imaginary, (float)state.C2Real)
            : new((float)state.C2Real, (float)state.C2Imaginary, (float)state.C1Real);
        RequestFrame();
    }

    internal void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active) RequestFrame();
        else
        {
            _revision++; _requested = false; _cts?.Cancel();
            _mouseDown = _dragging = false; MapHost.ReleaseMouseCapture();
        }
    }

    internal void ResetView() { _pose = DefaultPose; RequestFrame(); }

    private void RequestFrame()
    {
        _revision++;
        if (!_active || _disposed || _state is null || !IsLoaded) return;
        _requested = true;
        if (!_running) _ = RenderLoopAsync();
    }

    private async Task RenderLoopAsync()
    {
        _running = true;
        try
        {
            while (_requested && _active && !_disposed)
            {
                _requested = false;
                int revision = _revision;
                int parameterRevision = _parameterRevision;
                RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(MapHost);
                if (MapHost.ActualWidth < 2 || MapHost.ActualHeight < 2) break;
                var cts = new CancellationTokenSource();
                _cts = cts;
                Progress.Visibility = Visibility.Visible;
                Preparation.IsBusy = true;
                Preparation.Heading = "Подготовка среза Phoenix";
                Preparation.IsIndeterminate = false;
                Preparation.Message = "Построение объёма параметров…";
                if (_frame is null) Preparation.Visibility = Visibility.Visible;
                try
                {
                    var progress = new Progress<int>(value =>
                    {
                        if (_disposed || !ReferenceEquals(_cts, cts)) return;
                        Progress.Value = Preparation.Progress = value;
                        Preparation.Message = value < 80 ? "Построение объёма параметров…" : "Отрисовка поверхности…";
                    });
                    // Camera changes queue the latest frame; they do not restart a volume build.
                    PhoenixSliceFrame frame = await _renderer!.RenderAsync(_state!, _c1, _pose,
                        _marker, _shadows, surface.PixelWidth, surface.PixelHeight, progress, cts.Token);
                    if (cts.IsCancellationRequested || !_active || _disposed || parameterRevision != _parameterRevision) continue;
                    BitmapSource bitmap = BitmapSource.Create(frame.Width, frame.Height,
                        surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY,
                        PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap; _frame = frame; _frameRevision = revision;
                    Preparation.Visibility = Visibility.Collapsed;
                    MessageText.Text = frame.Depths.Any(d => d >= 0)
                        ? "Щелчок — выбор · перетаскивание — вращение · колесо — масштаб"
                        : "В этом срезе в области [−2; 2]³ поверхность не найдена. Измените фиксированную координату.";
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!_active || _disposed) continue;
                    Preparation.Visibility = Visibility.Visible;
                    Preparation.IsBusy = false;
                    Preparation.Heading = "Не удалось построить 3D-срез";
                    Preparation.Message = ex.Message;
                    Preparation.Detail = "Можно вернуться к выбору в 2D.";
                }
                finally
                {
                    if (ReferenceEquals(_cts, cts)) _cts = null;
                    cts.Dispose();
                    Progress.Visibility = Visibility.Collapsed;
                }
            }
        }
        finally { _running = false; }
    }

    private void Map_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true; _dragging = false;
        _start = _last = e.GetPosition(MapHost);
        MapHost.CaptureMouse(); e.Handled = true;
    }

    private void Rotate(Point point)
    {
        if (!_dragging && (point - _start).Length < 6) return;
        _dragging = true;
        Vector delta = point - _last; _last = point;
        _pose = Fractal3DCamera.Orbit(_pose, Vector3.Zero, delta.X * 0.45, delta.Y * 0.45);
        RequestFrame();
    }

    private void Map_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_mouseDown && e.LeftButton == MouseButtonState.Pressed) Rotate(e.GetPosition(MapHost));
    }

    private void Map_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        Point point = e.GetPosition(MapHost);
        Rotate(point);
        bool dragged = _dragging;
        _mouseDown = _dragging = false; MapHost.ReleaseMouseCapture();
        if (!dragged) SelectAt(point);
        e.Handled = true;
    }

    private void SelectAt(Point point)
    {
        if (_frame is not { } frame || _frameRevision != _revision)
        { MessageText.Text = "Дождитесь актуального кадра для выбора точки."; return; }
        int x = Math.Clamp((int)(point.X / MapHost.ActualWidth * frame.Width), 0, frame.Width - 1);
        int y = Math.Clamp((int)(point.Y / MapHost.ActualHeight * frame.Height), 0, frame.Height - 1);
        float depth = frame.Depths[y * frame.Width + x];
        if (depth < 0 || !float.IsFinite(depth))
        { MessageText.Text = "Под курсором нет поверхности. Поверните фигуру и щёлкните по ней."; return; }
        Vector3 ray = Fractal3DCamera.PixelRay(frame.Pose, PhoenixParameterSliceRenderer.FieldOfView,
            x + 0.5, y + 0.5, frame.Width, frame.Height);
        PointSelected?.Invoke(frame.Pose.Position + ray * depth);
    }

    private void Map_OnLostMouseCapture(object sender, MouseEventArgs e) => _mouseDown = _dragging = false;

    private void Map_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.8 :
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.08 : 1.25;
        _pose = _pose with { Distance = Math.Clamp(_pose.Distance * Math.Pow(step, -e.Delta / 120.0), 0.2, 60) };
        RequestFrame(); e.Handled = true;
    }

    private void Map_OnSizeChanged(object sender, SizeChangedEventArgs e) => RequestFrame();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; SetActive(false);
        // Wait for the device gate off the UI thread; the worker owns its snapshot and token.
        if (_renderer is { } renderer) _ = Task.Run(renderer.Dispose);
    }
}
