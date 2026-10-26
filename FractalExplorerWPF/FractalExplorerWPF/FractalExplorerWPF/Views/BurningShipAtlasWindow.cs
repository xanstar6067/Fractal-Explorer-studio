using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Целые степень и число итераций исходного корабля при фиксированной формуле.</summary>
public sealed class BurningShipAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly Fractal3DField _power;
    private readonly Fractal3DField _iterations;
    private readonly BurningShip3DFormula _formula;

    public BurningShipAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.BurningShip || !Enum.IsDefined(source.BurningShipFormula))
            throw new ArgumentException("Нужен режим горящего корабля 3D с допустимой формулой.");
        var mode = Fractal3DCatalog.Mode(source.Kind);
        _power = mode.PowerField; _iterations = mode.IterationsField; _formula = source.BurningShipFormula;
        if (!double.IsFinite(source.Power) || source.Power < _power.Minimum || source.Power > _power.Maximum ||
            source.Power != Math.Truncate(source.Power) || source.Iterations < _iterations.Minimum || source.Iterations > _iterations.Maximum)
            throw new ArgumentException("Степень и число итераций горящего корабля вне допустимого диапазона.");
        Initialize3DAtlas("Атлас параметров · Горящий корабль 3D", ["Степень n", "Итерации"],
            [(decimal)source.Power, source.Iterations],
            $"Формула: {Fractal3DCatalog.BurningShipFormulaName(_formula)}. " +
            "По горизонтали — целая степень, по вертикали — целое число итераций. " +
            "Превью используют выбранное число итераций, текущие радиус вылета, камеру и оформление. " +
            "Применение меняет степень и итерации. Для другой формулы выберите её в основном окне и откройте атлас заново. " +
            "В узком диапазоне строк или колонок меньше размера сетки, без повторов. Некоторые варианты могут быть пустыми или выходить за кадр.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 0
        ? ((decimal)_power.Minimum, (decimal)_power.Maximum)
        : ((decimal)_iterations.Minimum, (decimal)_iterations.Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => true;
    protected override decimal InitialRangeWidth(int index) => index == 0 ? 2m : 4m;
    protected override ParameterAtlasRange OverviewRange => new((decimal)_power.Minimum, (decimal)_power.Maximum,
        (decimal)_iterations.Minimum, (decimal)_iterations.Maximum);
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · выбранные степень и итерации";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.BurningShip || state.BurningShipFormula != _formula || point.Length != 2)
            throw new ArgumentException("Нужны степень и итерации той же формулы горящего корабля 3D.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Степень и число итераций должны быть целыми в допустимых пределах.");
        }
        state.Power = (int)point[0];
        // Выбранные 33–64 итерации восстанавливаются после общего ограничения качества превью.
        state.Iterations = (int)point[1];
    }
}
