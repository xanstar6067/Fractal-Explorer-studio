using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Views;

public partial class LyapunovPaletteWindow
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(RangeBox)) return;
        NumericInput.Real(this, RangeBox, "Диапазон", 1e-9, double.MaxValue);
        NumericInput.Real(this, ZeroBox, "Нулевая полоса", 1e-9, double.MaxValue);
    }
}
