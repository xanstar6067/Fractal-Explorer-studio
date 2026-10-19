using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Панель Physarum 3D: агенты, след и поиск выразительных сетей. Пробные прогоны поиска идут на
/// своих буферах того же устройства; четыре предыдущие сети вместе с агентами можно вернуть.
/// </summary>
public partial class Physarum3DEditor : UserControl, IFractal3DSimulationEditor<Physarum3DSettings>
{
    private static readonly int[] Sizes = [48, 64, 96, 128];

    private readonly IFractal3DSimulationHost _host;
    private readonly List<Physarum3DSettings> _searchHistory = [];
    private CancellationTokenSource? _searchCts;
    private bool _searchVariation;
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal Physarum3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Physarum 3D",
            Create = (device, s) => new Physarum3DGpuSimulation(device, s),
            Get = state => state.Physarum,
            Set = (state, s) => state.Physarum = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage = _ => "Подготовка живой сети…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Плетение сети · {done:N0} / {steps:N0} шагов"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<Physarum3DGpuSimulation, Physarum3DSettings, Physarum3DField, Physarum3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)PhysarumSpeedSlider.Value;
    public bool IsSearching => _searchCts is not null;
    public bool TryBrush(MouseButtonEventArgs e) => false;

    public void Show(Physarum3DSettings s)
    {
        _loading = true;
        try
        {
            PhysarumSizeBox.SelectedIndex = Array.IndexOf(Sizes, s.Size);
            PhysarumCountBox.Text = s.AgentCount.ToString();
            PhysarumSensorBox.Text = Format(s.SensorDistance); PhysarumAngleBox.Text = Format(s.SensorAngle);
            PhysarumTurnBox.Text = Format(s.TurnAngle); PhysarumMoveBox.Text = Format(s.Speed);
            PhysarumDepositBox.Text = Format(s.Deposit); PhysarumDiffusionBox.Text = Format(s.Diffusion);
            PhysarumDecayBox.Text = Format(s.Decay); PhysarumSeedBox.SelectedIndex = (int)s.SeedShape;
            PhysarumRandomBox.Text = s.Seed.ToString(); PhysarumWarmupBox.Text = s.WarmupSteps.ToString();
            PhysarumSpeedSlider.Value = s.StepsPerFrame; PhysarumLevelSlider.Value = s.Threshold;
            PhysarumExposureSlider.Value = s.Exposure;
            PhysarumCutBox.SelectedIndex = s.CutAxis; PhysarumCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
        InitializeNumericInput();
    }

    public Physarum3DSettings CaptureView(Physarum3DSettings applied) => applied with
    {
        Threshold = PhysarumLevelSlider.Value, Exposure = PhysarumExposureSlider.Value,
        CutAxis = Math.Max(0, PhysarumCutBox.SelectedIndex), CutPosition = PhysarumCutSlider.Value,
        StepsPerFrame = (int)PhysarumSpeedSlider.Value
    };

    public void Refresh()
    {
        PhysarumDeviceText.Text = Simulation.Device;
        PhysarumCutSlider.IsEnabled = PhysarumCutBox.SelectedIndex > 0;
        UpdateSearchButtons();
    }

    private void PhysarumRestart_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            Simulation.Restart(new Physarum3DSettings
            {
                Size = Sizes[Math.Clamp(PhysarumSizeBox.SelectedIndex, 0, 3)],
                AgentCount = ReadInt(PhysarumCountBox, "Агентов", 1024, 262144),
                SensorDistance = ReadDouble(PhysarumSensorBox, "Дальность чувств", 1, 16),
                SensorAngle = ReadDouble(PhysarumAngleBox, "Угол обзора", 5, 85),
                TurnAngle = ReadDouble(PhysarumTurnBox, "Поворот", 1, 70), Speed = ReadDouble(PhysarumMoveBox, "Скорость", .1, 2),
                Deposit = ReadDouble(PhysarumDepositBox, "След", .1, 8), Diffusion = ReadDouble(PhysarumDiffusionBox, "Размывание", 0, 1),
                Decay = ReadDouble(PhysarumDecayBox, "Испарение", .001, .2),
                WarmupSteps = ReadInt(PhysarumWarmupBox, "Подготовка", 0, 2000),
                Seed = ReadInt(PhysarumRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (Physarum3DSeed)Math.Max(0, PhysarumSeedBox.SelectedIndex),
                StepsPerFrame = (int)PhysarumSpeedSlider.Value, Threshold = PhysarumLevelSlider.Value,
                Exposure = PhysarumExposureSlider.Value, CutAxis = Math.Max(0, PhysarumCutBox.SelectedIndex),
                CutPosition = PhysarumCutSlider.Value
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void PhysarumView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void PhysarumRandom_OnClick(object sender, RoutedEventArgs e)
    {
        PhysarumRandomBox.Text = Random.Shared.Next().ToString();
        PhysarumRestart_OnClick(sender, e);
    }

    #region Поиск форм

    private void PhysarumSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(false);
    private void PhysarumVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(true);

    private async Task RunSearchAsync(bool variation)
    {
        if (_searchCts is not null) { CancelSearch(); return; }
        var live = Simulation;
        if (_host.IsClosing || _host.IsSuspended || live.Preparing || live.Shown is null) return;
        int epoch = live.Epoch;
        var cts = new CancellationTokenSource(); _searchCts = cts; _searchVariation = variation;
        live.Pause(); UpdateSearchButtons();
        PhysarumSearchProgress.Value = 0;
        PhysarumSearchStatus.Text = variation ? "Подбираем близкие параметры…" : "Ищем новую форму…";
        bool Current() => !_host.IsClosing && epoch == live.Epoch && ReferenceEquals(_searchCts, cts);
        var progress = new Progress<PhysarumSearchProgress>(p =>
        {
            if (Current() && !cts.IsCancellationRequested)
            {
                PhysarumSearchProgress.Value = p.Finalizing ? 95 : 85.0 * p.Checked / p.Total;
                PhysarumSearchStatus.Text = p.Finalizing ? "Плетём выбранную сеть в полном размере…" : $"Проверено форм: {p.Checked} / {p.Total}";
            }
        });
        try
        {
            // Let an already submitted live batch finish; trials have their own buffers.
            while (live.Busy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var current = live.Capture() with { Field = null, Live = null };
            var host = _host.DeviceHost.AddRef();
            PhysarumSearchResult? found;
            try { found = await Task.Run(() => Physarum3DRandomizer.Search(host, current, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { PhysarumSearchStatus.Text = "Выразительных сетей не встретилось. Попробуйте ещё раз."; return; }
            var previous = live.Checkpoint(live.Capture());
            var settings = found.Settings with { StepsPerFrame = (int)PhysarumSpeedSlider.Value,
                CutAxis = Math.Max(0, PhysarumCutBox.SelectedIndex), CutPosition = PhysarumCutSlider.Value };
            _searchCts = null;
            live.Restart(settings);
            _searchHistory.Add(previous);
            if (_searchHistory.Count > 4) _searchHistory.RemoveAt(0);
            PhysarumSearchProgress.Value = 100;
            PhysarumSearchStatus.Text = $"Найдена сеть · проверено {found.Checked} форм. Продолжите развитие или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) PhysarumSearchStatus.Text = "Поиск остановлен. Сеть сохранена."; }
        catch (Exception exception) { if (Current()) PhysarumSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
            cts.Dispose();
            if (!_host.IsClosing) live.NotifyChanged();
        }
    }

    private void PhysarumSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null || _searchHistory.Count == 0) return;
        try
        {
            Simulation.Restart(_searchHistory[^1]);
            _searchHistory.RemoveAt(_searchHistory.Count - 1);
            PhysarumSearchStatus.Text = "Возвращена предыдущая сеть и точное состояние агентов.";
            PhysarumSearchProgress.Value = 0;
        }
        catch (Exception exception) { PhysarumSearchStatus.Text = "Не удалось вернуть сеть: " + exception.Message; }
        UpdateSearchButtons();
    }

    public void CancelSearch() => _searchCts?.Cancel();

    private void UpdateSearchButtons()
    {
        bool searching = _searchCts is not null;
        bool ready = !Simulation.Preparing && Simulation.Shown is not null && !_host.IsSuspended && !_host.IsClosing;
        PhysarumSearchButton.Content = searching && !_searchVariation ? "Остановить поиск" : "Найти интересную форму";
        PhysarumVariationButton.Content = searching && _searchVariation ? "Остановить поиск" : "Вариация текущей";
        PhysarumSearchButton.IsEnabled = searching ? !_searchVariation : ready;
        PhysarumVariationButton.IsEnabled = searching ? _searchVariation : ready;
        PhysarumSearchUndoButton.IsEnabled = !searching && !Simulation.Preparing && _searchHistory.Count > 0;
        PhysarumSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
