using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DomainColoringWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, HueCyclesBox, "обороты оттенка", 0.05, 32);
        NumericInput.Real(this, MagnitudeExposureBox, "экспозиция модуля", 0.01, 20);
        NumericInput.Real(this, RingDensityBox, "плотность колец", 0.05, 32);
        NumericInput.Integer(this, PhaseSectorsBox, "число фазовых секторов", 1, 256);
        NumericInput.Real(this, ContourStrengthBox, "сила контуров", 0, 1);
        NumericInput.Real(this, SaturationBox, "насыщенность", 0, 1);
        NumericInput.Zoom(this, ZoomBox, FloatExp.FromDouble(1e-12), FloatExp.FromDouble(1e12), () => _viewport.Zoom);
    }
}
