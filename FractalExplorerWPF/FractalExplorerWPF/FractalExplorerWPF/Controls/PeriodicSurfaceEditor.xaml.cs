using System.Globalization;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class PeriodicSurfaceEditor : UserControl
{
    private bool _loading = true;
    private PeriodicSurfaceSettings _applied = new();
    private bool WidthActive => PeriodicRegionBox.SelectedIndex is 0 or 4;
    public event EventHandler? SettingsChanged;
    public event System.Windows.RoutedEventHandler? AtlasRequested;
    public event EventHandler? LabyrinthColoringRequested;

    public PeriodicSurfaceEditor()
    {
        InitializeComponent();
        Load(new());
        NumericInput.Real(this, PeriodicPeriodBox, "Период", .5, 4);
        NumericInput.Real(this, PeriodicLevelBox, "Уровень F", -.9, .9);
        NumericInput.Real(this, PeriodicThicknessBox, "Толщина / зазор по F", .02, 1.2, active: () => WidthActive);
        NumericInput.Real(this, PeriodicBlendBox, "Смешивание", 0, 1);
        NumericInput.Real(this, PeriodicGradientBox, "Градиент толщины", -.9, .9,
            active: () => WidthActive && PeriodicThicknessAxisBox.SelectedIndex > 0);
        NumericInput.Real(this, PeriodicStretchXBox, "Растяжение X", .5, 2);
        NumericInput.Real(this, PeriodicStretchYBox, "Растяжение Y", .5, 2);
        NumericInput.Real(this, PeriodicStretchZBox, "Растяжение Z", .5, 2);
        NumericInput.Real(this, PeriodicCutPositionBox, "Положение среза", -1, 1, active: () => PeriodicCutBox.SelectedIndex > 0);
    }

    public PeriodicSurfaceSettings Capture()
    {
        NumericInput.Prepare(this);
        var settings = new PeriodicSurfaceSettings
        {
            BlendFamily = (PeriodicSurfaceFamily)Math.Max(0, PeriodicBlendFamilyBox.SelectedIndex),
            Blend = ReadDouble(PeriodicBlendBox, "Смешивание", 0, 1),
            ThicknessAxis = Math.Max(0, PeriodicThicknessAxisBox.SelectedIndex),
            ThicknessGradient = WidthActive && PeriodicThicknessAxisBox.SelectedIndex > 0
                ? ReadDouble(PeriodicGradientBox, "Градиент толщины", -.9, .9) : _applied.ThicknessGradient,
            Family = (PeriodicSurfaceFamily)Math.Max(0, PeriodicFamilyBox.SelectedIndex),
            Region = (PeriodicSurfaceRegion)Math.Max(0, PeriodicRegionBox.SelectedIndex),
            Period = ReadDouble(PeriodicPeriodBox, "Период", .5, 4),
            Level = ReadDouble(PeriodicLevelBox, "Уровень F", -.9, .9),
            Thickness = WidthActive ? ReadDouble(PeriodicThicknessBox, "Толщина стенки по F", .02, 1.2) : _applied.Thickness,
            StretchX = ReadDouble(PeriodicStretchXBox, "Растяжение X", .5, 2),
            StretchY = ReadDouble(PeriodicStretchYBox, "Растяжение Y", .5, 2),
            StretchZ = ReadDouble(PeriodicStretchZBox, "Растяжение Z", .5, 2),
            CutAxis = Math.Max(0, PeriodicCutBox.SelectedIndex),
            CutPosition = PeriodicCutBox.SelectedIndex > 0 ? ReadDouble(PeriodicCutPositionBox, "Положение среза", -1, 1) : _applied.CutPosition
        };
        settings.Validate();
        _applied = settings;
        return settings;
    }

    public void Load(PeriodicSurfaceSettings? settings)
    {
        var s = settings ?? new();
        s.Validate();
        _loading = true;
        try
        {
            _applied = s;
            PeriodicBlendFamilyBox.SelectedIndex = (int)s.BlendFamily;
            PeriodicThicknessAxisBox.SelectedIndex = s.ThicknessAxis;
            PeriodicFamilyBox.SelectedIndex = (int)s.Family;
            PeriodicRegionBox.SelectedIndex = (int)s.Region;
            PeriodicCutBox.SelectedIndex = s.CutAxis;
            (Slider Slider, TextBox Box, double Value)[] fields =
            [
                (PeriodicBlendSlider, PeriodicBlendBox, s.Blend),
                (PeriodicGradientSlider, PeriodicGradientBox, s.ThicknessGradient),
                (PeriodicPeriodSlider, PeriodicPeriodBox, s.Period),
                (PeriodicLevelSlider, PeriodicLevelBox, s.Level),
                (PeriodicThicknessSlider, PeriodicThicknessBox, s.Thickness),
                (PeriodicStretchXSlider, PeriodicStretchXBox, s.StretchX),
                (PeriodicStretchYSlider, PeriodicStretchYBox, s.StretchY),
                (PeriodicStretchZSlider, PeriodicStretchZBox, s.StretchZ),
                (PeriodicCutPositionSlider, PeriodicCutPositionBox, s.CutPosition)
            ];
            foreach (var field in fields)
            {
                field.Slider.Value = field.Value;
                field.Box.SetCurrentValue(TextBox.TextProperty, field.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            UpdatePeriodicControls();
        }
        finally { _loading = false; }
    }

    private void UpdatePeriodicControls()
    {
        static string Formula(int family) => family switch
        {
            1 => "cos x + cos y + cos z",
            2 => "cos x · cos y · cos z − sin x · sin y · sin z",
            _ => "sin x · cos y + sin y · cos z + sin z · cos x"
        };
        PeriodicFormulaText.Text = PeriodicBlendSlider.Value <= 0
            ? $"F = {Formula(PeriodicFamilyBox.SelectedIndex)}"
            : $"F₁ = {Formula(PeriodicFamilyBox.SelectedIndex)}\nF₂ = {Formula(PeriodicBlendFamilyBox.SelectedIndex)}\nF = (1 − смесь) · F₁ + смесь · F₂";
        PeriodicThicknessCaption.Text = PeriodicRegionBox.SelectedIndex == 4 ? "Ширина зазора по F" : "Толщина стенки по F";
        PeriodicThicknessPanel.IsEnabled = PeriodicGradientPanel.IsEnabled = WidthActive;
        PeriodicGradientSlider.IsEnabled = PeriodicGradientBox.IsEnabled = WidthActive && PeriodicThicknessAxisBox.SelectedIndex > 0;
        PeriodicCutPositionSlider.IsEnabled = PeriodicCutPositionBox.IsEnabled = PeriodicCutBox.SelectedIndex > 0;
    }

    private void OnChanged(object sender, EventArgs e)
    {
        if (_loading || PeriodicCutPositionBox is null) return;
        UpdatePeriodicControls();
        if (ReferenceEquals(sender, PeriodicRegionBox) && PeriodicRegionBox.SelectedIndex == 4)
            LabyrinthColoringRequested?.Invoke(this, EventArgs.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void AtlasClicked(object sender, System.Windows.RoutedEventArgs e) => AtlasRequested?.Invoke(this, e);

    private void ColorLabyrinthsClicked(object sender, System.Windows.RoutedEventArgs e) =>
        LabyrinthColoringRequested?.Invoke(this, EventArgs.Empty);
}
