using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Именованные правила 3×3×3 по горизонтали и целые поколения по вертикали.</summary>
public sealed class CubeRuleAtlasWindow : Fractal3DParameterAtlasWindow
{
    internal sealed record RuleVariant(string Name, uint? Mask);
    private readonly Fractal3DKind _kind;
    private readonly Fractal3DField _generations;
    internal IReadOnlyList<RuleVariant> Rules { get; }

    public CubeRuleAtlasWindow(Fractal3DState source) : base(ValidateSource(source))
    {
        _kind = source.Kind;
        var mode = Fractal3DCatalog.Mode(_kind);
        _generations = mode.IterationsField;
        var rules = new List<RuleVariant>
        {
            new(source.CubeRuleMask.HasValue ? $"Текущее · {CubeRule.Count(source.CubeRuleMask.Value)} кубиков" : "Текущее · классика режима", source.CubeRuleMask)
        };
        Add("Менгер · 20 кубиков", CubeRule.Menger);
        Add("Виксек · 7 кубиков", CubeRule.Vicsek);
        Add("Кантор · 8 кубиков", CubeRule.Cantor);
        Add("Углы и центр · 9 кубиков", CubeRule.Cantor | (1u << 13));
        Add("Оболочка · 26 кубиков", CubeRule.All ^ (1u << 13));
        Add("Пустое правило", 0);
        Add("Полный куб", CubeRule.All);
        Rules = rules.AsReadOnly();
        Initialize3DAtlas($"Атлас правил и поколений · {mode.Definition.Title}", ["Номер правила из списка", "Поколение"],
            [1, source.Iterations],
            "По горизонтали — именованные правила из списка ниже, по вертикали — целые поколения. " +
            "Номер обозначает пункт списка; расстояние между номерами не означает сходство форм. " +
            "Первый пункт точно сохраняет текущее правило, включая отличие своей маски от классической формулы. " +
            "Классика этого режима допускает до 64 поколений, свои правила — до 12. Недоступные сочетания подписаны и не рассчитываются. " +
            "Пустая сцена и полный куб — допустимые варианты. Толщина сохраняется и действует только на классическую форму Виксека/Кантора; " +
            "у своих правил сторона кубика равна трети родительской. Применение меняет правило и поколение, сохраняя камеру, оформление и качество. " +
            "Правило можно вернуть кнопкой «Отменить» в редакторе; поколение задаётся отдельно. Тонкие детали проверяйте в основном окне: " +
            "миниатюры имеют ограниченный размер и бюджет шагов луча.\n\n" +
            string.Join("\n", Rules.Select((r, i) => $"{i + 1}. {r.Name}")));

        void Add(string name, uint mask)
        {
            uint? value = mask == CubeRule.Classic(_kind) ? null : mask;
            if (rules.All(r => r.Mask != value)) rules.Add(new(name, value));
        }
    }

    private static Fractal3DState ValidateSource(Fractal3DState source)
    {
        var mode = Fractal3DCatalog.Mode(source.Kind);
        if (!mode.HasCubeRule) throw new ArgumentException("Нужен режим с кубическим правилом.");
        if (source.CubeRuleMask > CubeRule.All || source.Iterations < mode.IterationsField.Minimum ||
            source.Iterations > (source.CubeRuleMask.HasValue ? 12 : mode.IterationsField.Maximum))
            throw new ArgumentException("Маска или поколение вне допустимого диапазона.");
        return source;
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 0
        ? (1, Rules.Count) : ((decimal)_generations.Minimum, (decimal)_generations.Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => true;
    protected override decimal InitialRangeWidth(int index) => 4;
    protected override ParameterAtlasRange OverviewRange => new(1, Rules.Count, (decimal)_generations.Minimum, (decimal)_generations.Maximum);
    protected override string ComponentValueLabel(int index, decimal value, string format) => index == 0
        ? $"{value}: {Rules[(int)value - 1].Name}" : base.ComponentValueLabel(index, value, format);
    protected override string? UnavailableSample(decimal[] point) =>
        Rules[(int)point[0] - 1].Mask.HasValue && point[1] > 12 ? "Своё правило: до 12 поколений" : null;
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · выбранные правила и поколения";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != _kind || point.Length != 2)
            throw new ArgumentException("Режим не соответствует атласу кубических правил.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum || point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Требуются целые номер правила и поколение в допустимых пределах.");
        }
        if (UnavailableSample(point) is { } reason) throw new ArgumentException(reason);
        state.CubeRuleMask = Rules[(int)point[0] - 1].Mask;
        state.Iterations = (int)point[1];
    }
}
