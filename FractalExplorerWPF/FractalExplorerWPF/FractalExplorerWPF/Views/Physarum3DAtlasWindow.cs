using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Physarum3DAtlasWindow : GpuSimulationParameterAtlasWindow<Physarum3DSettings, Physarum3DField, Physarum3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Physarum3DSettings, double> Read, Func<Physarum3DSettings, double, Physarum3DSettings> Write);
    private readonly Physarum3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public Physarum3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.Physarum, Set = (s,v) => s.Physarum = v,
        Create = (host,s) => new Physarum3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }
    })
    {
        _settings = source.Physarum with { };
        var p = new List<Parameter>
        {
            new("Дальность чувств · ячеек", 1, 16, 4, s => s.SensorDistance, (s,v) => s with { SensorDistance = v }),
            new("Угол обзора · градусов", 5, 85, 20, s => s.SensorAngle, (s,v) => s with { SensorAngle = v }),
            new("Поворот · градусов", 1, 70, 16, s => s.TurnAngle, (s,v) => s with { TurnAngle = v }),
            new("Скорость · ячеек за шаг", .1m, 2, .4m, s => s.Speed, (s,v) => s with { Speed = v }),
            new("Сила следа", .1m, 8, 2, s => s.Deposit, (s,v) => s with { Deposit = v }),
            new("Размывание следа", 0, 1, .2m, s => s.Diffusion, (s,v) => s with { Diffusion = v }),
            new("Испарение за шаг", .001m, .2m, .04m, s => s.Decay, (s,v) => s with { Decay = v }),
            new("Яркость нитей", .05m, 4, .4m, s => s.Exposure, (s,v) => s with { Exposure = v }),
            new("Порог видимости", .01m, .9m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v })
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Physarum 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт: {(_settings.Field is { } field ? $"след и агенты показанного шага {field.Step:N0}" : "исходные агенты и пустой след, шаг 0")}. " +
            "Каждый вариант независимо проходит одинаковое число дополнительных шагов. Сетка, число агентов, начальное распределение, seed и ось среза фиксированы. " +
            "Сетка и популяция не уменьшаются; яркость, порог и срез меняют только показ. " +
            "При применении прогон повторяется: точные след, позиции, направления и номер шага переносятся в основное окно на паузе. " +
            "«Вернуть предыдущую сеть» восстанавливает исходный снимок, сохраняя прежнюю историю поиска. Закрытие без применения не заменяет основную сеть. " +
            "Пустой или насыщенный вид — возможный результат параметров и отображения. Более длительное развитие может дать другую сеть.");
        AddTrialSteps(80);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Physarum3D || source.Physarum is null || source.Physarum.Live is not null)
            throw new ArgumentException("Нужны сохранённые след и агенты Physarum 3D либо исходная затравка, без билета другого GPU-окна.");
        source.Physarum.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны Physarum на ГП";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.Physarum;
        if (state.Kind != Fractal3DKind.Physarum3D || s is null || point.Length != _parameters.Length || s.Size != _settings.Size ||
            s.AgentCount != _settings.AgentCount || s.SeedShape != _settings.SeedShape || s.Seed != _settings.Seed || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, популяция, затравка или ось среза не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum) throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s,point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate(); state.Physarum = s;
    }
}
