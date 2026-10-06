using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Кана–Хиллиарда 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="CahnHilliard3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointCahnHilliard"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private CahnHilliard3DSettings _cahnSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private CahnHilliard3DSettings? _cahnResetTo;
    private CahnHilliard3DGpuSimulation? _cahnSimulation;
    private CahnHilliard3DVolume? _cahnShown, _cahnPending;
    private CancellationTokenSource? _cahnCts;
    private bool _cahnRunning, _cahnBusy;
    private int _cahnEpoch, _cahnQueuedSteps;
    private bool _cahnPreparing;
    private string _cahnDevice = "";

    private CahnHilliard3DSettings CaptureCahnHilliard() => Kind == Fractal3DKind.CahnHilliard3D
        ? _cahnSettings with
        {
            Level = CahnLevelSlider.Value, Invert = CahnInvertBox.IsChecked == true, CutAxis = Math.Max(0, CahnCutBox.SelectedIndex),
            CutPosition = CahnCutSlider.Value, StepsPerFrame = (int)CahnSpeedSlider.Value,
            Live = _cahnShown, Field = _cahnShown is null ? _cahnSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точный состав для файла сохранения: единственное чтение поля с ГП.</summary>
    private CahnHilliard3DSettings CheckpointCahnHilliard(CahnHilliard3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadCahnHilliard(CahnHilliard3DSettings? settings)
    {
        if (Kind != Fractal3DKind.CahnHilliard3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _cahnEpoch++; _cahnCts?.Cancel(); _cahnRunning = false;
        _cahnShown = _cahnPending = null;
        _cahnSettings = s; _cahnResetTo = s;
        _cahnDevice = _cahnSimulation?.DeviceName ?? "ГП · подготовка устройства";
        CahnSizeBox.SelectedIndex = s.Size == 32 ? 0 : s.Size == 64 ? 1 : 2;
        CahnMeanBox.Text = Format(s.Mean); CahnNoiseBox.Text = Format(s.Noise);
        CahnKappaBox.Text = Format(s.Kappa); CahnMobilityBox.Text = Format(s.Mobility); CahnDtBox.Text = Format(s.TimeStep);
        CahnSeedBox.SelectedIndex = (int)s.SeedShape; CahnRandomBox.Text = s.Seed.ToString();
        CahnWarmupBox.Text = s.WarmupSteps.ToString();
        CahnSpeedSlider.Value = s.StepsPerFrame; CahnLevelSlider.Value = s.Level;
        CahnInvertBox.IsChecked = s.Invert; CahnCutBox.SelectedIndex = s.CutAxis; CahnCutSlider.Value = s.CutPosition;
        _cahnQueuedSteps = s.Field is null ? s.WarmupSteps : 0;
        _cahnPreparing = true; CahnPreparationProgress.Value = 0;
        CahnPreparationOverlay.Visibility = Visibility.Visible;
        UpdateCahnLabels();
        _ = RunCahnWorkAsync();
    }

    private void UpdateCahnLabels()
    {
        if (CahnPlayButton is null || Kind != Fractal3DKind.CahnHilliard3D) return;
        CahnPlayButton.Content = _cahnRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        CahnTimeText.Text = $"t = {_cahnShown?.Time ?? _cahnSettings.Field?.Time ?? 0:F1} · шаг {_cahnShown?.Step ?? _cahnSettings.Field?.Step ?? 0:N0} · {_cahnSettings.Size}³ · " +
            (_cahnRunning ? "развивается" : _cahnBusy ? "расчёт…" : "пауза");
        CahnDeviceText.Text = _cahnDevice;
        CahnCutSlider.IsEnabled = CahnCutBox.SelectedIndex > 0;
        bool ready = !_cahnBusy && _cahnPending is null && _cahnShown is not null;
        CahnStepButton.IsEnabled = ready && !_cahnRunning;
        CahnPlayButton.IsEnabled = !_cahnPreparing;
        UpdateCancelAvailability();
    }

    private void CahnPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_cahnPreparing) return;
        if (_cahnRunning) { PauseCahnHilliard(); return; }
        _cahnRunning = true; UpdateCahnLabels();
        if (_cahnPending is null && !_cahnBusy)
        { _cahnQueuedSteps = (int)CahnSpeedSlider.Value; _ = RunCahnWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseCahnHilliard()
    {
        _cahnRunning = false; _cahnQueuedSteps = 0; _cahnCts?.Cancel();
        UpdateCahnLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void CahnStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_cahnPreparing || _cahnBusy || _cahnPending is not null) return;
        _cahnRunning = false; _cahnQueuedSteps = (int)CahnSpeedSlider.Value; _ = RunCahnWorkAsync();
    }

    private void CahnRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new CahnHilliard3DSettings
            {
                Size = CahnSizeBox.SelectedIndex == 0 ? 32 : CahnSizeBox.SelectedIndex == 1 ? 64 : 128,
                Mean = ReadDouble(CahnMeanBox, "Средний состав", -.55, .55), Noise = ReadDouble(CahnNoiseBox, "Шум", .001, .2),
                Kappa = ReadDouble(CahnKappaBox, "κ", .5, 8), Mobility = ReadDouble(CahnMobilityBox, "M", .01, 2),
                TimeStep = ReadDouble(CahnDtBox, "Δt", .01, 2), WarmupSteps = ReadInt(CahnWarmupBox, "Подготовка", 0, 5000),
                Seed = ReadInt(CahnRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (CahnHilliard3DSeed)Math.Max(0, CahnSeedBox.SelectedIndex),
                StepsPerFrame = (int)CahnSpeedSlider.Value, Level = CahnLevelSlider.Value, Invert = CahnInvertBox.IsChecked == true,
                CutAxis = Math.Max(0, CahnCutBox.SelectedIndex), CutPosition = CahnCutSlider.Value
            };
            _updatingUi = true;
            try { LoadCahnHilliard(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void CahnView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.CahnHilliard3D) return;
        UpdateCahnLabels(); ScheduleRender();
    }

    private void CahnStopPreparation_OnClick(object sender, RoutedEventArgs e) => PauseCahnHilliard();

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunCahnWorkAsync()
    {
        if (_cahnBusy || _isClosing || _suspended || Kind != Fractal3DKind.CahnHilliard3D) return;
        int steps = _cahnQueuedSteps, epoch = _cahnEpoch;
        var reset = _cahnResetTo ?? (_cahnSimulation is null ? _cahnSettings : null);
        var keep = _cahnShown;
        _cahnQueuedSteps = 0; _cahnResetTo = null; _cahnBusy = true;
        var cts = new CancellationTokenSource(); _cahnCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _cahnSimulation;
        UpdateCahnLabels();
        var progress = new Progress<int>(done =>
        {
            if (_isClosing || epoch != _cahnEpoch || !_cahnPreparing) return;
            CahnPreparationProgress.Value = steps > 0 ? 90.0*done/steps : 90;
            CahnPreparationText.Text = $"Спинодальный распад · {done:N0} / {steps:N0} шагов";
        });
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new CahnHilliard3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                simulation!.Advance(steps, cts.Token, progress);
                return simulation.Publish(keep);
            });
            _cahnDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _cahnEpoch) return;
            _cahnPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _cahnEpoch && !_isClosing)
            {
                _cahnRunning = false; _cahnPreparing = false; CahnPreparationOverlay.Visibility = Visibility.Collapsed;
                _cahnDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка Кана–Хиллиарда 3D: " + exception.Message;
            }
        }
        finally
        {
            _cahnSimulation = simulation;
            _cahnBusy = false; if (ReferenceEquals(_cahnCts, cts)) _cahnCts = null; cts.Dispose();
            if (_isClosing) DisposeCahnSimulation();
            else
            {
                UpdateCahnLabels();
                if (_cahnRunning && !_suspended && _cahnPending is null && _cahnQueuedSteps == 0 && _cahnResetTo is null)
                    _cahnQueuedSteps = (int)CahnSpeedSlider.Value;
                if (_cahnPending is null && (_cahnQueuedSteps > 0 || _cahnResetTo is not null))
                    _ = RunCahnWorkAsync();
            }
        }
    }

    private bool IsCurrentCahnFrame(Fractal3DState state) => Kind != Fractal3DKind.CahnHilliard3D ||
        Equals(state.CahnHilliard.Live, _cahnPending ?? _cahnShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendCahnHilliard()
    {
        if (Kind != Fractal3DKind.CahnHilliard3D) return;
        _cahnCts?.Cancel(); _cahnQueuedSteps = 0;
    }

    private void OnCahnFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.CahnHilliard3D || _isClosing || _suspended) return;
        if (_cahnPending is not null && Equals(state.CahnHilliard.Live, _cahnPending))
        { _cahnShown = _cahnPending; _cahnPending = null;
            _cahnPreparing = false; CahnPreparationOverlay.Visibility = Visibility.Collapsed; }
        UpdateCahnLabels();
        if (_cahnRunning && !_cahnBusy && _cahnPending is null)
        { _cahnQueuedSteps = (int)CahnSpeedSlider.Value; _ = RunCahnWorkAsync(); }
    }

    private void CloseCahnHilliard()
    {
        _cahnRunning = false; _cahnEpoch++; _cahnCts?.Cancel();
        if (!_cahnBusy) DisposeCahnSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeCahnSimulation()
    {
        var simulation = _cahnSimulation; _cahnSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
