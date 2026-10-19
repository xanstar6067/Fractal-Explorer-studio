using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class NewtonPoolsWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, IterationsBox, "Итерации", 1, 100000);
        NumericInput.Integer(this, HouseholderOrderBox, "Порядок Хаусхолдера", 2, 12);
        NumericInput.Real(this, RootToleranceBox, "Точность корней", 1e-12, .1);
        NumericInput.Real(this, RootSearchRadiusBox, "Радиус поиска", .01, 1e9);
        NumericInput.Real(this, RelaxationRealBox, "RelaxationRealBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, RelaxationImaginaryBox, "RelaxationImaginaryBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, FixedInitialZRealBox, "FixedInitialZRealBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, FixedInitialZImaginaryBox, "FixedInitialZImaginaryBox", -double.MaxValue, double.MaxValue);
        NumericInput.Zoom(this, ZoomBox, MinZoom, MaxZoom, () => _viewport.Zoom);
    }
}
