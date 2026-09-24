using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Навигация окна трёхмерного фрактала: CAD и игровой режим. В CAD левая кнопка вращает трекболом
/// вокруг точки поверхности, за которую схватили; по фону — поворачивает взгляд на месте. Правая
/// сдвигает картинку так, что схваченная точка идёт за курсором. Средняя влево-вправо кренит
/// камеру вокруг оси взгляда, вверх-вниз наклоняет взгляд на месте (тангаж). Колесо приближает к
/// точке поверхности под курсором на долю расстояния до неё, поэтому сквозь поверхность не
/// проскочить. Двойной щелчок левой — перелёт к точке, двойной щелчок средней — выровнять горизонт. Точку под курсором находит зонд
/// <see cref="Fractal3DRenderer.ProbeDistanceAsync"/>. Анимация камеры — автовращение, инерция
/// броска, доводка колеса и перелёты — живёт в <see cref="AdvanceAnimation"/> и двигается кадровым
/// циклом живого превью.
/// </summary>
public partial class Fractal3DWindow
{
    private const double RotationDegreesPerPixel = 0.35;
    private const double RollDegreesPerPixel = 0.35;
    private const double GameRollDegreesPerSecond = 75;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

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

    private enum DragMode
    {
        None,

        /// <summary>Левая кнопка нажата, но зонд ещё не сказал, поверхность под ней или фон.</summary>
        PendingRotate,

        Orbit,
        Look,
        Pan,
        Roll
    }

    private DragMode _drag;
    private MouseButton _dragButton;
    private Point _lastPoint;
    private Point _gameCursorScreen;
    private bool _gameLookCaptured;
    private Point _cursorPoint = new(double.NaN, double.NaN);
    private Vector3 _pivot;
    private double _panUnit;
    private double _pendingDragX;
    private double _pendingDragY;
    private double _lastDragMs = double.NegativeInfinity;
    private double _lastUiSyncMs = double.NegativeInfinity;

    private double _spinX;
    private double _spinY;
    private Vector3? _spinPivot;

    /// <summary>
    /// Растёт при каждом движении камеры, кроме колеса: точка под курсором, измеренная до него,
    /// уже не та. Колесо приближает вдоль луча через курсор, поэтому эта точка остаётся под ним.
    /// </summary>
    private int _viewRevision;

    private double _surfaceDistance = double.NaN;
    private bool _hitKnown;
    private Vector3? _cursorHit;
    private Point _hitPoint;
    private int _hitRevision;

    private double _lastProbeMs = double.NegativeInfinity;
    private bool _probeBusy;
    private bool _probeSupported = true;
    private Point? _probeWanted;
    private DispatcherTimer? _probeTimer;

    private CameraTransition? _transition;
    private readonly Fractal3DZoomGlide _zoom = new();

    private Line[]? _axisLines;
    private TextBlock[]? _axisLabels;

    private bool IsInteracting => _drag != DragMode.None;

    private bool HasInertia => Math.Abs(_spinX) > MinInertiaSpeed || Math.Abs(_spinY) > MinInertiaSpeed;

    /// <summary>Движется ли камера: от этого зависят черновое качество и лесенка уточнения.</summary>
    private bool IsMoving =>
        IsInteracting || HasInertia || _transition is not null || AutoRotateActive ||
        _zoom.IsActive(_distance) || HasGameInput;

    private Fractal3DNavigationMode SelectedNavigationMode => NavigationModeBox.SelectedIndex == 1
        ? Fractal3DNavigationMode.Game : Fractal3DNavigationMode.Cad;

    private bool GameKeyboardActive => SelectedNavigationMode == Fractal3DNavigationMode.Game &&
        IsActive && CanvasHost.IsKeyboardFocusWithin;

    private bool HasGameInput => GameKeyboardActive &&
        (Keyboard.IsKeyDown(Key.W) || Keyboard.IsKeyDown(Key.A) || Keyboard.IsKeyDown(Key.S) ||
         Keyboard.IsKeyDown(Key.D) || Keyboard.IsKeyDown(Key.Q) || Keyboard.IsKeyDown(Key.E));

