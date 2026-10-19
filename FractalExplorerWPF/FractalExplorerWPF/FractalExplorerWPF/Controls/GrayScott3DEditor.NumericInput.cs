namespace FractalExplorerWPF.Controls;

public partial class GrayScott3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(GraySizeBox)) return;
        NumericInput.Integer(this, GraySizeBox, "Сетка", 32, 128);
        NumericInput.Real(this, GrayFeedBox, "F", 0, .1);
        NumericInput.Real(this, GrayKillBox, "K", 0, .1);
        NumericInput.Real(this, GrayDuBox, "Диффузия U", .001, .16);
        NumericInput.Real(this, GrayDvBox, "Диффузия V", .001, .16);
        NumericInput.Integer(this, GrayRandomBox, "Случайное число", int.MinValue, int.MaxValue);
    }
}
