using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Параметры сфер и конечного приближения при фиксированной конфигурации генераторов.</summary>
public sealed class KleinianAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly KleinianConfiguration _configuration;
    private readonly Fractal3DField _depth;
    private readonly double[] _original;

    public KleinianAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Kleinian || source.Kleinian is not { } settings)
            throw new ArgumentException("Нужен режим групп Клейна.");
        settings.Validate();
        _configuration = settings.Configuration;
        _depth = Fractal3DCatalog.Mode(source.Kind).IterationsField;
        if (source.Iterations < _depth.Minimum || source.Iterations > _depth.Maximum)
            throw new ArgumentException("Глубина приближения вне допустимого диапазона.");
        _original = [settings.RadiusRatio, settings.MinRadius, settings.Thickness, source.Iterations];
        Initialize3DAtlas("Атлас параметров · Группы Клейна",
            ["Относительный радиус сфер", "Минимальный радиус деталей", "Утолщение множества", "Глубина приближения"],
            _original.Select(value => (decimal)value).ToArray(),
            $"{KleinianSettings.ConfigurationName(_configuration)}. " +
            "Выберите любые два параметра для осей; остальные фиксированы. " +
            "Радиус 1 даёт касание сфер, уменьшение радиуса открывает промежутки. " +
            "Глубина и минимальный радиус деталей задают конечное приближение; утолщение помогает рассмотреть тонкие связи. " +
            "Превью используют выбранные глубину и размер деталей, текущие камеру и оформление. " +
            "Общий предел — 65 536 сфер: при его достижении часть ветвей остаётся крупнее. " +
            "Применение переносит четыре числовых параметра в исходный вид.", xAxis: 0, yAxis: 2);
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index switch
    {
        0 => (.65m, 1m),
        1 => (.001m, .05m),
        2 => (0m, .04m),
        _ => ((decimal)_depth.Minimum, (decimal)_depth.Maximum)
    };
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index == 3;
    protected override decimal InitialRangeWidth(int index) => index switch
    {
        0 => .1m, 1 => .01m, 2 => .008m, _ => 4m
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
        $"{completed} / {total} · ГП, по одному варианту · выбранная глубина, до 65 536 сфер";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Kleinian || point.Length != 4 ||
            state.Kleinian is not { } settings || settings.Configuration != _configuration)
            throw new ArgumentException("Нужны четыре параметра той же конфигурации групп Клейна.");
        var values = new double[point.Length];
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum ||
                IsIntegerComponent(i) && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Параметр вне диапазона или требует целого значения.");
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        var candidate = settings with { RadiusRatio = values[0], MinRadius = values[1], Thickness = values[2] };
        candidate.Validate();
        state.Kleinian = candidate;
        state.Iterations = (int)values[3];
    }
}
