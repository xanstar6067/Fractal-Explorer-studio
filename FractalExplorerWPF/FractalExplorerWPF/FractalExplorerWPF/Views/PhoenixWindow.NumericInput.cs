using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class PhoenixWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, IterationsBox, "Итерации", 10, 100000);
        NumericInput.Integer(this, PrimaryPowerBox, "Степень a", 2, 12);
        NumericInput.Integer(this, SecondaryPowerBox, "Степень b", 0, 12);
        NumericInput.Integer(this, MaximumPeriodBox, "Максимальный период", 1, 64);
        NumericInput.Decimal(this, ThresholdBox, "Порог выхода", 2m, 1000m);
        NumericInput.Decimal(this, OrbitTrapRadiusBox, "Радиус ловушки", 0m, 1000m);
        NumericInput.Decimal(this, OrbitTrapStrengthBox, "Сила ловушки", 1e-28m, 1000m);
        NumericInput.Decimal(this, StripeFrequencyBox, "Частота полос", 0m, 1000m);
        NumericInput.Decimal(this, StripeStrengthBox, "Сила полос", 0m, 1m);
        NumericInput.Decimal(this, CycleToleranceBox, "Допуск цикла", 1e-28m, .1m);
        NumericInput.Decimal(this, C1RealBox, "C1RealBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C1ImaginaryBox, "C1ImaginaryBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C2RealBox, "C2RealBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C2ImaginaryBox, "C2ImaginaryBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, InitialZRealBox, "InitialZRealBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, InitialZImaginaryBox, "InitialZImaginaryBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, PreviousRealBox, "PreviousRealBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, PreviousImaginaryBox, "PreviousImaginaryBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Zoom(this, ZoomBox, MinZoom, MaxZoom, () => _viewport.Zoom);
    }
}
