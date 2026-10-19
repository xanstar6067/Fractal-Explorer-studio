using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class SnowCrystalWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, DiffusionBox, "Диффузия", .05, 2);
        NumericInput.Real(this, VaporBox, "Пар", 0, .99);
        NumericInput.Real(this, DepositionBox, "Осаждение", 0, .05);
        NumericInput.Integer(this, RadiusBox, "Радиус поля", 32, 320);
        NumericInput.Integer(this, SeedRadiusBox, "Радиус затравки", 0, 8);
    }
}
