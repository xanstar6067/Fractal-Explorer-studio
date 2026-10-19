using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DlaWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, ParticlesBox, "Частицы", 500, 100000);
        NumericInput.Integer(this, GridSizeBox, "Размер сетки", 201, 1401, step: 2);
        NumericInput.Integer(this, MaxStepsBox, "Шаги блуждания", 100, 100000);
        NumericInput.Integer(this, RandomSeedBox, "Seed", int.MinValue, int.MaxValue);
        NumericInput.Real(this, StickinessBox, "Прилипание", 0, 1);
        NumericInput.Real(this, DriftXBox, "Дрейф X", -2, 2);
        NumericInput.Real(this, DriftYBox, "Дрейф Y", -2, 2);
        NumericInput.Real(this, ParticleRadiusBox, "Радиус частицы", .25, 8);
        NumericInput.Real(this, CenterXBox, "Центр X", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, CenterYBox, "Центр Y", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, ViewWidthBox, "Ширина вида", .02, 10);
    }
}
