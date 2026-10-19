namespace FractalExplorerWPF.Controls;

public partial class CahnHilliard3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(CahnMeanBox)) return;
        NumericInput.Real(this, CahnMeanBox, "Средний состав", -.55, .55);
        NumericInput.Real(this, CahnNoiseBox, "Шум", .001, .2);
        NumericInput.Real(this, CahnKappaBox, "κ", .5, 8);
        NumericInput.Real(this, CahnMobilityBox, "M", .01, 2);
        NumericInput.Real(this, CahnDtBox, "Δt", .01, 2);
        NumericInput.Integer(this, CahnWarmupBox, "Подготовка", 0, 5000);
        NumericInput.Integer(this, CahnRandomBox, "Случайное число", int.MinValue, int.MaxValue);
    }
}
