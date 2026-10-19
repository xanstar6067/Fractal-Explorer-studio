using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class FlameWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, SamplesBox, "Сэмплы", 1000, 20000000);
        NumericInput.Integer(this, IterationsBox, "Итерации", 1, int.MaxValue);
        NumericInput.Integer(this, WarmupBox, "Прогрев", 0, int.MaxValue);
        NumericInput.Real(this, ScaleBox, "Масштаб", 1e-9, double.MaxValue, absolute: true);
        NumericInput.Real(this, ExposureBox, "Экспозиция", .1, 10);
        NumericInput.Real(this, GammaBox, "Гамма", .1, 5);
        NumericInput.Real(this, CenterXBox, "CenterXBox", -double.MaxValue, double.MaxValue);
        NumericInput.Real(this, CenterYBox, "CenterYBox", -double.MaxValue, double.MaxValue);
    }
}
