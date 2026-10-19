using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class FlameTransformEditorWindow
{
    private void InitializeNumericInput()
    {
        if (_transforms.Count == 0) { ABox.Text = EBox.Text = "0.5"; BBox.Text = CBox.Text = DBox.Text = FBox.Text = "0"; }
        NumericInput.Real(this, ABox, "A", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, BBox, "B", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, CBox, "C", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, DBox, "D", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, EBox, "E", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, FBox, "F", -double.MaxValue, double.MaxValue);
    }
}
