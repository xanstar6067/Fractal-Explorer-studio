namespace FractalExplorerWPF.Controls;

public partial class Lenia3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(LeniaRadiusBox)) return;
        NumericInput.Real(this, LeniaRadiusBox, "Радиус ядра", 2, 63);
        NumericInput.Real(this, LeniaMeanBox, "μ", .01, .5);
        NumericInput.Real(this, LeniaWidthBox, "σ", .002, .15);
        NumericInput.Real(this, LeniaBeta1Box, "β₁", 0, 1);
        NumericInput.Real(this, LeniaBeta2Box, "β₂", 0, 1);
        NumericInput.Real(this, LeniaBeta3Box, "β₃", 0, 1);
        NumericInput.Real(this, LeniaBeta4Box, "β₄", 0, 1);
        NumericInput.Real(this, LeniaDtBox, "Δt", .01, .2);
        NumericInput.Integer(this, LeniaWarmupBox, "Подготовка", 0, 3000);
        NumericInput.Integer(this, LeniaRandomBox, "Случайное число", int.MinValue, int.MaxValue);
        NumericInput.Real(this, LeniaNoiseBox, "Шум затравки", 0, .15);
    }
}
