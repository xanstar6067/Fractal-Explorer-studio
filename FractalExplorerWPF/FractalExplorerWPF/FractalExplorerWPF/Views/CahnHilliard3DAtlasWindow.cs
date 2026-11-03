using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class CahnHilliard3DAtlasWindow : GpuSimulationParameterAtlasWindow<CahnHilliard3DSettings, CahnHilliard3DField, CahnHilliard3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<CahnHilliard3DSettings, double> Read, Func<CahnHilliard3DSettings, double, CahnHilliard3DSettings> Write, bool Integer = false);
    private readonly CahnHilliard3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public CahnHilliard3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.CahnHilliard, Set = (s,v) => s.CahnHilliard = v,
        Create = (host,s) => new CahnHilliard3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }
    })
    {
        _settings = source.CahnHilliard with { };
        var p = new List<Parameter>
        {
            new("Межфазная энергия κ", .5m, 8, 2, s => s.Kappa, (s,v) => s with { Kappa = v }),
            new("Подвижность M", .01m, 2, .5m, s => s.Mobility, (s,v) => s with { Mobility = v }),
            new("Шаг времени Δt", .01m, 2, .5m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Уровень поверхности c", -.9m, .9m, .4m, s => s.Level, (s,v) => s with { Level = v }),
            new("Показанная фаза", 0, 1, 1, s => s.Invert ? 1 : 0, (s,v) => s with { Invert = v == 1 }, true)
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Кан–Хиллиард 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общее начальное поле: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}, t = {field.Time:G6}" : "исходная смесь, шаг 0, t = 0")}. " +
            "Каждый вариант независимо проходит одинаковое число дополнительных шагов N; физическое время увеличивается на N·Δt. Для сравнения при одном времени оставьте Δt фиксированным. " +
            "Допустимы только M·Δt ≤ 2. Сетка, средний состав, шум, затравка, seed и ось среза фиксированы; средний состав поля сохраняется. " +
            "Уровень, фаза и срез меняют только показ. При применении прогон повторяется и точное поле со временем переносится на паузе. " +
            "«Вернуть предыдущий» восстанавливает исходное показанное поле. Закрытие без применения не заменяет его. Пустой вид возможен при выбранном уровне; более длительное развитие может дать другие домены.");
        AddTrialSteps(120);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.CahnHilliard3D || source.CahnHilliard is null || source.CahnHilliard.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Кана–Хиллиарда 3D или исходная смесь, без билета другого GPU-окна.");
        source.CahnHilliard.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 4
        ? value == 0 ? "Первая фаза · c ≥ уровень" : "Вторая фаза · c ≤ уровень"
        : base.ComponentValueLabel(index,value,format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны Кана–Хиллиарда на ГП";
    protected override string? UnavailableSample(decimal[] point)
    {
        var s = Map(_settings,point);
        return s.Mobility * s.TimeStep > 2 ? "M·Δt > 2: уменьшите подвижность или шаг времени." : null;
    }
    private CahnHilliard3DSettings Map(CahnHilliard3DSettings settings, decimal[] point)
    {
        if (point.Length != _parameters.Length) throw new ArgumentException("Неверное число параметров атласа.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            settings = p.Write(settings,point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        return settings;
    }
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.CahnHilliard;
        if (state.Kind != Fractal3DKind.CahnHilliard3D || s is null || s.Size != _settings.Size || s.Mean != _settings.Mean ||
            s.Noise != _settings.Noise || s.SeedShape != _settings.SeedShape || s.Seed != _settings.Seed || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, исходная смесь или ось среза не соответствует атласу.");
        var mapped = Map(s,point);
        if (mapped.Mobility * mapped.TimeStep > 2) throw new ArgumentException("M·Δt должно быть не больше 2.");
        mapped.Validate(); state.CahnHilliard = mapped;
    }
}
