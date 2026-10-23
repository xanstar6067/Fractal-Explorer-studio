using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Общий атлас трёх компонент C и степени, с контрактом степени из класса режима.</summary>
public abstract class Julia3DShapeAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double[] _original;
    private readonly Fractal3DKind _kind;
    private readonly Fractal3DField _power;
    public decimal[] SelectedParameters => SelectedComponents;

    protected Julia3DShapeAtlasWindow(Fractal3DState source, Fractal3DKind expectedKind, string formulaHint = "") : base(source)
    {
        if (source.Kind != expectedKind) throw new ArgumentException("Состояние не соответствует режиму атласа.");
        _kind = expectedKind;
        var mode = Fractal3DCatalog.Mode(expectedKind);
        _power = mode.PowerField;
        _original = [source.JuliaCX, source.JuliaCY, source.JuliaCZ, source.Power];
        for (int i = 0; i < _original.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (!double.IsFinite(_original[i]) || _original[i] < (double)bounds.Minimum || _original[i] > (double)bounds.Maximum ||
                IsIntegerComponent(i) && _original[i] != Math.Truncate(_original[i]))
                throw new ArgumentException("Параметр вне допустимого диапазона или требует целого значения.");
        }
        Initialize3DAtlas("Атлас параметров · " + mode.Definition.Title, ["C · x", "C · y", "C · z", "Степень n"],
            _original.Select(value => (decimal)value).ToArray(),
            formulaHint + " Выберите две компоненты C или степень для осей; остальные значения фиксированы. " +
            "Превью используют текущие камеру и оформление. Применение меняет только C и степень. Некоторые варианты могут быть пустыми. " +
            (_power.IsInteger ? "Степень целая; при узком диапазоне колонок или строк меньше размера сетки, без повторов." : ""));
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        index == 3 ? ((decimal)_power.Minimum, (decimal)_power.Maximum) : (-8m, 8m);
    protected override bool IsIntegerComponent(int index) => index == 3 && _power.IsInteger;
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => index == 3 ? 2m : .4m;
    protected override ParameterAtlasRange OverviewRange => new(
        XAxisIndex == 3 ? 2m : -2m, XAxisIndex == 3 ? 16m : 2m,
        YAxisIndex == 3 ? 2m : -2m, YAxisIndex == 3 ? 16m : 2m);

    public void ApplySelectionTo(Fractal3DState state) => ApplyComponents(state, SelectedComponents);

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != _kind || point.Length != 4)
            throw new ArgumentException("Нужны три компоненты C и степень соответствующего режима.");
        var values = new double[4];
        for (int i = 0; i < values.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || IsIntegerComponent(i) && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Параметр вне допустимого диапазона.");
            // Не терять точность исходного double при возврате неизменённого decimal из оболочки.
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        state.JuliaCX = values[0]; state.JuliaCY = values[1]; state.JuliaCZ = values[2]; state.Power = values[3];
    }
}
