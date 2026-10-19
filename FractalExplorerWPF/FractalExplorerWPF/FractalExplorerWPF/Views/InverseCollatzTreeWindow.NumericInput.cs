using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class InverseCollatzTreeWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, DepthBox, "максимальная глубина", 1, 500);
        NumericInput.Integer(this, MaxNodesBox, "лимит узлов", 10, 1_000_000);
        NumericInput.Integer(this, AnimationIntervalBox, "интервал роста", 30, 5_000);
        NumericInput.Real(this, NodeRadiusBox, "радиус узла", 0.4, 20);
        NumericInput.Real(this, LineThicknessBox, "толщина линий", 0.2, 8);
    }
}
