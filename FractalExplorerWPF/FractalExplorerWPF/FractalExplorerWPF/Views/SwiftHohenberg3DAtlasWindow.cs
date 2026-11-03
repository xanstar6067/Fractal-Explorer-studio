using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class SwiftHohenberg3DAtlasWindow : GpuSimulationParameterAtlasWindow<SwiftHohenberg3DSettings, SwiftHohenberg3DField, SwiftHohenberg3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<SwiftHohenberg3DSettings, double> Read, Func<SwiftHohenberg3DSettings, double, SwiftHohenberg3DSettings> Write, bool Integer = false);
    private readonly SwiftHohenberg3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public SwiftHohenberg3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.SwiftHohenberg, Set = (s,v) => s.SwiftHohenberg = v,
        Create = (host,s) => new SwiftHohenberg3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.SwiftHohenberg with { };
        var p = new List<Parameter>
        {
            new("Неустойчивость r", -.5m, 1, .4m, s => s.R, (s,v) => s with { R = v }),
            new("Асимметрия b", -1.5m, 1.5m, .6m, s => s.Quadratic, (s,v) => s with { Quadratic = v }),
            new("Волновое число q₀", .4m, 1.5m, .3m, s => s.WaveNumber, (s,v) => s with { WaveNumber = v }),
            new("Шаг сетки Δx", .5m, 2, .4m, s => s.Spacing, (s,v) => s with { Spacing = v }),
            new("Шаг времени Δt", .002m, .05m, .02m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Масштаб показа поля A", .5m, 3, .6m, s => s.DisplayRange, (s,v) => s with { DisplayRange = v }),
            new("Уровень поверхности", .02m, .95m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v }),
            new("Показывать", 0, 2, 2, s => (int)s.View, (s,v) => s with { View = (SwiftHohenberg3DView)(int)v }, true)
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Свифт–Хоэнберг 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт φ: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходная затравка после спектральной проекции, шаг 0")}. " +
            "Число ячеек, затравка, начальный шум, seed, периодические границы и ось среза фиксированы. Без снимка исходное поле один раз создаётся и проецируется до изменения q₀/Δx, поэтому начальный узор одинаков у всех вариантов. " +
            "Каждый вариант независимо проходит N дополнительных шагов без повторной подготовки и уменьшения сетки. Длительность прогона — N·Δt; для равного времени оставьте Δt фиксированным. Счётчик хранит шаги, а не накопленное время при смене Δt. " +
            "q₀ выбирает длину волны λ₀ = 2π/q₀; Δx меняет размер области Nячеек·Δx при том же массиве φ. Ячейки с q₀·Δx > 1,2 недоступны до расчёта. Это ограничение спектральной полосы, а не гарантия устойчивости нелинейного шага. " +
            "Среднее φ может меняться. Вид, A, уровень и срез меняют только показ; границы φ ≈ 0 у нулевого поля заполняют весь объём. Короткий прогон не гарантирует устойчивую морфологию. " +
            "Применение повторяет прогон и переносит точное знаковое поле с номером шага на паузе. «Вернуть предыдущий» восстанавливает исходное поле. Закрытие без применения сохраняет его.");
        AddTrialSteps(48);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.SwiftHohenberg3D || source.SwiftHohenberg is null || source.SwiftHohenberg.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Свифта–Хоэнберга 3D или исходная затравка, без билета другого GPU-окна.");
        source.SwiftHohenberg.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 7
        ? value == 0 ? "Положительная фаза" : value == 1 ? "Отрицательная фаза" : "Границы φ ≈ 0"
        : base.ComponentValueLabel(index,value,format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые спектральные прогоны на ГП";
    private static string? BandError(SwiftHohenberg3DSettings s) => s.WaveNumber*s.Spacing > 1.2+1e-12
        ? "q₀·Δx > 1,2: уменьшите волновое число или шаг сетки, чтобы сохранить волну внутри спектральной полосы." : null;
    protected override string? UnavailableSample(decimal[] point) => BandError(Map(_settings,point));
    private SwiftHohenberg3DSettings Map(SwiftHohenberg3DSettings s, decimal[] point)
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
        var s = state.SwiftHohenberg;
        if (state.Kind != Fractal3DKind.SwiftHohenberg3D || s is null || s.Size != _settings.Size || s.Seed != _settings.Seed ||
            s.SeedShape != _settings.SeedShape || s.Noise != _settings.Noise || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, затравка, начальный шум, seed или ось среза не соответствует атласу.");
        var mapped = Map(s,point);
        if (BandError(mapped) is { } error) throw new ArgumentException(error);
        mapped.Validate(); state.SwiftHohenberg = mapped;
    }
}
