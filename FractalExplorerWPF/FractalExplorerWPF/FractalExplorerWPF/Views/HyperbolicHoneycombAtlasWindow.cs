using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Геометрия сот и сферические координаты наблюдателя в допустимом шаре.</summary>
public sealed class HyperbolicHoneycombAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<HyperbolicHoneycombSettings, double> Read,
        Func<HyperbolicHoneycombSettings, double, HyperbolicHoneycombSettings>? Write = null, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly HyperbolicHoneycombSettings _settings;

    public HyperbolicHoneycombAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _settings = source.HyperbolicHoneycomb with { };
        var parameters = new List<Parameter>
        {
            new("Глубина · радиальные слои", 1, 6, 2, s => s.Depth, (s, v) => s with { Depth = (int)v }, Integer: true),
            new("Радиус положения в шаре", 0, .85m, .2m, ObserverRadius),
            new("Азимут наблюдателя, °", -180, 180, 30, s => ObserverRadius(s) == 0 ? 0 : Math.Atan2(s.ObserverZ, s.ObserverX) * 180 / Math.PI),
            new("Широта наблюдателя, °", -90, 90, 30, s => ObserverRadius(s) == 0 ? 0 :
                Math.Atan2(s.ObserverY, Math.Sqrt(s.ObserverX * s.ObserverX + s.ObserverZ * s.ObserverZ)) * 180 / Math.PI)
        };
        int yAxis = 1;
        if (_settings.ShowEdges)
        {
            yAxis = parameters.Count;
            parameters.Add(new("Радиус рёбер в H³", .012m, .2m, .04m, s => s.EdgeRadius, (s, v) => s with { EdgeRadius = v }));
        }
        if (_settings.ShowFaces)
        {
            parameters.Add(new("Толщина граней в H³", .006m, .08m, .02m, s => s.FaceThickness, (s, v) => s with { FaceThickness = v }));
            if (!_settings.ShowEdges) yAxis = parameters.Count;
            parameters.Add(new("Непрозрачность граней", 0, 1, .3m, s => s.FaceOpacity, (s, v) => s with { FaceOpacity = v }));
        }
        if (_settings.CutAxis > 0)
            parameters.Add(new("Положение среза", -1, 1, .4m, s => s.CutPosition, (s, v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        var (p, q, r) = _settings.Symbol;
        Initialize3DAtlas("Атлас параметров · Гиперболические соты", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Разбиение: {{{p},{q},{r}}}. Тип сот, видимость рёбер/граней и ось среза фиксированы. " +
            "Выберите любые два параметра для осей; остальные фиксированы. Глубина задаёт радиальную область, а не число ячеек. " +
            "Положение наблюдателя: радиус 0–0,85 в исходном шаре, азимут вокруг Y от +X к +Z, широта в сторону +Y. " +
            "Такие координаты сохраняют допустимое положение во всех ячейках. При нулевом радиусе направление не влияет на вид. " +
            "Радиусы рёбер и толщины граней заданы в гиперболической метрике. Превью используют текущие камеру и оформление; " +
            "тонкие детали у границы проверяйте в основном окне с его качеством.", yAxis: yAxis);
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.HyperbolicHoneycomb || source.HyperbolicHoneycomb is null)
            throw new ArgumentException("Нужен режим гиперболических сот.");
        source.HyperbolicHoneycomb.Validate();
        return source;
    }

    private static double ObserverRadius(HyperbolicHoneycombSettings s) =>
        Math.Min(.85, Math.Sqrt(s.ObserverX * s.ObserverX + s.ObserverY * s.ObserverY + s.ObserverZ * s.ObserverZ));

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get
        {
            var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex);
            return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum);
        }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · выбранные радиальные слои";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var settings = state.HyperbolicHoneycomb;
        if (state.Kind != Fractal3DKind.HyperbolicHoneycomb || settings is null || point.Length != _parameters.Length ||
            settings.Tessellation != _settings.Tessellation || settings.ShowEdges != _settings.ShowEdges ||
            settings.ShowFaces != _settings.ShowFaces || settings.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Тип сот, видимость или ось среза не соответствует атласу.");
        var values = new double[point.Length];
        for (int i = 0; i < point.Length; i++)
        {
            var parameter = _parameters[i];
            if (point[i] < parameter.Minimum || point[i] > parameter.Maximum ||
                parameter.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{parameter.Name}» вне диапазона или требует целого значения.");
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
            if (parameter.Write is { } write) settings = write(settings, values[i]);
        }
        if (Enumerable.Range(1, 3).All(i => point[i] == (decimal)_original[i]))
        {
            // Избежать потери исходных double при неизменённых сферических координатах.
            settings = settings with { ObserverX = _settings.ObserverX, ObserverY = _settings.ObserverY, ObserverZ = _settings.ObserverZ };
        }
        else
        {
            double azimuth = values[2] * Math.PI / 180, latitude = values[3] * Math.PI / 180;
            double horizontal = values[1] * Math.Cos(latitude);
            settings = settings with { ObserverX = horizontal * Math.Cos(azimuth), ObserverY = values[1] * Math.Sin(latitude),
                ObserverZ = horizontal * Math.Sin(azimuth) };
        }
        settings.Validate();
        state.HyperbolicHoneycomb = settings;
    }
}
