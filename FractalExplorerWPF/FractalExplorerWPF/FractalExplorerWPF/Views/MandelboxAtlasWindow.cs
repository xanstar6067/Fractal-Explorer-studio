using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Три параметра свёртки Мандельбокса; форма инверсии остаётся выбранной в исходном окне.</summary>
public sealed class MandelboxAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double[] _original;
    public decimal[] SelectedParameters => SelectedComponents;

    public MandelboxAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Mandelbox) throw new ArgumentException("Нужен режим Мандельбокса.");
        _original = [source.BoxScale, source.BoxMinRadius, source.BoxFoldingLimit];
        for (int i = 0; i < _original.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (!double.IsFinite(_original[i]) || _original[i] < (double)bounds.Minimum || _original[i] > (double)bounds.Maximum)
                throw new ArgumentException("Параметры Мандельбокса вне допустимого диапазона.");
        }
        Initialize3DAtlas("Атлас параметров · Мандельбокс",
            ["Масштаб свёртки", "Минимальный радиус инверсии", "Предел свёртки по кубу"],
            _original.Select(value => (decimal)value).ToArray(),
            $"Инверсия: {Fractal3DCatalog.BoxInversionShapeName(source.BoxInversionShape)}. " +
            "Выберите любые два параметра для осей; третий фиксирован. " +
            "Превью используют текущие камеру, форму инверсии и оформление. " +
            "Применение меняет только три параметра свёртки. Некоторые варианты могут быть пустыми или выходить за кадр.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index switch
    {
        0 => (-8m, 8m),
        1 => (.01m, 4m),
        _ => (.1m, 8m)
    };
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => index == 0 ? .8m : .4m;
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
        if (state.Kind != Fractal3DKind.Mandelbox || point.Length != 3)
            throw new ArgumentException("Нужны три параметра свёртки Мандельбокса.");
        var values = new double[3];
        for (int i = 0; i < values.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum)
                throw new ArgumentException("Параметр вне допустимого диапазона.");
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        state.BoxScale = values[0]; state.BoxMinRadius = values[1]; state.BoxFoldingLimit = values[2];
    }
}
