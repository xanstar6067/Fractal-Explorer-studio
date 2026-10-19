using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotPaletteWindow
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(GammaBox)) return;
        NumericInput.Real(this, GammaBox, "Гамма", .1, 5);
        NumericInput.Integer(this, PeriodBox, "Период", 2, 100000);
    }
}
