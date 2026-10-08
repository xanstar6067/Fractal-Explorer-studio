using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Lenia 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="Lenia3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointLenia"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private Lenia3DSettings _leniaSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private Lenia3DSettings? _leniaResetTo;
    private Lenia3DGpuSimulation? _leniaSimulation;
    private Lenia3DVolume? _leniaShown, _leniaPending;
    private CancellationTokenSource? _leniaCts;
    private bool _leniaRunning, _leniaBusy;
    private int _leniaEpoch, _leniaQueuedSteps;
    private bool _leniaPreparing;
    private string _leniaDevice = "";

    private Lenia3DSettings CaptureLenia() => Kind == Fractal3DKind.Lenia3D
        ? _leniaSettings with
        {
            Threshold = LeniaLevelSlider.Value, CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex),
            CutPosition = LeniaCutSlider.Value, StepsPerFrame = (int)LeniaSpeedSlider.Value,
            Live = _leniaShown, Field = _leniaShown is null ? _leniaSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точное поле для файла сохранения: единственное чтение поля с ГП.</summary>
    private Lenia3DSettings CheckpointLenia(Lenia3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadLenia(Lenia3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Lenia3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        CancelLeniaSearch();
        _leniaEpoch++; _leniaCts?.Cancel(); _leniaRunning = false;
        LeniaSearchStatus.Text = s.Field is null ? "Выберите поиск или вариацию после подготовки." : "Показано сохранённое поле. Можно продолжить развитие или найти вариацию.";
        _leniaShown = _leniaPending = null;
        _leniaSettings = s; _leniaResetTo = s;
        _leniaDevice = _leniaSimulation?.DeviceName ?? "ГП · подготовка устройства";
        LeniaSizeBox.SelectedIndex = s.Size == 32 ? 0 : s.Size == 64 ? 1 : 2;
        LeniaRadiusBox.Text = Format(s.Radius); LeniaMeanBox.Text = Format(s.GrowthMean); LeniaWidthBox.Text = Format(s.GrowthWidth);
        LeniaShellCountBox.SelectedIndex = s.ShellCount-1;
        LeniaBeta1Box.Text = Format(s.Beta1); LeniaBeta2Box.Text = Format(s.Beta2); LeniaBeta3Box.Text = Format(s.Beta3); LeniaBeta4Box.Text = Format(s.Beta4);
        LeniaDtBox.Text = Format(s.TimeStep); LeniaGrowthBox.SelectedIndex = (int)s.Growth;
        LeniaSeedBox.SelectedIndex = (int)s.SeedShape; LeniaRandomBox.Text = s.Seed.ToString(); LeniaNoiseBox.Text = Format(s.SeedNoise);
        LeniaWarmupBox.Text = s.WarmupSteps.ToString();
        LeniaSpeedSlider.Value = s.StepsPerFrame; LeniaLevelSlider.Value = s.Threshold;
        LeniaCutBox.SelectedIndex = s.CutAxis; LeniaCutSlider.Value = s.CutPosition;
        _leniaQueuedSteps = s.Field is null ? s.WarmupSteps : 0;
        _leniaPreparing = true; LeniaPreparationProgress.Value = 0;
        LeniaPreparationOverlay.Visibility = Visibility.Visible;
        UpdateLeniaLabels();
        _ = RunLeniaWorkAsync();
    }

    private void UpdateLeniaLabels()
    {
        if (LeniaPlayButton is null || Kind != Fractal3DKind.Lenia3D) return;
        LeniaPlayButton.Content = _leniaRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        LeniaTimeText.Text = $"t = {_leniaShown?.Time ?? _leniaSettings.Field?.Time ?? 0:F1} · шаг {_leniaShown?.Step ?? _leniaSettings.Field?.Step ?? 0:N0} · {_leniaSettings.Size}³ · " +
            (_leniaRunning ? "развивается" : _leniaBusy ? "расчёт…" : "пауза");
        LeniaDeviceText.Text = _leniaDevice;
        LeniaCutSlider.IsEnabled = LeniaCutBox.SelectedIndex > 0;
        bool ready = !_leniaBusy && _leniaPending is null && _leniaShown is not null;
        LeniaStepButton.IsEnabled = ready && !_leniaRunning && _leniaSearchCts is null;
        LeniaPlayButton.IsEnabled = !_leniaPreparing && _leniaSearchCts is null;
        UpdateLeniaSearchButtons();
        UpdateCancelAvailability();
    }

    private void LeniaPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_leniaPreparing || _leniaSearchCts is not null) return;
        if (_leniaRunning) { PauseLenia(); return; }
        _leniaRunning = true; UpdateLeniaLabels();
        if (_leniaPending is null && !_leniaBusy)
        { _leniaQueuedSteps = (int)LeniaSpeedSlider.Value; _ = RunLeniaWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseLenia()
    {
        _leniaRunning = false; _leniaQueuedSteps = 0; _leniaCts?.Cancel();
        UpdateLeniaLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void LeniaStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_leniaPreparing || _leniaBusy || _leniaPending is not null || _leniaSearchCts is not null) return;
        _leniaRunning = false; _leniaQueuedSteps = (int)LeniaSpeedSlider.Value; _ = RunLeniaWorkAsync();
    }

    private void LeniaRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Lenia3DSettings
            {
                Size = LeniaSizeBox.SelectedIndex == 0 ? 32 : LeniaSizeBox.SelectedIndex == 1 ? 64 : 128,
                Radius = ReadDouble(LeniaRadiusBox, "Радиус ядра", 2, 63),
                GrowthMean = ReadDouble(LeniaMeanBox, "μ", .01, .5), GrowthWidth = ReadDouble(LeniaWidthBox, "σ", .002, .15),
                ShellCount = Math.Clamp(LeniaShellCountBox.SelectedIndex+1,1,4),
                Beta1 = ReadDouble(LeniaBeta1Box,"β₁",0,1), Beta2 = ReadDouble(LeniaBeta2Box,"β₂",0,1),
                Beta3 = ReadDouble(LeniaBeta3Box,"β₃",0,1), Beta4 = ReadDouble(LeniaBeta4Box,"β₄",0,1),
                Growth = (LeniaGrowth)Math.Max(0,LeniaGrowthBox.SelectedIndex),
                TimeStep = ReadDouble(LeniaDtBox, "Δt", .01, .2), WarmupSteps = ReadInt(LeniaWarmupBox, "Подготовка", 0, 3000),
                Seed = ReadInt(LeniaRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedNoise = ReadDouble(LeniaNoiseBox,"Шум затравки",0,.15),
                SeedShape = (Lenia3DSeed)Math.Max(0, LeniaSeedBox.SelectedIndex),
                StepsPerFrame = (int)LeniaSpeedSlider.Value, Threshold = LeniaLevelSlider.Value,
                CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex), CutPosition = LeniaCutSlider.Value
            };
            _updatingUi = true;
            try { LoadLenia(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void LeniaView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Lenia3D) return;
        UpdateLeniaLabels(); ScheduleRender();
    }

    private void LeniaStopPreparation_OnClick(object sender, RoutedEventArgs e) => PauseLenia();

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunLeniaWorkAsync()
    {
        if (_leniaBusy || _isClosing || _suspended || Kind != Fractal3DKind.Lenia3D) return;
        int steps = _leniaQueuedSteps, epoch = _leniaEpoch;
        var reset = _leniaResetTo ?? (_leniaSimulation is null ? _leniaSettings : null);
        var keep = _leniaShown;
        _leniaQueuedSteps = 0; _leniaResetTo = null; _leniaBusy = true;
        var cts = new CancellationTokenSource(); _leniaCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _leniaSimulation;
        UpdateLeniaLabels();
        var progress = new Progress<int>(done =>
        {
            if (_isClosing || epoch != _leniaEpoch || !_leniaPreparing) return;
            LeniaPreparationProgress.Value = steps > 0 ? 90.0*done/steps : 90;
            LeniaPreparationText.Text = $"Lenia · {done:N0} / {steps:N0} шагов";
        });
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new Lenia3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                simulation!.Advance(steps, cts.Token, progress);
                return simulation.Publish(keep);
            });
            _leniaDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _leniaEpoch) return;
            _leniaPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _leniaEpoch && !_isClosing)
            {
                _leniaRunning = false; _leniaPreparing = false; LeniaPreparationOverlay.Visibility = Visibility.Collapsed;
                _leniaDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка Lenia 3D: " + exception.Message;
            }
        }
        finally
        {
            _leniaSimulation = simulation;
            _leniaBusy = false; if (ReferenceEquals(_leniaCts, cts)) _leniaCts = null; cts.Dispose();
            if (_isClosing) DisposeLeniaSimulation();
            else
            {
                UpdateLeniaLabels();
                if (_leniaRunning && !_suspended && _leniaPending is null && _leniaQueuedSteps == 0 && _leniaResetTo is null)
                    _leniaQueuedSteps = (int)LeniaSpeedSlider.Value;
                if (_leniaPending is null && (_leniaQueuedSteps > 0 || _leniaResetTo is not null))
                    _ = RunLeniaWorkAsync();
            }
        }
    }

    private bool IsCurrentLeniaFrame(Fractal3DState state) => Kind != Fractal3DKind.Lenia3D ||
        Equals(state.Lenia.Live, _leniaPending ?? _leniaShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendLenia()
    {
        if (Kind != Fractal3DKind.Lenia3D) return;
        CancelLeniaSearch();
        _leniaCts?.Cancel(); _leniaQueuedSteps = 0;
    }

    private void OnLeniaFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Lenia3D || _isClosing || _suspended) return;
        if (_leniaPending is not null && Equals(state.Lenia.Live, _leniaPending))
        { _leniaShown = _leniaPending; _leniaPending = null;
            _leniaPreparing = false; LeniaPreparationOverlay.Visibility = Visibility.Collapsed; }
        UpdateLeniaLabels();
        if (_leniaRunning && !_leniaBusy && _leniaPending is null)
        { _leniaQueuedSteps = (int)LeniaSpeedSlider.Value; _ = RunLeniaWorkAsync(); }
    }

    private void CloseLenia()
    {
        CancelLeniaSearch();
        _leniaRunning = false; _leniaEpoch++; _leniaCts?.Cancel();
        if (!_leniaBusy) DisposeLeniaSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeLeniaSimulation()
    {
        var simulation = _leniaSimulation; _leniaSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
