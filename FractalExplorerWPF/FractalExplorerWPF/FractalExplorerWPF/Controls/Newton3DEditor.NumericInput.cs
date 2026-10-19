namespace FractalExplorerWPF.Controls;

public partial class Newton3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(IterationsBox)) return;
        NumericInput.Integer(this, IterationsBox, "Итерации Ньютона", 16, 128);
        NumericInput.Real(this, ExtentBox, "Полуразмер области", .2, 4);
        NumericInput.Real(this, SliceWBox, "Координата w", -2, 2);
        NumericInput.Real(this, RelaxationBox, "Релаксация", .2, 1.8);
        NumericInput.Integer(this, BoundaryBox, "Порог границы", 2, 127);
        NumericInput.OrderedPair(this, BoundaryBox, IterationsBox);
        NumericInput.Real(this, RootRadiusBox, "Радиус поиска корней", 1, 64);
    }
}
