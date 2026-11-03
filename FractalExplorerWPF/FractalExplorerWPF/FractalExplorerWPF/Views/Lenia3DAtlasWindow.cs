using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Lenia3DAtlasWindow : GpuSimulationParameterAtlasWindow<Lenia3DSettings, Lenia3DField, Lenia3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Lenia3DSettings, double> Read, Func<Lenia3DSettings, double, Lenia3DSettings> Write, bool Integer = false);
    private readonly Lenia3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public Lenia3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.Lenia, Set = (s,v) => s.Lenia = v,
        Create = (host,s) => new Lenia3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.Lenia with { };
        var p = new List<Parameter>
        {
            new("Центр роста μ", .01m, .5m, .04m, s => s.GrowthMean, (s,v) => s with { GrowthMean = v }),
            new("Ширина роста σ", .002m, .15m, .012m, s => s.GrowthWidth, (s,v) => s with { GrowthWidth = v }),
            new("Радиус ядра · ячеек", 2, _settings.Size / 2m - 1, 4, s => s.Radius, (s,v) => s with { Radius = v }),
            new("Функция роста", 0, 1, 1, s => (int)s.Growth, (s,v) => s with { Growth = (LeniaGrowth)(int)v }, true),
            new("Шаг времени Δt", .01m, .2m, .04m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Порог видимости", .01m, .9m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v })
        };
        for (int i = 0; i < _settings.ShellCount; i++)
        {
            int shell = i;
            p.Add(new($"Вес оболочки β{i + 1}", 0, 1, .4m, s => s.Shells[shell], (s,v) => shell switch
            { 0 => s with { Beta1 = v }, 1 => s with { Beta2 = v }, 2 => s with { Beta3 = v }, _ => s with { Beta4 = v } }));
        }
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Lenia 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}, t = {field.Time:G6}" : "затравка исходного радиуса, шаг 0, t = 0")}. " +
            $"Сетка, число оболочек ({_settings.ShellCount}), затравка, её шум, seed и ось среза фиксированы. Доступны веса только активных оболочек. " +
            "Каждый вариант независимо проходит одинаковое число дополнительных шагов N. Радиус меняет ядро, а не размер общего начального поля; сетка не уменьшается. " +
            "Физическое время увеличивается на N·Δt: для сравнения при одном времени оставьте Δt фиксированным. Порог и срез меняют только показ. " +
            "При применении прогон повторяется и точное поле со временем переносится на паузе. «Вернуть предыдущую форму» восстанавливает исходный снимок и сохраняет прежнюю историю поиска. " +
            "Закрытие без применения не заменяет поле. Исчезновение или заполнение среды — возможный результат параметров, а короткий прогон не гарантирует длительное выживание.");
        AddTrialSteps(72);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Lenia3D || source.Lenia is null || source.Lenia.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Lenia 3D или исходная затравка, без билета другого GPU-окна.");
        source.Lenia.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 3
        ? value == 0 ? "Полиномиальная" : "Гауссова" : base.ComponentValueLabel(index,value,format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны Lenia на ГП";
    protected override string? UnavailableSample(decimal[] point) => Map(_settings,point).Shells.Sum() <= 0
        ? "Хотя бы один активный вес β должен быть больше нуля." : null;
    protected override string? UnavailableSampleError(Exception exception) => exception is LeniaKernelException ? exception.Message : null;
    private Lenia3DSettings Map(Lenia3DSettings s, decimal[] point)
    {
        if (point.Length != _parameters.Length) throw new ArgumentException("Неверное число параметров атласа.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s,point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        return s;
    }
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.Lenia;
        if (state.Kind != Fractal3DKind.Lenia3D || s is null || s.Size != _settings.Size || s.ShellCount != _settings.ShellCount ||
            s.SeedShape != _settings.SeedShape || s.SeedNoise != _settings.SeedNoise || s.Seed != _settings.Seed || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, оболочки, затравка или ось среза не соответствует атласу.");
        var mapped = Map(s,point); mapped.Validate(); state.Lenia = mapped;
    }
}
