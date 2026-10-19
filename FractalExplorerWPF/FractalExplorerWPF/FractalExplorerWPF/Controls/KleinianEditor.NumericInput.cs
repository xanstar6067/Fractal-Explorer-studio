namespace FractalExplorerWPF.Controls;

public partial class KleinianEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(RadiusBox)) return;
        NumericInput.Real(this, RadiusBox, "Относительный радиус сфер", .65, 1);
        NumericInput.Real(this, MinRadiusBox, "Минимальный радиус деталей", .001, .05);
        NumericInput.Real(this, ThicknessBox, "Утолщение множества", 0, .04);
    }
}
