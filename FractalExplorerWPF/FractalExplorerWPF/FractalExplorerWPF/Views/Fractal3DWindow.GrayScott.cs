using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Gray–Scott 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="GrayScott3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointGrayScott"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private GrayScott3DSettings _graySettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private GrayScott3DSettings? _grayResetTo;
    private GrayScott3DGpuSimulation? _graySimulation;
    private GrayScott3DVolume? _grayShown, _grayPending;
    private CancellationTokenSource? _grayCts;
    private bool _grayRunning, _grayBusy;
    private int _grayEpoch, _grayQueuedSteps;
    private (double X, double Y, double Z, double Radius)? _grayBrush;
    private string _grayDevice = "";

    private GrayScott3DSettings CaptureGrayScott() => Kind == Fractal3DKind.GrayScott3D
        ? _graySettings with
        {
            Threshold = GrayThresholdSlider.Value, CutAxis = Math.Max(0, GrayCutBox.SelectedIndex),
            CutPosition = GrayCutSlider.Value, StepsPerFrame = (int)GraySpeedSlider.Value,
            Live = _grayShown, Field = _grayShown is null ? _graySettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точные U/V для файла сохранения: единственное чтение поля с ГП.</summary>
    private GrayScott3DSettings CheckpointGrayScott(GrayScott3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadGrayScott(GrayScott3DSettings? settings)
    {
        if (Kind != Fractal3DKind.GrayScott3D) return;
        CancelGraySearch();
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _grayEpoch++; _grayCts?.Cancel(); _grayRunning = false; _grayBrush = null;
        _grayShown = _grayPending = null;
        _graySettings = s; _grayResetTo = s;
        _grayDevice = _graySimulation?.DeviceName ?? "ГП · подготовка устройства";
        GraySizeBox.Text = s.Size.ToString(); GrayFeedBox.Text = Format(s.Feed); GrayKillBox.Text = Format(s.Kill);
        GrayDuBox.Text = Format(s.DiffusionU); GrayDvBox.Text = Format(s.DiffusionV);
        GraySeedBox.SelectedIndex = (int)s.SeedShape; GrayRandomBox.Text = s.Seed.ToString();
        GraySpeedSlider.Value = s.StepsPerFrame; GrayThresholdSlider.Value = s.Threshold;
        GrayCutBox.SelectedIndex = s.CutAxis; GrayCutSlider.Value = s.CutPosition;
        _grayQueuedSteps = s.Field is null ? s.InitialSteps : 0;
        UpdateGrayLabels();
        _ = RunGrayWorkAsync();
    }

    private void UpdateGrayLabels()
    {
        if (GrayPlayButton is null || Kind != Fractal3DKind.GrayScott3D) return;
        GrayPlayButton.Content = _grayRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        GrayTimeText.Text = $"Шаг {_grayShown?.Step ?? _graySettings.Field?.Step ?? 0:N0} · {_graySettings.Size}³ · " +
            (_grayRunning ? "развивается" : _grayBusy ? "расчёт…" : "пауза");
        GrayDeviceText.Text = _grayDevice;
        GrayCutSlider.IsEnabled = GrayCutBox.SelectedIndex > 0;
        bool ready = !_grayBusy && _grayPending is null && _grayShown is not null;
        GrayStepButton.IsEnabled = ready && !_grayRunning;
        GrayBrushButton.IsEnabled = ready;
        UpdateCancelAvailability();
    }

    private void GrayPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_grayRunning) { PauseGrayScott(); return; }
        _grayRunning = true; UpdateGrayLabels();
        if (_grayPending is null && !_grayBusy)
        { _grayQueuedSteps = (int)GraySpeedSlider.Value; _ = RunGrayWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseGrayScott()
    {
        _grayRunning = false; _grayQueuedSteps = 0; _grayCts?.Cancel();
        UpdateGrayLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void GrayStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_grayBusy || _grayPending is not null) return;
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
                StepsPerFrame = (int)GraySpeedSlider.Value, Threshold = GrayThresholdSlider.Value,
                CutAxis = Math.Max(0, GrayCutBox.SelectedIndex), CutPosition = GrayCutSlider.Value
            };
            _updatingUi = true;
            try { LoadGrayScott(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void GrayView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.GrayScott3D) return;
        UpdateGrayLabels(); ScheduleRender();
    }

    private void GrayBrush_OnClick(object sender, RoutedEventArgs e) => QueueGrayBrush(.5, .5, .5);

    private void QueueGrayBrush(double x, double y, double z)
    {
        if (_grayBusy || _grayPending is not null || _grayShown is null || _isClosing || _suspended) return;
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

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), кисть, шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunGrayWorkAsync()
    {
        if (_grayBusy || _isClosing || _suspended || Kind != Fractal3DKind.GrayScott3D) return;
        int steps = _grayQueuedSteps, epoch = _grayEpoch; var brush = _grayBrush;
        var reset = _grayResetTo ?? (_graySimulation is null ? _graySettings : null);
        var keep = _grayShown;
        _grayQueuedSteps = 0; _grayBrush = null; _grayResetTo = null; _grayBusy = true;
        var cts = new CancellationTokenSource(); _grayCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _graySimulation;
        UpdateGrayLabels();
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new GrayScott3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                if (brush is { } b) simulation!.Inject(b.X, b.Y, b.Z, b.Radius);
                simulation!.Advance(steps, cts.Token);
                return simulation.Publish(keep);
            });
            _grayDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _grayEpoch) return;
            _grayPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _grayEpoch && !_isClosing)
            {
                _grayRunning = false;
                _grayDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка Gray–Scott 3D: " + exception.Message;
            }
        }
        finally
        {
            _graySimulation = simulation;
            _grayBusy = false; if (ReferenceEquals(_grayCts, cts)) _grayCts = null; cts.Dispose();
            if (_isClosing) DisposeGraySimulation();
            else
            {
                UpdateGrayLabels();
                if (_grayRunning && !_suspended && _grayPending is null && _grayQueuedSteps == 0 && _grayResetTo is null)
                    _grayQueuedSteps = (int)GraySpeedSlider.Value;
                if (_grayPending is null && (_grayQueuedSteps > 0 || _grayResetTo is not null || _grayBrush is not null))
                    _ = RunGrayWorkAsync();
            }
        }
    }

    private bool IsCurrentGrayFrame(Fractal3DState state) => Kind != Fractal3DKind.GrayScott3D ||
        Equals(state.GrayScott.Live, _grayPending ?? _grayShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendGrayScott()
    {
        if (Kind != Fractal3DKind.GrayScott3D) return;
        _grayCts?.Cancel(); _grayQueuedSteps = 0;
    }

    private void OnGrayFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.GrayScott3D || _isClosing || _suspended) return;
        if (_grayPending is not null && Equals(state.GrayScott.Live, _grayPending))
        { _grayShown = _grayPending; _grayPending = null; }
        UpdateGrayLabels();
        if (_grayRunning && !_grayBusy && _grayPending is null)
        { _grayQueuedSteps = (int)GraySpeedSlider.Value; _ = RunGrayWorkAsync(); }
    }

    private void CloseGrayScott()
    {
        _grayRunning = false; _grayEpoch++; _grayCts?.Cancel(); CancelGraySearch();
        if (!_grayBusy) DisposeGraySimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeGraySimulation()
    {
        var simulation = _graySimulation; _graySimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
