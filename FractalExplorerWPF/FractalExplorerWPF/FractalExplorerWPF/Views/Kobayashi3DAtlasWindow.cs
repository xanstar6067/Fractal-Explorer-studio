using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Kobayashi3DAtlasWindow : GpuSimulationParameterAtlasWindow<Kobayashi3DSettings, Kobayashi3DField, Kobayashi3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Kobayashi3DSettings, double> Read, Func<Kobayashi3DSettings, double, Kobayashi3DSettings> Write);
    private readonly Kobayashi3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public Kobayashi3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.Kobayashi, Set = (s,v) => s.Kobayashi = v,
        Create = (host,s) => new Kobayashi3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.Kobayashi with { };
        var p = new List<Parameter>
        {
            new("Анизотропия δ", -.06m, .06m, .04m, s => s.Anisotropy, (s,v) => s with { Anisotropy = v }),
            new("Толщина границы ε₀", .5m, 2, .4m, s => s.InterfaceWidth, (s,v) => s with { InterfaceWidth = v }),
            new("Подвижность фазы M", .1m, 4, 1, s => s.Mobility, (s,v) => s with { Mobility = v }),
            new("Теплопроводность D", .1m, 4, 1, s => s.ThermalDiffusion, (s,v) => s with { ThermalDiffusion = v }),
            new("Скрытая теплота L", 0, 3, 1, s => s.LatentHeat, (s,v) => s with { LatentHeat = v }),
            new("Шум границы", 0, .1m, .04m, s => s.Noise, (s,v) => s with { Noise = v }),
            new("Запрошенный шаг Δt", .001m, .1m, .04m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Уровень фазы φ", .05m, .95m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v })
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Кобаяси 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт φ/T: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходная затравка, шаг 0")}. " +
            "Сетка, затравка, её радиус, расположение, переохлаждение, seed и ось среза фиксированы. Переохлаждение задаёт начальную температуру и уже содержится в общем поле. " +
            "Каждый вариант независимо проходит одинаковое число дополнительных шагов N, без уменьшения сетки и повторной подготовки. Толщина меняет уравнение, а не начальный профиль границы. " +
            "δ > 0 выделяет шесть осевых направлений, δ < 0 — восемь диагональных, δ = 0 — изотропную среду. " +
            "Фактический Δt автоматически ограничивается устойчивостью и зависит от ε₀, δ, M и D. Одинаковое N не означает одинаковое физическое время: длительность прогона равна N·фактический Δt. " +
            "Уровень и срез меняют только показ. Применение повторяет прогон и переносит точные φ/T с номером шага на паузе. «Вернуть предыдущий кристалл» возвращает исходное поле и камеру, сохраняя прежнюю историю поиска. " +
            "Закрытие без применения сохраняет кристалл. Короткий прогон не гарантирует ветвление; чистая среда без зародыша остаётся пустой.");
        AddTrialSteps(256);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Kobayashi3D || source.Kobayashi is null || source.Kobayashi.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Кобаяси 3D или исходная затравка, без билета другого GPU-окна.");
        source.Kobayashi.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны φ/T на ГП";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.Kobayashi;
        if (state.Kind != Fractal3DKind.Kobayashi3D || s is null || s.Size != _settings.Size || s.Undercooling != _settings.Undercooling ||
            s.SeedShape != _settings.SeedShape || s.SeedRadius != _settings.SeedRadius || s.SeedSpread != _settings.SeedSpread ||
            s.SeedCount != _settings.SeedCount || s.Seed != _settings.Seed || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, затравка, переохлаждение или ось среза не соответствует атласу.");
        if (point.Length != _parameters.Length) throw new ArgumentException("Неверное число параметров атласа.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum)
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s,point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate(); state.Kobayashi = s;
    }
}
