using System.Globalization;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private PeriodicSurfaceSettings CapturePeriodicSurface()
    {
        if (Kind != Fractal3DKind.PeriodicSurfaces) return new();
        var settings = new PeriodicSurfaceSettings
        {
            Family = (PeriodicSurfaceFamily)Math.Max(0, PeriodicFamilyBox.SelectedIndex),
            Region = (PeriodicSurfaceRegion)Math.Max(0, PeriodicRegionBox.SelectedIndex),
            Period = ReadDouble(PeriodicPeriodBox, "Период", .5, 4),
            Level = ReadDouble(PeriodicLevelBox, "Уровень F", -.9, .9),
            Thickness = ReadDouble(PeriodicThicknessBox, "Толщина стенки по F", .02, 1.2),
            StretchX = ReadDouble(PeriodicStretchXBox, "Растяжение X", .5, 2),
            StretchY = ReadDouble(PeriodicStretchYBox, "Растяжение Y", .5, 2),
            StretchZ = ReadDouble(PeriodicStretchZBox, "Растяжение Z", .5, 2),
            CutAxis = Math.Max(0, PeriodicCutBox.SelectedIndex),
            CutPosition = ReadDouble(PeriodicCutPositionBox, "Положение среза", -1, 1)
        };
        settings.Validate();
        return settings;
    }

    private void LoadPeriodicSurface(PeriodicSurfaceSettings? settings)
    {
        if (Kind != Fractal3DKind.PeriodicSurfaces) return;
        var s = settings ?? new();
        s.Validate();
        PeriodicFamilyBox.SelectedIndex = (int)s.Family;
        PeriodicRegionBox.SelectedIndex = (int)s.Region;
        PeriodicCutBox.SelectedIndex = s.CutAxis;
        (Slider Slider, TextBox Box, double Value)[] fields =
        [
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

    private void UpdatePeriodicControls()
    {
        PeriodicFormulaText.Text = PeriodicFamilyBox.SelectedIndex switch
        {
            1 => "F = cos x + cos y + cos z",
            2 => "F = cos x · cos y · cos z − sin x · sin y · sin z",
            _ => "F = sin x · cos y + sin y · cos z + sin z · cos x"
        };
        PeriodicThicknessPanel.IsEnabled = PeriodicRegionBox.SelectedIndex == 0;
        PeriodicCutPositionSlider.IsEnabled = PeriodicCutPositionBox.IsEnabled = PeriodicCutBox.SelectedIndex > 0;
    }

    private void Periodic_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.PeriodicSurfaces || PeriodicCutPositionBox is null) return;
        UpdatePeriodicControls();
        Parameter_OnChanged(sender, e);
    }
}
