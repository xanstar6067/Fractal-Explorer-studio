using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>Equation drafts, view controls and brushes, hosted by the common live simulation loop.</summary>
public partial class GinzburgLandau3DEditor : UserControl, IFractal3DSimulationEditor<GinzburgLandau3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private bool _loading = true;

    internal GinzburgLandau3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent(); _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Гинзбурга–Ландау 3D",
            Create = (device, s) => new GinzburgLandau3DGpuSimulation(device, s),
            Get = state => state.GinzburgLandau, Set = (state, s) => state.GinzburgLandau = s,
            Live = s => s.Live, Field = s => s.Field, With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage = _ => "Подготовка комплексного поля…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · t = {(shown?.Step ?? s.Field?.Step ?? 0) * s.TimeStep:F2} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Подготовка поля · {(int)(100L * done / Math.Max(1, steps))}%"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<GinzburgLandau3DGpuSimulation, GinzburgLandau3DSettings, GinzburgLandau3DField, GinzburgLandau3DVolume> Simulation { get; }
    public FrameworkElement View => this;
    public int StepsPerFrame => (int)GlSpeedSlider.Value;
    public bool IsSearching => false;
    public void CancelSearch() { }

    public void Show(GinzburgLandau3DSettings s)
    {
        _loading = true;
        try
        {
            GlSizeBox.Text = s.Size.ToString(); GlC1Box.Text = Format(s.C1); GlC3Box.Text = Format(s.C3);
            GlDtBox.Text = Format(s.TimeStep); GlSpacingBox.Text = Format(s.Spacing);
            GlBoundaryBox.SelectedIndex = (int)s.Boundary; GlSeedBox.SelectedIndex = (int)s.SeedShape;
            GlSeedNumberBox.Text = s.Seed.ToString(); GlWarmupBox.Text = s.WarmupSteps.ToString();
            GlSpeedSlider.Value = s.StepsPerFrame; GlViewBox.SelectedIndex = (int)s.View;
            GlThresholdSlider.Value = s.Threshold; GlCutBox.SelectedIndex = s.CutAxis; GlCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    public GinzburgLandau3DSettings CaptureView(GinzburgLandau3DSettings applied) => applied with
    {
        StepsPerFrame = StepsPerFrame, View = (GinzburgLandau3DView)Math.Max(0, GlViewBox.SelectedIndex),
        Threshold = GlThresholdSlider.Value, CutAxis = Math.Max(0, GlCutBox.SelectedIndex), CutPosition = GlCutSlider.Value
    };

    public void Refresh()
    {
        GlDeviceText.Text = Simulation.Device; GlCutSlider.IsEnabled = GlCutBox.SelectedIndex > 0;
        GlBrushButton.IsEnabled = Simulation.CanBrush;
    }

    private void GlRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = CaptureView(new()) with
            {
                Size = ReadInt(GlSizeBox, "Сетка", 32, 128), C1 = ReadDouble(GlC1Box, "c₁", -3, 3),
                C3 = ReadDouble(GlC3Box, "c₃", -3, 3), TimeStep = ReadDouble(GlDtBox, "Шаг времени", .0005, .05),
                Spacing = ReadDouble(GlSpacingBox, "Шаг сетки", .5, 2),
                Boundary = (GinzburgLandau3DBoundary)Math.Max(0, GlBoundaryBox.SelectedIndex),
                SeedShape = (GinzburgLandau3DSeed)Math.Max(0, GlSeedBox.SelectedIndex),
                Seed = ReadInt(GlSeedNumberBox, "Начальное число", int.MinValue, int.MaxValue),
                WarmupSteps = ReadInt(GlWarmupBox, "Подготовка", 0, 10000)
            };
            s.Validate(); Simulation.Restart(s);
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void GlView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (e.Source == GlViewBox) GlThresholdSlider.Value = GlViewBox.SelectedIndex switch { 0 => .15, 1 => .3, _ => .5 };
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void GlBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);
    private void Brush(double x, double y, double z)
    {
        double radius = GlBrushSlider.Value; var mode = (GinzburgLandau3DBrush)Math.Max(0, GlBrushBox.SelectedIndex);
        Simulation.Brush(simulation => simulation.Inject(x, y, z, radius, mode));
    }

    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (GlCutBox.SelectedIndex == 0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, GlCutBox.SelectedIndex - 1, GlCutSlider.Value, out var point)) Brush(point.X, point.Y, point.Z);
        return true;
    }
}
