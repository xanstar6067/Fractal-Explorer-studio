using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Возбудимая среда 3D: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр (<see cref="Excitable3DVolume"/>), рендер рисует его без копий
/// через ЦП, и только показанный кадр запускает следующую порцию. В оперативную память поле
/// попадает лишь при сохранении (<see cref="CheckpointExcitable"/>).
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private Excitable3DSettings _exSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private Excitable3DSettings? _exResetTo;
    private Excitable3DGpuSimulation? _exSimulation;
    private Excitable3DVolume? _exShown, _exPending;
    private CancellationTokenSource? _exCts;
    private bool _exRunning, _exBusy;
    private int _exEpoch, _exQueuedSteps;
    private (double X, double Y, double Z, double Radius)? _exBrush;
    private string _exDevice = "";
    private int _exPreparationPercent;

    private Excitable3DSettings CaptureExcitable() => Kind == Fractal3DKind.Excitable3D
        ? _exSettings with
        {
            Threshold = ExThresholdSlider.Value, CutAxis = Math.Max(0, ExCutBox.SelectedIndex),
            CutPosition = ExCutSlider.Value, StepsPerFrame = (int)ExSpeedSlider.Value,
            View = (Excitable3DView)Math.Max(0, ExViewBox.SelectedIndex),
            Live = _exShown, Field = _exShown is null ? _exSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точные U/V для файла сохранения: единственное чтение поля с ГП.</summary>
    private Excitable3DSettings CheckpointExcitable(Excitable3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    private void LoadExcitable(Excitable3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Excitable3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _exEpoch++; _exCts?.Cancel(); _exRunning = false; _exBrush = null;
        _exShown = _exPending = null;
        _exSettings = s; _exResetTo = s;
        _exDevice = _exSimulation?.DeviceName ?? "ГП · подготовка устройства";
        ExModelBox.SelectedIndex = (int)s.Model;
        FillExEquation(s);
        ExSizeBox.Text = s.Size.ToString(); ExSeedBox.SelectedIndex = (int)s.SeedShape;
        ExBoundaryBox.SelectedIndex = (int)s.Boundary; ExViewBox.SelectedIndex = (int)s.View;
        ExWarmupBox.Text = s.WarmupSteps.ToString();
        ExSpeedSlider.Value = s.StepsPerFrame; ExThresholdSlider.Value = s.Threshold;
        ExCutBox.SelectedIndex = s.CutAxis; ExCutSlider.Value = s.CutPosition;
        BeginSimulationPreparation(ExPreparing, "Подготовка свитка…");
        _exPreparationPercent = 0;
        _exQueuedSteps = s.Field is null ? s.InitialSteps : 0;
        UpdateExLabels();
        _ = RunExWorkAsync();
    }

    private void FillExEquation(Excitable3DSettings s)
    {
        ExABox.Text = Format(s.A); ExBBox.Text = Format(s.B); ExEpsilonBox.Text = Format(s.Epsilon);
        ExMu1Box.Text = Format(s.Mu1); ExMu2Box.Text = Format(s.Mu2);
        ExDuBox.Text = Format(s.DiffusionU); ExDvBox.Text = Format(s.DiffusionV);
        ExDtBox.Text = Format(s.TimeStep); ExSpacingBox.Text = Format(s.Spacing);
        ExMuPanel.Visibility = s.Model == Excitable3DModel.AlievPanfilov ? Visibility.Visible : Visibility.Collapsed;
        (ExALabel.Text, ExBLabel.Text, ExEquationText.Text) = s.Model switch
        {
            Excitable3DModel.Oregonator => ("Стехиометрия f", "Малый параметр q", "u′ = [u(1−u) − fv(u−q)/(u+q)] / ε\nv′ = u − v · химическая модель БЖ"),
            Excitable3DModel.FitzHughNagumo => ("Порог β", "Восстановление γ", "u′ = (u − u³/3 − v) / ε\nv′ = ε(u + β − γv)"),
            Excitable3DModel.AlievPanfilov => ("Порог a", "Скорость k", "u′ = −ku(u−a)(u−1) − uv\nv′ = −[ε + μ₁v/(u+μ₂)] [v + ku(u−a−1)]"),
            _ => ("Возбудимость a", "Порог b", "u′ = u(1−u)[u−(v+b)/a] / ε\nv′ = u − v")
        };
    }

    private void ExModel_OnChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Excitable3D || ExABox is null) return;
        var s = Excitable3DSettings.Default((Excitable3DModel)Math.Max(0, ExModelBox.SelectedIndex));
        FillExEquation(s); ExWarmupBox.Text = s.WarmupSteps.ToString();
    }

    private void UpdateExLabels()
    {
        if (ExPlayButton is null || Kind != Fractal3DKind.Excitable3D) return;
        ExPlayButton.Content = _exRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        ExTimeText.Text = $"Шаг {_exShown?.Step ?? _exSettings.Field?.Step ?? 0:N0} · {_exSettings.Size}³ · " +
            (_exRunning ? "развивается" : _exBusy ? "расчёт…" : "пауза");
        ExDeviceText.Text = _exDevice;
        ExPlayButton.IsEnabled = _exShown is not null;
        ExPreparing.Visibility = _exShown is null && (_exBusy || _exPending is not null) ? Visibility.Visible : Visibility.Collapsed;
        ExPreparing.Progress = .9 * _exPreparationPercent;
        ExPreparing.IsIndeterminate = _exPreparationPercent == 0;
        ExPreparing.Message = $"Подготовка свитка · {_exPreparationPercent}%";
        ExTimeText.Text += $" · t = {(_exShown?.Step ?? _exSettings.Field?.Step ?? 0) * _exSettings.TimeStep:F2}";
        ExCutSlider.IsEnabled = ExCutBox.SelectedIndex > 0;
        bool ready = !_exBusy && _exPending is null && _exShown is not null;
        ExStepButton.IsEnabled = ready && !_exRunning;
        ExBrushButton.IsEnabled = ready;
        UpdateCancelAvailability();
    }

    private void ExPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_exRunning) { PauseExcitable(); return; }
        _exRunning = true; UpdateExLabels();
        if (_exPending is null && !_exBusy)
        { _exQueuedSteps = (int)ExSpeedSlider.Value; _ = RunExWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseExcitable()
    {
        _exRunning = false; _exQueuedSteps = 0; _exCts?.Cancel();
        UpdateExLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void ExStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_exBusy || _exPending is not null) return;
        _exRunning = false; _exQueuedSteps = (int)ExSpeedSlider.Value; _ = RunExWorkAsync();
    }

    private void ExRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Excitable3DSettings
            {
                Model = (Excitable3DModel)Math.Max(0, ExModelBox.SelectedIndex),
                Size = ReadInt(ExSizeBox, "Сетка", 32, 128), A = ReadDouble(ExABox, "Параметр A", .0001, 3),
                B = ReadDouble(ExBBox, "Параметр B", .0001, 12), Epsilon = ReadDouble(ExEpsilonBox, "ε", .005, .5),
                Mu1 = ReadDouble(ExMu1Box, "μ₁", .01, .5), Mu2 = ReadDouble(ExMu2Box, "μ₂", .1, 1),
                DiffusionU = ReadDouble(ExDuBox, "Диффузия U", .01, 2), DiffusionV = ReadDouble(ExDvBox, "Диффузия V", 0, 1),
                TimeStep = ReadDouble(ExDtBox, "Шаг времени", .0001, .02), Spacing = ReadDouble(ExSpacingBox, "Шаг сетки", .25, 2),
                WarmupSteps = ReadInt(ExWarmupBox, "Подготовка", 0, 20000),
                Boundary = (Excitable3DBoundary)Math.Max(0, ExBoundaryBox.SelectedIndex),
                SeedShape = (Excitable3DSeed)Math.Max(0, ExSeedBox.SelectedIndex),
                View = (Excitable3DView)Math.Max(0, ExViewBox.SelectedIndex),
                StepsPerFrame = (int)ExSpeedSlider.Value, Threshold = ExThresholdSlider.Value,
                CutAxis = Math.Max(0, ExCutBox.SelectedIndex), CutPosition = ExCutSlider.Value
            };
            settings.Validate();
            _updatingUi = true;
            try { LoadExcitable(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void ExView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Excitable3D) return;
        if (e.Source == ExViewBox) ExThresholdSlider.Value = ExViewBox.SelectedIndex == 1 ? .15 : .5;
        UpdateExLabels(); ScheduleRender();
    }

    private void ExBrush_OnClick(object sender, RoutedEventArgs e) => QueueExBrush(.5, .5, .5);

    private void QueueExBrush(double x, double y, double z)
    {
        if (_exBusy || _exPending is not null || _exShown is null || _isClosing || _suspended) return;
        _exBrush = (x, y, z, ExBrushSlider.Value); _exQueuedSteps = 0; _ = RunExWorkAsync();
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    private bool TryExBrush(MouseButtonEventArgs e)
    {
        if (Kind != Fractal3DKind.Excitable3D || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (ExCutBox.SelectedIndex == 0) { StatusText.Text = "Для кисти выберите срез X, Y или Z."; return true; }
        var point = e.GetPosition(SavePreviewLayer);
        double h = Math.Max(1, SavePreviewLayer.ActualHeight), w = Math.Max(1, SavePreviewLayer.ActualWidth);
        var pose = Pose;
        var direction = Vector3.Normalize(pose.Forward * (float)(1 / Math.Tan(_fieldOfView * Math.PI / 360)) +
            pose.Right * (float)((point.X - w / 2) / (h / 2)) + pose.Up * (float)((h / 2 - point.Y) / (h / 2)));
        int axis = ExCutBox.SelectedIndex - 1;
        float component = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
        float origin = axis == 0 ? pose.Position.X : axis == 1 ? pose.Position.Y : pose.Position.Z;
        if (Math.Abs(component) < 1e-6) return true;
        double distance = (ExCutSlider.Value - origin) / component;
        var hit = pose.Position + direction * (float)distance;
        if (distance > 0 && Math.Abs(hit.X) <= 1 && Math.Abs(hit.Y) <= 1 && Math.Abs(hit.Z) <= 1)
            QueueExBrush((hit.X + 1) / 2, (hit.Y + 1) / 2, (hit.Z + 1) / 2);
        return true;
    }

    /// <summary>
    /// Одна порция работы на ГП: перезапуск (если нужен), кисть, шаги и публикация кадра.
    /// Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunExWorkAsync()
    {
        if (_exBusy || _isClosing || _suspended || Kind != Fractal3DKind.Excitable3D) return;
        int steps = _exQueuedSteps, epoch = _exEpoch; var brush = _exBrush;
        var reset = _exResetTo ?? (_exSimulation is null ? _exSettings : null);
        var keep = _exShown;
        _exQueuedSteps = 0; _exBrush = null; _exResetTo = null; _exBusy = true;
        var cts = new CancellationTokenSource(); _exCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _exSimulation;
        UpdateExLabels();
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new Excitable3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                if (brush is { } b) simulation!.Inject(b.X, b.Y, b.Z, b.Radius);
                if (keep is null && reset?.Field is null)
                {
                    int done = 0;
                    while (done < steps && !cts.IsCancellationRequested)
                    {
                        done += simulation!.Advance(Math.Min(128, steps - done), cts.Token);
                        int percent = (int)(100L * done / Math.Max(1, steps));
                        Dispatcher.BeginInvoke(() => { if (epoch == _exEpoch && !_isClosing) { _exPreparationPercent = percent; UpdateExLabels(); } });
                    }
                }
                else simulation!.Advance(steps, cts.Token);
                return simulation!.Publish(keep);
            });
            if (_isClosing || epoch != _exEpoch) return;
            _exDevice = simulation!.DeviceName;
            _exPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _exEpoch && !_isClosing)
            {
                _exRunning = false;
                _exDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                ShowPreparationError(exception.Message);
                StatusText.Text = "Ошибка расчёта среды 3D: " + exception.Message;
            }
        }
        finally
        {
            _exSimulation = simulation;
            _exBusy = false; if (ReferenceEquals(_exCts, cts)) _exCts = null; cts.Dispose();
            if (_isClosing) DisposeExSimulation();
            else
            {
                UpdateExLabels();
                if (_exRunning && !_suspended && _exPending is null && _exQueuedSteps == 0 && _exResetTo is null)
                    _exQueuedSteps = (int)ExSpeedSlider.Value;
                if (_exPending is null && (_exQueuedSteps > 0 || _exResetTo is not null || _exBrush is not null))
                    _ = RunExWorkAsync();
            }
        }
    }

    private bool IsCurrentExFrame(Fractal3DState state) => Kind != Fractal3DKind.Excitable3D ||
        Equals(state.Excitable.Live, _exPending ?? _exShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendExcitable()
    {
        if (Kind != Fractal3DKind.Excitable3D) return;
        _exCts?.Cancel(); _exQueuedSteps = 0;
    }

    private void OnExFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Excitable3D || _isClosing || _suspended) return;
        if (_exPending is not null && Equals(state.Excitable.Live, _exPending))
        { _exShown = _exPending; _exPending = null; }
        UpdateExLabels();
        if (_exRunning && !_exBusy && _exPending is null)
        { _exQueuedSteps = (int)ExSpeedSlider.Value; _ = RunExWorkAsync(); }
    }

    private void CloseExcitable()
    {
        _exRunning = false; _exEpoch++; _exCts?.Cancel();
        if (!_exBusy) DisposeExSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeExSimulation()
    {
        var simulation = _exSimulation; _exSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
