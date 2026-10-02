namespace FractalExplorerWPF.Models;

public enum LSystem3DColorSource { BranchDepth, DrawingOrder, Generation }

/// <summary>Deterministic, context-free spatial turtle grammar. Angles are local to the turtle.</summary>
public sealed record LSystem3DSettings
{
    public string Axiom { get; init; } = "A";
    public string RulesText { get; init; } = "A → F[&>A]\\[&>A]\\[&>A]";
    public string DrawSymbols { get; init; } = "F";
    public int Generations { get; init; } = 6;
    public double Yaw { get; init; } = 25;
    public double Pitch { get; init; } = 38;
    public double Roll { get; init; } = 120;
    /// <summary>Radius in units of the turtle's initial step, before fitting the complete scene.</summary>
    public double Radius { get; init; } = .13;
    public double BranchTaper { get; init; } = .72;
    public double StepDecay { get; init; } = .83;
    public double Growth { get; init; } = 1;
    public LSystem3DColorSource ColorSource { get; init; }

    public LSystem3DSettings GeometryKey() => this with { Growth = 1, ColorSource = default };

    public void Validate()
    {
        if (Generations is < 0 or > 12) throw new InvalidOperationException("Поколения: от 0 до 12.");
        if (!double.IsFinite(Yaw) || !double.IsFinite(Pitch) || !double.IsFinite(Roll) ||
            Math.Abs(Yaw) > 180 || Math.Abs(Pitch) > 180 || Math.Abs(Roll) > 180)
            throw new InvalidOperationException("Углы поворота: от −180° до 180°.");
        if (!double.IsFinite(Radius) || Radius is < .01 or > .5 ||
            !double.IsFinite(BranchTaper) || BranchTaper is < .3 or > 1 ||
            !double.IsFinite(StepDecay) || StepDecay is < .3 or > 1 ||
            !double.IsFinite(Growth) || Growth is < 0 or > 1)
            throw new InvalidOperationException("Недопустимая толщина, сужение, длина ветвей или стадия роста.");
        if (!Enum.IsDefined(ColorSource)) throw new InvalidOperationException("Неизвестный источник цвета L-системы.");
    }
}

public sealed record LSystem3DPreset(string Name, string Description, LSystem3DSettings Settings);

public static class LSystem3DPresets
{
    public static IReadOnlyList<LSystem3DPreset> All { get; } =
    [
        new("Дерево · три ветви", "Три побега вокруг ствола; наклон раскрывает крону, закрутка распределяет ветви.", new()),
        new("Растение · спиральные побеги", "Побеги чередуются по спирали. Команда ! дополнительно сужает стебель.", new()
        { Axiom = "A", RulesText = "A → F[&>!A]\\F[&>!A]\\A", Generations = 6, Pitch = 55, Roll = 137.5, Radius = .2, BranchTaper = .8, StepDecay = .85 }),
        new("Хвойное дерево", "Ярусы из четырёх ветвей и продолжающаяся верхушка.", new()
        { RulesText = "A → F[&B]\\[&B]\\[&B]\\[&B]FA\nB → F[+>B][->B]>B", Generations = 5, Pitch = 65, Roll = 90, Yaw = 22, Radius = .14, BranchTaper = .78, StepDecay = .82 }),
        new("Пространственный Гильберт", "Непрерывная трубка проходит все узлы кубической решётки. Для классической кривой оставьте углы 90°.", new()
        { Axiom = "A", RulesText = "A → B-F+CFC+F-D&F^D-F+&&CFC+F+B//\nB → A&F^CFB^F^D^^-F-D^|F^B|FC^F^A//\nC → |D^|F^B-F+C^F^A&&FA&F^C+F+B^F^D//\nD → |CFB-F+B|FA&F^A&&FB-F+B|FC//", Generations = 3, Yaw = 90, Pitch = 90, Roll = 90, Radius = .12, BranchTaper = 1, StepDecay = 1, ColorSource = LSystem3DColorSource.DrawingOrder }),
        new("Кубическое ветвление", "Шесть направлений в пространстве и уменьшающиеся дочерние ветви.", new()
        { RulesText = "A → F[+>A][->A][&>A][^>A]>A", Generations = 5, Yaw = 90, Pitch = 90, Roll = 90, Radius = .1, BranchTaper = .65, StepDecay = .55 }),
        new("Трубчатая спираль", "Каждый шаг наклоняет и закручивает черепаху; число поколений удваивает длину пути.", new()
        { Axiom = "F", RulesText = "F → F&\\F", Generations = 6, Yaw = 0, Pitch = 30, Roll = 4, Radius = .15, BranchTaper = 1, ColorSource = LSystem3DColorSource.DrawingOrder })
    ];
}
