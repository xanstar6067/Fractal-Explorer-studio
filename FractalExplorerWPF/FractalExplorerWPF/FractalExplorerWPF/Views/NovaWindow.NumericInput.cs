using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class NovaWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Integer(this, IterationsBox, "Итерации", 10, 100000);
        NumericInput.Decimal(this, ThresholdBox, "Порог выхода", 2m, 1000m);
        NumericInput.Decimal(this, MBox, "Релаксация", .1m, 5m);
        NumericInput.Decimal(this, PRealBox, "PRealBox", -10m, 10m);
        NumericInput.Decimal(this, PImaginaryBox, "PImaginaryBox", -10m, 10m);
        NumericInput.Decimal(this, Z0RealBox, "Z0RealBox", -10m, 10m);
        NumericInput.Decimal(this, Z0ImaginaryBox, "Z0ImaginaryBox", -10m, 10m);
        NumericInput.Decimal(this, CRealBox, "CRealBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, CImaginaryBox, "CImaginaryBox", decimal.MinValue, decimal.MaxValue);
        NumericInput.Zoom(this, ZoomBox, FloatExp.FromDouble(MinZoom), FloatExp.FromDouble(MaxZoom), () => _viewport.Zoom);
    }
}
