using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace FractalExplorerWPF.Views;

/// <summary>Параметрическое 3D-множество для выбора константы соответствующего Julia-режима.</summary>
public partial class Fractal3DConstantPickerWindow : Window
{
    private const double DefaultDistance = 4.5;
    private const double MinDistance = 3.1;
    private const double MaxDistance = 12;
    private readonly Fractal3DRenderer _renderer = new();
    private readonly Fractal3DState _state;
    private CancellationTokenSource? _renderCts;
    private int _viewRevision;
    private int _selectionRevision;
    private bool _closing;
    private bool _updatingText;
    private bool _mouseDown;
    private bool _dragging;
    private bool _rendering;
    private bool _renderingDraft;
    private bool _frameRequested;
    private bool _requestFull;
    private bool _loopAttached;
    private bool _selecting;
    private double _draftScale = 0.45;
    private byte[]? _draftBuffer;
    private byte[]? _fullBuffer;
    private WriteableBitmap? _draftBitmap;
    private WriteableBitmap? _fullBitmap;
    private double _maxDragDistance;
    private Point _dragStart;
    private Point _lastPoint;

    public (double X, double Y, double Z) SelectedConstant { get; private set; }

    public Fractal3DConstantPickerWindow(Fractal3DState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SelectedConstant = (source.JuliaCX, source.JuliaCY, source.JuliaCZ);
        bool burningShip = source.Kind == Fractal3DKind.BurningShipJulia;
        _state = Fractal3DCatalog.CreateDefaultState(
            burningShip ? Fractal3DKind.BurningShip : Fractal3DKind.Mandelbulb);
        _state.Power = source.Power;
        _state.BurningShipFormula = source.BurningShipFormula;
        _state.Iterations = source.Iterations;
        _state.Bailout = source.Bailout;
        _state.CameraDistance = DefaultDistance;
        _state.TargetX = _state.TargetY = _state.TargetZ = 0;
        _state.SoftShadows = false;
        _state.AmbientOcclusion = false;
        _state.Ssaa = 1;

        InitializeComponent();
        if (burningShip) MapTitleText.Text = "Карта Горящего корабля 3D";
        SetConstantText();
        ResetView();
        Loaded += (_, _) => RequestFrame(full: true);
    }

    /// <summary>Кадры запускаются подряд, пока камера движется; события мыши больше не откладывают рендер.</summary>
    private void RequestFrame(bool full = false)
    {
        if (!IsLoaded || _closing) return;
        _frameRequested = true;
        _requestFull = full && !_dragging;
        // Полный кадр может идти долго. Как только начинается движение, уступаем место черновику.
        if (_rendering && !_renderingDraft && !_requestFull) _renderCts?.Cancel();
        if (_loopAttached) return;
        _loopAttached = true;
        CompositionTarget.Rendering += RenderLoop_OnRendering;
    }

    private void RenderLoop_OnRendering(object? sender, EventArgs e)
    {
        if (_rendering || _selecting) return;
        if (!_frameRequested)
        {
            CompositionTarget.Rendering -= RenderLoop_OnRendering;
            _loopAttached = false;
            return;
        }
        bool full = _requestFull;
        _frameRequested = false;
        _ = RenderFrameAsync(full);
    }

