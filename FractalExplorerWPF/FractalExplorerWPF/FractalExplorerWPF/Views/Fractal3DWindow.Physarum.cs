using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Physarum 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="Physarum3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointPhysarum"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private Physarum3DSettings _physarumSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private Physarum3DSettings? _physarumResetTo;
    private Physarum3DGpuSimulation? _physarumSimulation;
    private Physarum3DVolume? _physarumShown, _physarumPending;
    private CancellationTokenSource? _physarumCts;
    private bool _physarumRunning, _physarumBusy;
    private int _physarumEpoch, _physarumQueuedSteps;
    private bool _physarumPreparing;
    private string _physarumDevice = "";

    private Physarum3DSettings CapturePhysarum() => Kind == Fractal3DKind.Physarum3D
        ? _physarumSettings with
        {
            Threshold = PhysarumLevelSlider.Value, Exposure = PhysarumExposureSlider.Value,
            CutAxis = Math.Max(0, PhysarumCutBox.SelectedIndex), CutPosition = PhysarumCutSlider.Value,
            StepsPerFrame = (int)PhysarumSpeedSlider.Value,
            Live = _physarumShown, Field = _physarumShown is null ? _physarumSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точный след и агенты для файла сохранения: единственное чтение поля с ГП.</summary>
    private Physarum3DSettings CheckpointPhysarum(Physarum3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadPhysarum(Physarum3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Physarum3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _physarumEpoch++; _physarumCts?.Cancel(); _physarumRunning = false;
        _physarumShown = _physarumPending = null;
        _physarumSettings = s; _physarumResetTo = s;
        _physarumDevice = _physarumSimulation?.DeviceName ?? "ГП · подготовка устройства";
        PhysarumSizeBox.SelectedIndex = Array.IndexOf(new[] {48,64,96,128},s.Size);
        PhysarumCountBox.Text = s.AgentCount.ToString();
        PhysarumSensorBox.Text = Format(s.SensorDistance); PhysarumAngleBox.Text = Format(s.SensorAngle);
        PhysarumTurnBox.Text = Format(s.TurnAngle); PhysarumMoveBox.Text = Format(s.Speed);
        PhysarumDepositBox.Text = Format(s.Deposit); PhysarumDiffusionBox.Text = Format(s.Diffusion);
        PhysarumDecayBox.Text = Format(s.Decay); PhysarumSeedBox.SelectedIndex = (int)s.SeedShape;
        PhysarumRandomBox.Text = s.Seed.ToString(); PhysarumWarmupBox.Text = s.WarmupSteps.ToString();
        PhysarumSpeedSlider.Value = s.StepsPerFrame; PhysarumLevelSlider.Value = s.Threshold;
        PhysarumExposureSlider.Value = s.Exposure;
        PhysarumCutBox.SelectedIndex = s.CutAxis; PhysarumCutSlider.Value = s.CutPosition;
        _physarumQueuedSteps = s.Field is null ? s.WarmupSteps : 0;
        _physarumPreparing = true; PhysarumPreparationProgress.Value = 0;
        PhysarumPreparationOverlay.Visibility = Visibility.Visible;
        UpdatePhysarumLabels();
        _ = RunPhysarumWorkAsync();
    }

    private void UpdatePhysarumLabels()
    {
        if (PhysarumPlayButton is null || Kind != Fractal3DKind.Physarum3D) return;
        PhysarumPlayButton.Content = _physarumRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        PhysarumTimeText.Text = $"Шаг {_physarumShown?.Step ?? _physarumSettings.Field?.Step ?? 0:N0} · {_physarumSettings.Size}³ · " +
            (_physarumRunning ? "развивается" : _physarumBusy ? "расчёт…" : "пауза");
        PhysarumDeviceText.Text = _physarumDevice;
        PhysarumCutSlider.IsEnabled = PhysarumCutBox.SelectedIndex > 0;
        bool ready = !_physarumBusy && _physarumPending is null && _physarumShown is not null;
        PhysarumStepButton.IsEnabled = ready && !_physarumRunning;
        PhysarumPlayButton.IsEnabled = !_physarumPreparing;
        UpdateCancelAvailability();
    }

    private void PhysarumPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_physarumPreparing) return;
        if (_physarumRunning) { PausePhysarum(); return; }
        _physarumRunning = true; UpdatePhysarumLabels();
        if (_physarumPending is null && !_physarumBusy)
        { _physarumQueuedSteps = (int)PhysarumSpeedSlider.Value; _ = RunPhysarumWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PausePhysarum()
    {
        _physarumRunning = false; _physarumQueuedSteps = 0; _physarumCts?.Cancel();
        UpdatePhysarumLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void PhysarumStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_physarumPreparing || _physarumBusy || _physarumPending is not null) return;
        _physarumRunning = false; _physarumQueuedSteps = (int)PhysarumSpeedSlider.Value; _ = RunPhysarumWorkAsync();
    }

    private void PhysarumRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Physarum3DSettings
            {
                Size = new[] {48,64,96,128}[Math.Clamp(PhysarumSizeBox.SelectedIndex,0,3)],
                AgentCount = ReadInt(PhysarumCountBox, "Агентов",1024,262144),
                SensorDistance = ReadDouble(PhysarumSensorBox,"Дальность чувств",1,16),
                SensorAngle = ReadDouble(PhysarumAngleBox,"Угол обзора",5,85),
                TurnAngle = ReadDouble(PhysarumTurnBox,"Поворот",1,70), Speed = ReadDouble(PhysarumMoveBox,"Скорость",.1,2),
                Deposit = ReadDouble(PhysarumDepositBox,"След",.1,8), Diffusion = ReadDouble(PhysarumDiffusionBox,"Размывание",0,1),
                Decay = ReadDouble(PhysarumDecayBox,"Испарение",.001,.2),
                WarmupSteps = ReadInt(PhysarumWarmupBox,"Подготовка",0,2000),
                Seed = ReadInt(PhysarumRandomBox,"Случайное число",int.MinValue,int.MaxValue),
                SeedShape = (Physarum3DSeed)Math.Max(0,PhysarumSeedBox.SelectedIndex),
                StepsPerFrame = (int)PhysarumSpeedSlider.Value, Threshold = PhysarumLevelSlider.Value,
                Exposure = PhysarumExposureSlider.Value, CutAxis = Math.Max(0,PhysarumCutBox.SelectedIndex),
                CutPosition = PhysarumCutSlider.Value
            };
            _updatingUi = true;
            try { LoadPhysarum(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void PhysarumView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Physarum3D) return;
        UpdatePhysarumLabels(); ScheduleRender();
    }

    private void PhysarumRandom_OnClick(object sender, RoutedEventArgs e)
    {
        PhysarumRandomBox.Text = Random.Shared.Next().ToString();
        PhysarumRestart_OnClick(sender,e);
    }

    private void PhysarumStopPreparation_OnClick(object sender, RoutedEventArgs e) => PausePhysarum();

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunPhysarumWorkAsync()
    {
        if (_physarumBusy || _isClosing || _suspended || Kind != Fractal3DKind.Physarum3D) return;
        int steps = _physarumQueuedSteps, epoch = _physarumEpoch;
        var reset = _physarumResetTo ?? (_physarumSimulation is null ? _physarumSettings : null);
        var keep = _physarumShown;
        _physarumQueuedSteps = 0; _physarumResetTo = null; _physarumBusy = true;
        var cts = new CancellationTokenSource(); _physarumCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _physarumSimulation;
        UpdatePhysarumLabels();
        var progress = new Progress<int>(done =>
        {
            if (_isClosing || epoch != _physarumEpoch || !_physarumPreparing) return;
            PhysarumPreparationProgress.Value = steps > 0 ? 90.0*done/steps : 90;
            PhysarumPreparationText.Text = $"Плетение сети · {done:N0} / {steps:N0} шагов";
        });
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new Physarum3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                simulation!.Advance(steps, cts.Token, progress);
                return simulation.Publish(keep);
            });
            _physarumDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _physarumEpoch) return;
            _physarumPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _physarumEpoch && !_isClosing)
            {
                _physarumRunning = false; _physarumPreparing = false; PhysarumPreparationOverlay.Visibility = Visibility.Collapsed;
                _physarumDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка Physarum 3D: " + exception.Message;
            }
        }
        finally
        {
            _physarumSimulation = simulation;
            _physarumBusy = false; if (ReferenceEquals(_physarumCts, cts)) _physarumCts = null; cts.Dispose();
            if (_isClosing) DisposePhysarumSimulation();
            else
            {
                UpdatePhysarumLabels();
                if (_physarumRunning && !_suspended && _physarumPending is null && _physarumQueuedSteps == 0 && _physarumResetTo is null)
                    _physarumQueuedSteps = (int)PhysarumSpeedSlider.Value;
                if (_physarumPending is null && (_physarumQueuedSteps > 0 || _physarumResetTo is not null))
                    _ = RunPhysarumWorkAsync();
            }
        }
    }

    private bool IsCurrentPhysarumFrame(Fractal3DState state) => Kind != Fractal3DKind.Physarum3D ||
        Equals(state.Physarum.Live, _physarumPending ?? _physarumShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendPhysarum()
    {
        if (Kind != Fractal3DKind.Physarum3D) return;
        _physarumCts?.Cancel(); _physarumQueuedSteps = 0;
    }

    private void OnPhysarumFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Physarum3D || _isClosing || _suspended) return;
        if (_physarumPending is not null && Equals(state.Physarum.Live, _physarumPending))
        { _physarumShown = _physarumPending; _physarumPending = null;
            _physarumPreparing = false; PhysarumPreparationOverlay.Visibility = Visibility.Collapsed; }
        UpdatePhysarumLabels();
        if (_physarumRunning && !_physarumBusy && _physarumPending is null)
        { _physarumQueuedSteps = (int)PhysarumSpeedSlider.Value; _ = RunPhysarumWorkAsync(); }
    }

    private void ClosePhysarum()
    {
        _physarumRunning = false; _physarumEpoch++; _physarumCts?.Cancel();
        if (!_physarumBusy) DisposePhysarumSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposePhysarumSimulation()
    {
        var simulation = _physarumSimulation; _physarumSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
