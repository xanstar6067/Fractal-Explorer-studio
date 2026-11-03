using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class GrayScott3DAtlasWindow : GpuSimulationParameterAtlasWindow<GrayScott3DSettings, GrayScott3DField, GrayScott3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<GrayScott3DSettings, double> Read, Func<GrayScott3DSettings, double, GrayScott3DSettings> Write);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly GrayScott3DSettings _settings;

    public GrayScott3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.GrayScott, Set = (s,v) => s.GrayScott = v,
        Create = (host, s) => new GrayScott3DGpuSimulation(host, s),
        Field = s => s.Field, With = (s, field, live) => s with { Field = field, Live = live }
    })
    {
        _settings = source.GrayScott with { };
        var parameters = new List<Parameter>
        {
            new("Подача F", 0, .1m, .02m, s => s.Feed, (s,v) => s with { Feed = v }),
            new("Убыль K", 0, .1m, .02m, s => s.Kill, (s,v) => s with { Kill = v }),
            new("Диффузия U", .001m, .16m, .04m, s => s.DiffusionU, (s,v) => s with { DiffusionU = v }),
            new("Диффузия V", .001m, .16m, .04m, s => s.DiffusionV, (s,v) => s with { DiffusionV = v }),
            new("Уровень поверхности V", .01m, .9m, .1m, s => s.Threshold, (s,v) => s with { Threshold = v })
        };
        if (_settings.CutAxis > 0) parameters.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray(); _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Gray–Scott 3D", _parameters.Select(p => p.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Каждый вариант стартует из одного поля U/V: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходная затравка, шаг 0")}. " +
            "У всех вариантов одинаковое число дополнительных шагов. Сетка, начальная форма, случайное число и ось среза фиксированы. " +
            "Прогоны независимы, разрешение сетки не уменьшается. Уровень и срез меняют отображение, а не U/V. " +
            "При применении выбранный прогон повторяется, точное поле и номер шага переносятся в основное окно на паузе. " +
            "«Вернуть предыдущий» восстанавливает исходное показанное поле. Закрытие атласа не заменяет поле основного окна. " +
            "Пустой вид возможен при затухании V или неподходящем уровне; это результат сравнения. Поведение на большем времени может отличаться.");
        AddTrialSteps(400);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.GrayScott3D || source.GrayScott is null || source.GrayScott.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Gray–Scott 3D или исходная затравка, без билета другого GPU-окна.");
        source.GrayScott.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны на ГП от общего поля";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var settings = state.GrayScott;
        if (state.Kind != Fractal3DKind.GrayScott3D || settings is null || point.Length != _parameters.Length || settings.Size != _settings.Size ||
            settings.SeedShape != _settings.SeedShape || settings.Seed != _settings.Seed || settings.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, затравка или ось среза не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum) throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            settings = p.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        settings.Validate(); state.GrayScott = settings;
    }
}
