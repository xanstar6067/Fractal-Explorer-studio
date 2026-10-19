using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class TranscendentalJuliaWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, LambdaRealBox, "Re λ", -1e6, 1e6);
        NumericInput.Real(this, LambdaImaginaryBox, "Im λ", -1e6, 1e6);
        NumericInput.Integer(this, IterationsBox, "итерации", 1, 10000);
        NumericInput.Integer(this, PeriodBox, "макс. период", 1, 32);
        NumericInput.Real(this, ToleranceBox, "допуск", 1e-12, 1e-3);
        NumericInput.Integer(this, DepthBox, "глубина прообразов", 1, 80);
        NumericInput.Integer(this, BranchesBox, "радиус ветвей", 1, 24);
        NumericInput.Integer(this, BudgetBox, "бюджет прообразов", 1000, 10_000_000);
        NumericInput.Integer(this, RayLimitBox, "частей лучей", 8, 10000);
        NumericInput.Real(this, RayAccuracyBox, "точность лучей", 0.05, 2);
        NumericInput.Real(this, CenterXBox, "центр X", -1e10, 1e10);
        NumericInput.Real(this, CenterYBox, "центр Y", -1e10, 1e10);
        NumericInput.Real(this, ZoomBox, "приближение", 1e-6, 1e10);
    }
}
