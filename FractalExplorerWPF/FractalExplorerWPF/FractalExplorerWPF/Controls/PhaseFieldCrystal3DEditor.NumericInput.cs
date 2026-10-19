namespace FractalExplorerWPF.Controls;

public partial class PhaseFieldCrystal3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(PfcEpsilonBox)) return;
        NumericInput.Real(this, PfcEpsilonBox, "Переохлаждение ε", -.2, .6);
        NumericInput.Real(this, PfcMeanBox, "Средняя плотность", -.6, .6);
        NumericInput.Real(this, PfcQBox, "Волновое число", .6, 1.4);
        NumericInput.Real(this, PfcSpacingBox, "Шаг сетки", .5, 1.5);
        NumericInput.Real(this, PfcMobilityBox, "Подвижность", .1, 2);
        NumericInput.Real(this, PfcDtBox, "Шаг времени", .002, .03);
        NumericInput.Real(this, PfcNoiseBox, "Шум", 0, .2);
        NumericInput.Integer(this, PfcSeedNumberBox, "Начальное число", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, PfcWarmupBox, "Подготовка", 0, 6000);
    }
}
