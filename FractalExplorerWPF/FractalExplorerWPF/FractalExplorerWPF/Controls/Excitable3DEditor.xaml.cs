using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>Панель свитковых волн 3D: модель возбудимой среды, уравнение, затравка, вид и сферическая кисть.</summary>
public partial class Excitable3DEditor : UserControl, IFractal3DSimulationEditor<Excitable3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal Excitable3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "свитковых волн 3D",
            Create = (device, s) => new Excitable3DGpuSimulation(device, s),
            Get = state => state.Excitable,
            Set = (state, s) => state.Excitable = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.InitialSteps : 0,
            PreparationMessage = _ => "Подготовка свитка…",
            Time = (shown, s) =>
            {
                long step = shown?.Step ?? s.Field?.Step ?? 0;
                return $"Шаг {step:N0} · t = {step * s.TimeStep:F2} · {s.Size}³";
            },
            ProgressMessage = (done, steps) => $"Подготовка свитка · {(int)(100L * done / Math.Max(1, steps))}%"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<Excitable3DGpuSimulation, Excitable3DSettings, Excitable3DField, Excitable3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)ExSpeedSlider.Value;
    public bool IsSearching => false;
    public void CancelSearch() { }

    public void Show(Excitable3DSettings s)
    {
        _loading = true;
        try
        {
            ExModelBox.SelectedIndex = (int)s.Model;
            FillEquation(s);
            ExSizeBox.Text = s.Size.ToString(); ExSeedBox.SelectedIndex = (int)s.SeedShape;
            ExBoundaryBox.SelectedIndex = (int)s.Boundary; ExViewBox.SelectedIndex = (int)s.View;
            ExWarmupBox.Text = s.WarmupSteps.ToString();
            ExSpeedSlider.Value = s.StepsPerFrame; ExThresholdSlider.Value = s.Threshold;
            ExCutBox.SelectedIndex = s.CutAxis; ExCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    private void FillEquation(Excitable3DSettings s)
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

    public Excitable3DSettings CaptureView(Excitable3DSettings applied) => applied with
    {
        Threshold = ExThresholdSlider.Value, CutAxis = Math.Max(0, ExCutBox.SelectedIndex),
        CutPosition = ExCutSlider.Value, StepsPerFrame = (int)ExSpeedSlider.Value,
        View = (Excitable3DView)Math.Max(0, ExViewBox.SelectedIndex)
    };

    public void Refresh()
    {
        ExDeviceText.Text = Simulation.Device;
        ExCutSlider.IsEnabled = ExCutBox.SelectedIndex > 0;
        ExBrushButton.IsEnabled = Simulation.CanBrush;
    }

    private void ExModel_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var s = Excitable3DSettings.Default((Excitable3DModel)Math.Max(0, ExModelBox.SelectedIndex));
        FillEquation(s); ExWarmupBox.Text = s.WarmupSteps.ToString();
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
            Simulation.Restart(settings);
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void ExView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (e.Source == ExViewBox) ExThresholdSlider.Value = ExViewBox.SelectedIndex == 1 ? .15 : .5;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void ExBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);

    private void Brush(double x, double y, double z)
    {
        double radius = ExBrushSlider.Value;
        Simulation.Brush(simulation => simulation.Inject(x, y, z, radius));
    }

    // Shift+click plants a spherical seed on the selected cutting plane; camera gestures stay available.
    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (ExCutBox.SelectedIndex == 0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, ExCutBox.SelectedIndex - 1, ExCutSlider.Value, out var point))
            Brush(point.X, point.Y, point.Z);
        return true;
    }
}
