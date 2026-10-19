namespace FractalExplorerWPF.Controls;

public partial class Kobayashi3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(KobSizeBox)) return;
        NumericInput.Integer(this, KobSizeBox, "Сетка", 32, 128);
        NumericInput.Real(this, KobWidthBox, "Толщина границы", .5, 2);
        NumericInput.Real(this, KobAnisotropyBox, "Анизотропия", -.06, .06);
        NumericInput.Real(this, KobMobilityBox, "Подвижность", .1, 4);
        NumericInput.Real(this, KobDiffusionBox, "Теплопроводность", .1, 4);
        NumericInput.Real(this, KobHeatBox, "Скрытая теплота", 0, 3);
        NumericInput.Real(this, KobColdBox, "Переохлаждение", .05, 1.5);
        NumericInput.Real(this, KobNoiseBox, "Шум", 0, .1);
        NumericInput.Real(this, KobDtBox, "Шаг времени", .001, .1);
        NumericInput.Integer(this, KobWarmupBox, "Подготовка", 0, 20000);
        NumericInput.Integer(this, KobRandomBox, "Случайное число", int.MinValue, int.MaxValue);
        NumericInput.Real(this, KobSeedRadiusBox, "Радиус зародыша", .035, .16);
        NumericInput.Real(this, KobSeedSpreadBox, "Разброс зародышей", 0, .3);
        NumericInput.Integer(this, KobSeedCountBox, "Число зародышей", 1, 8);
    }
}
