using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class DomainColoringPaletteWindow
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(GammaBox)) return;
        NumericInput.Real(this, GammaBox, "Гамма", .1, 5);
    }
}
