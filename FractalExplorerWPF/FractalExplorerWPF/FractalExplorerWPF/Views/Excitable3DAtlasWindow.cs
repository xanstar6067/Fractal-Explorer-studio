using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Excitable3DAtlasWindow : GpuSimulationParameterAtlasWindow<Excitable3DSettings, Excitable3DField, Excitable3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Excitable3DSettings, double> Read, Func<Excitable3DSettings, double, Excitable3DSettings> Write, bool Integer = false);
    private readonly Excitable3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public Excitable3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.Excitable, Set = (s,v) => s.Excitable = v,
        Create = (host,s) => new Excitable3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.Excitable with { };
        var (a,b) = _settings.Model switch
        {
            Excitable3DModel.Oregonator =>
                (new Parameter("Стехиометрия f", .5m, 3, .4m, s => s.A, (s,v) => s with { A = v }),
                 new Parameter("Малый параметр q", .0001m, .02m, .002m, s => s.B, (s,v) => s with { B = v })),
            Excitable3DModel.FitzHughNagumo =>
                (new Parameter("Порог β", .4m, 1.2m, .2m, s => s.A, (s,v) => s with { A = v }),
                 new Parameter("Восстановление γ", .5m, 1.5m, .2m, s => s.B, (s,v) => s with { B = v })),
            Excitable3DModel.AlievPanfilov =>
                (new Parameter("Порог a", .01m, .3m, .06m, s => s.A, (s,v) => s with { A = v }),
                 new Parameter("Скорость k", 1, 12, 2, s => s.B, (s,v) => s with { B = v })),
            _ =>
                (new Parameter("Возбудимость a", .4m, 1.2m, .2m, s => s.A, (s,v) => s with { A = v }),
                 new Parameter("Порог b", .001m, .2m, .04m, s => s.B, (s,v) => s with { B = v }))
        };
        var p = new List<Parameter>
        {
            a, b,
            new("Разделение времён ε", .005m, .5m, .02m, s => s.Epsilon, (s,v) => s with { Epsilon = v }),
            new("Диффузия U", .01m, 2, .4m, s => s.DiffusionU, (s,v) => s with { DiffusionU = v }),
            new("Диффузия V", 0, 1, .2m, s => s.DiffusionV, (s,v) => s with { DiffusionV = v }),
            new("Шаг сетки Δx", .25m, 2, .4m, s => s.Spacing, (s,v) => s with { Spacing = v }),
            new("Шаг времени Δt", .0001m, .02m, .004m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Уровень поверхности", .05m, .95m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v }),
            new("Границы", 0, 1, 1, s => (int)s.Boundary, (s,v) => s with { Boundary = (Excitable3DBoundary)(int)v }, true),
            new("Показывать", 0, 2, 2, s => (int)s.View, (s,v) => s with { View = (Excitable3DView)(int)v }, true)
        };
        if (_settings.Model == Excitable3DModel.AlievPanfilov)
        {
            p.Add(new("Восстановление μ₁", .01m, .5m, .1m, s => s.Mu1, (s,v) => s with { Mu1 = v }));
            p.Add(new("Восстановление μ₂", .1m, 1, .2m, s => s.Mu2, (s,v) => s with { Mu2 = v }));
        }
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas($"Атлас параметров · {_settings.Name}", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт U/V: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходная затравка, шаг 0")}. " +
            "Модель, число ячеек, затравка и ось среза фиксированы; μ₁/μ₂ доступны только у Aliev–Panfilov. Даже без снимка исходное U/V фиксируется до изменения коэффициентов, включая зависящее от них состояние покоя. " +
            "Каждый вариант независимо проходит N дополнительных шагов без повторной подготовки и уменьшения сетки. Длительность прогона — N·Δt; для равного времени оставьте Δt фиксированным. Счётчик хранит шаги, а не накопленное время при смене Δt. " +
            "Δx меняет размер области Nячеек·Δx при том же начальном U/V. Недопустимые шаги по реакции или диффузии отмечаются до расчёта. " +
            "Вид, уровень и срез меняют только показ. Нити — дискретная оценка обхода фазы вокруг стандартного центра цикла модели; при других коэффициентах он может не подходить. Короткий прогон не гарантирует устойчивые свитки. " +
            "Применение повторяет прогон и переносит точные U/V и шаг на паузе. «Вернуть предыдущий» восстанавливает исходное поле; закрытие без применения его сохраняет.");
        AddTrialSteps(240);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Excitable3D || source.Excitable is null || source.Excitable.Live is not null)
            throw new ArgumentException("Нужно сохранённое U/V возбудимой среды 3D или исходная затравка, без билета другого GPU-окна.");
        source.Excitable.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index switch
    {
        8 => value == 0 ? "Непроницаемые стенки" : "Периодические",
        9 => value == 0 ? "Фронты волн" : value == 1 ? "Вихревые нити" : "Волны и нити",
        _ => base.ComponentValueLabel(index,value,format)
    };
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны U/V на ГП";
    private static string? StepError(Excitable3DSettings s)
    {
        if (s.Model != Excitable3DModel.AlievPanfilov && s.TimeStep > .25 * s.Epsilon + 1e-12)
            return "Δt > ε/4: уменьшите шаг времени или увеличьте ε.";
        if (s.TimeStep * Math.Max(s.DiffusionU,s.DiffusionV) / (s.Spacing * s.Spacing) > .15)
            return "Δt·max(Du,Dv)/Δx² > 0,15: уменьшите шаг времени или диффузию либо увеличьте Δx.";
        return null;
    }
    protected override string? UnavailableSample(decimal[] point) => StepError(Map(_settings,point));
    private Excitable3DSettings Map(Excitable3DSettings s, decimal[] point)
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
        var s = state.Excitable;
        if (state.Kind != Fractal3DKind.Excitable3D || s is null || s.Size != _settings.Size || s.Model != _settings.Model ||
            s.SeedShape != _settings.SeedShape || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, модель, затравка или ось среза не соответствует атласу.");
        var mapped = Map(s,point);
        if (StepError(mapped) is { } error) throw new ArgumentException(error);
        mapped.Validate(); state.Excitable = mapped;
    }
}
