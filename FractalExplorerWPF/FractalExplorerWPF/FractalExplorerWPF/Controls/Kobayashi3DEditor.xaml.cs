using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Панель фазового поля Кобаяси 3D: среда, затравка, вид, кисть и поиск форм кристалла. Найденная
/// форма может вписываться в кадр; возврат восстанавливает точное поле, шаг роста и камеру.
/// </summary>
public partial class Kobayashi3DEditor : UserControl, IFractal3DSimulationEditor<Kobayashi3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private readonly List<(Kobayashi3DSettings Settings, Fractal3DPose Pose)> _searchHistory = [];
    private CancellationTokenSource? _searchCts;
    private bool _searchVariation;
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal Kobayashi3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Кобаяси 3D",
            Create = (device, s) => new Kobayashi3DGpuSimulation(device, s),
            Get = state => state.Kobayashi,
            Set = (state, s) => state.Kobayashi = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.InitialSteps : 0,
            PreparationMessage = _ => "Подготовка кристалла…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Выращивание кристалла: {done:N0} / {steps:N0} шагов"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<Kobayashi3DGpuSimulation, Kobayashi3DSettings, Kobayashi3DField, Kobayashi3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)KobSpeedSlider.Value;
    public bool IsSearching => _searchCts is not null;

    public void Show(Kobayashi3DSettings s)
    {
        _loading = true;
        try
        {
            KobSizeBox.Text = s.Size.ToString();
            KobWidthBox.Text = Format(s.InterfaceWidth); KobAnisotropyBox.Text = Format(s.Anisotropy);
            KobMobilityBox.Text = Format(s.Mobility); KobDiffusionBox.Text = Format(s.ThermalDiffusion);
            KobHeatBox.Text = Format(s.LatentHeat); KobColdBox.Text = Format(s.Undercooling);
            KobNoiseBox.Text = Format(s.Noise); KobDtBox.Text = Format(s.TimeStep);
            KobWarmupBox.Text = s.WarmupSteps.ToString();
            KobSeedBox.SelectedIndex = (int)s.SeedShape; KobRandomBox.Text = s.Seed.ToString();
            KobSeedRadiusBox.Text = Format(s.SeedRadius); KobSeedSpreadBox.Text = Format(s.SeedSpread);
            KobSeedCountBox.Text = s.SeedCount.ToString();
            KobSpeedSlider.Value = s.StepsPerFrame; KobThresholdSlider.Value = s.Threshold;
            KobCutBox.SelectedIndex = s.CutAxis; KobCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    public Kobayashi3DSettings CaptureView(Kobayashi3DSettings applied) => applied with
    {
        Threshold = KobThresholdSlider.Value, CutAxis = Math.Max(0, KobCutBox.SelectedIndex),
        CutPosition = KobCutSlider.Value, StepsPerFrame = (int)KobSpeedSlider.Value
    };

    public void Refresh()
    {
        KobDeviceText.Text = Simulation.Device;
        KobDtText.Text = $"Фактический шаг времени: {Simulation.Settings.EffectiveTimeStep:G3}";
        KobCutSlider.IsEnabled = KobCutBox.SelectedIndex > 0;
        KobBrushButton.IsEnabled = Simulation.CanBrush;
        UpdateSearchButtons();
    }

    private void KobRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Simulation.Restart(new Kobayashi3DSettings
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
                SeedRadius = ReadDouble(KobSeedRadiusBox, "Радиус зародыша", .035, .16),
                SeedSpread = ReadDouble(KobSeedSpreadBox, "Разброс зародышей", 0, .3),
                SeedCount = ReadInt(KobSeedCountBox, "Число зародышей", 1, 8),
                StepsPerFrame = (int)KobSpeedSlider.Value, Threshold = KobThresholdSlider.Value,
                CutAxis = Math.Max(0, KobCutBox.SelectedIndex), CutPosition = KobCutSlider.Value
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void KobView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void KobBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);

    private void Brush(double x, double y, double z)
    {
        double radius = KobBrushSlider.Value;
        Simulation.Brush(simulation => simulation.Inject(x, y, z, radius));
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (KobCutBox.SelectedIndex == 0) { _host.ShowStatus("Для затравки выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, KobCutBox.SelectedIndex - 1, KobCutSlider.Value, out var point))
            Brush(point.X, point.Y, point.Z);
        return true;
    }

    #region Поиск форм

    private void KobSearch_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(false);
    private void KobVariation_OnClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync(true);

    private async Task RunSearchAsync(bool variation)
    {
        if (_searchCts is not null) { CancelSearch(); return; }
        var live = Simulation;
        if (_host.IsClosing || _host.IsSuspended || live.Shown is null || live.ResetPending || live.Preparing) return;
        int epoch = live.Epoch;
        var cts = new CancellationTokenSource(); _searchCts = cts; _searchVariation = variation;
        live.Pause();
        KobSearchProgress.Value = 0;
        KobSearchStatus.Text = variation ? "Подбираем близкий кристалл…" : "Ищем новую форму кристалла…";
        bool Current() => !_host.IsClosing && !_host.IsSuspended && epoch == live.Epoch && ReferenceEquals(_searchCts, cts);
        var progress = new Progress<KobayashiSearchProgress>(p =>
        {
            if (!Current() || cts.IsCancellationRequested) return;
            KobSearchProgress.Value = p.Finalizing ? 85 + 15 * p.Fraction : 85.0 * (p.Checked + p.Fraction) / p.Total;
            KobSearchStatus.Text = p.Finalizing ? $"Проверка на выбранной сетке: {p.Fraction:P0}" :
                $"Проверка форм: {Math.Min(p.Checked + 1, p.Total)} / {p.Total} · {p.Fraction:P0}";
        });
        try
        {
            // Submitted steps can finish; trial simulations never touch the window's buffers.
            while (live.Busy) await Task.Delay(10, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            var captured = live.Capture();
            var previous = await Task.Run(() => live.Checkpoint(captured), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var host = _host.DeviceHost.AddRef();
            KobayashiSearchResult? found;
            try { found = await Task.Run(() => Kobayashi3DRandomizer.Search(host,
                previous with { Field = null, Live = null }, variation, progress, cts.Token), cts.Token); }
            finally { host.Release(); }
            cts.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (found is null) { KobSearchStatus.Text = "Выразительных растущих кристаллов не встретилось. Попробуйте ещё раз."; return; }
            var settings = found.Settings with { StepsPerFrame = (int)KobSpeedSlider.Value,
                Threshold = KobThresholdSlider.Value, CutAxis = Math.Max(0, KobCutBox.SelectedIndex), CutPosition = KobCutSlider.Value };
            var previousPose = _host.Pose;
            _searchCts = null;
            Apply(settings, fit: KobSearchFitBox.IsChecked == true);
            _searchHistory.Add((previous, previousPose));
            if (_searchHistory.Count > 4) _searchHistory.RemoveAt(0);
            KobSearchProgress.Value = 100;
            KobSearchStatus.Text = $"Найден кристалл · проверено {found.Checked} форм. Продолжите рост или попробуйте вариацию.";
        }
        catch (OperationCanceledException) { if (Current()) KobSearchStatus.Text = "Поиск остановлен. Кристалл сохранён."; }
        catch (Exception exception) { if (Current()) KobSearchStatus.Text = "Поиск не удался: " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
                if (cts.IsCancellationRequested && !_host.IsClosing)
                    KobSearchStatus.Text = epoch == live.Epoch ? "Поиск остановлен. Кристалл сохранён." :
                        "Поиск остановлен: применён другой кристалл.";
            }
            cts.Dispose();
            if (!_host.IsClosing) live.NotifyChanged();
        }
    }

    private void Apply(Kobayashi3DSettings settings, Fractal3DPose? pose = null, bool fit = false)
    {
        Simulation.Load(settings);
        if (pose is { } savedPose) _host.MoveCameraTo(savedPose);
        else if (fit && settings.Field is { } field) FitField(field, settings.Threshold);
        _host.RestartRender();
    }

    private void KobSearchUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null || Simulation.Busy || _searchHistory.Count == 0 || _host.IsClosing || _host.IsSuspended) return;
        try
        {
            var previous = _searchHistory[^1];
            Apply(previous.Settings, previous.Pose);
            _searchHistory.RemoveAt(_searchHistory.Count - 1);
            KobSearchStatus.Text = "Возвращены предыдущий кристалл, температура и точный шаг роста.";
            KobSearchProgress.Value = 0;
        }
        catch (Exception exception) { KobSearchStatus.Text = "Не удалось вернуть кристалл: " + exception.Message; }
        UpdateSearchButtons();
    }

    /// <summary>Вписывает в кадр твёрдую фазу найденного кристалла.</summary>
    private void FitField(Kobayashi3DField field, double threshold)
    {
        int n = field.Size; var values = field.Concentrations;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < n * n * n; i++)
        {
            if (values[i * 2] < threshold) continue;
            var p = new Vector3((i % n + .5f) * 2 / n - 1, (i / n % n + .5f) * 2 / n - 1,
                (i / (n * n) + .5f) * 2 / n - 1);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        if (!float.IsFinite(min.X)) return;
        var center = (min + max) * .5f; double radius = 0;
        for (int i = 0; i < n * n * n; i++)
        {
            if (values[i * 2] < threshold) continue;
            var p = new Vector3((i % n + .5f) * 2 / n - 1, (i / n % n + .5f) * 2 / n - 1,
                (i / (n * n) + .5f) * 2 / n - 1);
            radius = Math.Max(radius, Vector3.Distance(p, center));
        }
        radius += 2 * Math.Sqrt(3) / n; // Trilinear boundary and half-cell margin.
        _host.FitCameraTo(center, radius);
    }

    public void CancelSearch()
    {
        if (_searchCts is null) return;
        _searchCts.Cancel();
        KobSearchStatus.Text = "Остановка поиска…";
    }

    private void UpdateSearchButtons()
    {
        bool searching = _searchCts is not null;
        var live = Simulation;
        bool ready = live.Shown is not null && !live.ResetPending && !live.Preparing && !_host.IsSuspended && !_host.IsClosing;
        KobSearchButton.Content = searching && !_searchVariation ? "Остановить поиск" : "Найти интересную форму";
        KobVariationButton.Content = searching && _searchVariation ? "Остановить поиск" : "Вариация текущей";
        KobSearchButton.IsEnabled = searching ? !_searchVariation : ready;
        KobVariationButton.IsEnabled = searching ? _searchVariation : ready;
        KobSearchUndoButton.IsEnabled = !searching && ready && !live.Busy && _searchHistory.Count > 0;
        KobSearchProgress.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
