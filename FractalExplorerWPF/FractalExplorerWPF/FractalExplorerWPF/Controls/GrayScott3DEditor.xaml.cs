using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Панель Gray–Scott 3D: уравнение, затравка, вид, сферическая кисть и поиск узоров. Кандидаты
/// поиска проходят пробный прогон на ГП окна (<see cref="GrayScottRandomizer.Search3D"/>)
/// вперемежку с полосами кадра, найденный вид запускается как «Применить и начать заново».
/// «Вернуть предыдущий» восстанавливает точное поле и параметры до последней находки.
/// </summary>
public partial class GrayScott3DEditor : UserControl, IFractal3DSimulationEditor<GrayScott3DSettings>
{
    private const int SearchHistoryDepth = 4;

    private readonly IFractal3DSimulationHost _host;
    private readonly List<GrayScott3DSettings> _searchHistory = [];
    private CancellationTokenSource? _searchCts;
    private bool _searchVariation;
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal GrayScott3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Gray–Scott 3D",
            Create = (device, s) => new GrayScott3DGpuSimulation(device, s),
            Get = state => state.GrayScott,
            Set = (state, s) => state.GrayScott = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.InitialSteps : 0,
            PreparationMessage = _ => "Подготовка поля Gray–Scott 3D…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Gray–Scott · {done:N0} / {steps:N0} шагов"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<GrayScott3DGpuSimulation, GrayScott3DSettings, GrayScott3DField, GrayScott3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)GraySpeedSlider.Value;
    public bool IsSearching => _searchCts is not null;

    public void Show(GrayScott3DSettings s)
    {
        _loading = true;
        try
        {
            GraySizeBox.Text = s.Size.ToString(); GrayFeedBox.Text = Format(s.Feed); GrayKillBox.Text = Format(s.Kill);
            GrayDuBox.Text = Format(s.DiffusionU); GrayDvBox.Text = Format(s.DiffusionV);
            GraySeedBox.SelectedIndex = (int)s.SeedShape; GrayRandomBox.Text = s.Seed.ToString();
            GraySpeedSlider.Value = s.StepsPerFrame; GrayThresholdSlider.Value = s.Threshold;
            GrayCutBox.SelectedIndex = s.CutAxis; GrayCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    public GrayScott3DSettings CaptureView(GrayScott3DSettings applied) => applied with
    {
        Threshold = GrayThresholdSlider.Value, CutAxis = Math.Max(0, GrayCutBox.SelectedIndex),
        CutPosition = GrayCutSlider.Value, StepsPerFrame = (int)GraySpeedSlider.Value
    };

    public void Refresh()
    {
        GrayDeviceText.Text = Simulation.Device;
        GrayCutSlider.IsEnabled = GrayCutBox.SelectedIndex > 0;
        GrayBrushButton.IsEnabled = Simulation.CanBrush;
        UpdateSearchButtons();
    }

    private void GrayRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Simulation.Restart(new GrayScott3DSettings
            {
                Size = ReadInt(GraySizeBox, "Сетка", 32, 128), Feed = ReadDouble(GrayFeedBox, "F", 0, .1),
                Kill = ReadDouble(GrayKillBox, "K", 0, .1), DiffusionU = ReadDouble(GrayDuBox, "Диффузия U", .001, .16),
                DiffusionV = ReadDouble(GrayDvBox, "Диффузия V", .001, .16),
                Seed = ReadInt(GrayRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (GrayScott3DSeed)Math.Max(0, GraySeedBox.SelectedIndex),
                StepsPerFrame = (int)GraySpeedSlider.Value, Threshold = GrayThresholdSlider.Value,
                CutAxis = Math.Max(0, GrayCutBox.SelectedIndex), CutPosition = GrayCutSlider.Value
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void GrayView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void GrayBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);

    private void Brush(double x, double y, double z)
    {
        double radius = GrayBrushSlider.Value;
        Simulation.Brush(simulation => simulation.Inject(x, y, z, radius));
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (GrayCutBox.SelectedIndex == 0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, GrayCutBox.SelectedIndex - 1, GrayCutSlider.Value, out var point))
            Brush(point.X, point.Y, point.Z);
        return true;
    }

    #region Поиск узоров

    private void GraySearchRandom_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(false);

    private void GraySearchVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(true);

    private async Task RunSearchAsync(bool variation)
    {
        if (_searchCts is not null) { _searchCts.Cancel(); return; }
        if (_host.IsClosing) return;
        var current = Simulation.Settings with { Field = null, Live = null };
        var target = (GrayScottPatternTarget)Math.Clamp(GraySearchTargetBox.SelectedIndex, 0, 4);
        var host = _host.DeviceHost;
        var cts = new CancellationTokenSource(); _searchCts = cts; _searchVariation = variation;
        Simulation.NotifyChanged();
        GraySearchStatus.Text = variation ? "Ищем вариацию рядом с текущими F/K…" : "Ищем узор…";
        var progress = new Progress<GrayScottSearchProgress>(p =>
        {
            if (ReferenceEquals(_searchCts, cts)) GraySearchStatus.Text = $"Проверено вариантов: {p.Checked}, живых: {p.Alive}…";
        });
        try
        {
            var found = await Task.Run(() => GrayScottRandomizer.Search3D(host, current, target, variation, progress, cts.Token), cts.Token);
            if (_host.IsClosing) return;
            cts.Token.ThrowIfCancellationRequested();
            if (found is null)
            {
                GraySearchStatus.Text = variation
                    ? "Рядом с текущими F/K живых узоров не нашлось. Попробуйте «Случайный узор»."
                    : "Живых узоров не нашлось — попробуйте ещё раз.";
                return;
            }
            // Скорость и срез могли поменять во время поиска — берём действующие.
            var settings = found.State with
            {
                StepsPerFrame = (int)GraySpeedSlider.Value, CutAxis = Math.Max(0, GrayCutBox.SelectedIndex), CutPosition = GrayCutSlider.Value
            };
            RememberForUndo();
            _searchCts = null;
            Simulation.Restart(settings);
            string note = found.MatchesTarget ? string.Empty : " Узор выбранного вида не встретился — показан лучший из живых.";
            GraySearchStatus.Text = $"{found.Metrics.Describe(true)} · F {Format(settings.Feed)} · K {Format(settings.Kill)} · " +
                $"уровень {Format(settings.Threshold)} · проверено вариантов: {found.Checked}.{note}";
        }
        catch (OperationCanceledException)
        {
            if (!_host.IsClosing) GraySearchStatus.Text = "Поиск остановлен.";
        }
        catch (Exception exception)
        {
            if (!_host.IsClosing) GraySearchStatus.Text = $"Поиск не удался: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
            cts.Dispose();
            if (!_host.IsClosing) Simulation.NotifyChanged();
        }
    }

    /// <summary>Точное показанное поле (одно чтение с ГП); если кадр уже заменён — исходная затравка.</summary>
    private void RememberForUndo()
    {
        GrayScott3DSettings previous;
        try { previous = Simulation.Checkpoint(Simulation.Capture()); }
        catch (InvalidOperationException) { previous = Simulation.Capture() with { Live = null, Field = Simulation.Settings.Field }; }
        _searchHistory.Add(previous);
        if (_searchHistory.Count > SearchHistoryDepth) _searchHistory.RemoveAt(0);
    }

    private void GraySearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null || _searchHistory.Count == 0) return;
        var settings = _searchHistory[^1];
        _searchHistory.RemoveAt(_searchHistory.Count - 1);
        try
        {
            Simulation.Restart(settings);
            GraySearchStatus.Text = $"Возвращён предыдущий вид · F {Format(settings.Feed)} · K {Format(settings.Kill)}.";
        }
        catch (Exception exception) { GraySearchStatus.Text = $"Не удалось вернуть вид: {exception.Message}"; }
        UpdateSearchButtons();
    }

    /// <summary>Загрузка, пресет или перезапуск отменяют поиск: находка не должна их перекрыть.</summary>
    public void CancelSearch() => _searchCts?.Cancel();

    private void UpdateSearchButtons()
    {
        bool searching = _searchCts is not null;
        GraySearchButton.Content = searching && !_searchVariation ? "Остановить поиск" : "Случайный узор";
        GrayVariationButton.Content = searching && _searchVariation ? "Остановить поиск" : "Вариация";
        GraySearchButton.IsEnabled = !searching || !_searchVariation;
        GrayVariationButton.IsEnabled = !searching || _searchVariation;
        GraySearchTargetBox.IsEnabled = !searching;
        GraySearchUndoButton.IsEnabled = !searching && _searchHistory.Count > 0;
    }

    #endregion
}
