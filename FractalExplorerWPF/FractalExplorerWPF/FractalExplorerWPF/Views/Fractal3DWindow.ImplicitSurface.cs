using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Models;

using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private string _implicitAppliedFormula = new ImplicitSurfaceSettings().Formula;

    private ImplicitSurfaceSettings ReadImplicitSurface(string formula)
    {
        var settings = new ImplicitSurfaceSettings
        {
            Formula = formula,
            A = ReadDouble(ImplicitABox, "a", -10, 10), B = ReadDouble(ImplicitBBox, "b", -10, 10),
            C = ReadDouble(ImplicitCBox, "c", -10, 10),
            Extent = ReadDouble(ImplicitExtentBox, "Область ±R", .1, 10),
            Level = ReadDouble(ImplicitLevelBox, "Уровень F", -100, 100),
            CutAxis = Math.Max(0, ImplicitCutBox.SelectedIndex),
            CutPosition = ReadDouble(ImplicitCutPositionBox, "Положение среза", -10, 10)
        };
        settings.Validate();
        return settings;
    }
    private ImplicitSurfaceSettings CaptureImplicitSurface() => Kind == Fractal3DKind.ImplicitSurface
        ? ReadImplicitSurface(_implicitAppliedFormula) : new();

    private void LoadImplicitSurface(ImplicitSurfaceSettings? settings)
    {
        if (Kind != Fractal3DKind.ImplicitSurface) return;
        var s = settings ?? new(); s.Validate();
        _implicitAppliedFormula = s.Formula;
        ImplicitFormulaBox.Text = s.Formula;
        ImplicitCutBox.SelectedIndex = s.CutAxis;
        (Slider Slider, TextBox Box, double Value)[] fields =
        [
            (ImplicitASlider, ImplicitABox, s.A), (ImplicitBSlider, ImplicitBBox, s.B),
            (ImplicitCSlider, ImplicitCBox, s.C), (ImplicitExtentSlider, ImplicitExtentBox, s.Extent),
            (ImplicitLevelSlider, ImplicitLevelBox, s.Level),
            (ImplicitCutPositionSlider, ImplicitCutPositionBox, s.CutPosition)
        ];
        foreach (var field in fields)
        {
            field.Slider.Value = field.Value;
            field.Box.SetCurrentValue(TextBox.TextProperty, field.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        UpdateImplicitControls();
        ImplicitFormulaStatus.Text = "Формула применена. Параметры меняют поверхность автоматически.";
    }
    private void UpdateImplicitControls() => ImplicitCutPositionSlider.IsEnabled =
        ImplicitCutPositionBox.IsEnabled = ImplicitCutBox.SelectedIndex > 0;

    private void ImplicitFormula_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.ImplicitSurface || ImplicitFormulaStatus is null) return;
        ImplicitFormulaStatus.Text = ImplicitFormulaBox.Text == _implicitAppliedFormula
            ? "Формула применена." : "Формула изменена. Нажмите «Построить поверхность» или Ctrl+Enter.";
    }
    private void ImplicitFormula_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { ImplicitApply_OnClick(sender, new RoutedEventArgs()); e.Handled = true; }
    }
    private void ImplicitApply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = ReadImplicitSurface(ImplicitFormulaBox.Text.Trim());
            _implicitAppliedFormula = s.Formula;
            ImplicitFormulaStatus.Text = "Формула применена. Параметры меняют поверхность автоматически.";
            _renderCts?.Cancel();
            ScheduleRender(immediate: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { ImplicitFormulaStatus.Text = error.Message; }
    }
    private void Implicit_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.ImplicitSurface || ImplicitCutPositionBox is null) return;
        UpdateImplicitControls();
        Parameter_OnChanged(sender, e);
    }
    private bool IsCurrentImplicitFrame(Fractal3DState state)
    {
        if (Kind != Fractal3DKind.ImplicitSurface) return true;
        try { return state.ImplicitSurface == CaptureImplicitSurface(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }
}
