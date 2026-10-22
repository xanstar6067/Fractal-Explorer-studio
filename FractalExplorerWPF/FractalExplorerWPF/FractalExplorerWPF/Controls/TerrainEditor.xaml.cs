using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class TerrainEditor : UserControl
{
    private static readonly int[] Resolutions = [257, 513, 1025, 2049];
    private bool _loading = true;
    private TerrainSettings _applied = new();
    private bool ShapeActive => TerrainShapeBox.SelectedIndex > 0;
    private bool ErosionActive => ErosionSlider.Value > 0;
    public event EventHandler? SettingsChanged;
    public event EventHandler? SlopeColoringRequested;
    public event EventHandler? ExportRequested;

    public TerrainEditor()
    {
        InitializeComponent();
        Load(new());
        NumericInput.Integer(this, TerrainSeedBox, "Начальное число", 0, int.MaxValue);
        NumericInput.Integer(this, TerrainOctavesBox, "Уровни деталей", 1, 12);
        NumericInput.Real(this, TerrainRoughnessBox, "Шероховатость", .05, .95);
        NumericInput.Real(this, TerrainLacunarityBox, "Отношение масштабов", 1.5, 3);
        NumericInput.Real(this, TerrainScaleBox, "Масштаб деталей", .25, 16);
        NumericInput.Real(this, TerrainHeightBox, "Высота", .05, 8);
        NumericInput.Real(this, TerrainSizeBox, "Размер участка", 1, 20);
        NumericInput.Real(this, StrengthBox, "Сила формы", 0, 1, active: () => ShapeActive);
        NumericInput.Real(this, RadiusBox, "Радиус формы", .2, 1, active: () => ShapeActive);
        NumericInput.Integer(this, ErosionBox, "Проходы эрозии", 0, 64);
        NumericInput.Real(this, TalusBox, "Угол осыпания", 5, 60, active: () => ErosionActive);
    }

    public TerrainSettings Capture()
    {
        NumericInput.Prepare(this);
        var settings = new TerrainSettings
        {
            Type = (TerrainKind)Math.Max(0, TerrainTypeBox.SelectedIndex),
            Seed = ReadInt(TerrainSeedBox, "Начальное число", 0, int.MaxValue),
            Octaves = ReadInt(TerrainOctavesBox, "Уровни деталей", 1, 12),
            Roughness = ReadDouble(TerrainRoughnessBox, "Шероховатость", .05, .95),
            Lacunarity = ReadDouble(TerrainLacunarityBox, "Отношение масштабов", 1.5, 3),
            Scale = ReadDouble(TerrainScaleBox, "Масштаб деталей", .25, 16),
            Height = ReadDouble(TerrainHeightBox, "Высота", .05, 8),
            Size = ReadDouble(TerrainSizeBox, "Размер участка", 1, 20),
            Resolution = Resolutions[Math.Clamp(TerrainResolutionBox.SelectedIndex, 0, 3)],
            Shape = (TerrainShape)Math.Max(0, TerrainShapeBox.SelectedIndex),
            ShapeStrength = ShapeActive ? ReadDouble(StrengthBox, "Сила формы", 0, 1) : _applied.ShapeStrength,
            ShapeRadius = ShapeActive ? ReadDouble(RadiusBox, "Радиус формы", .2, 1) : _applied.ShapeRadius,
            ThermalIterations = ReadInt(ErosionBox, "Проходы эрозии", 0, 64),
            TalusAngle = ErosionActive ? ReadDouble(TalusBox, "Угол осыпания", 5, 60) : _applied.TalusAngle
        };
        settings.Validate();
        _applied = settings;
        return settings;
    }

    public void Load(TerrainSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            _applied = settings;
            TerrainTypeBox.SelectedIndex = (int)settings.Type;
            TerrainShapeBox.SelectedIndex = (int)settings.Shape;
            TerrainResolutionBox.SelectedIndex = Array.IndexOf(Resolutions, settings.Resolution);
            (TextBox Box, double Value)[] fields =
            [
                (TerrainSeedBox, settings.Seed), (TerrainOctavesBox, settings.Octaves),
                (TerrainRoughnessBox, settings.Roughness), (TerrainLacunarityBox, settings.Lacunarity),
                (TerrainScaleBox, settings.Scale), (TerrainHeightBox, settings.Height), (TerrainSizeBox, settings.Size)
            ];
            foreach (var field in fields) field.Box.Text = field.Value.ToString("R", CultureInfo.InvariantCulture);
            (Slider Slider, TextBox Box, double Value)[] sliders =
            [
                (StrengthSlider, StrengthBox, settings.ShapeStrength), (RadiusSlider, RadiusBox, settings.ShapeRadius),
                (ErosionSlider, ErosionBox, settings.ThermalIterations), (TalusSlider, TalusBox, settings.TalusAngle)
            ];
            foreach (var field in sliders)
            {
                field.Slider.Value = field.Value;
                field.Box.SetCurrentValue(TextBox.TextProperty, field.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            UpdateControls();
        }
        finally { _loading = false; }
    }

    public void SetExportState(string caption, bool enabled = true)
    {
        TerrainExportButton.Content = caption;
        TerrainExportButton.IsEnabled = enabled;
    }

    private void UpdateControls()
    {
        ShapeFields.IsEnabled = ShapeActive;
        TalusFields.IsEnabled = ErosionActive;
    }

    private void OnChanged(object sender, EventArgs e)
    {
        if (_loading || TalusFields is null) return;
        UpdateControls();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RandomClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        TerrainSeedBox.Text = Random.Shared.Next(int.MaxValue).ToString(CultureInfo.InvariantCulture);
    }
    private void SlopeClicked(object sender, RoutedEventArgs e) => SlopeColoringRequested?.Invoke(this, EventArgs.Empty);
    private void ExportClicked(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, EventArgs.Empty);
}
