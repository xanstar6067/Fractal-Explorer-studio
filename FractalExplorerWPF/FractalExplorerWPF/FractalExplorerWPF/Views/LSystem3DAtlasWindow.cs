using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Сравнение формы при неизменной применённой пространственной грамматике.</summary>
public sealed class LSystem3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<LSystem3DSettings, double> Read, Func<LSystem3DSettings, double, LSystem3DSettings> Write, bool Integer = false);
    private readonly Parameter[] _parameters =
    [
        new("Поколения", 0, 12, 2, s => s.Generations, (s,v) => s with { Generations = (int)v }, true),
        new("Поворот + / −, °", -180, 180, 40, s => s.Yaw, (s,v) => s with { Yaw = v }),
        new("Наклон & / ^, °", -180, 180, 40, s => s.Pitch, (s,v) => s with { Pitch = v }),
        new("Закрутка, °", -180, 180, 60, s => s.Roll, (s,v) => s with { Roll = v }),
        new("Радиус / начальный шаг", .01m, .5m, .1m, s => s.Radius, (s,v) => s with { Radius = v }),
        new("Сужение ветвей", .3m, 1, .2m, s => s.BranchTaper, (s,v) => s with { BranchTaper = v }),
        new("Длина после > / предыдущий шаг", .3m, 1, .2m, s => s.StepDecay, (s,v) => s with { StepDecay = v }),
        new("Стадия построения", 0, 1, .3m, s => s.Growth, (s,v) => s with { Growth = v })
    ];
    private readonly LSystem3DSettings _settings;
    private readonly double[] _original;

    public LSystem3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _settings = source.LSystem with { }; _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        Initialize3DAtlas("Атлас параметров · L-системы 3D", _parameters.Select(p => p.Name).ToArray(), _original.Select(v => (decimal)v).ToArray(),
            "Аксиома, применённые правила, рисующие буквы и источник цвета фиксированы. Черновик правил не используется и сохраняется в редакторе. " +
            "Выберите любые два параметра формы; остальные фиксируются. Поколения рассчитываются полностью, без усечения ветвей. " +
            "Лимиты: 1 000 000 символов и 100 000 сегментов. Превышение или отсутствие ветвей отмечаются в ячейке. " +
            "Угол действует только при соответствующей команде, сужение — внутри [ ] и после !, длина шага — после > / <. " +
            "Каждая полная форма вписывается отдельно; стадия показывает долю порядка рисования. При нуле вид пустой. " +
            "Камера и оформление сохраняются. Тонкие ветви проверяйте в основном окне. Построение остаётся на паузе.", xAxis: 2, yAxis: 3);
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.LSystem3D || source.LSystem is null) throw new ArgumentException("Нужен режим L-систем 3D.");
        try { source.LSystem.Validate(); }
        catch (InvalidOperationException ex) { throw new ArgumentException(ex.Message, ex); }
        return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string? UnavailableSampleError(Exception error) => error is LSystem3DGeometryException ? error.Message : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · полная геометрия на ЦП, кадр на ГП";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.LSystem;
        if (state.Kind != Fractal3DKind.LSystem3D || s is null || point.Length != _parameters.Length || s.Axiom != _settings.Axiom ||
            s.RulesText != _settings.RulesText || s.DrawSymbols != _settings.DrawSymbols || s.ColorSource != _settings.ColorSource)
            throw new ArgumentException("Грамматика или источник цвета не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона или требует целого значения.");
            s = p.Write(s, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate(); state.LSystem = s;
    }
}
