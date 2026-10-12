using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Панель Lenia 3D: правило роста, сферическое ядро, затравка и поиск живых форм. Пробные прогоны
/// поиска идут на своих буферах того же устройства; находка запускается как «начать заново»,
/// а четыре предыдущих точных поля можно вернуть.
/// </summary>
public partial class Lenia3DEditor : UserControl, IFractal3DSimulationEditor<Lenia3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private readonly List<Lenia3DSettings> _searchHistory = [];
    private CancellationTokenSource? _searchCts;
    private bool _searchVariation;
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal Lenia3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Lenia 3D",
            Create = (device, s) => new Lenia3DGpuSimulation(device, s),
            Get = state => state.Lenia,
            Set = (state, s) => state.Lenia = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage = _ => "Подготовка Lenia…",
            Time = (shown, s) => $"t = {shown?.Time ?? s.Field?.Time ?? 0:F1} · шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Lenia · {done:N0} / {steps:N0} шагов"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<Lenia3DGpuSimulation, Lenia3DSettings, Lenia3DField, Lenia3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)LeniaSpeedSlider.Value;
    public bool IsSearching => _searchCts is not null;
    public bool TryBrush(MouseButtonEventArgs e) => false;

    public void Show(Lenia3DSettings s)
    {
        _loading = true;
        try
        {
            LeniaSearchStatus.Text = s.Field is null ? "Выберите поиск или вариацию после подготовки." : "Показано сохранённое поле. Можно продолжить развитие или найти вариацию.";
            LeniaSizeBox.SelectedIndex = s.Size == 32 ? 0 : s.Size == 64 ? 1 : 2;
            LeniaRadiusBox.Text = Format(s.Radius); LeniaMeanBox.Text = Format(s.GrowthMean); LeniaWidthBox.Text = Format(s.GrowthWidth);
            LeniaShellCountBox.SelectedIndex = s.ShellCount - 1;
            LeniaBeta1Box.Text = Format(s.Beta1); LeniaBeta2Box.Text = Format(s.Beta2); LeniaBeta3Box.Text = Format(s.Beta3); LeniaBeta4Box.Text = Format(s.Beta4);
            LeniaDtBox.Text = Format(s.TimeStep); LeniaGrowthBox.SelectedIndex = (int)s.Growth;
            LeniaSeedBox.SelectedIndex = (int)s.SeedShape; LeniaRandomBox.Text = s.Seed.ToString(); LeniaNoiseBox.Text = Format(s.SeedNoise);
            LeniaWarmupBox.Text = s.WarmupSteps.ToString();
            LeniaSpeedSlider.Value = s.StepsPerFrame; LeniaLevelSlider.Value = s.Threshold;
            LeniaCutBox.SelectedIndex = s.CutAxis; LeniaCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    public Lenia3DSettings CaptureView(Lenia3DSettings applied) => applied with
    {
        Threshold = LeniaLevelSlider.Value, CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex),
        CutPosition = LeniaCutSlider.Value, StepsPerFrame = (int)LeniaSpeedSlider.Value
    };

    public void Refresh()
    {
        LeniaDeviceText.Text = Simulation.Device;
        LeniaCutSlider.IsEnabled = LeniaCutBox.SelectedIndex > 0;
        UpdateSearchButtons();
    }

    private void LeniaRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Simulation.Restart(new Lenia3DSettings
            {
                Size = LeniaSizeBox.SelectedIndex == 0 ? 32 : LeniaSizeBox.SelectedIndex == 1 ? 64 : 128,
                Radius = ReadDouble(LeniaRadiusBox, "Радиус ядра", 2, 63),
                GrowthMean = ReadDouble(LeniaMeanBox, "μ", .01, .5), GrowthWidth = ReadDouble(LeniaWidthBox, "σ", .002, .15),
                ShellCount = Math.Clamp(LeniaShellCountBox.SelectedIndex + 1, 1, 4),
                Beta1 = ReadDouble(LeniaBeta1Box, "β₁", 0, 1), Beta2 = ReadDouble(LeniaBeta2Box, "β₂", 0, 1),
                Beta3 = ReadDouble(LeniaBeta3Box, "β₃", 0, 1), Beta4 = ReadDouble(LeniaBeta4Box, "β₄", 0, 1),
                Growth = (LeniaGrowth)Math.Max(0, LeniaGrowthBox.SelectedIndex),
                TimeStep = ReadDouble(LeniaDtBox, "Δt", .01, .2), WarmupSteps = ReadInt(LeniaWarmupBox, "Подготовка", 0, 3000),
                Seed = ReadInt(LeniaRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedNoise = ReadDouble(LeniaNoiseBox, "Шум затравки", 0, .15),
                SeedShape = (Lenia3DSeed)Math.Max(0, LeniaSeedBox.SelectedIndex),
                StepsPerFrame = (int)LeniaSpeedSlider.Value, Threshold = LeniaLevelSlider.Value,
                CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex), CutPosition = LeniaCutSlider.Value
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void LeniaView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    #region Поиск форм

    private void LeniaSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(false);
    private void LeniaVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(true);

    private async Task RunSearchAsync(bool variation)
    {
        if (_searchCts is not null) { CancelSearch(); return; }
        var live = Simulation;
        if (_host.IsClosing || _host.IsSuspended || live.Preparing || live.Shown is null) return;
        int epoch = live.Epoch;
        var cts = new CancellationTokenSource(); _searchCts = cts; _searchVariation = variation;
        live.Pause(); UpdateSearchButtons();
        LeniaSearchProgress.Value = 0;
        LeniaSearchStatus.Text = variation ? "Подбираем близкие параметры…" : "Ищем новую форму…";
        bool Current() => !_host.IsClosing && epoch == live.Epoch && ReferenceEquals(_searchCts, cts);
        var progress = new Progress<LeniaSearchProgress>(p =>
        {
            if (Current() && !cts.IsCancellationRequested)
            {
                LeniaSearchProgress.Value = p.Finalizing ? 95 : 85.0 * p.Checked / p.Total;
                LeniaSearchStatus.Text = p.Finalizing ? "Проверяем длительное выживание…" : $"Проверено форм: {p.Checked} / {p.Total}";
            }
        });
        try
        {
            // Let an already submitted live batch finish; trials have their own buffers.
            while (live.Busy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var current = variation ? live.Checkpoint(live.Capture()) : live.Capture() with { Field = null, Live = null };
            var host = _host.DeviceHost.AddRef();
            LeniaSearchResult? found;
            try { found = await Task.Run(() => Lenia3DRandomizer.Search(host, current, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { LeniaSearchStatus.Text = "Устойчивых форм не встретилось. Попробуйте ещё раз или выберите готовый вид."; return; }
            var previous = live.Checkpoint(live.Capture());
            var settings = found.Settings with { StepsPerFrame = (int)LeniaSpeedSlider.Value,
                CutAxis = Math.Max(0, LeniaCutBox.SelectedIndex), CutPosition = LeniaCutSlider.Value };
            _searchCts = null;
            live.Restart(settings);
            _searchHistory.Add(previous);
            if (_searchHistory.Count > 4) _searchHistory.RemoveAt(0);
            LeniaSearchProgress.Value = 100;
            LeniaSearchStatus.Text = $"Найдена форма · проверено {found.Checked} вариантов. Выжила в пробном прогоне; продолжите развитие или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) LeniaSearchStatus.Text = "Поиск остановлен. Поле сохранено."; }
        catch (Exception exception) { if (Current()) LeniaSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
            cts.Dispose();
            if (!_host.IsClosing) live.NotifyChanged();
        }
    }

    private void LeniaSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null || _searchHistory.Count == 0) return;
        try
        {
            Simulation.Restart(_searchHistory[^1]);
            _searchHistory.RemoveAt(_searchHistory.Count - 1);
            LeniaSearchStatus.Text = "Возвращена предыдущая форма и точное поле клеток.";
            LeniaSearchProgress.Value = 0;
        }
        catch (Exception exception) { LeniaSearchStatus.Text = "Не удалось вернуть форму: " + exception.Message; }
        UpdateSearchButtons();
    }

    public void CancelSearch() => _searchCts?.Cancel();

    private void UpdateSearchButtons()
    {
        bool searching = _searchCts is not null;
        bool ready = !Simulation.Preparing && Simulation.Shown is not null && !_host.IsSuspended && !_host.IsClosing;
        LeniaSearchButton.Content = searching && !_searchVariation ? "Остановить поиск" : "Найти живую форму";
        LeniaVariationButton.Content = searching && _searchVariation ? "Остановить поиск" : "Вариация текущей";
        LeniaSearchButton.IsEnabled = searching ? !_searchVariation : ready;
        LeniaVariationButton.IsEnabled = searching ? _searchVariation : ready;
        LeniaSearchUndoButton.IsEnabled = !searching && !Simulation.Preparing && _searchHistory.Count > 0;
        LeniaSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
