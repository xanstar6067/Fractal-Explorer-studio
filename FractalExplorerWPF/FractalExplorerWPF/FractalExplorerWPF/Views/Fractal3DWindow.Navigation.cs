using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Навигация окна трёхмерного фрактала. Правая кнопка вращает камеру — вокруг фрактала или как
/// игровая камера, левая и средняя перемещают. Колесо без правой кнопки приближает к цели (по
/// желанию — к точке под курсором), с зажатой правой едет вдоль оси объектива, как в редакторе
/// уровней; там же работает полёт на WASD/QE. Шаг движения берётся от расстояния до поверхности
/// под курсором, которое измеряет зонд <see cref="Fractal3DRenderer.ProbeDistanceAsync"/>.
/// Анимация камеры — автовращение, инерция броска и перелёт к выбранной точке — живёт в
/// <see cref="AdvanceAnimation"/> и двигается кадровым циклом живого превью.
/// </summary>
public partial class Fractal3DWindow
{
    private const double RotationDegreesPerPixel = 0.35;

    /// <summary>Затухание броска, 1/с: за это время скорость падает в e раз.</summary>
    private const double InertiaDamping = 7;

    /// <summary>°/с, ниже которых инерция считается остановившейся.</summary>
    private const double MinInertiaSpeed = 4;

    private const double MaxInertiaSpeed = 900;

    /// <summary>Бросок учитывается, только если мышь двигалась прямо перед отпусканием.</summary>
    private const double FlingWindowMs = 90;

    private const double ProbeIntervalMs = 120;
    private const double UiSyncIntervalMs = 90;
    private const double TransitionSeconds = 0.35;
    private const double MaxCameraDistance = 1e5;

    /// <summary>Мировых единиц в секунду на единицу опорного расстояния при полёте с клавиатуры.</summary>
    private const double FlightSpeed = 1.2;

    private bool _rotating;
    private bool _panning;
    private Point _lastPoint;
    private Point _cursorPoint = new(double.NaN, double.NaN);
    private double _lastDragMs = double.NegativeInfinity;
    private double _lastUiSyncMs = double.NegativeInfinity;
    private double _yawVelocity;
    private double _pitchVelocity;
    private readonly HashSet<Key> _flightKeys = [];

    private double _surfaceDistance = double.NaN;
    private double _lastProbeMs = double.NegativeInfinity;
    private bool _probeBusy;
    private bool _probeSupported = true;

    private CameraTransition? _transition;

    private bool IsInteracting => _rotating || _panning;

    private bool HasInertia =>
        Math.Abs(_yawVelocity) > MinInertiaSpeed || Math.Abs(_pitchVelocity) > MinInertiaSpeed;

    /// <summary>Движется ли камера: от этого зависят черновое качество и лесенка уточнения.</summary>
    private bool IsMoving => IsInteracting || HasInertia || _transition is not null || AutoRotateActive;

    /// <summary>Автовращение с нулевой скоростью камеру не двигает и уточнению не мешает.</summary>
    private bool AutoRotateActive =>
        AutoRotateBox.IsChecked == true &&
        TryReadDouble(AutoRotateSpeedBox.Text, out double speed) && speed != 0;

    /// <summary>Точка, по которой меряется расстояние до поверхности: курсор или центр кадра.</summary>
    private Point ProbePoint => double.IsNaN(_cursorPoint.X)
        ? new Point(SavePreviewLayer.ActualWidth / 2, SavePreviewLayer.ActualHeight / 2)
        : _cursorPoint;

    #region Анимация камеры

