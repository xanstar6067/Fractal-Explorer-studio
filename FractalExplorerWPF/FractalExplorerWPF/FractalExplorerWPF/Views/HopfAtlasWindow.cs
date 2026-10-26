using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Параметры семейства и проекции Хопфа при фиксированных выделении и ручном наборе точек.</summary>
public sealed class HopfAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<HopfSettings, double> Read, Func<HopfSettings, double, HopfSettings> Write, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly HopfSettings _settings;

    public HopfAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _settings = source.Hopf.Copy();
        var parameters = new List<Parameter>
        {
            new("Поворот XW, °", -180, 180, 30, s => s.XW, (s, v) => s with { XW = v }),
            new("Поворот YW, °", -180, 180, 30, s => s.YW, (s, v) => s with { YW = v }),
            new("Поворот ZW, °", -180, 180, 30, s => s.ZW, (s, v) => s with { ZW = v }),
            new("Поворот XY, °", -180, 180, 30, s => s.XY, (s, v) => s with { XY = v }),
            new("Поворот XZ, °", -180, 180, 30, s => s.XZ, (s, v) => s with { XZ = v }),
            new("Поворот YZ, °", -180, 180, 30, s => s.YZ, (s, v) => s with { YZ = v }),
            new("Толщина трубок", .003m, .12m, .02m, s => s.Thickness, (s, v) => s with { Thickness = v }),
            new("Радиус видимой области", 1, 8, 1, s => s.ClipRadius, (s, v) => s with { ClipRadius = v })
        };
        if (_settings.Family == HopfFamily.Custom)
        {
            if (_settings.SelectedFiber >= 0)
            {
                int selected = _settings.SelectedFiber;
                parameters.Add(new("Долгота выбранной точки, °", -180, 180, 30,
                    s => s.Points[selected].Longitude, (s, v) => MovePoint(s, selected, longitude: v)));
                parameters.Add(new("Широта выбранной точки, °", -90, 90, 30,
                    s => s.Points[selected].Latitude, (s, v) => MovePoint(s, selected, latitude: v)));
            }
        }
        else
        {
            bool layered = _settings.Family is HopfFamily.Latitudes or HopfFamily.LinkedTori;
            if (_settings.SelectedFiber < 0)
            {
                parameters.Add(new("Колец на семейство", 1, 64, 8, s => s.Fibers, (s, v) => s with { Fibers = (int)v }, Integer: true));
                if (layered)
                    parameters.Add(new("Число семейств", 1, 4, 2, s => s.Layers, (s, v) => s with { Layers = (int)v }, Integer: true));
            }
            if (_settings.Family != HopfFamily.Sphere)
                parameters.Add(new("Широта семейства, °", -85, 85, 30, s => s.Latitude, (s, v) => s with { Latitude = v }));
            if (_settings.Family == HopfFamily.Latitudes && (_settings.SelectedFiber < 0 || _settings.Layers > 1))
                parameters.Add(new("Разброс широт, °", 0, 150, 30, s => s.Spread, (s, v) => s with { Spread = v }));
            parameters.Add(new("Сдвиг по долготе, °", -180, 180, 30, s => s.Phase, (s, v) => s with { Phase = v }));
        }
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        string family = _settings.Family switch
        {
            HopfFamily.Latitude => "одна широта", HopfFamily.Latitudes => "несколько широт",
            HopfFamily.Sphere => "вся сфера", HopfFamily.LinkedTori => "сцепленные торы", _ => "ручные точки"
        };
        Initialize3DAtlas("Атлас параметров · Расслоение Хопфа", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(value => (decimal)value).ToArray(),
            $"Семейство: {family}; {_settings.BasePoints().Count} колец. " +
            "Выберите любые два параметра для осей; остальные фиксированы. Плоскости XW/YW/ZW меняют проекцию из 4D. " +
            "С выделенным кольцом число колец и семейств фиксировано; для их сравнения снимите выделение в основном окне. " +
            "У ручного набора можно менять координаты выбранной точки; остальные точки сохраняются. Пустой набор даёт фон. " +
            "Кольца у полюса проекции уходят в бесконечность и обрезаются видимой областью. " +
            "Превью используют текущие камеру и оформление. При открытии анимация ставится на паузу; продолжить её можно вручную после закрытия.");
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Hopf || source.Hopf is null)
            throw new ArgumentException("Нужен режим расслоения Хопфа.");
        source.Hopf.Validate();
        return source;
    }

    private static HopfSettings MovePoint(HopfSettings settings, int index, double? longitude = null, double? latitude = null)
    {
        var points = settings.Points.ToList();
        var point = points[index];
        points[index] = point with { Longitude = longitude ?? point.Longitude, Latitude = latitude ?? point.Latitude };
        return settings with { Points = points };
    }

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
        $"{completed} / {total} · ГП, по одному варианту · кольца выбранного семейства";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var settings = state.Hopf;
        if (state.Kind != Fractal3DKind.Hopf || settings is null || point.Length != _parameters.Length ||
            settings.Family != _settings.Family || settings.SelectedFiber != _settings.SelectedFiber || settings.OnlySelected != _settings.OnlySelected ||
            !settings.Points.SequenceEqual(_settings.Points) || _settings.SelectedFiber >= 0 &&
            (settings.Fibers != _settings.Fibers || settings.Layers != _settings.Layers))
            throw new ArgumentException("Семейство, ручные точки или выделение не соответствует атласу Хопфа.");
        for (int i = 0; i < point.Length; i++)
        {
            var parameter = _parameters[i];
            if (point[i] < parameter.Minimum || point[i] > parameter.Maximum ||
                parameter.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{parameter.Name}» вне диапазона или требует целого значения.");
            settings = parameter.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        settings.Validate();
        state.Hopf = settings.Copy();
    }
}