    /// <summary>Автовращение с нулевой скоростью камеру не двигает и уточнению не мешает.</summary>
    private bool AutoRotateActive =>
        SelectedNavigationMode == Fractal3DNavigationMode.Cad && AutoRotateBox.IsChecked == true &&
        TryReadDouble(AutoRotateSpeedBox.Text, out double speed) && speed != 0;

    /// <summary>Точка, по которой меряется расстояние до поверхности: курсор или центр кадра.</summary>
    private Point ProbePoint => double.IsNaN(_cursorPoint.X)
        ? new Point(SavePreviewLayer.ActualWidth / 2, SavePreviewLayer.ActualHeight / 2)
        : _cursorPoint;

    /// <summary>Новое положение камеры от вращения, сдвига или перелёта: точка под курсором устарела.</summary>
    private void MoveCamera(Fractal3DPose pose)
    {
        Pose = pose;
        InvalidateCursorHit();
    }

    private void InvalidateCursorHit() => _viewRevision++;

    /// <summary>Загрузка состояния и ручной ввод камеры гасят любое её движение.</summary>
    private void StopCameraMotion()
    {
        _transition = null;
        _spinX = 0;
        _spinY = 0;
        _zoom.Clear();
        InvalidateCursorHit();
    }

    #region Анимация камеры

    /// <summary>
    /// Шаг анимации кадрового цикла: перелёт, доводка колеса, инерция броска и автовращение.
    /// Возвращает, изменилась ли камера.
    /// </summary>
    private bool AdvanceAnimation(double seconds)
    {
        if (seconds <= 0) return false;
        bool changed = AdvanceTransition(seconds) | AdvanceZoom(seconds) | AdvanceGameMovement(seconds);

        if (!IsInteracting && _transition is null && RotationInertiaBox.IsChecked == true && HasInertia)
        {
            ApplyRotation(_spinPivot, _spinX * seconds, _spinY * seconds);
            double damping = Math.Exp(-InertiaDamping * seconds);
            _spinX *= damping;
            _spinY *= damping;
            changed = true;
        }
        else if (!IsInteracting)
        {
            _spinX = 0;
            _spinY = 0;
        }

        if (AutoRotateActive && !IsInteracting && _transition is null &&
            TryReadDouble(AutoRotateSpeedBox.Text, out double speed))
        {
            // Поворотный стол вокруг мировой вертикали через точку наблюдения.
            MoveCamera(Fractal3DCamera.RotateAround(Pose, _target,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(speed * seconds * Math.PI / 180))));
            changed = true;
        }

