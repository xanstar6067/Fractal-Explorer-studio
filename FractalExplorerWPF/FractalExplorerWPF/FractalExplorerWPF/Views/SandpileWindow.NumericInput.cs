using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class SandpileWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, SizeBox, "Размер сетки", 33, 513, step: 2);
        NumericInput.Integer(this, InitialBox, "Начальный песок", 0, 10000000);
        NumericInput.Integer(this, BrushGrainsBox, "Песок кисти", 1, 1000000);
        NumericInput.Integer(this, BrushRadiusBox, "Радиус кисти", 0, 20);
        NumericInput.Integer(this, DropBox, "Порция песка", 1, 1000000);
    }
}
