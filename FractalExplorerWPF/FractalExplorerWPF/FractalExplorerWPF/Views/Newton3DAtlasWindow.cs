using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Числовые вариации одной применённой формулы и выбранной области Ньютона.</summary>
public sealed class Newton3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    internal const int PreviewSize = 32;
    private sealed record Parameter(string Name, decimal Minimum, decimal Maximum, decimal Width,
        Func<Newton3DSettings, double> Read, Func<Newton3DSettings, double, Newton3DSettings> Write, bool Integer = false);
    private readonly Parameter[] _parameters;
    private readonly double[] _original;
    private readonly Newton3DSettings _settings;

    public Newton3DAtlasWindow(Fractal3DState source) : base(PreviewSource(source))
    {
        _settings = source.Newton with { };
        var prepared = Newton3DFormula.Prepare(_settings);
        var parameters = new List<Parameter>
        {
            new("Координата w", -2, 2, .4m, s => s.SliceW, (s, v) => s with { SliceW = v }),
            new("Релаксация", .2m, 1.8m, .3m, s => s.Relaxation, (s, v) => s with { Relaxation = v }),
            new("Полуразмер области", .2m, 4, .4m, s => s.Extent, (s, v) => s with { Extent = v })
        };
        if (_settings.Region == Newton3DRegion.Boundary)
            parameters.Add(new("Порог границы · итерации", 2, _settings.MaxIterations - 1, 4,
                s => s.BoundaryIterations, (s, v) => s with { BoundaryIterations = (int)v }, true));
        if (_settings.CutAxis > 0)
            parameters.Add(new("Положение отсечения · доля R", -1, 1, .4m,
                s => s.CutPosition, (s, v) => s with { CutPosition = v }));
        _parameters = parameters.ToArray();
        _original = _parameters.Select(p => p.Read(_settings)).ToArray();
        string region = _settings.Region switch
        {
            Newton3DRegion.PlusBasin => $"бассейн + {_settings.PlusRoot + 1}",
            Newton3DRegion.RootPair => $"пара корней {_settings.PlusRoot + 1} / {_settings.MinusRoot + 1}",
            _ => "границы медленной сходимости"
        };
        string cut = _settings.CutAxis == 0 ? "нет" : "XYZ"[_settings.CutAxis - 1].ToString();
        Initialize3DAtlas("Атлас параметров · Ньютон 3D", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Применено: {_settings.Expression}. Корней: {prepared.RootCount}; область: {region}. " +
            $"Отсечение: {cut}{(_settings.SliceOnly ? ", только плоскость" : "")}. " +
            $"Итерации фиксированы: {_settings.MaxIterations}; порог границы всегда меньше этого числа. " +
            $"Превью — {PreviewSize}³; применение сохраняет сетку {_settings.Size}³, формулу, порядок корней, " +
            "тип области, ось отсечения, камеру и оформление. Неиспользуемые настройки скрыты; остальные числовые параметры фиксированы. " +
            "Неприменённые формула и радиус поиска остаются в редакторе. Тонкие границы проверяйте в основном окне.");
    }

    private static Fractal3DState PreviewSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.Newton3D || source.Newton is null)
            throw new ArgumentException("Нужен режим Ньютон 3D.");
        source.Newton.Validate();
        var preview = source.Clone();
        // Ограничиваем только исходник рендера; ApplyComponents сохраняет Size целевого вида.
        preview.Newton = preview.Newton with { Size = PreviewSize };
        return preview;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) =>
        (_parameters[index].Minimum, _parameters[index].Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => _parameters[index].Integer;
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
        $"{completed} / {total} · ГП, по одному варианту · поле {PreviewSize}³ · {_settings.MaxIterations} итераций";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        var s = state.Newton;
        if (state.Kind != Fractal3DKind.Newton3D || s is null || point.Length != _parameters.Length ||
            s.Formula != _settings.Formula || s.Degree != _settings.Degree || !Equals(s.Roots, _settings.Roots) ||
            s.RootSearchRadius != _settings.RootSearchRadius || s.MaxIterations != _settings.MaxIterations ||
            s.Region != _settings.Region || s.PlusRoot != _settings.PlusRoot || s.MinusRoot != _settings.MinusRoot ||
            s.CutAxis != _settings.CutAxis || s.SliceOnly != _settings.SliceOnly)
            throw new ArgumentException("Формула, корни или фиксированные настройки не соответствуют атласу Ньютона.");
        for (int i = 0; i < point.Length; i++)
        {
            var p = _parameters[i];
            if (point[i] < p.Minimum || point[i] > p.Maximum || p.Integer && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException($"Параметр «{p.Name}» вне диапазона или требует целого значения.");
            s = p.Write(s, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        }
        s.Validate();
        state.Newton = s;
    }
}