    private async Task RenderFrameAsync(bool full)
    {
        _rendering = true;
        _renderingDraft = !full;
        // Пустой Image до первого кадра ещё не имеет размера; контейнер карты уже разложен.
        int width = Math.Max(1, (int)Math.Round(MapHost.ActualWidth - 2));
        int height = Math.Max(1, (int)Math.Round(MapHost.ActualHeight - 2));
        if (width < 2 || height < 2) { _rendering = false; return; }
        double scale = full ? 1 : _draftScale;
        int renderWidth = Math.Max(1, (int)Math.Round(width * scale));
        int renderHeight = Math.Max(1, (int)Math.Round(height * scale));
        Fractal3DState snapshot = _state.Clone();
        int renderedViewRevision = _viewRevision;
        int renderedSelectionRevision = _selectionRevision;
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var watch = Stopwatch.StartNew();
        bool rendered = false;
        try
        {
            Fractal3DPixels frame = await _renderer.RenderPixelsAsync(snapshot, renderWidth, renderHeight,
                full ? _fullBuffer : _draftBuffer, null, cts.Token);
            if (full) _fullBuffer = frame.Buffer;
            else _draftBuffer = frame.Buffer;
            if (frame.Completed && !cts.IsCancellationRequested && !_closing &&
                renderedSelectionRevision == _selectionRevision)
            {
                rendered = true;
                WriteableBitmap? bitmap = full ? _fullBitmap : _draftBitmap;
                if (bitmap is null || bitmap.PixelWidth != renderWidth || bitmap.PixelHeight != renderHeight)
                    bitmap = new WriteableBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Bgra32, null);
                if (full) _fullBitmap = bitmap;
                else _draftBitmap = bitmap;
                bitmap.WritePixels(new Int32Rect(0, 0, renderWidth, renderHeight),
                    frame.Buffer, renderWidth * 4, 0);
                RenderOptions.SetBitmapScalingMode(PreviewImage,
                    full ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
                PreviewImage.Source = bitmap;
                if (full && renderedViewRevision == _viewRevision) UpdateStatus();

                if (!full && _dragging)
                {
                    double aimed = Math.Clamp(_draftScale * Math.Sqrt(33 / Math.Max(watch.Elapsed.TotalMilliseconds, 1)),
                        0.22, 0.75);
                    _draftScale = Math.Clamp(Math.Round((_draftScale * 0.7 + aimed * 0.3) * 20) / 20, 0.22, 0.75);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing) StatusText.Text = $"3D-карта недоступна: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
            _rendering = false;
            if (!_closing && !_selecting && !_frameRequested && renderedViewRevision != _viewRevision)
                RequestFrame(full: !_dragging);
            else if (!_closing && !_selecting && !_frameRequested && rendered && !full && !_dragging)
                RequestFrame(full: true);
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
        RequestFrame(full: true);
    }

    private Vector3 ConstantVector => new((float)SelectedConstant.X, (float)SelectedConstant.Y, (float)SelectedConstant.Z);

    private void UpdateMarker()
    {
        Vector3 marker = ConstantVector;
        _state.PickerMarker = new Vector4(marker, 0.065f);
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
        _selectionRevision++;
        UpdateMarker();
        UpdateStatus();
        RequestFrame();
    }

    private void MapHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _mouseDown = true;
        _dragging = false;
        _dragStart = _lastPoint = e.GetPosition(MapHost);
        _maxDragDistance = 0;
        MapHost.CaptureMouse();
        e.Handled = true;
    }

    private void MapHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(MapHost);
        _maxDragDistance = Math.Max(_maxDragDistance, (current - _dragStart).Length);
        if (!_dragging && _maxDragDistance < 6) return;
        _dragging = true;
        Vector delta = current - _lastPoint;
        _lastPoint = current;
        RotateBy(delta);
        e.Handled = true;
    }

    private void RotateBy(Vector delta)
    {
        if (delta.LengthSquared < 0.01) return;
        Fractal3DPose rotated = Fractal3DCamera.Orbit(Fractal3DCamera.Pose(_state), Vector3.Zero,
            delta.X * 0.45, delta.Y * 0.45);
        Fractal3DCamera.Apply(rotated, _state);
        _viewRevision++;
        RequestFrame();
    }

    private async void MapHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_mouseDown) return;
        Point point = e.GetPosition(MapHost);
        _maxDragDistance = Math.Max(_maxDragDistance, (point - _dragStart).Length);
        bool wasDragging = _dragging || _maxDragDistance >= 6;
        if (wasDragging) RotateBy(point - _lastPoint);
        _mouseDown = _dragging = false;
        MapHost.ReleaseMouseCapture();
        if (wasDragging) RequestFrame(full: true);
        else await SelectAtAsync(point);
        e.Handled = true;
    }

    private void MapHost_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        bool wasDragging = _dragging || _maxDragDistance >= 6;
        _mouseDown = _dragging = false;
        if (wasDragging) RequestFrame(full: true);
    }

    private async Task SelectAtAsync(Point point)
    {
        int width = Math.Max(1, (int)Math.Round(MapHost.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(MapHost.ActualHeight));
        Fractal3DState snapshot = _state.Clone();
        int revision = _viewRevision;
        // Зонд 1×1 не должен ждать длинного полного кадра от предыдущего вида.
        _selecting = true;
        _renderCts?.Cancel();
        StatusText.Text = "Поиск точки поверхности...";
        bool changed = false;
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
            _selectionRevision++;
            SetConstantText();
            UpdateMarker();
            changed = true;
        }
        catch (Exception ex)
        {
            if (!_closing) StatusText.Text = $"Не удалось выбрать C: {ex.Message}";
        }
        finally
        {
            _selecting = false;
            if (!_closing) RequestFrame(full: !changed);
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
        RequestFrame();
        e.Handled = true;
    }

    private void MapHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RequestFrame(full: true);
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
        if (_loopAttached) CompositionTarget.Rendering -= RenderLoop_OnRendering;
        _renderCts?.Cancel();
        if (_mouseDown) MapHost.ReleaseMouseCapture();
        Task.Run(_renderer.Dispose);
    }
}
