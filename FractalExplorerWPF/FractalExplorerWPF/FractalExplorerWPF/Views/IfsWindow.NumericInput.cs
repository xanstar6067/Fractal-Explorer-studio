using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class IfsWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, IterationsBox, "Итерации", 10000, 10000000);
        NumericInput.Real(this, ScaleBox, "Масштаб", .05, 40);
        NumericInput.Real(this, CenterXBox, "CenterXBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, CenterYBox, "CenterYBox", -double.MaxValue, double.MaxValue);
    }
}
