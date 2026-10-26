using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class QuaternionJuliaAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double[] _original;
    public decimal[] SelectedConstant => SelectedComponents[..4];
    public decimal[] SelectedParameters => SelectedComponents;

    public QuaternionJuliaAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.QuaternionJulia) throw new ArgumentException("Нужен режим кватернионного Жюлиа.");
        var slice = source.QuaternionJulia ?? new(); slice.Validate();
        _original = [source.JuliaCX, source.JuliaCY, source.JuliaCZ, source.JuliaCW,
            source.QuaternionSlice, slice.XW, slice.YW, slice.ZW];
        for (int i = 0; i < _original.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (!double.IsFinite(_original[i]) || _original[i] < (double)bounds.Minimum || _original[i] > (double)bounds.Maximum)
                throw new ArgumentException("Компоненты C: −8…8, координата среза w: −4…4, наклоны: −180…180°.");
        }
        Initialize3DAtlas("Атлас параметров · Кватернионное Жюлиа",
            ["C · x (вещественная)", "C · y (i)", "C · z (j)", "C · w (k)",
             "Срез · w", "Наклон XW · градусы", "Наклон YW · градусы", "Наклон ZW · градусы"],
            _original.Select(value => (decimal)value).ToArray(),
            "Выберите два параметра из четырёх компонент C, координаты среза w и трёх наклонов; остальные фиксированы. " +
            "C · w — компонента константы, а срез w задаёт положение в 4D. Порядок наклонов: XW → YW → ZW. " +
            "Камера и оформление — из текущего вида. Применение сохраняет качество, итерации и настройки прохода; " +
            "проход остаётся на паузе. Некоторые срезы могут быть пустыми.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        index < 4 ? (-8m, 8m) : index == 4 ? (-4m, 4m) : (-180m, 180m);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => index < 5 ? .4m : 30m;
    protected override ParameterAtlasRange OverviewRange
    {
        get
        {
            var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex);
            return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum);
        }
    }

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.QuaternionJulia || point.Length != _original.Length)
            throw new ArgumentException("Нужны четыре компоненты C, координата среза и три наклона.");
        var values = new double[point.Length];
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum)
                throw new ArgumentException("Параметр кватернионного Жюлиа вне допустимого диапазона.");
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        var slice = state.QuaternionJulia ?? new();
        var next = slice with { XW = values[5], YW = values[6], ZW = values[7] };
        next.Validate();
        state.JuliaCX = values[0]; state.JuliaCY = values[1];
        state.JuliaCZ = values[2]; state.JuliaCW = values[3];
        state.QuaternionSlice = values[4];
        // Не создаём DTO при неизменном старом срезе; настройки прохода берём из целевого вида.
        if (next != slice) state.QuaternionJulia = next == new QuaternionJuliaSettings() ? null : next;
    }
}
