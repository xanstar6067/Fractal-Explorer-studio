using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Воспроизводимые варианты поля ABC с исходными параметрами интегрирования.</summary>
public sealed class AbcFlowAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<AbcFlowSettings, double> Read, Func<AbcFlowSettings, double, AbcFlowSettings> Write, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly AbcFlowSettings _settings;
    private readonly bool _lyapunovColoring;

    public AbcFlowAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _settings = source.AbcFlow with { };
        _lyapunovColoring = source.ColoringMode == Fractal3DColoringMode.OrbitTrap;
        decimal pi = (decimal)Math.PI;
        var parameters = new List<Parameter>
        {
            new("Коэффициент A", -3, 3, 1, s => s.A, (s, v) => s with { A = v }),
            new("Коэффициент B", -3, 3, 1, s => s.B, (s, v) => s with { B = v }),
            new("Коэффициент C", -3, 3, 1, s => s.C, (s, v) => s with { C = v }),
            new("Число траекторий", 1, 48, 8, s => s.SeedCount, (s, v) => s with { SeedCount = (int)v }, Integer: true),
            new("Центр группы · x, рад", -pi, pi, 1, s => s.StartX, (s, v) => s with { StartX = v }),
            new("Центр группы · y, рад", -pi, pi, 1, s => s.StartY, (s, v) => s with { StartY = v }),
            new("Центр группы · z, рад", -pi, pi, 1, s => s.StartZ, (s, v) => s with { StartZ = v }),
            new("Радиус / разброс, рад", .0001m, pi, (decimal)Math.Clamp(_settings.Spread, .0002, 1),
                s => s.Spread, (s, v) => s with { Spread = v }),
            new("Отсчётов на траекторию", 64, 2048, 256, s => s.Samples, (s, v) => s with { Samples = (int)v }, Integer: true),
            new("Шаг времени RK4", .001m, .04m, .01m, s => s.TimeStep, (s, v) => s with { TimeStep = v }),
            new("Толщина линий / точек", .002m, .04m, .01m, s => s.Thickness, (s, v) => s with { Thickness = v })
        };
        if (_settings.ShowParticles && !_settings.SectionOnly)
            parameters.Add(new("Фаза частиц · доля времени", 0, 1, .3m, s => s.Phase, (s, v) => s with { Phase = v }));
        if (_settings.SectionOnly)
            parameters.Add(new("Положение сечения, рад", -pi, pi, 1, s => s.SectionPosition, (s, v) => s with { SectionPosition = v }));
        if (_lyapunovColoring)
            parameters.Add(new("Диапазон окраски ±λ(t)", .01m, 2, .3m, s => s.LyapunovRange, (s, v) => s with { LyapunovRange = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Поток ABC", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            "Выберите любые два параметра для осей; остальные фиксированы. Группа начальных точек, видимость линий/частиц, " +
            "ось и направление пересечений фиксированы. Начальные точки воспроизводимы. Превью используют исходные число траекторий, " +
            "отсчёты и шаг, пока вы сами их не измените. Длительность T = отсчёты × 4 × шаг: изменение любого из этих двух полей меняет T. " +
            "Для сравнения точности сохраняйте T при уменьшении шага и увеличении отсчётов. " +
            "Фаза — доля рассчитанного времени, а не абсолютное время. Положение плоскости доступно в режиме «только точки сечения»; " +
            "миниатюры показывают 3D-вид. Нет пересечений — возможен пустой вид. Камера и оформление взяты из исходного окна. " +
            "Тонкие линии и расхождение долгих траекторий проверяйте в основном окне. Большие сетки рассчитываются последовательно и могут занять время.");
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.AbcFlow || source.AbcFlow is null)
            throw new ArgumentException("Нужен режим потока ABC.");
        source.AbcFlow.Validate();
        return source;
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
        $"{completed} / {total} · ГП, по одному варианту · выбранные отсчёты и шаг RK4";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var settings = state.AbcFlow;
        if (state.Kind != Fractal3DKind.AbcFlow || settings is null || point.Length != _parameters.Length ||
            settings.Group != _settings.Group || settings.ShowTrails != _settings.ShowTrails || settings.ShowParticles != _settings.ShowParticles ||
            settings.SectionOnly != _settings.SectionOnly || settings.SectionAxis != _settings.SectionAxis ||
            settings.PositiveCrossings != _settings.PositiveCrossings ||
            (state.ColoringMode == Fractal3DColoringMode.OrbitTrap) != _lyapunovColoring)
            throw new ArgumentException("Группа, вид отображения, сечение или источник окраски не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var parameter = _parameters[i];
            if (point[i] < parameter.Minimum || point[i] > parameter.Maximum ||
                parameter.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{parameter.Name}» вне диапазона или требует целого значения.");
            double value = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
            settings = parameter.Write(settings, value);
        }
        settings.Validate();
        state.AbcFlow = settings;
    }
}
