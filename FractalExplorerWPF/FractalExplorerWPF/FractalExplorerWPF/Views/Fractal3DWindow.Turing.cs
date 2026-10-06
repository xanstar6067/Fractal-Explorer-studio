using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Узоры Тьюринга 3D: моделирование и рендер делят устройство Direct3D окна, как у Gray–Scott 3D.
/// Порция шагов публикует на ГП новый кадр (<see cref="Turing3DVolume"/>), рендер рисует его без
/// копий через ЦП, и только показанный кадр запускает следующую порцию. Форма, симметрия и
/// масштабы применяются к текущему полю перед следующей порцией, время при этом не сбрасывается.
/// В оперативную память поле попадает лишь при сохранении и переносе на другую сетку.
/// </summary>
public partial class Fractal3DWindow
{
    /// <summary>Действующее правило и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    private Turing3DSettings _turingSettings = new();
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private Turing3DSettings? _turingResetTo;
    private bool _turingConfigure;
    private int? _turingResizeTo;
    private Turing3DGpuSimulation? _turingSimulation;
    private Turing3DVolume? _turingShown, _turingPending;
    private CancellationTokenSource? _turingCts;
    private bool _turingRunning, _turingBusy;
    private int _turingEpoch, _turingQueuedSteps;
    private (double X, double Y, double Z)? _turingBrush;
    private string _turingDevice = "";
    private readonly List<(CheckBox Enabled, TextBox Radius, TextBox Amount)> _turingLayerRows = [];

    private Turing3DSettings CaptureTuring() => Kind == Fractal3DKind.Turing3D
        ? _turingSettings with
        {
            Level = Math.Round(TuringLevelSlider.Value, 3),
            SheetThickness = TuringSheetBox.IsChecked == true ? Math.Round(TuringSheetSlider.Value, 3) : 0,
            CutAxis = Math.Max(0, TuringCutBox.SelectedIndex), CutPosition = TuringCutSlider.Value,
            StepsPerFrame = (int)TuringSpeedSlider.Value,
            Brush = (TuringBrush)Math.Max(0, TuringBrushBox.SelectedIndex),
            BrushRadius = Math.Round(TuringBrushRadiusSlider.Value, 3), BrushStrength = Math.Round(TuringBrushStrengthSlider.Value, 3),
            Live = _turingShown, Field = _turingShown is null ? _turingSettings.Field : null
        } : new();

    /// <summary>Показанный кадр как точное поле для файла сохранения: единственное чтение поля с ГП.</summary>
    private Turing3DSettings CheckpointTuring(Turing3DSettings settings) =>
        settings.Live is { } live ? settings with { Field = live.Source.ReadCheckpoint(live), Live = null } : settings;

    /// <summary>Правило из ползунков формы поверх применённого; масштабы меняются отдельно.</summary>
    private Turing3DSettings CaptureTuringRule() => _turingSettings with
    {
        Region = (Turing3DRegion)Math.Max(0, TuringRegionBox.SelectedIndex),
        ShellThickness = Math.Round(TuringShellSlider.Value, 3),
        DetailSize = Math.Round(TuringDetailSlider.Value, 3),
        EvolutionRate = Math.Round(TuringRateSlider.Value, 3),
        InhibitorRatio = Math.Round(TuringRatioSlider.Value, 3),
        Symmetry = (Turing3DSymmetry)Math.Max(0, TuringSymmetryBox.SelectedIndex),
        Arms = (int)TuringArmsSlider.Value,
        Mirror = TuringMirrorBox.IsChecked == true,
        Boundary = (TuringBoundary)Math.Max(0, TuringBoundaryBox.SelectedIndex)
    };

