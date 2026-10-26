using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас числовых параметров одной применённой формулы F(x,y,z).</summary>
public sealed class ImplicitSurfaceAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<ImplicitSurfaceSettings, double> Read, Func<ImplicitSurfaceSettings, double, ImplicitSurfaceSettings> Write);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly ImplicitSurfaceSettings _settings;

    public ImplicitSurfaceAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.ImplicitSurface || source.ImplicitSurface is null)
            throw new ArgumentException("Нужна неявная поверхность F(x,y,z).");
        var formula = source.ImplicitSurface.Validate();
        _settings = source.ImplicitSurface with { };
        var parameters = new List<Parameter>();
        if (formula.UsesParameter('a'))
            parameters.Add(new("Параметр a", -10, 10, .4m, s => s.A, (s, v) => s with { A = v }));
        if (formula.UsesParameter('b'))
            parameters.Add(new("Параметр b", -10, 10, .4m, s => s.B, (s, v) => s with { B = v }));
        if (formula.UsesParameter('c'))
            parameters.Add(new("Параметр c", -10, 10, .4m, s => s.C, (s, v) => s with { C = v }));
        parameters.Add(new("Уровень F", -100, 100, .4m, s => s.Level, (s, v) => s with { Level = v }));
        parameters.Add(new("Область ±R", .1m, 10, .4m, s => s.Extent, (s, v) => s with { Extent = v }));
        if (_settings.CutAxis > 0)
            parameters.Add(new("Положение среза", -10, 10, .4m, s => s.CutPosition, (s, v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        string cut = _settings.CutAxis == 0 ? "без среза" : $"срез по {"XYZ"[_settings.CutAxis - 1]}";
        Initialize3DAtlas("Атлас параметров · Неявные поверхности F(x,y,z)", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Применённая формула: {_settings.Formula}\n" +
            $"Область: −R…R по каждой оси; {cut}. Выберите два параметра, остальные фиксированы. " +
            "Доступны только встречающиеся в формуле a/b/c. Формула, ось среза, камера и оформление сохраняются; " +
            "неприменённый текст в основном редакторе остаётся черновиком. Некоторые варианты могут быть пустыми " +
            "или содержать неопределённые участки. Тонкие детали уточняйте в основном окне.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => _parameters[index].Width;
    protected override ParameterAtlasRange OverviewRange
    {
        get
        {
            var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex);
            return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum);
        }
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · применённая формула";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.ImplicitSurface;
        if (state.Kind != Fractal3DKind.ImplicitSurface || s is null || point.Length != _parameters.Length ||
            s.Formula != _settings.Formula || s.CutAxis != _settings.CutAxis)
            throw new ArgumentException("Формула или ось среза не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum)
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона.");
            s = p.Write(s, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        state.ImplicitSurface = s;
    }
}
