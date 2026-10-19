using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class BasinExplorerWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, DampingBox, "Трение", 0, 10);
        NumericInput.Real(this, RestoringForceBox, "Возвратная сила", 0, 10);
        NumericInput.Real(this, PhysicalHeightBox, "Высота / смягчение", 0.03, 5);
        NumericInput.Real(this, PhysicsTimeStepBox, "Шаг времени", 0.001, 0.1);
        NumericInput.Real(this, PhysicsMaxTimeBox, "Время моделирования", 0.1, 1000);
        NumericInput.Real(this, SettleSpeedBox, "Скорость покоя", 0.001, 1);
        NumericInput.Real(this, SettleTimeBox, "Время покоя", 0.1, 10);
        NumericInput.Real(this, PhysicalEscapeBox, "Радиус ухода", 2, 1e6);
        NumericInput.Real(this, VelocityXBox, "Скорость X", -100, 100);
        NumericInput.Real(this, VelocityYBox, "Скорость Y", -100, 100);
        NumericInput.Real(this, ForceCenterXBox, "Координата X", -1e4, 1e4);
        NumericInput.Real(this, ForceCenterYBox, "Координата Y", -1e4, 1e4);
        NumericInput.Real(this, ForceCenterStrengthBox, "Сила центра", 0.01, 100);
        NumericInput.Real(this, ForceCenterRadiusBox, "Радиус захвата", 0.01, 10);
        NumericInput.Real(this, LearningRateBox, "Шаг α", 1e-6, 10);
        NumericInput.Real(this, OptimizerMomentumBox, "Momentum / β₁", 0, 0.9999);
        NumericInput.Real(this, AdamBeta2Box, "β₂", 0, 0.99999);
        NumericInput.Real(this, AdamEpsilonBox, "ε Adam", 1e-12, 0.1);
        NumericInput.Real(this, FlowTimeStepBox, "Макс. шаг времени", 0.001, 1);
        NumericInput.Real(this, FlowMaxTimeBox, "Макс. время", 0.01, 2000);
        NumericInput.Real(this, FlowAccuracyBox, "Точность интегратора", 1e-10, 1e-3);
        NumericInput.Real(this, PlanarToleranceBox, "Допуск сходимости", 1e-8, 0.01);
        NumericInput.Real(this, PlanarSearchRadiusBox, "Радиус поиска", 0.1, 100);
        NumericInput.Real(this, PlanarEscapeRadiusBox, "Радиус ухода", 2, 1e6);
        NumericInput.Integer(this, IterationsBox, "Итерации", 1, 100_000);
        NumericInput.Real(this, ShadingScaleBox, "Шкала яркости", 0.1, 1e6);
        NumericInput.Real(this, RootToleranceBox, "Точность корней", 1e-12, 0.1);
        NumericInput.Real(this, RootSearchRadiusBox, "Радиус поиска", 0.01, 1e9);
        NumericInput.Integer(this, MaxPeriodBox, "Максимальный период", 1, 64);
        NumericInput.Real(this, CycleToleranceBox, "Допуск цикла", 1e-12, 0.1);
        NumericInput.Real(this, AttractorSearchRadiusBox, "Радиус затравок", 0.01, 1e6);
        NumericInput.Real(this, EscapeRadiusBox, "Радиус ухода", 2, 1e150);
        NumericInput.Real(this, LaguerreDegreeBox, "Параметр n", 1, 1024);
        NumericInput.Zoom(this, ZoomBox, FloatExp.FromDouble(MinZoom), FloatExp.FromDouble(MaxZoom), () => _viewport.Zoom);
    }
}
