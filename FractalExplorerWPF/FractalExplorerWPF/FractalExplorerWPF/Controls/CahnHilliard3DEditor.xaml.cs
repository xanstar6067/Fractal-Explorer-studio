using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>Панель Кана–Хиллиарда 3D: спинодальный распад смеси с сохранением среднего состава.</summary>
public partial class CahnHilliard3DEditor : UserControl, IFractal3DSimulationEditor<CahnHilliard3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private readonly List<CahnHilliard3DSettings> _atlasHistory = [];
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal CahnHilliard3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Кана–Хиллиарда 3D",
            Create = (device, s) => new CahnHilliard3DGpuSimulation(device, s),
            Get = state => state.CahnHilliard,
            Set = (state, s) => state.CahnHilliard = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage = _ => "Подготовка спинодального распада…",
            Time = (shown, s) => $"t = {shown?.Time ?? s.Field?.Time ?? 0:F1} · шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Спинодальный распад · {done:N0} / {steps:N0} шагов"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<CahnHilliard3DGpuSimulation, CahnHilliard3DSettings, CahnHilliard3DField, CahnHilliard3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)CahnSpeedSlider.Value;
    public bool IsSearching => false;
    public void CancelSearch() { }
    public bool TryBrush(MouseButtonEventArgs e) => false;

    public event EventHandler? AtlasRequested;
    private void Atlas_OnClick(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);

    internal void ApplyAtlasResult(CahnHilliard3DSettings result, CahnHilliard3DSettings previous)
    {
        if (result.Field is null || result.Live is not null) throw new ArgumentException("Нужно точное поле выбранного прогона.");
        result.Validate();
        _atlasHistory.Add(previous with { Live = null });
        if (_atlasHistory.Count > 4) _atlasHistory.RemoveAt(0);
        Simulation.Restart(result); AtlasUndoButton.IsEnabled = true;
        _host.ShowStatus($"Вариант атласа · t = {result.Field.Time:G6}, шаг {result.Field.Step:N0}. Исходное поле доступно через «Вернуть предыдущий».");
    }

    private void AtlasUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_atlasHistory.Count == 0) return;
        var previous = _atlasHistory[^1]; _atlasHistory.RemoveAt(_atlasHistory.Count - 1);
        Simulation.Restart(previous); AtlasUndoButton.IsEnabled = _atlasHistory.Count > 0;
        _host.ShowStatus("Восстановлено предыдущее поле Кана–Хиллиарда 3D.");
    }

    public void Show(CahnHilliard3DSettings s)
    {
        _loading = true;
        try
        {
            CahnSizeBox.SelectedIndex = s.Size == 32 ? 0 : s.Size == 64 ? 1 : 2;
            CahnMeanBox.Text = s.Mean.ToString("R", CultureInfo.InvariantCulture); CahnNoiseBox.Text = s.Noise.ToString("R", CultureInfo.InvariantCulture);
            CahnKappaBox.Text = s.Kappa.ToString("R", CultureInfo.InvariantCulture); CahnMobilityBox.Text = s.Mobility.ToString("R", CultureInfo.InvariantCulture); CahnDtBox.Text = s.TimeStep.ToString("R", CultureInfo.InvariantCulture);
            CahnSeedBox.SelectedIndex = (int)s.SeedShape; CahnRandomBox.Text = s.Seed.ToString();
            CahnWarmupBox.Text = s.WarmupSteps.ToString();
            CahnSpeedSlider.Value = s.StepsPerFrame; CahnLevelSlider.Value = s.Level;
            CahnInvertBox.IsChecked = s.Invert; CahnCutBox.SelectedIndex = s.CutAxis; CahnCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
        InitializeNumericInput();
    }

    public CahnHilliard3DSettings CaptureView(CahnHilliard3DSettings applied) => applied with
    {
        Level = CahnLevelSlider.Value, Invert = CahnInvertBox.IsChecked == true, CutAxis = Math.Max(0, CahnCutBox.SelectedIndex),
        CutPosition = CahnCutSlider.Value, StepsPerFrame = (int)CahnSpeedSlider.Value
    };

    public void Refresh()
    {
        CahnDeviceText.Text = Simulation.Device;
        CahnCutSlider.IsEnabled = CahnCutBox.SelectedIndex > 0;
    }

    private void CahnRestart_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            Simulation.Restart(new CahnHilliard3DSettings
            {
                Size = CahnSizeBox.SelectedIndex == 0 ? 32 : CahnSizeBox.SelectedIndex == 1 ? 64 : 128,
                Mean = ReadDouble(CahnMeanBox, "Средний состав", -.55, .55), Noise = ReadDouble(CahnNoiseBox, "Шум", .001, .2),
                Kappa = ReadDouble(CahnKappaBox, "κ", .5, 8), Mobility = ReadDouble(CahnMobilityBox, "M", .01, 2),
                TimeStep = ReadDouble(CahnDtBox, "Δt", .01, 2), WarmupSteps = ReadInt(CahnWarmupBox, "Подготовка", 0, 5000),
                Seed = ReadInt(CahnRandomBox, "Случайное число", int.MinValue, int.MaxValue),
                SeedShape = (CahnHilliard3DSeed)Math.Max(0, CahnSeedBox.SelectedIndex),
                StepsPerFrame = (int)CahnSpeedSlider.Value, Level = CahnLevelSlider.Value, Invert = CahnInvertBox.IsChecked == true,
                CutAxis = Math.Max(0, CahnCutBox.SelectedIndex), CutPosition = CahnCutSlider.Value
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void CahnView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }
}
