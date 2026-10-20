using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class PhoenixWindow
{
    private PhoenixParameterAtlasWindow? _parameterAtlas;
    private void ParameterAtlasButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_parameterAtlas is { } existing) { existing.Activate(); return; }
        CreateParameterAtlas().Show();
    }
    private PhoenixParameterAtlasWindow CreateParameterAtlas()
    {
        NumericInput.CommitAll(this);
        var atlas = new PhoenixParameterAtlasWindow(CaptureState("atlas")) { Owner = this };
        _parameterAtlas = atlas;
        atlas.ParametersApplied += (_, _) => ApplyAtlasParameters(atlas.SelectedParameters);
        atlas.Closed += (_, _) => { if (ReferenceEquals(_parameterAtlas, atlas)) _parameterAtlas = null; };
        return atlas;
    }
    private void ApplyAtlasParameters(PhoenixAtlasParameters point)
    {
        _updatingControls = true;
        C1RealBox.Text = point.C1Real.ToString(CultureInfo.InvariantCulture);
        C1ImaginaryBox.Text = point.C1Imaginary.ToString(CultureInfo.InvariantCulture);
        C2RealBox.Text = point.C2Real.ToString(CultureInfo.InvariantCulture);
        C2ImaginaryBox.Text = point.C2Imaginary.ToString(CultureInfo.InvariantCulture);
        SelectByTag(PlaneModeBox, PhoenixPlaneMode.Julia);
        _updatingControls = false;
        UpdatePlaneUi(); ScheduleRender();
    }
    private void RefreshParameterAtlas()
    {
        if (_parameterAtlas is null || _updatingControls) return;
        try { _parameterAtlas.UpdateFormulaParameters(CaptureState("atlas")); }
        catch (InvalidOperationException) { /* Сфокусированный черновик не меняет действующую формулу. */ }
    }
}
