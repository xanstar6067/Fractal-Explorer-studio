using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class CollatzWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, ArgumentCyclesBox, "обороты аргумента", 0.1, 20);
        NumericInput.Real(this, MagnitudeScaleBox, "масштаб модуля", 0.01, 20);
        NumericInput.Real(this, TrapScaleBox, "чувствительность ловушки", 0.01, 100);
        NumericInput.Real(this, CycleToleranceBox, "допуск цикла", 1e-12, 0.1);
        NumericInput.Integer(this, MaximumPeriodBox, "максимальный период", 1, 64);
        NumericInput.Real(this, DensityExposureBox, "экспозиция плотности", 0.1, 10);
        NumericInput.Integer(this, DensitySampleStepBox, "шаг начальных точек", 1, 8);
        NumericInput.Integer(this, IterationsBox, "Итерации", 10, 100000);
        NumericInput.Decimal(this, ThresholdBox, "Порог выхода", 2m, 10000m);
        NumericInput.Decimal(this, PParameterBox, "p", -100m, 100m);
        NumericInput.Decimal(this, QRealParameterBox, "Re(q)", -100m, 100m);
        NumericInput.Decimal(this, QImaginaryParameterBox, "Im(q)", -100m, 100m);
        NumericInput.Zoom(this, ZoomBox, FloatExp.FromDouble(MinZoom), FloatExp.FromDouble(MaxZoom), () => _viewport.Zoom);
    }
}
