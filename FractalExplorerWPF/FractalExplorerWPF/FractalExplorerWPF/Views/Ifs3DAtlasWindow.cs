using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Точные коэффициенты одной аффинной карты; другие карты и параметры вида фиксированы.</summary>
public sealed class Ifs3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private sealed record Parameter(string Name, Func<Ifs3DTransform, double> Read, Action<Ifs3DTransform, double> Write);
    private static readonly Parameter[] Matrix =
    [
        new("M11 · x → x′", t => t.M11, (t, v) => t.M11 = v),
        new("M12 · y → x′", t => t.M12, (t, v) => t.M12 = v),
        new("M13 · z → x′", t => t.M13, (t, v) => t.M13 = v),
        new("Перенос Tx", t => t.Tx, (t, v) => t.Tx = v),
        new("M21 · x → y′", t => t.M21, (t, v) => t.M21 = v),
        new("M22 · y → y′", t => t.M22, (t, v) => t.M22 = v),
        new("M23 · z → y′", t => t.M23, (t, v) => t.M23 = v),
        new("Перенос Ty", t => t.Ty, (t, v) => t.Ty = v),
        new("M31 · x → z′", t => t.M31, (t, v) => t.M31 = v),
        new("M32 · y → z′", t => t.M32, (t, v) => t.M32 = v),
        new("M33 · z → z′", t => t.M33, (t, v) => t.M33 = v),
        new("Перенос Tz", t => t.Tz, (t, v) => t.Tz = v)
    ];
    private readonly Parameter[] _parameters;
    private readonly Ifs3DTransform[] _transforms;
    private readonly double[] _original;
    private readonly int _iterations;
    private readonly decimal _maximumWeight;
    private readonly bool _otherPositiveWeight;
    public int TransformIndex { get; }
    protected override int PreviewIterationCount => Math.Clamp(_iterations, 10_000, 40_000);

    public Ifs3DAtlasWindow(Fractal3DState source, int transformIndex = 0) : base(ValidateSource(source, transformIndex))
    {
        TransformIndex = transformIndex; _iterations = source.Iterations;
        _transforms = source.IfsTransforms.Select(t => t.Clone()).ToArray();
        var selected = _transforms[transformIndex];
        // Decimal UI cannot represent arbitrarily large double weights; retain such a weight exactly and fixed.
        bool editableWeight = selected.Probability < 1e28;
        _maximumWeight = editableWeight ? Math.Max(10, (decimal)selected.Probability) : 10;
        _parameters = editableWeight ? [.. Matrix, new("Вес выбора", t => t.Probability, (t, v) => t.Probability = v)] : Matrix;
        _otherPositiveWeight = _transforms.Where((_, i) => i != transformIndex).Any(t => t.Probability > 0);
        _original = _parameters.Select(p => p.Read(selected)).ToArray();
        Initialize3DAtlas($"Атлас IFS 3D · Преобразование {transformIndex + 1}", _parameters.Select(p => p.Name).ToArray(),
            _original.Select(v => (decimal)v).ToArray(),
            $"Выбрано преобразование {transformIndex + 1} из {_transforms.Length}. Остальные карты фиксированы. " +
            "Выберите любые два коэффициента, переноса или вес для осей; остальные значения фиксированы. Начальные оси — Tx/Ty. " +
            "Сумма весов должна оставаться положительной; нулевой вес отключает выбранную карту. " +
            (editableWeight ? "" : "Очень большой вес этой карты фиксирован и сохраняется точно. ") +
            "Большие коэффициенты могут дать расходящиеся или вырожденные формы. Рендер IFS нормирует каждую форму по её границам. " +
            "Миниатюры используют до 40 000 точек орбиты и текущие камеру/оформление. Число точек основного вида сохраняется. " +
            "Проверьте выбранную форму в основном окне: при большем числе точек плотность и границы могут отличаться. " +
            "Применение возвращает одну карту в редактор одним действием с отменой. Кнопки «Применить»/«Готово» редактора " +
            "фиксируют изменения; закрытие редактора возвращает последнее применённое состояние.", xAxis: 3, yAxis: 7);
    }

    private static Fractal3DState ValidateSource(Fractal3DState source, int index)
    {
        if (source.Kind != Fractal3DKind.Ifs3D || source.IfsTransforms is null || (uint)index >= source.IfsTransforms.Count)
            throw new ArgumentException("Выберите существующее преобразование объёмного IFS.");
        var field = Fractal3DCatalog.Mode(source.Kind).IterationsField;
        if (source.Iterations < field.Minimum || source.Iterations > field.Maximum || source.IfsTransforms.Any(t => t is null ||
            Matrix.Any(p => !double.IsFinite(p.Read(t)) || Math.Abs(p.Read(t)) > 1000) || !double.IsFinite(t.Probability) || t.Probability < 0))
            throw new ArgumentException("Коэффициенты, веса или число точек IFS вне допустимого диапазона.");
        double total = source.IfsTransforms.Sum(t => t.Probability);
        if (!double.IsFinite(total) || total <= 0) throw new ArgumentException("Сумма весов IFS должна быть конечной и положительной.");
        return source;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index < 12 ? (-1000, 1000) : (0, _maximumWeight);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => index < 12 ? .4m : Math.Min(_maximumWeight, Math.Max(1, (decimal)_original[index]));
    protected override ParameterAtlasRange OverviewRange
    {
        get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); }
    }
    protected override string? UnavailableSample(decimal[] point) =>
        point.Length > 12 && point[12] == 0 && !_otherPositiveWeight ? "Нужен хотя бы один положительный вес" : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · до {iterations:N0} точек · по одному варианту";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Ifs3D || state.IfsTransforms is null || state.IfsTransforms.Count != _transforms.Length ||
            point.Length != _parameters.Length || state.IfsTransforms.Any(t => t is null))
            throw new ArgumentException("Список преобразований не соответствует атласу.");
        for (int i = 0; i < _transforms.Length; i++)
            if (i != TransformIndex && (!Matrix.All(p => p.Read(state.IfsTransforms[i]) == p.Read(_transforms[i])) ||
                state.IfsTransforms[i].Probability != _transforms[i].Probability))
                throw new ArgumentException("Другие преобразования изменились после открытия атласа.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum) throw new ArgumentException("Параметр IFS вне допустимого диапазона.");
        }
        if (UnavailableSample(point) is { } reason) throw new ArgumentException(reason);
        var candidate = _transforms[TransformIndex].Clone();
        for (int i = 0; i < point.Length; i++)
            _parameters[i].Write(candidate, point[i] == (decimal)_original[i] ? _original[i] : (double)point[i]);
        state.IfsTransforms[TransformIndex] = candidate;
    }
}
