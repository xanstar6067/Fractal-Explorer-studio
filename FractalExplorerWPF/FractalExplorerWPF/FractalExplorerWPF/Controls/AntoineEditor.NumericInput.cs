namespace FractalExplorerWPF.Controls;

public partial class AntoineEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(ScaleBox)) return;
        NumericInput.Real(this, ScaleBox, "Масштаб звеньев", 1.35, 1.5);
        NumericInput.Real(this, ThicknessBox, "Толщина звеньев", 0, 1);
        NumericInput.Real(this, TiltBox, "Наклон звеньев", 60, 90);
    }
}
