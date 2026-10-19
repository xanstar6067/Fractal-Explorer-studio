using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class BuddhabrotPaletteWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, GammaBox, "Гамма", .1, 5);
        NumericInput.Integer(this, StepsBox, "Шаги", 2, 100000);
    }
}