    /// <summary>
    /// Шаг анимации кадрового цикла: перелёт к точке, инерция броска, автовращение и полёт с
    /// клавиатуры. Возвращает, изменилась ли камера.
    /// </summary>
    private bool AdvanceAnimation(double seconds)
    {
        if (seconds <= 0) return false;
        bool changed = AdvanceTransition(seconds);

        if (!IsInteracting && _transition is null && RotationInertiaBox.IsChecked == true && HasInertia)
        {
            ApplyRotation(_yawVelocity * seconds, _pitchVelocity * seconds);
            double damping = Math.Exp(-InertiaDamping * seconds);
            _yawVelocity *= damping;
            _pitchVelocity *= damping;
            changed = true;
        }
        else if (!IsInteracting)
        {
            _yawVelocity = 0;
            _pitchVelocity = 0;
        }

        if (AutoRotateActive && !_rotating && _transition is null &&
            TryReadDouble(AutoRotateSpeedBox.Text, out double speed))
        {
            _yaw += speed * seconds;
            changed = true;
        }

        if (_rotating && _flightKeys.Count > 0 && FlyStep(seconds))
        {
            // В полёте курсор стоит на месте, поэтому расстояние до поверхности обновляем сами.
            RequestProbe(ProbePoint);
            changed = true;
        }

        if (changed) AfterCameraChanged();
        return changed;
    }

    private bool AdvanceTransition(double seconds)
    {
        if (_transition is not { } transition) return false;

        transition.Elapsed += seconds;
        double progress = Math.Clamp(transition.Elapsed / TransitionSeconds, 0, 1);
        double eased = progress * progress * (3 - 2 * progress);

        _yaw = transition.FromYaw + (transition.ToYaw - transition.FromYaw) * eased;
        _pitch = Math.Clamp(transition.FromPitch + (transition.ToPitch - transition.FromPitch) * eased,
            Fractal3DCamera.MinPitch, Fractal3DCamera.MaxPitch);
        // Расстояние ведётся геометрически: приближение к поверхности идёт равномерно на глаз.
        _distance = transition.FromDistance *
            Math.Pow(transition.ToDistance / transition.FromDistance, eased);
        _target = Vector3.Lerp(transition.FromTarget, transition.ToTarget, (float)eased);

        if (progress >= 1) _transition = null;
        return true;
    }

    /// <summary>Плавный перелёт камеры к новому виду вместо скачка.</summary>
    private void BeginTransition(double yaw, double pitch, double distance, Vector3 target)
    {
        // Кратчайший поворот: без этого «сброс вида» мог бы прокрутить почти полный круг.
        while (yaw - _yaw > 180) yaw -= 360;
        while (yaw - _yaw < -180) yaw += 360;

        _yawVelocity = 0;
        _pitchVelocity = 0;
        _transition = new CameraTransition
        {
            FromYaw = _yaw,
            ToYaw = yaw,
            FromPitch = _pitch,
            ToPitch = Math.Clamp(pitch, Fractal3DCamera.MinPitch, Fractal3DCamera.MaxPitch),
            FromDistance = Math.Max(_distance, Fractal3DCamera.MinDistance),
            ToDistance = Math.Clamp(distance, Fractal3DCamera.MinDistance, MaxCameraDistance),
            FromTarget = _target,
            ToTarget = target
        };
        AttachLoop();
        RequestFrame(FrameQuality.Draft);
    }

    private sealed class CameraTransition
    {
        public double FromYaw { get; init; }
        public double ToYaw { get; init; }
        public double FromPitch { get; init; }
        public double ToPitch { get; init; }
        public double FromDistance { get; init; }
        public double ToDistance { get; init; }
        public Vector3 FromTarget { get; init; }
        public Vector3 ToTarget { get; init; }
        public double Elapsed { get; set; }
    }

    #endregion

