using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class InverseCollatzPaletteWindow
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(GammaBox)) return;
        NumericInput.Real(this, GammaBox, "Гамма", .1, 5);
        NumericInput.Integer(this, LevelsPerCycleBox, "Уровни цикла", 2, 500);
    }
}
