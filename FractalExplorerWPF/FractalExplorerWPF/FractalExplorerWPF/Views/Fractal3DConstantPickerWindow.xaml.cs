using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace FractalExplorerWPF.Views;

/// <summary>Параметрический Мандельбульб для выбора трёхмерной константы Жюлиабульба.</summary>
public partial class Fractal3DConstantPickerWindow : Window
{
    private const double DefaultDistance = 4.5;
    private const double MinDistance = 3.1;
    private const double MaxDistance = 12;
    private readonly Fractal3DRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly Fractal3DState _state;
    private CancellationTokenSource? _renderCts;
    private int _renderVersion;
    private int _viewRevision;
    private bool _closing;
    private bool _updatingText;
    private bool _mouseDown;
    private bool _dragging;
    private Point _dragStart;
    private Point _lastPoint;

    public (double X, double Y, double Z) SelectedConstant { get; private set; }

    public Fractal3DConstantPickerWindow(Fractal3DState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SelectedConstant = (source.JuliaCX, source.JuliaCY, source.JuliaCZ);
        _state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        _state.Power = source.Power;
        _state.Iterations = source.Iterations;
        _state.Bailout = source.Bailout;
        _state.CameraDistance = DefaultDistance;
        _state.TargetX = _state.TargetY = _state.TargetZ = 0;
        _state.SoftShadows = false;
        _state.AmbientOcclusion = false;
        _state.Ssaa = 1;

        InitializeComponent();
        _renderTimer.Tick += RenderTimer_OnTick;
        SetConstantText();
        ResetView();
        Loaded += (_, _) => ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded || _closing) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private async void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        int width = Math.Max(1, (int)Math.Round(PreviewImage.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(PreviewImage.ActualHeight));
        if (width < 2 || height < 2) return;
        double scale = _dragging ? 0.55 : 1;
        int renderWidth = Math.Max(1, (int)Math.Round(width * scale));
        int renderHeight = Math.Max(1, (int)Math.Round(height * scale));
        Fractal3DState snapshot = _state.Clone();
        int version = ++_renderVersion;
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        StatusText.Text = "Рендер 3D-карты...";
        try
        {
            var bitmap = await _renderer.RenderAsync(snapshot, renderWidth, renderHeight, null, cts.Token);
            if (!cts.IsCancellationRequested && version == _renderVersion && !_closing)
            {
                PreviewImage.Source = bitmap;
                UpdateMarker();
                MarkerLayer.Opacity = 0.35;
                UpdateStatus();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing && version == _renderVersion) StatusText.Text = $"3D-карта недоступна: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
        }
    }

    private void ResetView()
    {
        Vector3 marker = ConstantVector;
        if (marker.LengthSquared() < 1e-8f)
        {
            _state.CameraYaw = 35;
            _state.CameraPitch = 18;
        }
        else
        {
            // Камера остаётся на орбите вокруг начала координат, но оказывается со стороны C.
            (double yaw, double pitch) = Fractal3DCamera.Angles(marker);
            _state.CameraYaw = yaw;
            _state.CameraPitch = pitch;
        }
        _state.CameraRoll = 0;
        _state.CameraDistance = Math.Clamp(DefaultDistance + Math.Max(0, marker.Length() - 1.5) * 1.4,
            DefaultDistance, MaxDistance);
        _state.TargetX = _state.TargetY = _state.TargetZ = 0;
        _viewRevision++;
        UpdateMarker();
        ScheduleRender();
    }

    private Vector3 ConstantVector => new((float)SelectedConstant.X, (float)SelectedConstant.Y, (float)SelectedConstant.Z);

    private void UpdateMarker()
    {
        Vector3 marker = ConstantVector;
        _state.PickerMarker = new Vector4(marker, 0.065f);
        Fractal3DConstantMarker.Draw(MarkerLayer, _state, marker);
        MarkerLayer.Opacity = 1;
    }

    private void UpdateStatus() => StatusText.Text =
        $"C = ({SelectedConstant.X:G6}; {SelectedConstant.Y:G6}; {SelectedConstant.Z:G6}). " +
        "Вращение и масштаб сохраняют фигуру в центре кадра.";