    private void LoadTuring(Turing3DSettings? settings)
    {
        if (Kind != Fractal3DKind.Turing3D) return;
        var s = (settings ?? new()) with { Live = null }; s.Validate();
        _turingEpoch++; _turingCts?.Cancel(); _turingRunning = false; _turingBrush = null;
        _turingConfigure = false; _turingResizeTo = null;
        _turingShown = _turingPending = null;
        _turingSettings = s; _turingResetTo = s;
        _turingDevice = _turingSimulation?.DeviceName ?? "ГП · подготовка устройства";
        TuringReactionEditor.Load(s.Reaction);
        TuringRegionBox.SelectedIndex = (int)s.Region; TuringShellSlider.Value = s.ShellThickness;
        TuringDetailSlider.Value = s.DetailSize; TuringDepthSlider.Value = s.Layers.Count;
        TuringRateSlider.Value = s.EvolutionRate; TuringRatioSlider.Value = s.InhibitorRatio;
        TuringSymmetryBox.SelectedIndex = (int)s.Symmetry; TuringArmsSlider.Value = s.Arms;
        TuringMirrorBox.IsChecked = s.Mirror; TuringBoundaryBox.SelectedIndex = (int)s.Boundary;
        TuringSizeBox.Text = s.Size.ToString(CultureInfo.InvariantCulture);
        TuringSeedBox.Text = s.Seed.ToString(CultureInfo.InvariantCulture);
        TuringWarmupBox.Text = s.WarmupSteps.ToString(CultureInfo.InvariantCulture);
        TuringSpeedSlider.Value = s.StepsPerFrame;
        TuringLevelSlider.Value = s.Level; TuringSheetBox.IsChecked = s.SheetThickness > 0;
        if (s.SheetThickness > 0) TuringSheetSlider.Value = s.SheetThickness;
        TuringCutBox.SelectedIndex = s.CutAxis; TuringCutSlider.Value = s.CutPosition;
        TuringBrushBox.SelectedIndex = (int)s.Brush;
        TuringBrushRadiusSlider.Value = s.BrushRadius; TuringBrushStrengthSlider.Value = s.BrushStrength;
        BuildTuringLayerRows(s.Layers);
        _turingQueuedSteps = s.InitialSteps;
        UpdateTuringLabels();
        _ = RunTuringWorkAsync();
    }

    private void BuildTuringLayerRows(IReadOnlyList<Turing3DScale> layers)
    {
        TuringLayersPanel.Children.Clear(); _turingLayerRows.Clear();
        foreach (Turing3DScale layer in layers)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var enabled = new CheckBox { IsChecked = layer.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Масштаб участвует в соревновании" };
            var radius = new TextBox { Text = layer.Radius.ToString("G6", CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 4, 0) };
            var amount = new TextBox { Text = layer.Amount.ToString("G6", CultureInfo.InvariantCulture) };
            Grid.SetColumn(radius, 1); Grid.SetColumn(amount, 2);
            row.Children.Add(enabled); row.Children.Add(radius); row.Children.Add(amount);
            TuringLayersPanel.Children.Add(row);
            _turingLayerRows.Add((enabled, radius, amount));
        }
    }

