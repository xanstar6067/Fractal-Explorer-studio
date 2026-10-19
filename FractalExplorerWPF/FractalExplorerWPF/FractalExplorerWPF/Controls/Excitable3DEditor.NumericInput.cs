namespace FractalExplorerWPF.Controls;

public partial class Excitable3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(ExSizeBox)) return;
        NumericInput.Integer(this, ExSizeBox, "Сетка", 32, 128);
        NumericInput.Real(this, ExABox, "Параметр A", .0001, 3);
        NumericInput.Real(this, ExBBox, "Параметр B", .0001, 12);
        NumericInput.Real(this, ExEpsilonBox, "ε", .005, .5);
        NumericInput.Real(this, ExMu1Box, "μ₁", .01, .5);
        NumericInput.Real(this, ExMu2Box, "μ₂", .1, 1);
        NumericInput.Real(this, ExDuBox, "Диффузия U", .01, 2);
        NumericInput.Real(this, ExDvBox, "Диффузия V", 0, 1);
        NumericInput.Real(this, ExDtBox, "Шаг времени", .0001, .02);
        NumericInput.Real(this, ExSpacingBox, "Шаг сетки", .25, 2);
        NumericInput.Integer(this, ExWarmupBox, "Подготовка", 0, 20000);
    }
}
