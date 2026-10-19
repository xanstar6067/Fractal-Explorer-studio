using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class LSystemWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, DepthBox, "Глубина", 0, 20);
        NumericInput.Real(this, AngleBox, "Угол", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, InitialAngleBox, "Начальный угол", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, StartThicknessBox, "Начальная толщина", .1, 50);
        NumericInput.Real(this, EndThicknessBox, "Конечная толщина", .1, 50);
        NumericInput.Real(this, AnimationDurationBox, "Длительность анимации", .5, 120);
    }
}
