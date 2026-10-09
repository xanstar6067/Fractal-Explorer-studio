using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Кобаяси 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="Kobayashi3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointKobayashi"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private Kobayashi3DSettings _kobSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private Kobayashi3DSettings? _kobResetTo;
    private Kobayashi3DGpuSimulation? _kobSimulation;
    private Kobayashi3DVolume? _kobShown, _kobPending;
    private CancellationTokenSource? _kobCts;
    private bool _kobRunning, _kobBusy;
    private int _kobEpoch, _kobQueuedSteps;
    private (double X, double Y, double Z, double Radius)? _kobBrush;
    private string _kobDevice = "";

    private Kobayashi3DSettings CaptureKobayashi() => Kind == Fractal3DKind.Kobayashi3D
        ? _kobSettings with
        {
            Threshold = KobThresholdSlider.Value, CutAxis = Math.Max(0, KobCutBox.SelectedIndex),
            CutPosition = KobCutSlider.Value, StepsPerFrame = (int)KobSpeedSlider.Value,
            Live = _kobShown, Field = _kobShown is null ? _kobSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точные φ/T для файла сохранения: единственное чтение поля с ГП.</summary>
    private Kobayashi3DSettings CheckpointKobayashi(Kobayashi3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadKobayashi(Kobayashi3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Kobayashi3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _kobEpoch++; _kobCts?.Cancel(); _kobRunning = false; _kobBrush = null;
        _kobShown = _kobPending = null;
        _kobSettings = s; _kobResetTo = s;
        _kobDevice = _kobSimulation?.DeviceName ?? "ГП · подготовка устройства";
        KobSizeBox.Text = s.Size.ToString();
        KobWidthBox.Text = Format(s.InterfaceWidth); KobAnisotropyBox.Text = Format(s.Anisotropy);
        KobMobilityBox.Text = Format(s.Mobility); KobDiffusionBox.Text = Format(s.ThermalDiffusion);
        KobHeatBox.Text = Format(s.LatentHeat); KobColdBox.Text = Format(s.Undercooling);
        KobNoiseBox.Text = Format(s.Noise); KobDtBox.Text = Format(s.TimeStep);
        KobWarmupBox.Text = s.WarmupSteps.ToString();
        KobSeedBox.SelectedIndex = (int)s.SeedShape; KobRandomBox.Text = s.Seed.ToString();
        KobSpeedSlider.Value = s.StepsPerFrame; KobThresholdSlider.Value = s.Threshold;
        KobCutBox.SelectedIndex = s.CutAxis; KobCutSlider.Value = s.CutPosition;
        _kobQueuedSteps = s.Field is null ? s.InitialSteps : 0;
        KobPreparationOverlay.Visibility = Visibility.Visible;
        KobPreparationProgress.Value = 0;
        UpdateKobLabels();
        _ = RunKobWorkAsync();
    }

    private void UpdateKobLabels()
    {
        if (KobPlayButton is null || Kind != Fractal3DKind.Kobayashi3D) return;
        KobPlayButton.Content = _kobRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        KobTimeText.Text = $"Шаг {_kobShown?.Step ?? _kobSettings.Field?.Step ?? 0:N0} · {_kobSettings.Size}³ · " +
            (_kobRunning ? "развивается" : _kobBusy ? "расчёт…" : "пауза");
        KobDeviceText.Text = _kobDevice;
        KobDtText.Text = $"Фактический шаг времени: {_kobSettings.EffectiveTimeStep:G3}";
        KobCutSlider.IsEnabled = KobCutBox.SelectedIndex > 0;
        bool ready = !_kobBusy && _kobPending is null && _kobShown is not null;
        KobStepButton.IsEnabled = ready && !_kobRunning;
        KobBrushButton.IsEnabled = ready;
        UpdateCancelAvailability();
    }

    private void KobStopPreparation_OnClick(object sender, RoutedEventArgs e) => PauseKobayashi();

    private void KobPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_kobRunning) { PauseKobayashi(); return; }
        _kobRunning = true; UpdateKobLabels();
        if (_kobPending is null && !_kobBusy)
        { _kobQueuedSteps = (int)KobSpeedSlider.Value; _ = RunKobWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseKobayashi()
    {
        _kobRunning = false; _kobQueuedSteps = 0; _kobCts?.Cancel();
        UpdateKobLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void KobStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_kobBusy || _kobPending is not null) return;
        _kobRunning = false; _kobQueuedSteps = (int)KobSpeedSlider.Value; _ = RunKobWorkAsync();
    }

    private void KobRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Kobayashi3DSettings
            {
                Size = ReadInt(KobSizeBox, "Сетка", 32, 128),
                InterfaceWidth = ReadDouble(KobWidthBox, "Толщина границы", .5, 2),
                Anisotropy = ReadDouble(KobAnisotropyBox, "Анизотропия", -.06, .06),
                Mobility = ReadDouble(KobMobilityBox, "Подвижность", .1, 4),
                ThermalDiffusion = ReadDouble(KobDiffusionBox, "Теплопроводность", .1, 4),
                LatentHeat = ReadDouble(KobHeatBox, "Скрытая теплота", 0, 3),
                Undercooling = ReadDouble(KobColdBox, "Переохлаждение", .05, 1.5),
                Noise = ReadDouble(KobNoiseBox, "Шум", 0, .1), TimeStep = ReadDouble(KobDtBox, "Шаг времени", .001, .1),
                WarmupSteps = ReadInt(KobWarmupBox, "Подготовка", 0, 20000),
                Seed = ReadInt(KobRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (Kobayashi3DSeed)Math.Max(0, KobSeedBox.SelectedIndex),
                StepsPerFrame = (int)KobSpeedSlider.Value, Threshold = KobThresholdSlider.Value,
                CutAxis = Math.Max(0, KobCutBox.SelectedIndex), CutPosition = KobCutSlider.Value
            };
            _updatingUi = true;
            try { LoadKobayashi(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void KobView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Kobayashi3D) return;
        UpdateKobLabels(); ScheduleRender();
    }

    private void KobBrush_OnClick(object sender, RoutedEventArgs e) => QueueKobBrush(.5, .5, .5);

    private void QueueKobBrush(double x, double y, double z)
    {
        if (_kobBusy || _kobPending is not null || _kobShown is null || _isClosing || _suspended) return;
        _kobBrush = (x, y, z, KobBrushSlider.Value); _kobQueuedSteps = 0; _ = RunKobWorkAsync();
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    private bool TryKobBrush(MouseButtonEventArgs e)
    {
        if (Kind != Fractal3DKind.Kobayashi3D || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (KobCutBox.SelectedIndex == 0) { StatusText.Text = "Для затравки выберите срез X, Y или Z."; return true; }
        var point = e.GetPosition(SavePreviewLayer);
        double h = Math.Max(1, SavePreviewLayer.ActualHeight), w = Math.Max(1, SavePreviewLayer.ActualWidth);
        var pose = Pose;
        var direction = Vector3.Normalize(pose.Forward * (float)(1 / Math.Tan(_fieldOfView * Math.PI / 360)) +
            pose.Right * (float)((point.X - w / 2) / (h / 2)) + pose.Up * (float)((h / 2 - point.Y) / (h / 2)));
        int axis = KobCutBox.SelectedIndex - 1;
        float component = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
        float origin = axis == 0 ? pose.Position.X : axis == 1 ? pose.Position.Y : pose.Position.Z;
        if (Math.Abs(component) < 1e-6) return true;
        double distance = (KobCutSlider.Value - origin) / component;
        var hit = pose.Position + direction * (float)distance;
        if (distance > 0 && Math.Abs(hit.X) <= 1 && Math.Abs(hit.Y) <= 1 && Math.Abs(hit.Z) <= 1)
            QueueKobBrush((hit.X + 1) / 2, (hit.Y + 1) / 2, (hit.Z + 1) / 2);
        return true;
    }

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), кисть, шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunKobWorkAsync()
    {
        if (_kobBusy || _isClosing || _suspended || Kind != Fractal3DKind.Kobayashi3D) return;
        int steps = _kobQueuedSteps, epoch = _kobEpoch; var brush = _kobBrush;
        var reset = _kobResetTo ?? (_kobSimulation is null ? _kobSettings : null);
        var keep = _kobShown;
        _kobQueuedSteps = 0; _kobBrush = null; _kobResetTo = null; _kobBusy = true;
        var cts = new CancellationTokenSource(); _kobCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _kobSimulation;
        UpdateKobLabels();
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new Kobayashi3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                if (brush is { } b) simulation!.Inject(b.X, b.Y, b.Z, b.Radius);
                int done = 0;
                while (done < steps && !cts.IsCancellationRequested)
                {
                    done += simulation!.Advance(Math.Min(64, steps - done), cts.Token);
                    int completed = done;
                    if (reset is not null && steps > 0) Dispatcher.BeginInvoke(() =>
                    {
                        if (epoch != _kobEpoch || _isClosing) return;
                        KobPreparationProgress.Value = 100.0 * completed / steps;
                        KobPreparationText.Text = $"Выращивание кристалла: {completed:N0} / {steps:N0} шагов";
                    });
                }
                return simulation!.Publish(keep);
            });
            _kobDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _kobEpoch) return;
            _kobPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _kobEpoch && !_isClosing)
            {
                _kobRunning = false;
                KobPreparationOverlay.Visibility = Visibility.Collapsed;
                _kobDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка Кобаяси 3D: " + exception.Message;
            }
        }
        finally
        {
            _kobSimulation = simulation;
            _kobBusy = false; if (ReferenceEquals(_kobCts, cts)) _kobCts = null; cts.Dispose();
            if (_isClosing) DisposeKobSimulation();
            else
            {
                UpdateKobLabels();
                if (_kobRunning && !_suspended && _kobPending is null && _kobQueuedSteps == 0 && _kobResetTo is null)
                    _kobQueuedSteps = (int)KobSpeedSlider.Value;
                if (_kobPending is null && (_kobQueuedSteps > 0 || _kobResetTo is not null || _kobBrush is not null))
                    _ = RunKobWorkAsync();
            }
        }
    }

    private bool IsCurrentKobFrame(Fractal3DState state) => Kind != Fractal3DKind.Kobayashi3D ||
        Equals(state.Kobayashi.Live, _kobPending ?? _kobShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendKobayashi()
    {
        if (Kind != Fractal3DKind.Kobayashi3D) return;
        _kobCts?.Cancel(); _kobQueuedSteps = 0;
    }

    private void OnKobFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Kobayashi3D || _isClosing || _suspended) return;
        if (_kobPending is not null && Equals(state.Kobayashi.Live, _kobPending))
        { _kobShown = _kobPending; _kobPending = null; }
        KobPreparationOverlay.Visibility = Visibility.Collapsed;
        UpdateKobLabels();
        if (_kobRunning && !_kobBusy && _kobPending is null)
        { _kobQueuedSteps = (int)KobSpeedSlider.Value; _ = RunKobWorkAsync(); }
    }

    private void CloseKobayashi()
    {
        _kobRunning = false; _kobEpoch++; _kobCts?.Cancel();
        if (!_kobBusy) DisposeKobSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeKobSimulation()
    {
        var simulation = _kobSimulation; _kobSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
