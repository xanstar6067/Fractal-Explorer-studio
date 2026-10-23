using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас степени и глубины итераций Мандельбульба.</summary>
public sealed class MandelbulbAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly Fractal3DField _power;
    private readonly Fractal3DField _iterations;
    private readonly double _originalPower;
    public decimal[] SelectedParameters => SelectedComponents;

    public MandelbulbAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Mandelbulb) throw new ArgumentException("Нужен режим Мандельбульба.");
        var mode = Fractal3DCatalog.Mode(source.Kind);
        _power = mode.PowerField; _iterations = mode.IterationsField;
        _originalPower = source.Power;
        if (!double.IsFinite(source.Power) || source.Power < _power.Minimum || source.Power > _power.Maximum ||
            source.Iterations < _iterations.Minimum || source.Iterations > _iterations.Maximum)
            throw new ArgumentException("Параметры Мандельбульба вне допустимого диапазона.");
        Initialize3DAtlas("Атлас параметров · Мандельбульб", ["Степень n", "Итерации"],
            [(decimal)source.Power, source.Iterations],
            "По горизонтали меняется степень, по вертикали — целое число итераций. " +
            "Превью используют текущие камеру и оформление. Применение меняет только степень и итерации. " +
            "Некоторые варианты могут быть пустыми. При узком диапазоне итераций строк меньше размера сетки, без повторов.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 0
        ? ((decimal)_power.Minimum, (decimal)_power.Maximum)
        : ((decimal)_iterations.Minimum, (decimal)_iterations.Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index == 1 || _power.IsInteger;
    protected override decimal InitialRangeWidth(int index) => index == 0 ? 2m : 4m;
    protected override ParameterAtlasRange OverviewRange => new(2m, 16m, 3m, 16m);
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · итерации по оси Y";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Mandelbulb || point.Length != 2)
            throw new ArgumentException("Нужны степень и итерации Мандельбульба.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum ||
                IsIntegerComponent(i) && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Параметр вне диапазона или требует целого значения.");
        }
        state.Power = point[0] == (decimal)_originalPower ? _originalPower : (double)point[0];
        // Применяется после ограничения исходного снимка превью: выбранные 33–64 итерации не обрезаются.
        state.Iterations = (int)point[1];
    }
}