    private void UpdateTuringLabels()
    {
        if (TuringPlayButton is null || Kind != Fractal3DKind.Turing3D) return;
        var culture = CultureInfo.CurrentCulture;
        TuringMcCabePanel.Visibility = TuringMcCabeRatioPanel.Visibility = TuringScalesExpander.Visibility = _turingSettings.Reaction.IsClassical ? Visibility.Collapsed : Visibility.Visible;
        ((ComboBoxItem)ColoringModeBox.Items[(int)Fractal3DColoringMode.OrbitTrap]).Content = _turingSettings.Reaction.IsClassical ? "По концентрации U" : "По масштабу узора";
        TuringPlayButton.Content = _turingRunning ? "Ⅱ Пауза" : "▶ Продолжить";
        TuringTimeText.Text = $"Шаг {_turingShown?.Step ?? _turingSettings.Field?.Step ?? 0:N0} · {_turingSettings.Size}³ · " +
            (_turingRunning ? "развивается" : _turingBusy ? "расчёт…" : "пауза");
        TuringDeviceText.Text = _turingDevice;
        var symmetry = (Turing3DSymmetry)Math.Max(0, TuringSymmetryBox.SelectedIndex);
        TuringShellPanel.Visibility = TuringRegionBox.SelectedIndex == (int)Turing3DRegion.Shell ? Visibility.Visible : Visibility.Collapsed;
        TuringArmsPanel.Visibility = symmetry == Turing3DSymmetry.Axial ? Visibility.Visible : Visibility.Collapsed;
        TuringMirrorBox.IsEnabled = symmetry != Turing3DSymmetry.None;
        TuringShellText.Text = string.Format(culture, "Толщина оболочки · {0:P0} радиуса", TuringShellSlider.Value);
        TuringDetailText.Text = string.Format(culture, "Размер деталей · {0:F2}", TuringDetailSlider.Value);
        int depth = (int)TuringDepthSlider.Value;
        TuringDepthText.Text = $"Глубина структуры · {depth} {(depth == 1 ? "масштаб" : depth < 5 ? "масштаба" : "масштабов")}";
        TuringRateText.Text = string.Format(culture, "Отклик · {0:F2}", TuringRateSlider.Value);
        TuringRatioText.Text = string.Format(culture, "Радиус подавления · ×{0:F2} к активатору", TuringRatioSlider.Value);
        TuringArmsText.Text = Turing3DSettings.SymmetryName(Turing3DSymmetry.Axial, (int)TuringArmsSlider.Value, TuringMirrorBox.IsChecked == true);
        TuringSpeedText.Text = $"Шагов на кадр · {(int)TuringSpeedSlider.Value}";
        TuringLevelText.Text = string.Format(culture, "Уровень поверхности · {0:F2}", TuringLevelSlider.Value);
        TuringSheetSlider.IsEnabled = TuringSheetBox.IsChecked == true;
        TuringCutSlider.IsEnabled = TuringCutBox.SelectedIndex > 0;
        TuringBrushRadiusText.Text = string.Format(culture, "Радиус · {0:F2} ребра", TuringBrushRadiusSlider.Value);
        TuringBrushStrengthText.Text = string.Format(culture, "Сила · {0:P0}", TuringBrushStrengthSlider.Value);
        bool ready = !_turingBusy && _turingPending is null && _turingShown is not null;
        TuringStepButton.IsEnabled = ready && !_turingRunning;
        TuringBrushButton.IsEnabled = ready;
        UpdateCancelAvailability();
    }

    private void TuringReaction_OnChanged(object? sender, EventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Turing3D) return;
        var reaction = TuringReactionEditor.Settings;
        if (reaction.Model != _turingSettings.Reaction.Model)
        {
            var settings = CaptureTuring() with { Reaction = reaction, Field = null, Live = null, WarmupSteps = reaction.IsClassical ? 400 : 120 };
            _updatingUi = true;
            try { LoadTuring(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        else { _turingSettings = _turingSettings with { Reaction = reaction }; _turingConfigure = true; UpdateTuringLabels(); }
    }

    private void TuringPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_turingRunning) { PauseTuring(); return; }
        _turingRunning = true; UpdateTuringLabels();
        if (_turingPending is null && !_turingBusy)
        { _turingQueuedSteps = (int)TuringSpeedSlider.Value; _ = RunTuringWorkAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    private void PauseTuring()
    {
        _turingRunning = false; _turingQueuedSteps = 0; _turingCts?.Cancel();
        UpdateTuringLabels(); RequestFrame(FrameQuality.Draft);
    }

    private void TuringStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_turingBusy || _turingPending is not null) return;
        _turingRunning = false; _turingQueuedSteps = (int)TuringSpeedSlider.Value; _ = RunTuringWorkAsync();
    }

