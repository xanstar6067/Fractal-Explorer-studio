using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Turing3DAtlasWindow : GpuSimulationParameterAtlasWindow<Turing3DSettings, Turing3DField, Turing3DVolume>
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Turing3DSettings, double> Read, Func<Turing3DSettings, double, Turing3DSettings> Write, bool Integer = false);
    private readonly Turing3DSettings _settings;
    private readonly Parameter[] _parameters;
    private readonly double[] _original;

    public Turing3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source), new()
    {
        Get = s => s.Turing, Set = (s,v) => s.Turing = v,
        Create = (host,s) => new Turing3DGpuSimulation(host,s),
        Field = s => s.Field, With = (s,field,live) => s with { Field = field, Live = live }, FreezeInitialField = true
    })
    {
        _settings = source.Turing with { Layers = source.Turing.Layers.ToArray() };
        var p = new List<Parameter>();
        if (_settings.Reaction.IsClassical)
        {
            p.Add(new("Реакция A", .001m, 5, .5m, s => s.Reaction.A, (s,v) => s with { Reaction = s.Reaction with { A = v } }));
            p.Add(new("Реакция B", .01m, 8, 1, s => s.Reaction.B, (s,v) => s with { Reaction = s.Reaction with { B = v } }));
            p.Add(new("Диффузия U", .01m, 4, 1, s => s.Reaction.DiffusionU, (s,v) => s with { Reaction = s.Reaction with { DiffusionU = v } }));
            p.Add(new("Диффузия V", .01m, 40, 8, s => s.Reaction.DiffusionV, (s,v) => s with { Reaction = s.Reaction with { DiffusionV = v } }));
        }
        p.Add(new("Размер деталей", .25m, 3, .5m, s => s.DetailSize, (s,v) => s with { DetailSize = v }));
        p.Add(new("Отклик", .1m, 3, .5m, s => s.EvolutionRate, (s,v) => s with { EvolutionRate = v }));
        if (!_settings.Reaction.IsClassical)
        {
            p.Add(new("Радиус подавления", 1.2m, 4, .6m, s => s.InhibitorRatio, (s,v) => s with { InhibitorRatio = v }));
            for (int i = 0; i < _settings.Layers.Count; i++)
            {
                if (!_settings.Layers[i].Enabled) continue;
                int layer = i;
                p.Add(new($"Масштаб {i + 1} · радиус", .5m, 64, 8, s => s.Layers[layer].Radius,
                    (s,v) => WithLayer(s,layer,s.Layers[layer] with { Radius = v })));
                p.Add(new($"Масштаб {i + 1} · отклик", .001m, .15m, .03m, s => s.Layers[layer].Amount,
                    (s,v) => WithLayer(s,layer,s.Layers[layer] with { Amount = v })));
            }
        }
        if (_settings.Region == Turing3DRegion.Shell)
            p.Add(new("Толщина оболочки", .05m, .6m, .15m, s => s.ShellThickness, (s,v) => s with { ShellThickness = v }));
        if (_settings.Symmetry == Turing3DSymmetry.Axial)
            p.Add(new("Число лучей", 1, 16, 4, s => s.Arms, (s,v) => s with { Arms = (int)v }, true));
        p.Add(new("Уровень поверхности", .05m, .95m, .2m, s => s.Level, (s,v) => s with { Level = v }));
        if (_settings.SheetThickness > 0)
            p.Add(new("Толщина мембраны", .02m, .3m, .08m, s => s.SheetThickness, (s,v) => s with { SheetThickness = v }));
        if (_settings.CutAxis > 0)
            p.Add(new("Положение среза", -1, 1, .5m, s => s.CutPosition, (s,v) => s with { CutPosition = v }));
        _parameters = p.ToArray(); _original = p.Select(x => x.Read(_settings)).ToArray();
        Initialize3DAtlas($"Атлас параметров · Тьюринг 3D · {_settings.Reaction.Name}", p.Select(x => x.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            $"Общее начальное поле: {(_settings.Field is { } field ? $"показанный шаг {field.Step:N0}" : "шум по seed, шаг 0")}. " +
            "Все варианты проходят одинаковое число дополнительных шагов независимо друг от друга. Сетка, seed, модель реакции, форма области, группа симметрии, отражения и границы фиксированы. " +
            "У Маккейба меняются только включённые масштабы. Уровень, мембрана и срез меняют отображение. " +
            "При применении прогон повторяется и его точное поле переносится на паузе; «Вернуть предыдущий» восстанавливает исходное поле. " +
            "Закрытие без применения сохраняет основное поле. Пустой вид может означать однородное поле или неподходящий уровень. Большее время наблюдения может дать другой узор.");
        AddTrialSteps(_settings.Reaction.IsClassical ? 40 : 120);
    }
    private static Turing3DSettings WithLayer(Turing3DSettings s, int index, Turing3DScale value)
    { var layers = s.Layers.ToArray(); layers[index] = value; return s with { Layers = layers }; }
    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Turing3D || source.Turing is null || source.Turing.Live is not null)
            throw new ArgumentException("Нужно сохранённое поле Тьюринга 3D или исходный шум, без билета другого GPU-окна.");
        source.Turing.Validate(); return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum,x.Maximum,y.Minimum,y.Maximum); }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · независимые прогоны Тьюринга на ГП";
    protected override string? UnavailableSample(decimal[] point)
    {
        var s = Map(_settings,point);
        return s.Reaction.IsClassical && (s.Reaction.Equilibrium.U > 1000 || s.Reaction.Equilibrium.V > 1000)
            ? "Равновесная концентрация выше 1000: увеличьте A или уменьшите B." : null;
    }
    private Turing3DSettings Map(Turing3DSettings settings, decimal[] point)
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
        var s = state.Turing;
        if (state.Kind != Fractal3DKind.Turing3D || s is null || s.Size != _settings.Size || s.Seed != _settings.Seed ||
            s.Reaction.Model != _settings.Reaction.Model || s.Region != _settings.Region || s.Symmetry != _settings.Symmetry ||
            s.Mirror != _settings.Mirror || s.Boundary != _settings.Boundary || s.CutAxis != _settings.CutAxis ||
            (s.SheetThickness > 0) != (_settings.SheetThickness > 0) || s.Layers.Count != _settings.Layers.Count ||
            s.Layers.Where((l,i) => l.Enabled != _settings.Layers[i].Enabled).Any())
            throw new ArgumentException("Модель, сетка или фиксированные параметры не соответствуют атласу.");
        var mapped = Map(s,point); mapped.Validate(); state.Turing = mapped;
    }
}
