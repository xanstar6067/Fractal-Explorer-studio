using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, AttractorABox, "Параметр a", -100, 100);
        NumericInput.Real(this, AttractorBBox, "Параметр b", -100, 100);
        NumericInput.Real(this, AttractorCBox, "Параметр c", -100, 100);
        NumericInput.Real(this, AttractorDBox, "Параметр d", -100, 100);
        NumericInput.Real(this, AttractorEBox, "Параметр e", -100, 100);
        NumericInput.Real(this, AttractorFBox, "Параметр f", -100, 100);
        NumericInput.Real(this, AttractorStepBox, "Шаг времени", .0001, .05);
        NumericInput.Real(this, AttractorStartXBox, "Начальная координата X", -100, 100);
        NumericInput.Real(this, AttractorStartYBox, "Начальная координата Y", -100, 100);
        NumericInput.Real(this, AttractorStartZBox, "Начальная координата Z", -100, 100);
        NumericInput.Integer(this, DlaRandomSeedBox, "Случайное число", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, FlameSeedBox, "Начальное число Flame", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, FlameWarmupBox, "Прогрев Flame", 10, 1000);
        NumericInput.Real(this, FlameExposureBox, "Экспозиция", .05, 20);
        NumericInput.Real(this, FlameGammaBox, "Гамма", .5, 4);
        NumericInput.Real(this, FlameDensityBox, "Плотность", .05, 10);
        NumericInput.Real(this, FlameVibrancyBox, "Насыщенность", 0, 2);
        NumericInput.Real(this, ImplicitABox, "a", -10, 10);
        NumericInput.Real(this, ImplicitBBox, "b", -10, 10);
        NumericInput.Real(this, ImplicitCBox, "c", -10, 10);
        NumericInput.Real(this, ImplicitExtentBox, "Область ±R", .1, 10);
        NumericInput.Real(this, ImplicitLevelBox, "Уровень F", -100, 100);
        NumericInput.Real(this, ImplicitCutPositionBox, "Положение среза", -10, 10);
        NumericInput.Integer(this, KifsSectorsBox, "Число лучей", 3, 16);
        NumericInput.Real(this, KifsScaleBox, "Масштаб KIFS", 1.2, 4);
        NumericInput.Real(this, KifsRadiusBox, "Размер исходной формы", .1, 2);
        NumericInput.Real(this, KifsRotationXBox, "Поворот X", -180, 180);
        NumericInput.Real(this, KifsRotationYBox, "Поворот Y", -180, 180);
        NumericInput.Real(this, KifsRotationZBox, "Поворот Z", -180, 180);
        NumericInput.Real(this, KifsOffsetXBox, "Смещение X", -2, 2);
        NumericInput.Real(this, KifsOffsetYBox, "Смещение Y", -2, 2);
        NumericInput.Real(this, KifsOffsetZBox, "Смещение Z", -2, 2);
        NumericInput.Real(this, LichtenbergEtaBox, "η", 0, 8);
        NumericInput.Integer(this, LichtenbergSeedBox, "Случайное число", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, LyapunovTransientBox, "Прогрев", 0, 4096);
        NumericInput.Integer(this, LyapunovSamplesBox, "Итераций измерения", 16, 4096);
        NumericInput.Real(this, LyapunovInitialBox, "Начальное x", .000001, .999999);
        NumericInput.Real(this, LyapunovMinABox, "Минимум A", 0, 4);
        NumericInput.Real(this, LyapunovMaxABox, "Максимум A", 0, 4);
        NumericInput.Real(this, LyapunovMinBBox, "Минимум B", 0, 4);
        NumericInput.Real(this, LyapunovMaxBBox, "Максимум B", 0, 4);
        NumericInput.Real(this, LyapunovMinCBox, "Минимум C", 0, 4);
        NumericInput.Real(this, LyapunovMaxCBox, "Максимум C", 0, 4);
        NumericInput.Real(this, LyapunovLevelBox, "Уровень λ", -2, 1);
        NumericInput.Real(this, LyapunovBandBox, "Полуширина полосы", .005, .5);
        NumericInput.Real(this, LyapunovRangeBox, "Диапазон окраски", .05, 4);
        NumericInput.Real(this, PeriodicPeriodBox, "Период", .5, 4);
        NumericInput.Real(this, PeriodicLevelBox, "Уровень F", -.9, .9);
        NumericInput.Real(this, PeriodicThicknessBox, "Толщина стенки по F", .02, 1.2);
        NumericInput.Real(this, PeriodicStretchXBox, "Растяжение X", .5, 2);
        NumericInput.Real(this, PeriodicStretchYBox, "Растяжение Y", .5, 2);
        NumericInput.Real(this, PeriodicStretchZBox, "Растяжение Z", .5, 2);
        NumericInput.Real(this, PeriodicCutPositionBox, "Положение среза", -1, 1);
        NumericInput.Real(this, AutoRotateSpeedBox, "Скорость автовращения", -720, 720);
        NumericInput.Integer(this, MaxStepsBox, "Шагов луча", 16, 1024);
        NumericInput.Real(this, DetailBox, "Детализация", 0.05, 8);
        NumericInput.Real(this, MaxDistanceBox, "Дальность трассировки", 1, 1000);
        NumericInput.Real(this, ShadowSharpnessBox, "Жёсткость теней", 1, 128);
        NumericInput.Real(this, AoStrengthBox, "Сила затенения", 0, 1);
        NumericInput.Integer(this, TerrainSeedBox, "Начальное число", 0, int.MaxValue);
        NumericInput.Integer(this, TerrainOctavesBox, "Уровни деталей", 1, 12);
        NumericInput.Real(this, TerrainRoughnessBox, "Шероховатость", 0.05, 0.95);
        NumericInput.Real(this, TerrainLacunarityBox, "Отношение масштабов", 1.5, 3);
        NumericInput.Real(this, TerrainScaleBox, "Масштаб деталей", 0.25, 16);
        NumericInput.Real(this, TerrainHeightBox, "Высота", 0.05, 8);
        NumericInput.Real(this, TerrainSizeBox, "Размер участка", 1, 20);
        NumericInput.Real(this, BailoutBox, "Радиус вылета", 1.01, 1e6);
        NumericInput.Real(this, JuliaCXBox, "Первая координата C", -8, 8);
        NumericInput.Real(this, JuliaCYBox, "Вторая координата C", -8, 8);
        NumericInput.Real(this, JuliaCZBox, "Третья координата C", -8, 8);
        NumericInput.Real(this, JuliaCWBox, "Четвёртая координата C", -8, 8);
        NumericInput.Integer(this, PhoenixSecondaryPowerBox, "Степень при C₁", 0, 1);
        NumericInput.Real(this, PhoenixMemoryXBox, "Вещественная часть C₂", -8, 8);
        NumericInput.Real(this, PhoenixMemoryYBox, "Координата i константы C₂", -8, 8);
        NumericInput.Real(this, PhoenixMemoryZBox, "Координата j константы C₂", -8, 8);
        NumericInput.Real(this, SliceBox, "Координата среза", -4, 4);
        NumericInput.Real(this, BoxScaleBox, "Масштаб свёртки", -8, 8);
        NumericInput.Real(this, BoxMinRadiusBox, "Минимальный радиус инверсии", 0.01, 4);
        NumericInput.Real(this, BoxFoldingBox, "Предел свёртки по кубу", 0.1, 8);
        NumericInput.Real(this, HybridMixBox, "Доля второй операции", 0, 1);
        NumericInput.Integer(this, HybridBulbStepsBox, "Итерации Мандельбульба подряд", 1, 6);
        NumericInput.Integer(this, HybridBoxStepsBox, "Итерации Мандельбокса подряд", 1, 6);
        NumericInput.Real(this, SierpinskiScaleBox, "Масштаб складывания", 1.05, 8);
        NumericInput.Real(this, CubeThicknessBox, "Толщина элементов", 0.5, 1.5);
        NumericInput.Real(this, LightYawBox, "Азимут света", -720, 720);
        NumericInput.Real(this, LightPitchBox, "Высота света", -89.9, 89.9);
        NumericInput.Real(this, AmbientBox, "Фоновый свет", 0, 2);
        NumericInput.Real(this, SpecularBox, "Блики", 0, 4);
        NumericInput.Real(this, EffectStrengthBox, "Сила эффекта", 0, 8);
        NumericInput.Real(this, ColorScaleBox, "Масштаб цвета", 0.01, 100);
        NumericInput.Real(this, ColorOffsetBox, "Сдвиг цвета", -100, 100);
        NumericInput.Real(this, SkyLightMixBox, "Влияние неба на свет", 0, 1);
        NumericInput.Angle(this, YawBox, "Поворот", () => CameraAngles.Yaw);
        NumericInput.Real(this, PitchBox, "Наклон", -90, 90, appliedValue: () => CameraAngles.Pitch);
        NumericInput.Angle(this, RollBox, "Крен", () => CameraAngles.Roll);
        NumericInput.Real(this, DistanceBox, "Расстояние", Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance, appliedValue: () => _distance);
        NumericInput.Real(this, FieldOfViewBox, "Угол обзора", 5, 160, appliedValue: () => _fieldOfView);
        NumericInput.Real(this, TargetXBox, "Цель X", -1e10, 1e10, appliedValue: () => _target.X);
        NumericInput.Real(this, TargetYBox, "Цель Y", -1e10, 1e10, appliedValue: () => _target.Y);
        NumericInput.Real(this, TargetZBox, "Цель Z", -1e10, 1e10, appliedValue: () => _target.Z);
        NumericInput.Include(this, BuddhabrotEditor);
        NumericInput.Include(this, KleinianEditor);
        NumericInput.Include(this, AntoineEditor);
        NumericInput.Include(this, QuaternionEditor);
        NumericInput.Include(this, NewtonEditor);
        NumericInput.Include(this, HyperbolicEditor);
        if (_mode.HasCubeRule)
            NumericInput.Integer(this, IterationsBox, _mode.IterationsField.Name,
                () => (int)_mode.IterationsField.Minimum,
                () => CubeEditor.Capture().HasValue ? 12 : (int)_mode.IterationsField.Maximum);
        else RegisterModeField(IterationsBox, _mode.IterationsField);
        RegisterModeField(PowerBox, _mode.PowerField);
    }

    private void RegisterModeField(System.Windows.Controls.TextBox box, Fractal3DField field)
    {
        if (field.IsInteger) NumericInput.Integer(this, box, field.Name, (int)field.Minimum, (int)field.Maximum);
        else NumericInput.Real(this, box, field.Name, field.Minimum, field.Maximum);
    }
}
