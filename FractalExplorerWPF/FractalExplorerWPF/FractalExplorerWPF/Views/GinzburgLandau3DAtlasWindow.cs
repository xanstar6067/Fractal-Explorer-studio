using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class GinzburgLandau3DAtlasWindow : GpuSimulationParameterAtlasWindow<GinzburgLandau3DSettings, GinzburgLandau3DField, GinzburgLandau3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<GinzburgLandau3DSettings, double> Read, Func<GinzburgLandau3DSettings, double, GinzburgLandau3DSettings> Write, bool Integer = false);
    private readonly GinzburgLandau3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public GinzburgLandau3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.GinzburgLandau, Set = (s,v) => s.GinzburgLandau = v,
        Create = (host,s) => new GinzburgLandau3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.GinzburgLandau with { };
        var p = new List<Parameter>
        {
            new("Дисперсия c₁", -3, 3, 1, s => s.C1, (s,v) => s with { C1 = v }),
            new("Нелинейная частота c₃", -3, 3, 1, s => s.C3, (s,v) => s with { C3 = v }),
            new("Шаг сетки Δx", .5m, 2, .4m, s => s.Spacing, (s,v) => s with { Spacing = v }),
            new("Шаг времени Δt", .0005m, .05m, .01m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Уровень поверхности", .02m, .95m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v }),
            new("Границы", 0, 1, 1, s => (int)s.Boundary, (s,v) => s with { Boundary = (GinzburgLandau3DBoundary)(int)v }, true),
            new("Показывать", 0, 2, 2, s => (int)s.View, (s,v) => s with { View = (GinzburgLandau3DView)(int)v }, true)
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Гинзбург–Ландау 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт Re(A)/Im(A): {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходное поле, шаг 0")}. " +
            "Число ячеек, начальная форма, seed и ось среза фиксированы. Даже без снимка исходное комплексное поле фиксируется до изменения Δx: ширина ядра вихря и амплитуда фазовой волны не пересоздаются для каждой ячейки. " +
            "Каждый вариант независимо проходит N дополнительных шагов без повторной подготовки и уменьшения сетки. Длительность прогона — N·Δt; для равного времени оставьте Δt фиксированным. Счётчик хранит шаги, а не накопленное время при смене Δt. " +
            "Δx меняет размер области Nячеек·Δx при том же массиве Re/Im. Ячейки с Δt·(1+c₁²)/Δx² > 0,15 недоступны до расчёта; это ограничение устойчивости, а не гарантия точности. " +
            "Вид, уровень и срез меняют только показ. Малая амплитуда не доказывает вихревой заряд; нити определяются дискретным оборотом фазы и могут исчезать при развитии. " +
            "Применение повторяет прогон и переносит точные Re/Im с номером шага на паузе. «Вернуть предыдущий» восстанавливает исходное поле. Закрытие без применения сохраняет его.");
        AddTrialSteps(160);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.GinzburgLandau3D || source.GinzburgLandau is null || source.GinzburgLandau.Live is not null)
            throw new ArgumentException("Нужно сохранённое комплексное поле Гинзбурга–Ландау 3D или исходная форма, без билета другого GPU-окна.");
        source.GinzburgLandau.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index switch
    {
        5 => value == 0 ? "Непроницаемые стенки" : "Периодические",
        6 => value == 0 ? "Вихревые нити" : value == 1 ? "Дефекты амплитуды" : "Амплитуда |A|",
        _ => base.ComponentValueLabel(index,value,format)
    };
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны комплексного поля на ГП";
    private static string? StepError(GinzburgLandau3DSettings s) => s.TimeStep * (1+s.C1*s.C1) / (s.Spacing*s.Spacing) > .15+1e-12
        ? "Δt·(1+c₁²)/Δx² > 0,15: уменьшите Δt или |c₁| либо увеличьте Δx." : null;
    protected override string? UnavailableSample(decimal[] point) => StepError(Map(_settings,point));
    private GinzburgLandau3DSettings Map(GinzburgLandau3DSettings s, decimal[] point)
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
        var s = state.GinzburgLandau;
        if (state.Kind != Fractal3DKind.GinzburgLandau3D || s is null || s.Size != _settings.Size || s.Seed != _settings.Seed ||
            s.SeedShape != _settings.SeedShape || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, начальная форма, seed или ось среза не соответствует атласу.");
        var mapped = Map(s,point);
        if (StepError(mapped) is { } error) throw new ArgumentException(error);
        mapped.Validate(); state.GinzburgLandau = mapped;
    }
}
