using System.Numerics;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

public readonly record struct LSystem3DSegment(Vector3 Start, Vector3 End, float Radius, int BranchDepth, int Generation);

/// <summary>Complete geometry is fitted once; growth and camera changes keep the same coordinates.</summary>
public sealed class LSystem3DGeometry
{
    public const int MaximumSymbols = 1_000_000;
    public const int MaximumSegments = 100_000;
    public required LSystem3DSegment[] Segments { get; init; }
    public required Vector4[] Nodes { get; init; }
    public required Vector4[] Capsules { get; init; }
    public required int SymbolCount { get; init; }
    public required int MaximumBranchDepth { get; init; }

    private readonly record struct Token(char Symbol, int Generation);
    private readonly record struct Turtle(Vector3 Position, Quaternion Rotation, float Step, float Radius, int Depth);

    public static LSystem3DGeometry Build(LSystem3DSettings settings, CancellationToken token)
    {
        settings.Validate();
        string axiom = LSystemEngine.NormalizeSymbols(settings.Axiom);
        if (axiom.Length == 0) throw new InvalidOperationException("Аксиома не может быть пустой.");
        if (axiom.Length > MaximumSymbols) throw new InvalidOperationException("Аксиома слишком длинная.");
        var drawing = LSystemEngine.NormalizeSymbols(settings.DrawSymbols).ToHashSet();
        if (drawing.Count == 0 || drawing.Any(c => "+-&^\\/|[]!><f".Contains(c)))
            throw new InvalidOperationException("Укажите рисующие буквы, например F или FG. Команды черепахи нельзя назначить рисующими.");
        var rules = LSystemEngine.ParseRules(settings.RulesText, spatialCommands: true);
        var symbols = axiom.Select(c => new Token(c, 0)).ToList();
        for (int generation = 1; generation <= settings.Generations; generation++)
        {
            token.ThrowIfCancellationRequested();
            var next = new List<Token>();
            for (int i = 0; i < symbols.Count; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                Token current = symbols[i];
                string? replacement = rules.GetValueOrDefault(current.Symbol);
                if ((long)next.Count + (replacement?.Length ?? 1) > MaximumSymbols)
                    throw new InvalidOperationException($"Поколение {generation} превышает лимит {MaximumSymbols:N0} символов. Уменьшите число поколений.");
                if (replacement is null) next.Add(current);
                else foreach (char c in replacement) next.Add(new(c, generation));
            }
            symbols = next;
        }

        var segments = new List<LSystem3DSegment>();
        var stack = new Stack<Turtle>();
        // ABOP frame: heading +Y, left −X, up +Z. Quaternion stores the entire local frame.
        var turtle = new Turtle(Vector3.Zero, Quaternion.Identity, 1, (float)settings.Radius, 0);
        int maxDepth = 0;
        for (int i = 0; i < symbols.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            Token current = symbols[i];
            char c = current.Symbol;
            if (drawing.Contains(c) || c == 'f')
            {
                Vector3 end = turtle.Position + Vector3.Transform(Vector3.UnitY, turtle.Rotation) * turtle.Step;
                if (!float.IsFinite(end.X) || !float.IsFinite(end.Y) || !float.IsFinite(end.Z))
                    throw new InvalidOperationException("Координаты вышли за допустимый диапазон.");
                if (c != 'f')
                {
                    if (segments.Count == MaximumSegments)
                        throw new InvalidOperationException($"Более {MaximumSegments:N0} ветвей. Уменьшите число поколений.");
                    segments.Add(new(turtle.Position, end, turtle.Radius, turtle.Depth, current.Generation));
                }
                turtle = turtle with { Position = end };
                continue;
            }
            switch (c)
            {
                case '+': Turn(Vector3.UnitZ, settings.Yaw); break;
                case '-': Turn(Vector3.UnitZ, -settings.Yaw); break;
                case '&': Turn(-Vector3.UnitX, settings.Pitch); break;
                case '^': Turn(-Vector3.UnitX, -settings.Pitch); break;
                case '\\': Turn(Vector3.UnitY, settings.Roll); break;
                case '/': Turn(Vector3.UnitY, -settings.Roll); break;
                case '|': Turn(Vector3.UnitZ, 180); break;
                case '[':
                    stack.Push(turtle);
                    turtle = turtle with { Depth = turtle.Depth + 1, Radius = Math.Max(.001f, turtle.Radius * (float)settings.BranchTaper) };
                    maxDepth = Math.Max(maxDepth, turtle.Depth);
                    break;
                case ']':
                    if (!stack.TryPop(out turtle)) throw new InvalidOperationException($"Лишняя ] в позиции {i + 1} развёрнутой строки.");
                    break;
                case '!': turtle = turtle with { Radius = Math.Max(.001f, turtle.Radius * (float)settings.BranchTaper) }; break;
                case '>': turtle = turtle with { Step = Math.Max(.001f, turtle.Step * (float)settings.StepDecay) }; break;
                case '<': turtle = turtle with { Step = Math.Min(10000f, turtle.Step / (float)settings.StepDecay) }; break;
            }
        }
        if (stack.Count != 0) throw new InvalidOperationException("Остались незакрытые [ в развёрнутой строке.");
        if (segments.Count == 0) throw new InvalidOperationException("Нет ветвей: проверьте рисующие символы и правила.");
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var s in segments)
        {
            min = Vector3.Min(min, Vector3.Min(s.Start, s.End) - new Vector3(s.Radius));
            max = Vector3.Max(max, Vector3.Max(s.Start, s.End) + new Vector3(s.Radius));
        }
        Vector3 center = (min + max) * .5f;
        float scale = 2f / Math.Max((max - min).X, Math.Max((max - min).Y, (max - min).Z));
        var fitted = segments.Select(s => s with { Start = (s.Start - center) * scale, End = (s.End - center) * scale, Radius = s.Radius * scale }).ToArray();
        var capsules = new Vector4[fitted.Length * 3];
        for (int i = 0; i < fitted.Length; i++)
        {
            var s = fitted[i];
            capsules[i * 3] = new(s.Start, s.Radius);
            capsules[i * 3 + 1] = new(s.End, i);
            capsules[i * 3 + 2] = new(s.BranchDepth / (float)Math.Max(maxDepth, 1),
                i / (float)Math.Max(fitted.Length - 1, 1), s.Generation / (float)Math.Max(settings.Generations, 1), 0);
        }
        // Preorder threaded BVH: each node stores the index after its entire subtree, no GPU stack.
        int[] indices = Enumerable.Range(0, fitted.Length).ToArray();
        var nodes = new List<Vector4>(fitted.Length * 4);
        BuildNode(0, indices.Length);
        return new() { Segments = fitted, Capsules = capsules, Nodes = nodes.ToArray(), SymbolCount = symbols.Count, MaximumBranchDepth = maxDepth };

        void Turn(Vector3 localAxis, double degrees)
        {
            Quaternion delta = Quaternion.CreateFromAxisAngle(localAxis, (float)(degrees * Math.PI / 180));
            turtle = turtle with { Rotation = Quaternion.Normalize(turtle.Rotation * delta) };
        }
        void BuildNode(int start, int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int i = start; i < start + count; i++)
            {
                var s = fitted[indices[i]];
                lo = Vector3.Min(lo, Vector3.Min(s.Start, s.End) - new Vector3(s.Radius));
                hi = Vector3.Max(hi, Vector3.Max(s.Start, s.End) + new Vector3(s.Radius));
            }
            int node = nodes.Count;
            nodes.Add(default); nodes.Add(new(hi, count == 1 ? indices[start] : -1));
            if (count > 1)
            {
                Vector3 size = hi - lo;
                int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                Array.Sort(indices, start, count, Comparer<int>.Create((a, b) =>
                    (fitted[a].Start[axis] + fitted[a].End[axis]).CompareTo(fitted[b].Start[axis] + fitted[b].End[axis])));
                int left = count / 2;
                BuildNode(start, left); BuildNode(start + left, count - left);
            }
            nodes[node] = new(lo, nodes.Count / 2);
        }
    }
}
