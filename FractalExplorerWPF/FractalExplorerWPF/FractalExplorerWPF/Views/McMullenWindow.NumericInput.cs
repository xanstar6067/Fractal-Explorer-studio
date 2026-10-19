using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class McMullenWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, CenterXBox, "центр X", -1e6, 1e6);
        NumericInput.Real(this, CenterYBox, "центр Y", -1e6, 1e6);
        NumericInput.Real(this, ZoomBox, "приближение", 1e-4, 1e10);
        NumericInput.Integer(this, PBox, "степень p", 2, 12);
        NumericInput.Integer(this, QBox, "порядок полюса q", 1, 12);
        NumericInput.Real(this, LambdaRealBox, "Re λ", -1e6, 1e6);
        NumericInput.Real(this, LambdaImaginaryBox, "Im λ", -1e6, 1e6);
        NumericInput.Integer(this, IterationsBox, "итерации", 1, 10000);
        NumericInput.Real(this, EscapeRadiusBox, "радиус выхода", 2, 1e6);
        NumericInput.Real(this, ColorScaleBox, "масштаб цвета", 0.1, 1000);
        NumericInput.Real(this, ColorOffsetBox, "сдвиг цвета", 0, 1);
        NumericInput.Real(this, TrapRadiusBox, "радиус ловушки", 0, 100);
        NumericInput.Integer(this, PeriodBox, "период", 1, 64);
        NumericInput.Real(this, ToleranceBox, "допуск", 1e-12, 1e-3);
    }
}