    /// <summary>Форма и симметрия: новое правило вступит в силу перед следующей порцией шагов.</summary>
    private void TuringRule_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Turing3D) return;
        _turingSettings = CaptureTuringRule(); _turingConfigure = true;
        UpdateTuringLabels(); ScheduleRender();
    }

    private void TuringDepth_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Turing3D) return;
        var layers = Turing3DSettings.DefaultLayers((int)TuringDepthSlider.Value);
        BuildTuringLayerRows(layers);
        _turingSettings = CaptureTuringRule() with { Layers = layers }; _turingConfigure = true;
        UpdateTuringLabels();
    }

    private void TuringLayersApply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var layers = _turingLayerRows.Select((row, index) => new Turing3DScale(row.Enabled.IsChecked == true,
                ReadDouble(row.Radius, $"Радиус масштаба {index + 1}", .5, 64),
                ReadDouble(row.Amount, $"Отклик масштаба {index + 1}", .001, .15))).ToArray();
            var settings = CaptureTuringRule() with { Layers = layers };
            settings.Validate();
            _turingSettings = settings; _turingConfigure = true;
            StatusText.Text = "Масштабы применятся со следующего шага.";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void TuringView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Turing3D) return;
        UpdateTuringLabels(); ScheduleRender();
    }

    private void TuringLabels_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.Turing3D) return;
        UpdateTuringLabels();
    }

    private void TuringResize_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            int size = ReadInt(TuringSizeBox, "Сетка", Turing3DField.MinSize, Turing3DField.MaxSize);
            if (size == _turingSettings.Size) return;
            _turingResizeTo = size;
            if (!_turingBusy && _turingPending is null) _ = RunTuringWorkAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void TuringNewDrawing_OnClick(object sender, RoutedEventArgs e)
    {
        TuringSeedBox.Text = Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);
        TuringRestart_OnClick(sender, e);
    }

    private void TuringRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // The view as shown, the rule from the form, and a fresh field from the typed grid and number.
            Turing3DSettings view = CaptureTuring(), rule = CaptureTuringRule();
            var settings = rule with
            {
                Size = ReadInt(TuringSizeBox, "Сетка", Turing3DField.MinSize, Turing3DField.MaxSize),
                Seed = ReadInt(TuringSeedBox, "Случайное число", int.MinValue, int.MaxValue),
                WarmupSteps = ReadInt(TuringWarmupBox, "Начальное развитие", 0, 2000),
                Level = view.Level, SheetThickness = view.SheetThickness, CutAxis = view.CutAxis, CutPosition = view.CutPosition,
                StepsPerFrame = view.StepsPerFrame, Brush = view.Brush, BrushRadius = view.BrushRadius, BrushStrength = view.BrushStrength,
                Field = null, Live = null
            };
            _updatingUi = true;
            try { LoadTuring(settings); } finally { _updatingUi = false; }
            _renderCts?.Cancel(); ScheduleRender(immediate: true);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void TuringBrush_OnClick(object sender, RoutedEventArgs e) => QueueTuringBrush(.5, .5, .5);

    private void QueueTuringBrush(double x, double y, double z)
    {
        if (_turingBusy || _turingPending is not null || _turingShown is null || _isClosing || _suspended) return;
        _turingBrush = (x, y, z); _turingQueuedSteps = 0; _ = RunTuringWorkAsync();
    }

    // Shift+click paints on the selected cutting plane; camera gestures stay available.
    private bool TryTuringBrush(MouseButtonEventArgs e)
    {
        if (Kind != Fractal3DKind.Turing3D || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (TuringCutBox.SelectedIndex == 0) { StatusText.Text = "Для кисти выберите срез X, Y или Z."; return true; }
        var point = e.GetPosition(SavePreviewLayer);
        double h = Math.Max(1, SavePreviewLayer.ActualHeight), w = Math.Max(1, SavePreviewLayer.ActualWidth);
        var pose = Pose;
        var direction = Vector3.Normalize(pose.Forward * (float)(1 / Math.Tan(_fieldOfView * Math.PI / 360)) +
            pose.Right * (float)((point.X - w / 2) / (h / 2)) + pose.Up * (float)((h / 2 - point.Y) / (h / 2)));
        int axis = TuringCutBox.SelectedIndex - 1;
        float component = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
        float origin = axis == 0 ? pose.Position.X : axis == 1 ? pose.Position.Y : pose.Position.Z;
        if (Math.Abs(component) < 1e-6) return true;
        double distance = (TuringCutSlider.Value - origin) / component;
        var hit = pose.Position + direction * (float)distance;
        if (distance > 0 && Math.Abs(hit.X) <= 1 && Math.Abs(hit.Y) <= 1 && Math.Abs(hit.Z) <= 1)
            QueueTuringBrush((hit.X + 1) / 2, (hit.Y + 1) / 2, (hit.Z + 1) / 2);
        return true;
    }

    /// <summary>
    /// Одна порция работы на ГП: перезапуск или новое правило, перенос на другую сетку, кисть,
    /// шаги и публикация кадра. Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunTuringWorkAsync()
    {
        if (_turingBusy || _isClosing || _suspended || Kind != Fractal3DKind.Turing3D) return;
        int steps = _turingQueuedSteps, epoch = _turingEpoch; var brush = _turingBrush;
        var reset = _turingResetTo ?? (_turingSimulation is null ? _turingSettings : null);
        var rule = _turingSettings;
        bool configure = _turingConfigure && reset is null;
        int? resize = _turingResizeTo;
        var stroke = (Radius: TuringBrushRadiusSlider.Value, Strength: TuringBrushStrengthSlider.Value,
            Kind: (TuringBrush)Math.Max(0, TuringBrushBox.SelectedIndex));
        var keep = _turingShown;
        _turingQueuedSteps = 0; _turingBrush = null; _turingResetTo = null; _turingConfigure = false; _turingResizeTo = null;
        _turingBusy = true;
        var cts = new CancellationTokenSource(); _turingCts = cts;
        var host = _renderer.DeviceHost;
        var simulation = _turingSimulation;
        Turing3DField? resized = null;
        UpdateTuringLabels();
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = new Turing3DGpuSimulation(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                else if (configure) simulation!.Configure(rule with { Size = simulation.Size });
                if (resize is int size && size != simulation!.Size)
                {
                    resized = Turing3DField.Resize(simulation.ReadCurrent(), size);
                    simulation.Reset(rule with { Size = size, Field = resized });
                    keep = null;
                }
                if (brush is { } b) simulation!.Paint(b.X, b.Y, b.Z, stroke.Radius, stroke.Strength, stroke.Kind);
                simulation!.Advance(steps, cts.Token);
                return simulation.Publish(keep);
            });
            _turingDevice = simulation!.DeviceName;
            if (_isClosing || epoch != _turingEpoch) return;
            if (resized is not null) _turingSettings = _turingSettings with { Size = resized.Size, Field = resized };
            _turingPending = volume;
            RequestFrame(FrameQuality.Draft);
        }
        catch (Exception exception)
        {
            if (epoch == _turingEpoch && !_isClosing)
            {
                _turingRunning = false;
                _turingDevice = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                StatusText.Text = "Ошибка узоров Тьюринга 3D: " + exception.Message;
            }
        }
        finally
        {
            _turingSimulation = simulation;
            _turingBusy = false; if (ReferenceEquals(_turingCts, cts)) _turingCts = null; cts.Dispose();
            if (_isClosing) DisposeTuringSimulation();
            else
            {
                UpdateTuringLabels();
                if (_turingRunning && !_suspended && _turingPending is null && _turingQueuedSteps == 0 && _turingResetTo is null)
                    _turingQueuedSteps = (int)TuringSpeedSlider.Value;
                if (_turingPending is null && (_turingQueuedSteps > 0 || _turingResetTo is not null || _turingBrush is not null || _turingResizeTo is not null))
                    _ = RunTuringWorkAsync();
            }
        }
    }

    private bool IsCurrentTuringFrame(Fractal3DState state) => Kind != Fractal3DKind.Turing3D ||
        Equals(state.Turing.Live, _turingPending ?? _turingShown);

    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    private void SuspendTuring()
    {
        if (Kind != Fractal3DKind.Turing3D) return;
        _turingCts?.Cancel(); _turingQueuedSteps = 0;
    }

    private void OnTuringFrameDisplayed(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.Turing3D || _isClosing || _suspended) return;
        if (_turingPending is not null && Equals(state.Turing.Live, _turingPending))
        { _turingShown = _turingPending; _turingPending = null; }
        UpdateTuringLabels();
        if (_turingBusy || _turingPending is not null) return;
        if (_turingRunning) { _turingQueuedSteps = (int)TuringSpeedSlider.Value; _ = RunTuringWorkAsync(); }
        else if (_turingResizeTo is not null || _turingBrush is not null) _ = RunTuringWorkAsync();
    }

    private void CloseTuring()
    {
        _turingRunning = false; _turingEpoch++; _turingCts?.Cancel();
        if (!_turingBusy) DisposeTuringSimulation();
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeTuringSimulation()
    {
        var simulation = _turingSimulation; _turingSimulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