    private void SetConstantText()
    {
        _updatingText = true;
        XBox.Text = SelectedConstant.X.ToString("G9", CultureInfo.InvariantCulture);
        YBox.Text = SelectedConstant.Y.ToString("G9", CultureInfo.InvariantCulture);
        ZBox.Text = SelectedConstant.Z.ToString("G9", CultureInfo.InvariantCulture);
        _updatingText = false;
        UpdateStatus();
    }

    private static bool TryRead(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) &&
        double.IsFinite(value) && value >= -8 && value <= 8;

    private void ConstantText_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText || !IsInitialized) return;
        if (!TryRead(XBox.Text, out double x) || !TryRead(YBox.Text, out double y) ||
            !TryRead(ZBox.Text, out double z)) return;
        SelectedConstant = (x, y, z);
        UpdateMarker();
        UpdateStatus();
        ScheduleRender();
    }

    private void MapHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _dragging = false;
        _dragStart = _lastPoint = e.GetPosition(MapHost);
        MapHost.CaptureMouse();
        e.Handled = true;
    }

    private void MapHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(MapHost);
        if (!_dragging && (current - _dragStart).Length < 4) return;
        _dragging = true;
        Vector delta = current - _lastPoint;
        _lastPoint = current;
        Fractal3DPose rotated = Fractal3DCamera.Orbit(Fractal3DCamera.Pose(_state), Vector3.Zero,
            delta.X * 0.45, delta.Y * 0.45);
        Fractal3DCamera.Apply(rotated, _state);
        _viewRevision++;
        UpdateMarker();
        ScheduleRender();
        e.Handled = true;
    }

    private async void MapHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        Point point = e.GetPosition(MapHost);
        bool wasDragging = _dragging;
        _mouseDown = _dragging = false;
        MapHost.ReleaseMouseCapture();
        if (wasDragging) ScheduleRender();
        else await SelectAtAsync(point);
        e.Handled = true;
    }

    private void MapHost_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = _dragging = false;
        ScheduleRender();
    }

    private async Task SelectAtAsync(Point point)
    {
        int width = Math.Max(1, (int)Math.Round(MapHost.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(MapHost.ActualHeight));
        Fractal3DState snapshot = _state.Clone();
        int revision = _viewRevision;
        StatusText.Text = "Поиск точки поверхности...";
        try
        {
            double distance = await _renderer.ProbeDistanceAsync(snapshot, point.X, point.Y,
                width, height, CancellationToken.None);
            if (_closing || revision != _viewRevision) return;
            if (double.IsNaN(distance))
            {
                StatusText.Text = "Под курсором нет поверхности. Поверните фигуру и щёлкните по ней.";
                return;
            }
            Fractal3DPose pose = Fractal3DCamera.Pose(snapshot);
            Vector3 ray = Fractal3DCamera.PixelRay(snapshot, point.X, point.Y, width, height);
            Vector3 hit = pose.Position + ray * (float)distance;
            SelectedConstant = (hit.X, hit.Y, hit.Z);
            SetConstantText();
            UpdateMarker();
        }
        catch (Exception ex)
        {
            if (!_closing) StatusText.Text = $"Не удалось выбрать C: {ex.Message}";
        }
    }

    private void MapHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 1.8 :
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.08 : 1.25;
        double notches = e.Delta / 120.0;
        _state.CameraDistance = Math.Clamp(_state.CameraDistance * Math.Pow(step, -notches),
            MinDistance, MaxDistance);
        _viewRevision++;
        UpdateMarker();
        ScheduleRender();
        e.Handled = true;
    }

    private void MapHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMarker();
        ScheduleRender();
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e) => ResetView();

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(XBox.Text, out double x) || !TryRead(YBox.Text, out double y) ||
            !TryRead(ZBox.Text, out double z))
        {
            MessageBox.Show(this, "Все координаты C должны быть числами от −8 до 8.",
                "Константа C", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedConstant = (x, y, z);
        DialogResult = true;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _renderTimer.Stop();
        _renderCts?.Cancel();
        if (_mouseDown) MapHost.ReleaseMouseCapture();
        Task.Run(_renderer.Dispose);
    }
}
