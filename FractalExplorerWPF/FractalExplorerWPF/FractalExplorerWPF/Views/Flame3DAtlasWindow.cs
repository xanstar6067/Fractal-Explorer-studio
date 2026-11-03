using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Flame3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly Flame3DSettings _settings;
    private readonly int _iterations;
    private readonly double[] _original;
    private readonly Flame3DVariation[] _variations = Enum.GetValues<Flame3DVariation>();
    public int TransformIndex { get; }
    protected override int PreviewIterationCount => Math.Clamp(_iterations, 50_000, 100_000);

    public Flame3DAtlasWindow(Fractal3DState source, int transformIndex = 0) : base(ValidateSource(source, transformIndex))
    {
        _settings = source.Flame.Clone(); _iterations = source.Iterations; TransformIndex = transformIndex;
        var t = _settings.Transforms[transformIndex];
        _original = [Array.IndexOf(_variations, t.Variation) + 1, t.Amount, t.Weight, t.ColorSpeed, t.Map.Tx, t.Map.Ty, t.Map.Tz];
        Initialize3DAtlas($"Атлас Flame 3D · Слой {transformIndex + 1}",
            ["Вариация · номер", "Сила вариации", "Вес выбора", "Смешивание цвета", "Перенос Tx", "Перенос Ty", "Перенос Tz"],
            _original.Select(v => (decimal)v).ToArray(),
            "Выберите две оси; остальные параметры фиксированы. Номера вариаций обозначают пункты списка ниже. " +
            "Меняется только выбранный слой; матрица 3×3, цвет слоя и остальные слои сохраняются. " +
            "Сила 0 даёт исходную аффинную карту. Нулевой вес отключает слой; сумма весов должна быть положительной. " +
            "Миниатюры используют до 100 000 точек с исходными seed, прогревом, камерой и тональной коррекцией. " +
            "Плотность и мелкие детали проверяйте в основном окне с его числом точек. " +
            "Выбор возвращается в редактор одним действием с отменой; «Применить»/«Готово» редактора фиксируют результат.\n\n" +
            string.Join("\n", _variations.Select((v, i) => $"{i + 1}. {Flame3DPresets.Name(v)}")));
    }

    private static Fractal3DState ValidateSource(Fractal3DState source, int index)
    {
        if (source.Kind != Fractal3DKind.Flame3D || source.Flame is null)
            throw new ArgumentException("Нужен объёмный Flame.");
        try { source.Flame.Validate(); } catch (InvalidOperationException ex) { throw new ArgumentException(ex.Message, ex); }
        if ((uint)index >= source.Flame.Transforms.Count || source.Iterations is < 50_000 or > 10_000_000)
            throw new ArgumentException("Выберите существующий слой Flame с допустимым числом точек.");
        return source;
    }
    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index switch
    { 0 => (1, _variations.Length), 1 or 3 => (0, 1), 2 => (0, 1000), _ => (-1000, 1000) };
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index == 0;
    protected override decimal InitialRangeWidth(int index) => index == 0 ? 2 : index == 2 ? 1 : .4m;
    protected override ParameterAtlasRange OverviewRange
    { get { var x = ComponentBounds(XAxisIndex); var y = ComponentBounds(YAxisIndex); return new(x.Minimum, x.Maximum, y.Minimum, y.Maximum); } }
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 0
        ? $"{value}: {Flame3DPresets.Name(_variations[(int)value - 1])}" : base.ComponentValueLabel(index, value, format);
    protected override string? UnavailableSample(decimal[] point) => point[2] == 0 &&
        !_settings.Transforms.Where((_, i) => i != TransformIndex).Any(t => t.Weight > 0) ? "Нужен положительный вес" : null;
    protected override string PreviewStatus(int completed, int total, int iterations) => $"{completed} / {total} · до {iterations:N0} точек · по одному варианту";
    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Flame3D || state.Flame is null || state.Flame.Transforms.Count != _settings.Transforms.Count || point.Length != 7)
            throw new ArgumentException("Список слоёв не соответствует атласу.");
        for (int i = 0; i < point.Length; i++)
        {
            var b = ComponentBounds(i);
            if (point[i] < b.Minimum || point[i] > b.Maximum || i == 0 && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Параметр Flame вне диапазона или требует целого номера.");
        }
        for (int i = 0; i < _settings.Transforms.Count; i++)
            if (i != TransformIndex && !Same(state.Flame.Transforms[i], _settings.Transforms[i]))
                throw new ArgumentException("Другие слои изменились после открытия атласа.");
        if (UnavailableSample(point) is { } reason) throw new ArgumentException(reason);
        double Value(int i) => point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        var candidate = state.Flame.Clone(); var t = _settings.Transforms[TransformIndex].Clone();
        t.Variation = _variations[(int)point[0] - 1]; t.Amount = Value(1); t.Weight = Value(2); t.ColorSpeed = Value(3);
        t.Map.Tx = Value(4); t.Map.Ty = Value(5); t.Map.Tz = Value(6);
        candidate.Transforms[TransformIndex] = t; candidate.Validate(); state.Flame = candidate;
    }
    private static bool Same(Flame3DTransform a, Flame3DTransform b) => a is not null && a.Map is not null &&
        a.Weight == b.Weight && a.Variation == b.Variation && a.Amount == b.Amount && a.ColorSpeed == b.ColorSpeed &&
        a.Color == b.Color && a.Map.Probability == b.Map.Probability && Flame3DSettings.Values(a.Map).SequenceEqual(Flame3DSettings.Values(b.Map));
}
