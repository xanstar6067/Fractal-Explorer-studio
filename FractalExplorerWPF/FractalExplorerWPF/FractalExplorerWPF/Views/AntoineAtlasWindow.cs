using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Геометрия цепочки и поколение конечного приближения ожерелья Антуана.</summary>
public sealed class AntoineAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double[] _original;
    private readonly Fractal3DField _generation;

    public AntoineAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.AntoineNecklace || source.Antoine is not { } settings)
            throw new ArgumentException("Нужен режим ожерелья Антуана.");
        settings.Validate();
        _generation = Fractal3DCatalog.Mode(source.Kind).IterationsField;
        if (source.Iterations < _generation.Minimum || source.Iterations > _generation.Maximum)
            throw new ArgumentException("Поколение вне допустимого диапазона.");
        _original = [settings.ScaleFactor, settings.Thickness, settings.Tilt, settings.Links, source.Iterations];
        Initialize3DAtlas("Атлас параметров · Ожерелье Антуана",
            ["Масштаб звеньев", "Толщина звеньев", "Наклон звеньев, °", "Звеньев в цепочке", "Поколение"],
            _original.Select(value => (decimal)value).ToArray(),
            "Выберите любые два параметра для осей; остальные фиксированы. Число звеньев — только чётное, от 24 до 48. " +
            "Толщина 0–1 выбирает значение внутри безопасного диапазона: ноль не означает исчезновение трубы. " +
            "Поколение 0 — исходный тор; в поколении d показано N^d колец, до 110 592 на третьем уровне. " +
            "Превью используют выбранное поколение и текущие камеру, свет и оформление. " +
            "Для мелких колец приблизьте отдельное звено в основном окне перед открытием атласа. " +
            "Применение переносит пять параметров в исходный вид.", xAxis: 0, yAxis: 2);
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index switch
    {
        0 => (1.35m, 1.5m),
        1 => (0m, 1m),
        2 => (60m, 90m),
        3 => (24m, 48m),
        _ => ((decimal)_generation.Minimum, (decimal)_generation.Maximum)
    };
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index is 3 or 4;
    protected override int IntegerComponentStep(int index) => index == 3 ? 2 : 1;
    protected override decimal InitialRangeWidth(int index) => index switch
    {
        0 => .06m, 1 => .3m, 2 => 12m, 3 => 8m, _ => 2m
    };
    protected override ParameterAtlasRange OverviewRange
    {
        get
        {
            var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex);
            return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum);
        }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · выбранное поколение 0–3";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.AntoineNecklace || state.Antoine is not { } settings || point.Length != 5)
            throw new ArgumentException("Нужны пять параметров ожерелья Антуана.");
        var values = new double[point.Length];
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || IsIntegerComponent(i) &&
                (point[i] != decimal.Truncate(point[i]) || (point[i] - bounds.Minimum) % IntegerComponentStep(i) != 0))
                throw new ArgumentException("Параметр вне диапазона; число звеньев должно быть чётным, поколение — целым.");
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        var candidate = settings with { ScaleFactor = values[0], Thickness = values[1], Tilt = values[2], Links = (int)values[3] };
        candidate.Validate();
        state.Antoine = candidate;
        state.Iterations = (int)values[4];
    }
}