    #region Кнопки мыши

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _ = FlyToCursorAsync(e.GetPosition(SavePreviewLayer));
            e.Handled = true;
            return;
        }
        BeginInteraction(e.GetPosition(CanvasHost), rotate: false);
    }

    private void CanvasHost_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginInteraction(e.GetPosition(CanvasHost), rotate: true);
        e.Handled = true;
    }

    private void CanvasHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) BeginInteraction(e.GetPosition(CanvasHost), rotate: false);
    }

    private void BeginInteraction(Point point, bool rotate)
    {
        _transition = null;
        _yawVelocity = 0;
        _pitchVelocity = 0;
        _rotating = rotate;
        _panning = !rotate;
        _lastPoint = point;
        _lastDragMs = _clock.Elapsed.TotalMilliseconds;
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = rotate ? Cursors.ScrollAll : Cursors.SizeAll;
        AttachLoop();
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        _cursorPoint = e.GetPosition(SavePreviewLayer);
        if (!IsInteracting)
        {
            RequestProbe(_cursorPoint);
            return;
        }

        Point current = e.GetPosition(CanvasHost);
        double deltaX = current.X - _lastPoint.X;
        double deltaY = current.Y - _lastPoint.Y;
        if (deltaX == 0 && deltaY == 0) return;
        _lastPoint = current;

        double now = _clock.Elapsed.TotalMilliseconds;
        if (_rotating)
        {
            double dragYaw = deltaX * RotationDegreesPerPixel;
            double dragPitch = deltaY * RotationDegreesPerPixel;
            ApplyRotation(dragYaw, dragPitch);
            TrackInertia(dragYaw, dragPitch, now);
        }
        else
        {
            Fractal3DState state = CaptureCameraOnly();
            Fractal3DCameraBasis camera = Fractal3DCamera.Build(state);
            double unit = Fractal3DCamera.WorldUnitsPerPixel(state, CanvasHost.ActualHeight);
            _target -= camera.Right * (float)(deltaX * unit);
            _target += camera.Up * (float)(deltaY * unit);
        }

        _lastDragMs = now;
        AfterCameraChanged();
    }

    private void CanvasHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsInteracting) return;
        if (_rotating && e.ChangedButton != MouseButton.Right) return;
        if (_panning && e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;

        bool wasRotating = _rotating;
        _rotating = false;
        _panning = false;
        _flightKeys.Clear();
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;

        bool fling = wasRotating && RotationInertiaBox.IsChecked == true &&
                     _clock.Elapsed.TotalMilliseconds - _lastDragMs < FlingWindowMs;
        if (!fling)
        {
            _yawVelocity = 0;
            _pitchVelocity = 0;
        }

        // Полный кадр попросит сам цикл, когда увидит, что движение кончилось: отпускание кнопки
        // ещё не остановка, если включена инерция.
        SyncCameraUi(immediate: true);
    }

    private void ApplyRotation(double dragYaw, double dragPitch)
    {
        Fractal3DOrbit rotated = Fractal3DCamera.Rotate(
            new Fractal3DOrbit(_yaw, _pitch, _distance, _target), SelectedRotationAnchor, dragYaw, dragPitch);
        _yaw = rotated.Yaw;
        _pitch = rotated.Pitch;
        _target = rotated.Target;
    }

    private void TrackInertia(double dragYaw, double dragPitch, double now)
    {
        double seconds = Math.Clamp((now - _lastDragMs) / 1000, 0.004, 0.1);
        double yawSpeed = Math.Clamp(dragYaw / seconds, -MaxInertiaSpeed, MaxInertiaSpeed);
        double pitchSpeed = Math.Clamp(dragPitch / seconds, -MaxInertiaSpeed, MaxInertiaSpeed);
        _yawVelocity = _yawVelocity * 0.6 + yawSpeed * 0.4;
        _pitchVelocity = _pitchVelocity * 0.6 + pitchSpeed * 0.4;
    }

    #endregion

    #region Колесо и полёт

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        int notches = e.Delta / 120;
        if (notches == 0) notches = Math.Sign(e.Delta);
        if (notches == 0) return;

        if (_rotating) DollyCamera(notches);
        else ZoomCamera(notches, e.GetPosition(SavePreviewLayer));

        AfterCameraChanged();
        e.Handled = true;
    }

    /// <summary>Обычное приближение: меняется радиус орбиты, точка наблюдения остаётся на месте.</summary>
    private void ZoomCamera(int notches, Point point)
    {
        double step = Math.Pow(WheelStep(), notches);
        double distance = Math.Clamp(_distance / step, Fractal3DCamera.MinDistance, MaxCameraDistance);

        if (ZoomToCursorBox.IsChecked == true)
        {
            // Точка под курсором остаётся на месте: цель подтягивается к ней в той же пропорции,
            // в какой сократилось расстояние.
            Fractal3DState state = CaptureCameraOnly();
            Fractal3DCameraBasis camera = Fractal3DCamera.Build(state);
            double width = Math.Max(SavePreviewLayer.ActualWidth, 1);
            double height = Math.Max(SavePreviewLayer.ActualHeight, 1);
            double unit = Fractal3DCamera.WorldUnitsPerPixel(state, height);
            Vector3 pivot = _target +
                camera.Right * (float)((point.X - width / 2) * unit) -
                camera.Up * (float)((point.Y - height / 2) * unit);
            float keep = (float)(distance / Math.Max(_distance, Fractal3DCamera.MinDistance));
            _target = pivot + (_target - pivot) * keep;
        }

        _distance = distance;
    }

    /// <summary>
    /// Игровое приближение при зажатой правой кнопке: камера вместе с точкой наблюдения едет вдоль
    /// оси объектива, поэтому внутрь фрактала можно влететь. Шаг — доля расстояния до поверхности
    /// под курсором, так что вплотную он мельчает сам и сквозь поверхность не проскочить.
    /// </summary>
    private void DollyCamera(int notches)
    {
        double factor = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 0.6
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.06
            : 0.25;
        Vector3 forward = -Fractal3DCamera.Direction(_yaw, _pitch);
        _target += forward * (float)(MovementReference() * factor * notches);
        RequestProbe(ProbePoint, force: true);
    }

    private static double WheelStep() =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.6
        : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.03
        : 1.15;

    private static bool IsFlightKey(Key key) =>
        key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E;

    private bool FlyStep(double seconds)
    {
        Fractal3DCameraBasis camera = Fractal3DCamera.Build(CaptureCameraOnly());
        Vector3 move = Vector3.Zero;
        if (_flightKeys.Contains(Key.W)) move += camera.Forward;
        if (_flightKeys.Contains(Key.S)) move -= camera.Forward;
        if (_flightKeys.Contains(Key.D)) move += camera.Right;
        if (_flightKeys.Contains(Key.A)) move -= camera.Right;
        if (_flightKeys.Contains(Key.E)) move += camera.Up;
        if (_flightKeys.Contains(Key.Q)) move -= camera.Up;
        if (move.LengthSquared() < 1e-12f) return false;

        double modifier = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 0.25
            : 1;
        _target += Vector3.Normalize(move) *
            (float)(MovementReference() * FlightSpeed * modifier * seconds);
        return true;
    }

    /// <summary>
    /// Опорное расстояние для шага движения: до поверхности под курсором, если зонд её нашёл,
    /// иначе радиус орбиты.
    /// </summary>
    private double MovementReference() =>
        double.IsNaN(_surfaceDistance)
            ? Math.Max(_distance, Fractal3DCamera.MinDistance)
            : Math.Clamp(_surfaceDistance, Fractal3DCamera.MinDistance, MaxCameraDistance);

    #endregion

    #region Зонд поверхности

    /// <summary>Измеряет расстояние до поверхности под курсором, не чаще чем раз в <see cref="ProbeIntervalMs"/>.</summary>
    private void RequestProbe(Point point, bool force = false)
    {
        if (_isClosing || _probeBusy || !_probeSupported) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (!force && now - _lastProbeMs < ProbeIntervalMs) return;

        _lastProbeMs = now;
        _probeBusy = true;
        _ = ProbeAsync(point);
    }

    private async Task ProbeAsync(Point point)
    {
        try
        {
            if (!TryCaptureState(out Fractal3DState state)) return;
            (double x, double y, int width, int height) = ToFramePixel(point);
            if (x < 0 || y < 0 || x > width || y > height) return;
            _surfaceDistance = await _renderer.ProbeDistanceAsync(
                state, x, y, width, height, CancellationToken.None);
            if (!_isClosing) UpdateCameraText();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Зонд — удобство, а не обязательная часть кадра: если устройство его не потянуло,
            // шаг движения дальше считается от радиуса орбиты.
            _surfaceDistance = double.NaN;
            _probeSupported = false;
        }
        finally
        {
            _probeBusy = false;
        }
    }

    /// <summary>Двойной щелчок: перелёт к точке поверхности под курсором.</summary>
    private async Task FlyToCursorAsync(Point point)
    {
        if (!_probeSupported || !TryCaptureState(out Fractal3DState state)) return;
        (double x, double y, int width, int height) = ToFramePixel(point);

        double distance;
        try
        {
            distance = await _renderer.ProbeDistanceAsync(state, x, y, width, height, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _probeSupported = false;
            StatusText.Text = exception.Message;
            return;
        }

        if (double.IsNaN(distance))
        {
            StatusText.Text = "Под курсором нет поверхности: не к чему лететь.";
            return;
        }

        Vector3 position = Fractal3DCamera.Position(state);
        Vector3 hit = position + Fractal3DCamera.PixelRay(state, x, y, width, height) * (float)distance;
        (double yaw, double pitch) = Fractal3DCamera.Angles(position - hit);
        _surfaceDistance = distance;
        BeginTransition(yaw, pitch, distance, hit);
        StatusText.Text = $"Перелёт к точке на расстоянии {distance:G4}.";
    }

    private (double X, double Y, int Width, int Height) ToFramePixel(Point point)
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        return (point.X / Math.Max(surface.LogicalWidth, 1) * surface.PixelWidth,
            point.Y / Math.Max(surface.LogicalHeight, 1) * surface.PixelHeight,
            surface.PixelWidth, surface.PixelHeight);
    }

    /// <summary>
    /// Состояние для зонда: если в поле параметров сейчас недописанное значение, берётся последнее
    /// посчитанное состояние с текущей камерой.
    /// </summary>
    private bool TryCaptureState(out Fractal3DState state)
    {
        try
        {
            state = CaptureState("probe");
            _lastGoodState = state;
            return true;
        }
        catch (InvalidOperationException)
        {
            if (_lastGoodState is null)
            {
                state = null!;
                return false;
            }
            state = _lastGoodState.Clone();
            state.CameraYaw = _yaw;
            state.CameraPitch = _pitch;
            state.CameraDistance = _distance;
            state.TargetX = _target.X;
            state.TargetY = _target.Y;
            state.TargetZ = _target.Z;
            state.FieldOfView = _fieldOfView;
            return true;
        }
    }

    #endregion

    #region Панель навигации

    private void Navigation_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi) return;
        if (AutoRotateBox.IsChecked == true) AttachLoop();
        UpdateCameraText();
    }

    private void AfterCameraChanged()
    {
        _yaw -= Math.Floor((_yaw + 180) / 360) * 360;
        SyncCameraUi(immediate: !IsMoving);
        RequestFrame(FrameQuality.Draft);
    }

    /// <summary>
    /// Поля камеры обновляются не чаще <see cref="UiSyncIntervalMs"/>: во время движения полная
    /// перерисовка семи полей на каждом кадре стоила бы дороже самого кадра.
    /// </summary>
    private void SyncCameraUi(bool immediate)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (!immediate && now - _lastUiSyncMs < UiSyncIntervalMs) return;
        _lastUiSyncMs = now;

        _updatingUi = true;
        SyncCameraBoxes();
        _updatingUi = false;
        UpdateCameraText();
    }

    #endregion
}
