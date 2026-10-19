namespace FractalExplorerWPF.Controls;

public partial class Physarum3DEditor
{
    private void InitializeNumericInput()
    {
        if (NumericInput.IsRegistered(PhysarumCountBox)) return;
        NumericInput.Integer(this, PhysarumCountBox, "Агентов", 1024, 262144);
        NumericInput.Real(this, PhysarumSensorBox, "Дальность чувств", 1, 16);
        NumericInput.Real(this, PhysarumAngleBox, "Угол обзора", 5, 85);
        NumericInput.Real(this, PhysarumTurnBox, "Поворот", 1, 70);
        NumericInput.Real(this, PhysarumMoveBox, "Скорость", .1, 2);
        NumericInput.Real(this, PhysarumDepositBox, "След", .1, 8);
        NumericInput.Real(this, PhysarumDiffusionBox, "Размывание", 0, 1);
        NumericInput.Real(this, PhysarumDecayBox, "Испарение", .001, .2);
        NumericInput.Integer(this, PhysarumWarmupBox, "Подготовка", 0, 2000);
        NumericInput.Integer(this, PhysarumRandomBox, "Случайное число", int.MinValue, int.MaxValue);
    }
}
