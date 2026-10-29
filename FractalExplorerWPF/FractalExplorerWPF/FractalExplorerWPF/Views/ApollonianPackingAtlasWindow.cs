using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Поколения существующей упаковки и именованные источники окраски при общей камере.</summary>
public sealed class ApollonianPackingAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly Fractal3DMode _mode;
    internal IReadOnlyList<Fractal3DColoringMode> Colorings { get; }

    public ApollonianPackingAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _mode = Fractal3DCatalog.Mode(source.Kind);
        Colorings = Array.AsReadOnly(Enum.GetValues<Fractal3DColoringMode>().Where(_mode.HasColoringMode).ToArray());
        int coloring = Colorings.ToList().IndexOf(source.ColoringMode) + 1;
        Initialize3DAtlas("Атлас поколений и окраски · Аполлонова упаковка сфер",
            ["Поколение сфер", "Окраска · номер из списка"], [source.Iterations, coloring],
            "По горизонтали — целое число поколений, по вертикали — именованные источники окраски из списка ниже. " +
            "В этой модели геометрия задаётся только поколением: начальная конфигурация касающихся сфер фиксирована. " +
            "Номер окраски обозначает пункт списка. Камера, палитра, её масштаб/сдвиг, повтор и стиль освещения одинаковы для всех ячеек. " +
            "При «Материале» используется цвет поверхности; другие источники показывают геометрические данные сфер или рендера. " +
            "Применение меняет только поколение и источник окраски. Некоторые источники могут выглядеть похоже при текущем оформлении. " +
            "Выбранное поколение рассчитывается полностью; миниатюры имеют ограниченные размер и качество, " +
            "поэтому мелкие сферы проверяйте в основном окне.\n\n" +
            string.Join("\n", Colorings.Select((c, i) => $"{i + 1}. {_mode.ColoringModeName(c)}")));
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.ApollonianPacking)
            throw new ArgumentException("Нужна аполлонова упаковка сфер.");
        var mode = Fractal3DCatalog.Mode(source.Kind);
        if (source.Iterations < mode.IterationsField.Minimum || source.Iterations > mode.IterationsField.Maximum ||
            !Enum.IsDefined(source.ColoringMode) || !mode.HasColoringMode(source.ColoringMode))
            throw new ArgumentException("Поколение или источник окраски вне допустимого диапазона.");
        return source;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 0
        ? ((decimal)_mode.IterationsField.Minimum, (decimal)_mode.IterationsField.Maximum) : (1, Colorings.Count);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => true;
    protected override decimal InitialRangeWidth(int index) => 4;
    protected override ParameterAtlasRange OverviewRange => new((decimal)_mode.IterationsField.Minimum,
        (decimal)_mode.IterationsField.Maximum, 1, Colorings.Count);
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 1
        ? $"{value}: {_mode.ColoringModeName(Colorings[(int)value - 1])}" : base.ComponentValueLabel(index, value, format);
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · выбранные поколения и окраска";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.ApollonianPacking || point.Length != 2)
            throw new ArgumentException("Нужны поколение и источник окраски аполлоновой упаковки сфер.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Требуются целые поколение и номер окраски в допустимых пределах.");
        }
        state.Iterations = (int)point[0];
        state.ColoringMode = Colorings[(int)point[1] - 1];
    }
}
