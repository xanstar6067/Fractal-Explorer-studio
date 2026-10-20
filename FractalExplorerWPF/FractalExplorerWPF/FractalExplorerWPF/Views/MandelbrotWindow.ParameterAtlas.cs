using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotWindow
{
    private JuliaParameterAtlasWindow? _parameterAtlas;

    private void ParameterAtlasButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_definition.HasJuliaConstant) return;
        if (_parameterAtlas is { } existing) { existing.Activate(); return; }
        CreateParameterAtlas().Show();
    }

    private JuliaParameterAtlasWindow CreateParameterAtlas()
    {
        NumericInput.CommitAll(this);
        var atlas = new JuliaParameterAtlasWindow(CaptureState("atlas")) { Owner = this };
        _parameterAtlas = atlas;
        atlas.ConstantApplied += (_, _) => ApplyAtlasConstant(atlas.SelectedReal, atlas.SelectedImaginary);
        atlas.Closed += (_, _) => { if (ReferenceEquals(_parameterAtlas, atlas)) _parameterAtlas = null; };
        return atlas;
    }

    private void ApplyAtlasConstant(decimal real, decimal imaginary)
    {
        if (_closed || !_definition.HasJuliaConstant) return;
        _updatingControls = true;
        JuliaRealBox.Text = real.ToString(CultureInfo.InvariantCulture);
        JuliaImaginaryBox.Text = imaginary.ToString(CultureInfo.InvariantCulture);
        _updatingControls = false;
        UpdateJuliaMapMarker();
        ScheduleRender();
    }
}
