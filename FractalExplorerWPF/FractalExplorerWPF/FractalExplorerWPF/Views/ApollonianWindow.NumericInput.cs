using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class ApollonianWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, DepthBox, "Глубина", 1, 18);
        NumericInput.Integer(this, CircleLimitBox, "Лимит окружностей", 20, 150000);
        NumericInput.Real(this, MinimumRadiusBox, "Минимальный радиус", 1e-9, .1);
        NumericInput.Real(this, LineWidthBox, "Толщина линии", .25, 20);
        NumericInput.Real(this, ViewWidthBox, "Ширина вида", 1e-6, 20);
        NumericInput.Real(this, CenterXBox, "CenterXBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, CenterYBox, "CenterYBox", -double.MaxValue, double.MaxValue);
    }
}