        if (changed) AfterCameraChanged();
        return changed;
    }

    private bool AdvanceGameMovement(double seconds)
    {
        if (!GameKeyboardActive) return false;
        int forward = (Keyboard.IsKeyDown(Key.W) ? 1 : 0) - (Keyboard.IsKeyDown(Key.S) ? 1 : 0);
        int right = (Keyboard.IsKeyDown(Key.D) ? 1 : 0) - (Keyboard.IsKeyDown(Key.A) ? 1 : 0);
        int roll = (Keyboard.IsKeyDown(Key.E) ? 1 : 0) - (Keyboard.IsKeyDown(Key.Q) ? 1 : 0);
        if (forward == 0 && right == 0 && roll == 0) return false;

        Fractal3DPose pose = Pose;
        double speedMultiplier = GameSpeedSlider.Value;
        if (roll != 0) pose = Fractal3DCamera.Roll(pose, roll * GameRollDegreesPerSecond * speedMultiplier * seconds);
        Vector3 direction = pose.Forward * forward + pose.Right * right;
        if (direction != Vector3.Zero)
        {
            double speed = Math.Clamp(pose.Distance * 1.5, 0.05, 500) * speedMultiplier;
            pose = pose with { Target = pose.Target + Vector3.Normalize(direction) * (float)(speed * seconds) };
        }
        MoveCamera(pose);
        return true;
    }

    /// <summary>Доводка колеса: камера догоняет цель, заданную последними щелчками.</summary>
    private bool AdvanceZoom(double seconds)
    {
        if (_zoom.IsActive(_distance)) Pose = _zoom.Advance(Pose, seconds);
        else if (_zoom.HasRemainder) Pose = _zoom.Finish(Pose);
        else return false;
        return true;
    }

    /// <summary>Доехать недоеханный шаг колеса разом: вращение и сдвиг начинаются с места, где камера будет.</summary>
    private void FinishZoom()
    {
        if (_zoom.HasRemainder) Pose = _zoom.Finish(Pose);
    }

    private bool AdvanceTransition(double seconds)
    {
        if (_transition is not { } transition) return false;

        transition.Elapsed += seconds;
        double progress = Math.Clamp(transition.Elapsed / TransitionSeconds, 0, 1);
        float eased = (float)(progress * progress * (3 - 2 * progress));

        Fractal3DPose from = transition.From, to = transition.To;
        // Расстояние ведётся геометрически: приближение к поверхности идёт равномерно на глаз.
        MoveCamera(new Fractal3DPose(
            Quaternion.Normalize(Quaternion.Slerp(from.Orientation, to.Orientation, eased)),
            from.Distance * Math.Pow(to.Distance / from.Distance, eased),
            Vector3.Lerp(from.Target, to.Target, eased)));

        if (progress >= 1) _transition = null;
        return true;
    }

    /// <summary>Плавный перелёт камеры к новому виду вместо скачка.</summary>
    private void BeginTransition(Fractal3DPose to)
    {
        FinishZoom();
        _spinX = 0;
        _spinY = 0;
        _transition = new CameraTransition(Pose, to with
        {
            Distance = Math.Clamp(to.Distance, Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance)
        });
        AttachLoop();
        RequestFrame(FrameQuality.Draft);
    }

    /// <summary>Убрать крен, не меняя направления взгляда.</summary>
    private void LevelHorizon()
    {
        Fractal3DPose pose = Pose;
        BeginTransition(pose with { Orientation = Fractal3DCamera.Level(pose.Orientation) });
        StatusText.Text = "Горизонт выровнен.";
    }

    private sealed class CameraTransition(Fractal3DPose from, Fractal3DPose to)
    {
        public Fractal3DPose From { get; } = from;
        public Fractal3DPose To { get; } = to;
        public double Elapsed { get; set; }
    }

    #endregion

    #region Кнопки мыши

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectedNavigationMode == Fractal3DNavigationMode.Game)
        {
            CanvasHost.Focus();
            e.Handled = true;
            return;
        }
        Point point = e.GetPosition(SavePreviewLayer);
        if (e.ClickCount == 2)
        {
            _ = FlyToCursorAsync(point);
            e.Handled = true;
            return;
        }
        if (IsInteracting) return;

        BeginDrag(DragMode.PendingRotate, MouseButton.Left, point, Cursors.ScrollAll);
        if (!_probeSupported)
        {
            // Без зонда поверхность не найти: вращаем вокруг точки наблюдения, как раньше.
            StartRotation(_target);
        }
        else if (TryGetCursorHit(point, out Vector3? hit))
        {
            StartRotation(hit);
        }
        else
        {
            _ = ResolvePivotAsync(point);
        }
        e.Handled = true;
    }

    private void CanvasHost_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteracting) return;
        CanvasHost.Focus();
        if (SelectedNavigationMode == Fractal3DNavigationMode.Game)
        {
            _gameCursorScreen = CanvasHost.PointToScreen(e.GetPosition(CanvasHost));
            _gameLookCaptured = true;
            BeginDrag(DragMode.Look, MouseButton.Right, e.GetPosition(SavePreviewLayer), Cursors.None);
            CenterGameCursor();
            e.Handled = true;
            return;
        }
        Point point = e.GetPosition(SavePreviewLayer);
        FinishZoom();

        // Схваченная точка должна идти за курсором: шаг сдвига считается на её глубине.
        double depth = _distance;
        if (TryGetCursorHit(point, out Vector3? hit) && hit is { } surface)
            depth = Math.Max(Vector3.Dot(surface - Pose.Position, Pose.Forward), Fractal3DCamera.MinDistance);
        _panUnit = Fractal3DCamera.WorldUnitsPerPixel(depth, _fieldOfView, SavePreviewLayer.ActualHeight);

        BeginDrag(DragMode.Pan, MouseButton.Right, point, Cursors.SizeAll);
        e.Handled = true;
    }

    private void CanvasHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (SelectedNavigationMode == Fractal3DNavigationMode.Game)
        {
            CanvasHost.Focus();
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            LevelHorizon();
        }
        else if (!IsInteracting)
        {
            FinishZoom();
            BeginDrag(DragMode.Roll, MouseButton.Middle, e.GetPosition(SavePreviewLayer), Cursors.SizeAll);
        }
        e.Handled = true;
    }

    private void BeginDrag(DragMode mode, MouseButton button, Point point, Cursor cursor)
    {
        _transition = null;
        _spinX = 0;
        _spinY = 0;
        _pendingDragX = 0;
        _pendingDragY = 0;
        _drag = mode;
        _dragButton = button;
        _lastPoint = point;
        _lastDragMs = _clock.Elapsed.TotalMilliseconds;
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = cursor;
        AttachLoop();
    }

    /// <summary>Вокруг точки поверхности — трекбол, по фону (<c>null</c>) — поворот взгляда на месте.</summary>
    private void StartRotation(Vector3? pivot)
    {
        FinishZoom();
        _drag = pivot is null ? DragMode.Look : DragMode.Orbit;
        _pivot = pivot ?? default;
        if (_pendingDragX != 0 || _pendingDragY != 0)
        {
            ApplyRotation(pivot, _pendingDragX, _pendingDragY);
            _pendingDragX = 0;
            _pendingDragY = 0;
            AfterCameraChanged();
        }
    }

    /// <summary>Зонд под точкой нажатия: пока он считает, движение мыши копится и применяется потом.</summary>
    private async Task ResolvePivotAsync(Point point)
    {
        Vector3? pivot = _target;
        try
        {
            if (TryCaptureState(out Fractal3DState state))
            {
                (double x, double y, int width, int height) = ToFramePixel(point);
                double distance = await _renderer.ProbeDistanceAsync(state, x, y, width, height, CancellationToken.None);
                pivot = double.IsNaN(distance)
                    ? null
                    : Fractal3DCamera.Position(state) +
                      Fractal3DCamera.PixelRay(state, x, y, width, height) * (float)distance;
            }
        }
        catch (Exception)
        {
            _probeSupported = false;
        }

        if (!_isClosing && _drag == DragMode.PendingRotate) StartRotation(pivot);
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point current = e.GetPosition(SavePreviewLayer);
        _cursorPoint = current;
        if (SelectedNavigationMode == Fractal3DNavigationMode.Game)
        {
            if (_drag == DragMode.Look)
            {
                Point centre = new(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2);
                Point local = e.GetPosition(CanvasHost);
                double dx = local.X - centre.X, dy = local.Y - centre.Y;
                if (Math.Abs(dx) >= 0.5 || Math.Abs(dy) >= 0.5)
                {
                    double sensitivity = RotationDegreesPerPixel * GameSensitivitySlider.Value;
                    MoveCamera(Fractal3DCamera.Look(Pose, -dx * sensitivity, -dy * sensitivity));
                    CenterGameCursor();
                    AfterCameraChanged();
                }
            }
            return;
        }
        if (!IsInteracting)
        {
            RequestProbe(current);
            return;
        }

        double deltaX = current.X - _lastPoint.X;
        double deltaY = current.Y - _lastPoint.Y;
        if (deltaX == 0 && deltaY == 0) return;
        _lastPoint = current;
        double now = _clock.Elapsed.TotalMilliseconds;

        switch (_drag)
        {
            case DragMode.PendingRotate:
                _pendingDragX += deltaX * RotationDegreesPerPixel;
                _pendingDragY += deltaY * RotationDegreesPerPixel;
                return;
            case DragMode.Orbit:
            case DragMode.Look:
            {
                double dragX = deltaX * RotationDegreesPerPixel;
                double dragY = deltaY * RotationDegreesPerPixel;
                ApplyRotation(_drag == DragMode.Orbit ? _pivot : null, dragX, dragY);
                TrackInertia(dragX, dragY, now);
                break;
            }
            case DragMode.Pan:
            {
                Fractal3DPose pose = Pose;
                MoveCamera(pose with
                {
                    Target = pose.Target + (pose.Up * (float)deltaY - pose.Right * (float)deltaX) * (float)_panUnit
                });
                break;
            }
            case DragMode.Roll:
                // Влево-вправо — крен, вверх-вниз — тангаж на месте; картинка в обоих идёт за мышью.
                MoveCamera(Fractal3DCamera.Look(
                    Fractal3DCamera.Roll(Pose, deltaX * RollDegreesPerPixel), 0, deltaY * RotationDegreesPerPixel));
                break;
        }

        _lastDragMs = now;
        AfterCameraChanged();
    }

    private void CanvasHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsInteracting || e.ChangedButton != _dragButton) return;

        bool gameLook = _gameLookCaptured;
        _gameLookCaptured = false;
        bool rotating = _drag is DragMode.Orbit or DragMode.Look;
        _spinPivot = _drag == DragMode.Orbit ? _pivot : null;
        _drag = DragMode.None;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        if (gameLook) SetCursorPos((int)Math.Round(_gameCursorScreen.X), (int)Math.Round(_gameCursorScreen.Y));

        bool fling = rotating && !gameLook && RotationInertiaBox.IsChecked == true &&
                     _clock.Elapsed.TotalMilliseconds - _lastDragMs < FlingWindowMs;
        if (!fling)
        {
            _spinX = 0;
            _spinY = 0;
        }

        // Полный кадр попросит сам цикл, когда увидит, что движение кончилось: отпускание кнопки
        // ещё не остановка, если включена инерция.
        SyncCameraUi(immediate: true);
        if (!gameLook) RequestProbe(e.GetPosition(SavePreviewLayer), force: true);
        e.Handled = true;
    }

    private void CenterGameCursor()
    {
        Point centre = CanvasHost.PointToScreen(new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));
        SetCursorPos((int)Math.Round(centre.X), (int)Math.Round(centre.Y));
    }

    private void CanvasHost_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None) return;
        bool gameLook = _gameLookCaptured;
        _gameLookCaptured = false;
        _drag = DragMode.None;
        Mouse.OverrideCursor = null;
        _spinX = _spinY = 0;
        if (gameLook) SetCursorPos((int)Math.Round(_gameCursorScreen.X), (int)Math.Round(_gameCursorScreen.Y));
    }

    private void ApplyRotation(Vector3? pivot, double dragX, double dragY) =>
        MoveCamera(pivot is { } point
            ? Fractal3DCamera.Orbit(Pose, point, dragX, dragY)
            : Fractal3DCamera.Look(Pose, dragX, dragY));

    private void TrackInertia(double dragX, double dragY, double now)
    {
        double seconds = Math.Clamp((now - _lastDragMs) / 1000, 0.004, 0.1);
        double speedX = Math.Clamp(dragX / seconds, -MaxInertiaSpeed, MaxInertiaSpeed);
        double speedY = Math.Clamp(dragY / seconds, -MaxInertiaSpeed, MaxInertiaSpeed);
        _spinX = _spinX * 0.6 + speedX * 0.4;
        _spinY = _spinY * 0.6 + speedY * 0.4;
    }

    #endregion

    #region Колесо

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SelectedNavigationMode == Fractal3DNavigationMode.Game)
        {
            e.Handled = true;
            return;
        }
        int notches = e.Delta / 120;
        if (notches == 0) notches = Math.Sign(e.Delta);
        if (notches == 0) return;

        // Колесо — это уже управление камерой: начатый перелёт к точке ему не хозяин.
        _transition = null;
        ZoomCamera(notches, e.GetPosition(SavePreviewLayer));
        AfterCameraChanged();
        e.Handled = true;
    }

    /// <summary>
    /// Приближение к точке под курсором, как в CAD: камера едет по лучу через курсор, и за каждый
    /// щелчок расстояние до этой точки сокращается в одно и то же число раз — к поверхности можно
    /// подходить сколько угодно, но сквозь неё не проскочить. Если под курсором фон, опорой служит
    /// точка луча на глубине точки наблюдения. Цель считается от уже назначенной колесом, поэтому
    /// быстрая серия щелчков складывается, а не спорит сама с собой.
    /// </summary>
    private void ZoomCamera(int notches, Point point)
    {
        Fractal3DPose planned = new(_orientation, _zoom.PlannedDistance(_distance), _zoom.PlannedTarget(_target));
        Vector3 forward = planned.Forward;
        Vector3 plannedPosition = planned.Position;
        Vector3 ray = Fractal3DCamera.PixelRay(planned, _fieldOfView, point.X, point.Y,
            Math.Max(SavePreviewLayer.ActualWidth, 1), Math.Max(SavePreviewLayer.ActualHeight, 1));

        bool known = TryGetCursorHit(point, out Vector3? hit);
        Vector3 pivot = hit ?? plannedPosition + ray * (float)(planned.Distance / Math.Max(Vector3.Dot(ray, forward), 1e-3f));
        if (!known) RequestProbe(point, force: true);

        // Точка наблюдения переезжает на глубину опоры — вид от этого не меняется, зато расстояние
        // теперь мерит путь до неё, и геометрическая доводка не проедет сквозь поверхность.
        double MinDepth(double depth) => Math.Clamp(depth, Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance);
        Vector3 position = Pose.Position;
        _distance = MinDepth(Vector3.Dot(pivot - position, forward));
        _target = position + forward * (float)_distance;
        double plannedDepth = MinDepth(Vector3.Dot(pivot - plannedPosition, forward));
        Vector3 plannedTarget = plannedPosition + forward * (float)plannedDepth;

        double distance = MinDepth(plannedDepth / Math.Pow(WheelStep(), notches));
        float keep = (float)(distance / plannedDepth);
        _zoom.Aim(distance, pivot + (plannedTarget - pivot) * keep - _target);
    }

    private static double WheelStep() =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.6
        : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.03
        : 1.15;

    #endregion

    #region Зонд поверхности

    /// <summary>
    /// Точка поверхности под курсором, если зонд уже измерил её для этого положения камеры:
    /// <c>true</c> и <paramref name="hit"/> = <c>null</c> значит «там фон».
    /// </summary>
    private bool TryGetCursorHit(Point point, out Vector3? hit)
    {
        hit = _cursorHit;
        return _hitKnown && _hitRevision == _viewRevision &&
               Math.Abs(point.X - _hitPoint.X) < 0.5 && Math.Abs(point.Y - _hitPoint.Y) < 0.5;
    }

    /// <summary>
    /// Измеряет расстояние до поверхности под курсором не чаще чем раз в
    /// <see cref="ProbeIntervalMs"/>. Отложенный запрос не теряется: последняя точка, где
    /// остановился курсор, будет измерена, как только зонд освободится.
    /// </summary>
    private void RequestProbe(Point point, bool force = false)
    {
        if (_isClosing || !_probeSupported) return;
        _probeWanted = point;
        if (_probeBusy) return;

        double wait = force ? 0 : ProbeIntervalMs - (_clock.Elapsed.TotalMilliseconds - _lastProbeMs);
        if (wait <= 0)
        {
            StartProbe();
            return;
        }

        _probeTimer ??= CreateProbeTimer();
        if (_probeTimer.IsEnabled) return;
        _probeTimer.Interval = TimeSpan.FromMilliseconds(wait);
        _probeTimer.Start();
    }

    private DispatcherTimer CreateProbeTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_probeBusy) StartProbe();
        };
        return timer;
    }

    private void StartProbe()
    {
        if (_probeWanted is not { } point) return;
        _probeWanted = null;
        _probeTimer?.Stop();
        _lastProbeMs = _clock.Elapsed.TotalMilliseconds;
        _probeBusy = true;
        _ = ProbeAsync(point);
    }

    private async Task ProbeAsync(Point point)
    {
        try
        {
            int revision = _viewRevision;
            if (!TryCaptureState(out Fractal3DState state)) return;
            (double x, double y, int width, int height) = ToFramePixel(point);
            if (x < 0 || y < 0 || x > width || y > height) return;
            double distance = await _renderer.ProbeDistanceAsync(
                state, x, y, width, height, CancellationToken.None);
            if (_isClosing) return;

            _surfaceDistance = distance;
            if (revision == _viewRevision)
            {
                // Точка откладывается от камеры, для которой её мерили: колесо, покрутившееся за
                // это время, двигает камеру по тому же лучу, и точка остаётся под курсором.
                _hitKnown = true;
                _hitPoint = point;
                _hitRevision = revision;
                _cursorHit = double.IsNaN(distance)
                    ? null
                    : Fractal3DCamera.Position(state) +
                      Fractal3DCamera.PixelRay(state, x, y, width, height) * (float)distance;
            }
            UpdateCameraText();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Зонд — удобство, а не обязательная часть кадра: если устройство его не потянуло,
            // вращение идёт вокруг точки наблюдения, а колесо приближает к её плоскости.
            _surfaceDistance = double.NaN;
            _probeSupported = false;
        }
        finally
        {
            _probeBusy = false;
            if (_probeWanted is { } next && !_isClosing) RequestProbe(next);
        }
    }

    /// <summary>Двойной щелчок: камера разворачивается к точке поверхности под курсором и берёт её целью.</summary>
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

        Fractal3DPose pose = Fractal3DCamera.Pose(state);
        Vector3 ray = Fractal3DCamera.PixelRay(state, x, y, width, height);
        Vector3 hit = pose.Position + ray * (float)distance;
        _surfaceDistance = distance;
        BeginTransition(new Fractal3DPose(Fractal3DCamera.TurnToward(pose.Orientation, ray), distance, hit));
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
            Fractal3DCamera.Apply(Pose, state);
            state.FieldOfView = _fieldOfView;
            return true;
        }
    }

    #endregion

    #region Панель навигации и оси

    private void NavigationMode_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi) return;
        if (_drag != DragMode.None) CanvasHost.ReleaseMouseCapture();
        StopCameraMotion();
        UpdateNavigationHelp();
        RequestFrame(FrameQuality.Full);
    }

    private void UpdateNavigationHelp()
    {
        bool game = SelectedNavigationMode == Fractal3DNavigationMode.Game;
        GameNavigationPanel.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
        NavigationHelpText.Text = game
            ? "Щёлкните по холсту для управления. W/S: вперёд/назад по направлению взгляда, A/D: влево/вправо, Q/E: крен. Удерживайте правую кнопку мыши для обзора; курсор вернётся на место после отпускания. Левая, средняя кнопки и колесо не меняют вид. F11: полноэкранный режим."
            : "Левая кнопка: вращение вокруг точки, за которую схватили; по фону — поворот взгляда на месте. Правая: сдвиг. Средняя: влево-вправо — крен, вверх-вниз — наклон взгляда на месте. Колесо: приближение к точке под курсором (Ctrl — быстро, Shift — точно). Двойной щелчок левой: перелёт к точке, средней: выровнять горизонт. F11: полноэкранный режим.";
        RotationInertiaBox.IsEnabled = !game;
        AutoRotateBox.IsEnabled = !game;
        AutoRotateSpeedBox.IsEnabled = !game;
    }

    private void GameSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi) UpdateGameSliderLabels();
    }

    private void UpdateGameSliderLabels()
    {
        GameSpeedLabel.Text = $"Скорость движения: {GameSpeedSlider.Value:0.0}×";
        GameSensitivityLabel.Text = $"Чувствительность мыши: {GameSensitivitySlider.Value:0.0}×";
    }

    private bool HandleGameKeyDown(KeyEventArgs e)
    {
        if (!GameKeyboardActive || e.Key is not (Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E))
            return false;
        AttachLoop();
        e.Handled = true;
        return true;
    }

    private void Navigation_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi) return;
        if (AutoRotateBox.IsChecked == true) AttachLoop();
        UpdateCameraText();
    }

    private void AfterCameraChanged()
    {
        UpdateAxisTriad();
        SyncCameraUi(immediate: !IsMoving);
        RequestFrame(FrameQuality.Draft);
        if (!IsInteracting) RequestProbe(ProbePoint);
    }

    /// <summary>
    /// Поля камеры обновляются не чаще <see cref="UiSyncIntervalMs"/>: во время движения полная
    /// перерисовка восьми полей на каждом кадре стоила бы дороже самого кадра.
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

    /// <summary>
    /// Мировые оси в углу холста, как в CAD: без выделенного верха у камеры это главный ориентир,
    /// где сейчас X, Y и Z. Ось, уходящая от зрителя, рисуется бледнее и под остальными.
    /// </summary>
    private void UpdateAxisTriad()
    {
        const double centre = 32, length = 22;
        if (_axisLines is null)
        {
            Brush[] brushes =
            [
                new SolidColorBrush(Color.FromRgb(232, 72, 72)),
                new SolidColorBrush(Color.FromRgb(96, 204, 96)),
                new SolidColorBrush(Color.FromRgb(88, 140, 255))
            ];
            string[] names = ["X", "Y", "Z"];
            _axisLines = new Line[3];
            _axisLabels = new TextBlock[3];
            for (int i = 0; i < 3; i++)
            {
                _axisLines[i] = new Line
                {
                    X1 = centre, Y1 = centre, Stroke = brushes[i], StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                _axisLabels[i] = new TextBlock
                {
                    Text = names[i], Foreground = brushes[i], FontWeight = FontWeights.Bold, FontSize = 11
                };
                AxisTriad.Children.Add(_axisLines[i]);
                AxisTriad.Children.Add(_axisLabels[i]);
            }
        }

        Fractal3DPose pose = Pose;
        Vector3[] axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        for (int i = 0; i < 3; i++)
        {
            double screenX = Vector3.Dot(axes[i], pose.Right) * length;
            double screenY = -Vector3.Dot(axes[i], pose.Up) * length;
            double away = Vector3.Dot(axes[i], pose.Forward);
            _axisLines[i].X2 = centre + screenX;
            _axisLines[i].Y2 = centre + screenY;
            _axisLines[i].Opacity = away > 0 ? 0.45 : 1;
            TextBlock label = _axisLabels![i];
            Canvas.SetLeft(label, centre + screenX * 1.3 - 4);
            Canvas.SetTop(label, centre + screenY * 1.3 - 8);
            label.Opacity = _axisLines[i].Opacity;
            int z = away > 0 ? 0 : 2;
            Panel.SetZIndex(_axisLines[i], z);
            Panel.SetZIndex(label, z + 1);
        }
    }

    #endregion
}
