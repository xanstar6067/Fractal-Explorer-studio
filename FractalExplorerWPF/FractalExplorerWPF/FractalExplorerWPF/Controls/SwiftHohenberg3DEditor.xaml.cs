using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class SwiftHohenberg3DEditor : UserControl, IFractal3DSimulationEditor<SwiftHohenberg3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private bool _loading = true;

    internal SwiftHohenberg3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent(); _host = host;
        Simulation = new(host, this, new()
        {
            Title = "Свифта–Хоэнберга 3D", Create = (device, s) => new SwiftHohenberg3DGpuSimulation(device, s),
            Get = state => state.SwiftHohenberg, Set = (state, s) => state.SwiftHohenberg = s,
            Live = s => s.Live, Field = s => s.Field, With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage = _ => "Подготовка пространственного узора…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · t = {(shown?.Step ?? s.Field?.Step ?? 0) * s.TimeStep:F2} · {s.Size}³",
            ProgressMessage = (done, steps) => $"Подготовка узора · {(int)(100L * done / Math.Max(1, steps))}%"
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<SwiftHohenberg3DGpuSimulation, SwiftHohenberg3DSettings, SwiftHohenberg3DField, SwiftHohenberg3DVolume> Simulation { get; }
    public FrameworkElement View => this;
    public int StepsPerFrame => (int)ShSpeedSlider.Value;
    public bool IsSearching => false;
    public void CancelSearch() { }

    public void Show(SwiftHohenberg3DSettings s)
    {
        _loading = true;
        try
        {
            ShSizeBox.SelectedIndex = s.Size switch { 32 => 0, 128 => 2, _ => 1 };
            ShRBox.Text = Format(s.R); ShBBox.Text = Format(s.Quadratic); ShQBox.Text = Format(s.WaveNumber);
            ShSpacingBox.Text = Format(s.Spacing); ShDtBox.Text = Format(s.TimeStep); ShNoiseBox.Text = Format(s.Noise);
            ShSeedBox.SelectedIndex = (int)s.SeedShape; ShSeedNumberBox.Text = s.Seed.ToString(); ShWarmupBox.Text = s.WarmupSteps.ToString();
            ShSpeedSlider.Value = s.StepsPerFrame; ShViewBox.SelectedIndex = (int)s.View; ShRangeSlider.Value = s.DisplayRange;
            ShThresholdSlider.Value = s.Threshold; ShCutBox.SelectedIndex = s.CutAxis; ShCutSlider.Value = s.CutPosition;
        }
        finally { _loading = false; }
    }

    public SwiftHohenberg3DSettings CaptureView(SwiftHohenberg3DSettings applied) => applied with
    {
        StepsPerFrame = StepsPerFrame, View = (SwiftHohenberg3DView)Math.Max(0, ShViewBox.SelectedIndex),
        DisplayRange = ShRangeSlider.Value, Threshold = ShThresholdSlider.Value,
        CutAxis = Math.Max(0, ShCutBox.SelectedIndex), CutPosition = ShCutSlider.Value
    };

    public void Refresh()
    {
        ShDeviceText.Text = Simulation.Device; ShCutSlider.IsEnabled = ShCutBox.SelectedIndex > 0;
        ShBrushButton.IsEnabled = Simulation.CanBrush;
    }

    private void ShRestart_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = CaptureView(new()) with
            {
                Size = ShSizeBox.SelectedIndex switch { 0 => 32, 2 => 128, _ => 64 },
                R = ReadDouble(ShRBox, "Неустойчивость r", -.5, 1), Quadratic = ReadDouble(ShBBox, "Асимметрия b", -1.5, 1.5),
                WaveNumber = ReadDouble(ShQBox, "Волновое число q₀", .4, 1.5), Spacing = ReadDouble(ShSpacingBox, "Шаг сетки", .5, 2),
                TimeStep = ReadDouble(ShDtBox, "Шаг времени", .002, .05), Noise = ReadDouble(ShNoiseBox, "Шум", 0, .3),
                SeedShape = (SwiftHohenberg3DSeed)Math.Max(0, ShSeedBox.SelectedIndex),
                Seed = ReadInt(ShSeedNumberBox, "Начальное число", int.MinValue, int.MaxValue),
                WarmupSteps = ReadInt(ShWarmupBox, "Подготовка", 0, 5000)
            };
            s.Validate(); Simulation.Restart(s);
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void ShView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (e.Source == ShViewBox) ShThresholdSlider.Value = ShViewBox.SelectedIndex == 2 ? .85 : .15;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void ShBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);
    private void Brush(double x, double y, double z)
    {
        double radius = ShBrushSlider.Value, value = ShBrushBox.SelectedIndex switch { 1 => -1, 2 => 0, _ => 1 };
        Simulation.Brush(simulation => simulation.Inject(x, y, z, radius, value));
    }

    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (ShCutBox.SelectedIndex == 0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, ShCutBox.SelectedIndex - 1, ShCutSlider.Value, out var point)) Brush(point.X, point.Y, point.Z);
        return true;
    }
}
