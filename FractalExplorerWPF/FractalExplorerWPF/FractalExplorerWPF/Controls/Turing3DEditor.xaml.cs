using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Панель узоров Тьюринга 3D. Форма, симметрия и масштабы применяются к текущему полю перед
/// следующей порцией шагов, время при этом не сбрасывается; смена модели реакции начинает новое
/// поле. В оперативную память поле попадает лишь при сохранении и переносе на другую сетку.
/// </summary>
public partial class Turing3DEditor : UserControl, IFractal3DSimulationEditor<Turing3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private readonly List<(CheckBox Enabled, TextBox Radius, TextBox Amount)> _layerRows = [];
    // Обработчики полей молчат, пока панель создаётся или заполняется сохранёнными значениями.
    private bool _loading = true;

    internal Turing3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent();
        _host = host;
        Simulation = new(host, this, new()
        {
            Title = "узоров Тьюринга 3D",
            Create = (device, s) => new Turing3DGpuSimulation(device, s),
            Get = state => state.Turing,
            Set = (state, s) => state.Turing = s,
            Live = s => s.Live,
            Field = s => s.Field,
            With = (s, live, field) => s with { Live = live, Field = field },
            InitialSteps = s => s.InitialSteps,
            PreparationMessage = s => s.Field is null ? "Выращиваем начальный узор…" : "Восстанавливаем поле…",
            Time = (shown, s) => $"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · {s.Size}³",
            RenderingMessage = "Отрисовываем начальный узор…",
            DisableControlsWhilePreparing = true,
            Configure = (simulation, rule) => simulation.Configure(rule with { Size = simulation.Size })
        });
        _loading = false;
    }

    internal Fractal3DLiveSimulation<Turing3DGpuSimulation, Turing3DSettings, Turing3DField, Turing3DVolume> Simulation { get; }

    public FrameworkElement View => this;
    public int StepsPerFrame => (int)TuringSpeedSlider.Value;
    public bool IsSearching => false;
    public void CancelSearch() { }

    public void Show(Turing3DSettings s)
    {
        _loading = true;
        try
        {
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
            BuildLayerRows(s.Layers);
        }
        finally { _loading = false; }
        if (!NumericInput.IsRegistered(TuringSizeBox))
        {
            NumericInput.Integer(this, TuringSizeBox, "Размер сетки", Turing3DField.MinSize, Turing3DField.MaxSize);
            NumericInput.Integer(this, TuringSeedBox, "Seed", int.MinValue, int.MaxValue);
            NumericInput.Integer(this, TuringWarmupBox, "Начальное развитие", 0, 2000);
            NumericInput.Include(this, TuringReactionEditor);
        }
    }

    public Turing3DSettings CaptureView(Turing3DSettings applied) => applied with
    {
        Level = Math.Round(TuringLevelSlider.Value, 3),
        SheetThickness = TuringSheetBox.IsChecked == true ? Math.Round(TuringSheetSlider.Value, 3) : 0,
        CutAxis = Math.Max(0, TuringCutBox.SelectedIndex), CutPosition = TuringCutSlider.Value,
        StepsPerFrame = (int)TuringSpeedSlider.Value,
        Brush = (TuringBrush)Math.Max(0, TuringBrushBox.SelectedIndex),
        BrushRadius = Math.Round(TuringBrushRadiusSlider.Value, 3), BrushStrength = Math.Round(TuringBrushStrengthSlider.Value, 3)
    };

    /// <summary>Правило из ползунков формы поверх применённого; масштабы меняются отдельно.</summary>
    private Turing3DSettings CaptureRule() => Simulation.Settings with
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

    private void BuildLayerRows(IReadOnlyList<Turing3DScale> layers)
    {
        foreach (var old in _layerRows) { NumericInput.Forget(this, old.Radius); NumericInput.Forget(this, old.Amount); }
        TuringLayersPanel.Children.Clear(); _layerRows.Clear();
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
            _layerRows.Add((enabled, radius, amount));
            NumericInput.Real(this, radius, "Радиус масштаба", .5, 64);
            NumericInput.Real(this, amount, "Отклик масштаба", .001, .15);
        }
    }

    public void Refresh()
    {
        var culture = CultureInfo.CurrentCulture;
        bool classical = Simulation.Settings.Reaction.IsClassical;
        TuringMcCabePanel.Visibility = TuringMcCabeRatioPanel.Visibility = TuringScalesExpander.Visibility = classical ? Visibility.Collapsed : Visibility.Visible;
        _host.SetColoringModeName(Fractal3DColoringMode.OrbitTrap, classical ? "По концентрации U" : "По масштабу узора");
        TuringDeviceText.Text = Simulation.Device;
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
        TuringBrushButton.IsEnabled = Simulation.CanBrush;
    }

    private void TuringReaction_OnChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        var reaction = TuringReactionEditor.Settings;
        if (reaction.Model != Simulation.Settings.Reaction.Model)
            Simulation.Restart(Simulation.Capture() with
                { Reaction = reaction, Field = null, Live = null, WarmupSteps = reaction.IsClassical ? 400 : 120 });
        else
        {
            Simulation.Reconfigure(Simulation.Settings with { Reaction = reaction });
            Simulation.NotifyChanged();
        }
    }

    /// <summary>Форма и симметрия: новое правило вступит в силу перед следующей порцией шагов.</summary>
    private void TuringRule_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.Reconfigure(CaptureRule());
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void TuringDepth_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var layers = Turing3DSettings.DefaultLayers((int)TuringDepthSlider.Value);
        BuildLayerRows(layers);
        Simulation.Reconfigure(CaptureRule() with { Layers = layers });
        Simulation.NotifyChanged();
    }

    private void TuringLayersApply_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            var layers = _layerRows.Select((row, index) => new Turing3DScale(row.Enabled.IsChecked == true,
                ReadDouble(row.Radius, $"Радиус масштаба {index + 1}", .5, 64),
                ReadDouble(row.Amount, $"Отклик масштаба {index + 1}", .001, .15))).ToArray();
            var settings = CaptureRule() with { Layers = layers };
            settings.Validate();
            Simulation.Reconfigure(settings);
            _host.ShowStatus("Масштабы применятся со следующего шага.");
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void TuringView_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }

    private void TuringLabels_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Simulation.NotifyChanged();
    }

    /// <summary>Текущее поле пересчитывается на новую сетку без сброса времени.</summary>
    private void TuringResize_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            int size = ReadInt(TuringSizeBox, "Сетка", Turing3DField.MinSize, Turing3DField.MaxSize);
            if (size == Simulation.Settings.Size) return;
            Simulation.Enqueue("resize", (simulation, rule) =>
            {
                if (size == simulation.Size) return default;
                var resized = Turing3DField.Resize(simulation.ReadCurrent(), size);
                simulation.Reset(rule with { Size = size, Field = resized });
                return new(true, () => Simulation.Replace(Simulation.Settings with { Size = resized.Size, Field = resized }));
            }, "Переносим поле…");
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void TuringNewDrawing_OnClick(object sender, RoutedEventArgs e)
    {
        TuringSeedBox.Text = Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);
        TuringRestart_OnClick(sender, e);
    }

    private void TuringRestart_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            // The view as shown, the rule from the form, and a fresh field from the typed grid and number.
            Turing3DSettings view = Simulation.Capture(), rule = CaptureRule();
            Simulation.Restart(rule with
            {
                Size = ReadInt(TuringSizeBox, "Сетка", Turing3DField.MinSize, Turing3DField.MaxSize),
                Seed = ReadInt(TuringSeedBox, "Случайное число", int.MinValue, int.MaxValue),
                WarmupSteps = ReadInt(TuringWarmupBox, "Начальное развитие", 0, 2000),
                Level = view.Level, SheetThickness = view.SheetThickness, CutAxis = view.CutAxis, CutPosition = view.CutPosition,
                StepsPerFrame = view.StepsPerFrame, Brush = view.Brush, BrushRadius = view.BrushRadius, BrushStrength = view.BrushStrength,
                Field = null, Live = null
            });
        }
        catch (Exception exception) { _host.ShowStatus(exception.Message); }
    }

    private void TuringBrush_OnClick(object sender, RoutedEventArgs e) => Brush(.5, .5, .5);

    private void Brush(double x, double y, double z)
    {
        double radius = TuringBrushRadiusSlider.Value, strength = TuringBrushStrengthSlider.Value;
        var kind = (TuringBrush)Math.Max(0, TuringBrushBox.SelectedIndex);
        Simulation.Brush(simulation => simulation.Paint(x, y, z, radius, strength, kind));
    }

    // Shift+click paints on the selected cutting plane; camera gestures stay available.
    public bool TryBrush(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled = true;
        if (TuringCutBox.SelectedIndex == 0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if (_host.TryPickCutPlane(e, TuringCutBox.SelectedIndex - 1, TuringCutSlider.Value, out var point))
            Brush(point.X, point.Y, point.Z);
        return true;
    }
}
