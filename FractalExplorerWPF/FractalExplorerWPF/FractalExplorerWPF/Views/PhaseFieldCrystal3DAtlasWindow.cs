using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class PhaseFieldCrystal3DAtlasWindow : GpuSimulationParameterAtlasWindow<PhaseFieldCrystal3DSettings, PhaseFieldCrystal3DField, PhaseFieldCrystal3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<PhaseFieldCrystal3DSettings, double> Read, Func<PhaseFieldCrystal3DSettings, double, PhaseFieldCrystal3DSettings> Write, bool Integer = false);
    private readonly PhaseFieldCrystal3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public PhaseFieldCrystal3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.PhaseFieldCrystal, Set = (s,v) => s.PhaseFieldCrystal = v,
        Create = (host,s) => new PhaseFieldCrystal3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.PhaseFieldCrystal with { };
        var p = new List<Parameter>
        {
            new("Переохлаждение ε", -.2m, .6m, .2m, s => s.Undercooling, (s,v) => s with { Undercooling = v }),
            new("Волновое число q₀", .6m, 1.4m, .2m, s => s.WaveNumber, (s,v) => s with { WaveNumber = v }),
            new("Подвижность M", .1m, 2, .4m, s => s.Mobility, (s,v) => s with { Mobility = v }),
            new("Шаг сетки Δx", .5m, 1.5m, .3m, s => s.Spacing, (s,v) => s with { Spacing = v }),
            new("Шаг времени Δt", .002m, .03m, .01m, s => s.TimeStep, (s,v) => s with { TimeStep = v }),
            new("Масштаб показа поля A", .5m, 3, .6m, s => s.DisplayRange, (s,v) => s with { DisplayRange = v }),
            new("Уровень поверхности", .02m, .95m, .2m, s => s.Threshold, (s,v) => s with { Threshold = v }),
            new("Показывать", 0, 2, 2, s => (int)s.View, (s,v) => s with { View = (PhaseFieldCrystal3DView)(int)v }, true)
        };
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · Фазовое поле кристалла 3D", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общий старт ψ: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "исходная затравка после спектральной проекции, шаг 0")}; ψ̄ = {_settings.Field?.Mean ?? _settings.Mean:G6}. " +
            "Средняя плотность фиксирована как часть сохраняемого поля. Число ячеек, затравка, шум, seed, периодические границы и ось среза также фиксированы. Без снимка исходная затравка один раз создаётся и проецируется до изменения q₀/Δx, затем используется у всех вариантов. " +
            "Каждый вариант независимо проходит N дополнительных шагов без повторной подготовки и уменьшения сетки. Длительность прогона — N·Δt; для равного времени оставьте Δt фиксированным. M задаёт скорость перераспределения плотности. Счётчик хранит шаги, а не накопленное время при смене Δt. " +
            "q₀ задаёт предпочтительную λ₀ = 2π/q₀; Δx меняет размер области Nячеек·Δx при том же массиве ψ. Недопустимы q₀·Δx > 1,2 или M·Δt > 0,03. Эти ограничения не гарантируют точность или убывание энергии при произвольном состоянии. " +
            "Вид, A, уровень и срез меняют только показ. Местная модуляция воспроизводится из ψ при публикации и не классифицирует решётку или дефекты. Короткий прогон не гарантирует кристаллизацию. " +
            "Применение повторяет прогон и переносит точное знаковое поле, его среднее и шаг на паузе. «Вернуть предыдущий» восстанавливает исходный снимок. Закрытие без применения сохраняет его.");
        AddTrialSteps(48);
    }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.PhaseFieldCrystal3D || source.PhaseFieldCrystal is null || source.PhaseFieldCrystal.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле кристалла 3D или исходная затравка, без билета другого GPU-окна.");
        source.PhaseFieldCrystal.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 7
        ? value == 0 ? "Пики плотности" : value == 1 ? "Впадины плотности" : "Местная модуляция"
        : base.ComponentValueLabel(index,value,format);
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны PFC на ГП";
    private static string? ConstraintError(PhaseFieldCrystal3DSettings s)
    {
        if (s.WaveNumber*s.Spacing > 1.2+1e-12)
            return "q₀·Δx > 1,2: уменьшите волновое число или шаг сетки для сохранения волны внутри спектральной полосы.";
        if (s.Mobility*s.TimeStep > .03+1e-12)
            return "M·Δt > 0,03: уменьшите подвижность или шаг времени.";
        return null;
    }
    protected override string? UnavailableSample(decimal[] point) => ConstraintError(Map(_settings,point));
    private PhaseFieldCrystal3DSettings Map(PhaseFieldCrystal3DSettings s, decimal[] point)
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
        var s = state.PhaseFieldCrystal;
        if (state.Kind != Fractal3DKind.PhaseFieldCrystal3D || s is null || s.Size != _settings.Size || s.Mean != _settings.Mean ||
            s.Seed != _settings.Seed || s.SeedShape != _settings.SeedShape || s.Noise != _settings.Noise || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Сетка, средняя плотность, затравка, шум, seed или ось среза не соответствует атласу.");
        var mapped = Map(s,point);
        if (ConstraintError(mapped) is { } error) throw new ArgumentException(error);
        mapped.Validate(); state.PhaseFieldCrystal = mapped;
    }
}
