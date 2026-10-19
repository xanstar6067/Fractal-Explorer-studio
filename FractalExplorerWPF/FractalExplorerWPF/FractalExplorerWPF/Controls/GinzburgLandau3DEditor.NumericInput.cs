namespace FractalExplorerWPF.Controls;

public partial class GinzburgLandau3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(GlSizeBox)) return;
        NumericInput.Integer(this, GlSizeBox, "Сетка", 32, 128);
        NumericInput.Real(this, GlC1Box, "c₁", -3, 3);
        NumericInput.Real(this, GlC3Box, "c₃", -3, 3);
        NumericInput.Real(this, GlDtBox, "Шаг времени", .0005, .05);
        NumericInput.Real(this, GlSpacingBox, "Шаг сетки", .5, 2);
        NumericInput.Integer(this, GlSeedNumberBox, "Начальное число", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, GlWarmupBox, "Подготовка", 0, 10000);
    }
}
