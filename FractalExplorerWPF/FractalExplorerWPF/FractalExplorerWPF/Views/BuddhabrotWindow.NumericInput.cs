using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class BuddhabrotWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, SamplesBox, "Сэмплы", 1000, int.MaxValue);
        NumericInput.Integer(this, IterationsBox, "Итерации", 10, 10000);
        NumericInput.Decimal(this, ZoomBox, "Масштаб", .0000001m, 100000m, appliedValue: () => _zoom);
        NumericInput.Decimal(this, MinReBox, "MinReBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, MaxReBox, "MaxReBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, MinImBox, "MinImBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, MaxImBox, "MaxImBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.OrderedPair(this, MinReBox, MaxReBox);
        NumericInput.OrderedPair(this, MinImBox, MaxImBox);
    }
}
