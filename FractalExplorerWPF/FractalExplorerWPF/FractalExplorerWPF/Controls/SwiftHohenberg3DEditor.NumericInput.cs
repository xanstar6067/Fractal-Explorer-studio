namespace FractalExplorerWPF.Controls;

public partial class SwiftHohenberg3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(ShRBox)) return;
        NumericInput.Real(this, ShRBox, "Неустойчивость r", -.5, 1);
        NumericInput.Real(this, ShBBox, "Асимметрия b", -1.5, 1.5);
        NumericInput.Real(this, ShQBox, "Волновое число q₀", .4, 1.5);
        NumericInput.Real(this, ShSpacingBox, "Шаг сетки", .5, 2);
        NumericInput.Real(this, ShDtBox, "Шаг времени", .002, .05);
        NumericInput.Real(this, ShNoiseBox, "Шум", 0, .3);
        NumericInput.Integer(this, ShSeedNumberBox, "Начальное число", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, ShWarmupBox, "Подготовка", 0, 5000);
    }
}
