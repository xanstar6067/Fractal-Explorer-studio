using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Локальное сравнение коэффициентов одной автономной системы и её начальных условий.</summary>
public sealed class Attractor3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Attractor3DSettings, double> Read, Action<Attractor3DSettings, double> Write);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly Attractor3DSystem _system;
    private readonly int _iterations;
    protected override int PreviewIterationCount => Math.Clamp(_iterations, 50_000, 100_000);

    public Attractor3DAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _system = source.Attractor.System; _iterations = source.Iterations;
        string[] names = Attractor3DSystems.ParameterNames(_system);
        Func<Attractor3DSettings, double>[] reads = [s => s.A, s => s.B, s => s.C, s => s.D, s => s.E, s => s.F];
        Action<Attractor3DSettings, double>[] writes = [(s,v) => s.A = v, (s,v) => s.B = v, (s,v) => s.C = v,
            (s,v) => s.D = v, (s,v) => s.E = v, (s,v) => s.F = v];
        var parameters = new List<Parameter>();
        for (int i = 0; i < names.Length; i++)
            parameters.Add(new(names[i], -100, 100, (decimal)Math.Max(.1, Math.Abs(reads[i](source.Attractor)) * .2), reads[i], writes[i]));
        parameters.Add(new("Шаг времени RK4", .0001m, .05m, (decimal)source.Attractor.TimeStep,
            s => s.TimeStep, (s,v) => s.TimeStep = v));
        parameters.Add(new("Начальная точка · X", -100, 100, 1, s => s.StartX, (s,v) => s.StartX = v));
        parameters.Add(new("Начальная точка · Y", -100, 100, 1, s => s.StartY, (s,v) => s.StartY = v));
        parameters.Add(new("Начальная точка · Z", -100, 100, 1, s => s.StartZ, (s,v) => s.StartZ = v));
        _parameters = parameters.ToArray(); _original = _parameters.Select(p => p.Read(source.Attractor)).ToArray();
        Initialize3DAtlas($"Атлас параметров · {Attractor3DSystems.Name(_system)}", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            "Система фиксирована; выберите любые два её параметра для осей. Остальные параметры, камера и оформление взяты из основного окна. " +
            "Каждая траектория рассчитывается заново из выбранной начальной точки. Миниатюры используют до 100 000 точек и укороченный переходный участок; " +
            "длительность наблюдения T = число точек × шаг. Изменение шага меняет T. Число точек полного расчёта сохраняется. " +
            "Границы каждой траектории подгоняются отдельно, поэтому размеры миниатюр не показывают абсолютный масштаб. " +
            "Расходящиеся траектории и сходимость к точке отмечаются в ячейке. Пустой или простой вид не доказывает отсутствие хаоса; " +
            "выбранный вариант проверяйте полным расчётом в основном окне.", yAxis: names.Length > 1 ? 1 : names.Length + 1);
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.StrangeAttractor || source.Attractor is null || !Enum.IsDefined(source.Attractor.System) ||
            source.Iterations is < 50_000 or > 5_000_000)
            throw new ArgumentException("Нужен режим странных аттракторов с 50 000–5 000 000 точек.");
        var s = source.Attractor;
        if (new[] { s.A, s.B, s.C, s.D, s.E, s.F, s.StartX, s.StartY, s.StartZ }.Any(v => !double.IsFinite(v) || v < -100 || v > 100) ||
            !double.IsFinite(s.TimeStep) || s.TimeStep < .0001 || s.TimeStep > .05)
            throw new ArgumentException("Параметры системы вне допустимого диапазона.");
        return source;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string? UnavailableSampleError(Exception error) => error is Attractor3DTrajectoryException ? error.Message : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · траектория на ЦП, кадр на ГП · до {iterations:N0} точек";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.StrangeAttractor || state.Attractor is null || state.Attractor.System != _system || point.Length != _parameters.Length)
            throw new ArgumentException("Система не соответствует атласу.");
        var settings = state.Attractor.Clone();
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum) throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            p.Write(settings, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        state.Attractor = settings;
    }
}
