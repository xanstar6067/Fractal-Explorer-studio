using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private GrayScott3DSettings _graySettings = new();
    private GrayScott3DField? _grayPendingField, _grayEngineField;
    private IGrayScott3DEngine? _grayEngine;
    private GrayScott3DSettings? _grayEngineSettings;
    private CancellationTokenSource? _grayCts;
    private bool _grayRunning, _grayBusy;
    private int _grayEpoch, _grayQueuedSteps;
    private (double X, double Y, double Z, double Radius)? _grayBrush;
    private string _grayDevice = "";
    private string? _grayFallbackReason;

    private GrayScott3DSettings CaptureGrayScott() => Kind == Fractal3DKind.GrayScott3D
        ? _graySettings with
        {
            Threshold = GrayThresholdSlider.Value, CutAxis = Math.Max(0, GrayCutBox.SelectedIndex),
            CutPosition = GrayCutSlider.Value, StepsPerFrame = (int)GraySpeedSlider.Value
        } : new();

    private void LoadGrayScott(GrayScott3DSettings? settings)
    {
        if (Kind != Fractal3DKind.GrayScott3D) return;
        var s = settings ?? new(); s.Validate();
        _grayEpoch++; _grayCts?.Cancel(); _grayRunning = false; _grayPendingField = null; _grayBrush = null;
        _grayDevice = s.Backend == GrayScottBackend.Cpu ? "ЦП · 3D-сетка" : "ГП · при продолжении";
        _graySettings = s with { Field = s.Field ?? new GrayScott3DSimulation(s).Snapshot() };
        GraySizeBox.Text = s.Size.ToString(); GrayFeedBox.Text = Format(s.Feed); GrayKillBox.Text = Format(s.Kill);
        GrayDuBox.Text = Format(s.DiffusionU); GrayDvBox.Text = Format(s.DiffusionV);
        GraySeedBox.SelectedIndex = (int)s.SeedShape; GrayRandomBox.Text = s.Seed.ToString();
        GrayBackendBox.SelectedIndex = (int)s.Backend;
        GraySpeedSlider.Value = s.StepsPerFrame; GrayThresholdSlider.Value = s.Threshold;
        GrayCutBox.SelectedIndex = s.CutAxis; GrayCutSlider.Value = s.CutPosition;
        _grayQueuedSteps = s.Field is null ? s.InitialSteps : 0;
        UpdateGrayLabels();
        if (_grayQueuedSteps > 0) _ = RunGrayWorkAsync();
    }

    private void UpdateGrayLabels()
    {
        if (GrayPlayButton is null || Kind != Fractal3DKind.GrayScott3D) return;
        GrayPlayButton.Content = _grayRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        GrayTimeText.Text = $"Шаг {_graySettings.Field?.Step ?? 0:N0} · {_graySettings.Size}³ · " +
            (_grayRunning ? "развивается" : _grayBusy ? "расчёт…" : "пауза");
        GrayDeviceText.Text = _grayDevice;
        GrayCutSlider.IsEnabled = GrayCutBox.SelectedIndex > 0;
        GrayStepButton.IsEnabled = !_grayBusy && !_grayRunning && _grayPendingField is null;
        GrayBrushButton.IsEnabled = !_grayBusy && _grayPendingField is null;
        UpdateCancelAvailability();
    }

    private void GrayPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_grayRunning) { PauseGrayScott(); return; }
        _grayRunning = true; UpdateGrayLabels();
        if (_grayPendingField is null && !_grayBusy)
        { _grayQueuedSteps = (int)GraySpeedSlider.Value; _ = RunGrayWorkAsync(); }
    }

    private void PauseGrayScott()
    {
        _grayRunning = false; _grayQueuedSteps = 0; _grayCts?.Cancel();
        UpdateGrayLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void GrayStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_grayBusy || _grayPendingField is not null) return;
        _grayRunning = false; _grayQueuedSteps = (int)GraySpeedSlider.Value; _ = RunGrayWorkAsync();
    }

    private void GrayRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new GrayScott3DSettings
            {
                Size = ReadInt(GraySizeBox, "Сетка", 32, 128), Feed = ReadDouble(GrayFeedBox, "F", 0, .1),
                Kill = ReadDouble(GrayKillBox, "K", 0, .1), DiffusionU = ReadDouble(GrayDuBox, "Диффузия U", .001, .16),
                DiffusionV = ReadDouble(GrayDvBox, "Диффузия V", .001, .16),
                Seed = ReadInt(GrayRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (GrayScott3DSeed)Math.Max(0, GraySeedBox.SelectedIndex),
                Backend = (GrayScottBackend)Math.Max(0, GrayBackendBox.SelectedIndex),
                StepsPerFrame = (int)GraySpeedSlider.Value, Threshold = GrayThresholdSlider.Value,
                CutAxis = Math.Max(0, GrayCutBox.SelectedIndex), CutPosition = GrayCutSlider.Value
            };
            _updatingUi = true;
            try { LoadGrayScott(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void GrayBackend_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.GrayScott3D) return;
        _grayEpoch++; _grayCts?.Cancel(); _grayPendingField = null;
        _graySettings = _graySettings with { Backend = (GrayScottBackend)GrayBackendBox.SelectedIndex };
        _grayQueuedSteps = _grayRunning ? (int)GraySpeedSlider.Value : 0;
        if (!_grayBusy && _grayRunning) _ = RunGrayWorkAsync();
        UpdateGrayLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void GrayView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.GrayScott3D) return;
        UpdateGrayLabels(); ScheduleRender();
    }

    private void GrayBrush_OnClick(object sender, RoutedEventArgs e) => QueueGrayBrush(.5, .5, .5);

    private void QueueGrayBrush(double x, double y, double z)
    {
        if (_grayBusy || _grayPendingField is not null || _isClosing || _suspended) return;
        _grayBrush = (x, y, z, GrayBrushSlider.Value); _grayQueuedSteps = 0; _ = RunGrayWorkAsync();
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    private bool TryGrayBrush(MouseButtonEventArgs e)
    {
        if (Kind != Fractal3DKind.GrayScott3D || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (GrayCutBox.SelectedIndex == 0) { StatusText.Text = "Для кисти выберите срез X, Y или Z."; return true; }
        var point = e.GetPosition(SavePreviewLayer);
        double h = Math.Max(1, SavePreviewLayer.ActualHeight), w = Math.Max(1, SavePreviewLayer.ActualWidth);
        var pose = Pose;
        var direction = Vector3.Normalize(pose.Forward * (float)(1 / Math.Tan(_fieldOfView * Math.PI / 360)) +
            pose.Right * (float)((point.X - w / 2) / (h / 2)) + pose.Up * (float)((h / 2 - point.Y) / (h / 2)));
        int axis = GrayCutBox.SelectedIndex - 1;
        float component = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
        float origin = axis == 0 ? pose.Position.X : axis == 1 ? pose.Position.Y : pose.Position.Z;
        if (Math.Abs(component) < 1e-6) return true;
        double distance = (GrayCutSlider.Value - origin) / component;
        var hit = pose.Position + direction * (float)distance;
        if (distance > 0 && Math.Abs(hit.X) <= 1 && Math.Abs(hit.Y) <= 1 && Math.Abs(hit.Z) <= 1)
            QueueGrayBrush((hit.X + 1) / 2, (hit.Y + 1) / 2, (hit.Z + 1) / 2);
        return true;
    }

    private async Task RunGrayWorkAsync()
    {
        if (_grayBusy || _isClosing || _suspended || Kind != Fractal3DKind.GrayScott3D) return;
        int steps = _grayQueuedSteps, epoch = _grayEpoch; var brush = _grayBrush;
        _grayQueuedSteps = 0; _grayBrush = null; _grayBusy = true;
        var settings = CaptureGrayScott(); var cts = new CancellationTokenSource(); _grayCts = cts;
        UpdateGrayLabels();
        try
        {
            var result = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                var equation = settings with { Field = null, CutAxis = 0, CutPosition = 0, Threshold = .18, StepsPerFrame = 16 };
                if (_grayEngine is null || !ReferenceEquals(_grayEngineField, settings.Field) || _grayEngineSettings != equation)
                {
                    _grayEngine?.Dispose(); _grayEngine = null;
                    _grayEngine = GrayScott3DEngineFactory.Create(settings, out _grayFallbackReason);
                    _grayEngineSettings = equation;
                }
                if (brush is { } b) _grayEngine.Inject(b.X, b.Y, b.Z, b.Radius);
                for (int remaining = steps; remaining > 0; remaining -= Math.Min(remaining, 256))
                    _grayEngine.Advance(Math.Min(remaining, 256), cts.Token);
                var field = _grayEngine.Snapshot(); _grayEngineField = field;
                return (Field: field, Device: _grayEngine.DeviceName, Fallback: _grayFallbackReason);
            });
            if (_isClosing || epoch != _grayEpoch || _suspended) return;
            _grayPendingField = result.Field;
            _grayDevice = result.Device;
            if (result.Fallback is not null)
            {
                _graySettings = _graySettings with { Backend = GrayScottBackend.Cpu };
                _updatingUi = true; GrayBackendBox.SelectedIndex = (int)GrayScottBackend.Cpu; _updatingUi = false;
                _grayDevice += " · ГП недоступен: " + result.Fallback;
            }
            RequestFrame(FrameQuality.Draft);
        }
        catch (OperationCanceledException) { _grayEngineField = null; }
        catch (Exception exception)
        {
            _grayEngineField = null;
            if (epoch == _grayEpoch && !_isClosing)
            {
                _grayRunning = false;
                // The last displayed field is still authoritative after an engine failure.
                _graySettings = _graySettings with { Backend = GrayScottBackend.Cpu };
                _updatingUi = true; GrayBackendBox.SelectedIndex = 0; _updatingUi = false;
                _grayDevice = "Сбой расчёта; поле сохранено, выбран ЦП. " + exception.Message;
            }
        }
        finally
        {
            _grayBusy = false; if (ReferenceEquals(_grayCts, cts)) _grayCts = null; cts.Dispose();
            if (_isClosing) { _grayEngine?.Dispose(); _grayEngine = null; }
            else
            {
                UpdateGrayLabels();
                if (_grayRunning && !_suspended && _grayPendingField is null && _grayQueuedSteps == 0)
                    _grayQueuedSteps = (int)GraySpeedSlider.Value;
                if (_grayQueuedSteps > 0 && _grayPendingField is null) _ = RunGrayWorkAsync();
            }
        }
    }

    private bool IsCurrentGrayFrame(Fractal3DState state) => Kind != Fractal3DKind.GrayScott3D ||
        ReferenceEquals(state.GrayScott.Field, _grayPendingField ?? _graySettings.Field);

    private void SuspendGrayScott()
    {
        if (Kind != Fractal3DKind.GrayScott3D) return;
        _grayEpoch++; _grayCts?.Cancel(); _grayQueuedSteps = 0; _grayPendingField = null;
    }

    private void OnGrayFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.GrayScott3D || _isClosing || _suspended) return;
        if (_grayPendingField is not null && ReferenceEquals(state.GrayScott.Field, _grayPendingField))
        { _graySettings = _graySettings with { Field = _grayPendingField }; _grayPendingField = null; }
        UpdateGrayLabels();
        if (_grayRunning && !_grayBusy) { _grayQueuedSteps = (int)GraySpeedSlider.Value; _ = RunGrayWorkAsync(); }
    }

    private void CloseGrayScott()
    {
        _grayRunning = false; _grayEpoch++; _grayCts?.Cancel();
        if (!_grayBusy) { _grayEngine?.Dispose(); _grayEngine = null; }
    }
}
