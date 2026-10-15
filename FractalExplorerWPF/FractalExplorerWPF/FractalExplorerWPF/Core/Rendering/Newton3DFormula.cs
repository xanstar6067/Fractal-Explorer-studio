using System.Collections.Concurrent;
using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Общий с Ньютоном+ синтаксис, символьная производная и обнаружение корней.</summary>
internal sealed class Newton3DFormula
{
    private static readonly ConcurrentDictionary<string, Newton3DFormula> Programs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<double, Newton3DRootCatalog> _rootCatalogs = new();
    public ExpressionNode Expression { get; }
    public ExpressionNode Derivative { get; }
    public CompiledComplexExpression Compiled { get; }
    public CompiledComplexExpression CompiledDerivative { get; }

    private Newton3DFormula(string text)
    {
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '(' && ++depth > 32) throw new ArgumentException("Слишком большая вложенность формулы (не более 32).");
            if (c == ')') depth--;
        }
        var tokens = new Tokenizer(text).Tokenize();
        if (tokens.Count > 256) throw new ArgumentException("Слишком сложная формула (не более 256 токенов).");
        // Bound recursion before entering the shared recursive parser (including chains of unary signs).
        int run = 0;
        foreach (var token in tokens)
        {
            run = token.Type == TokenType.Operator ? run + 1 : 0;
            if (run > 32) throw new ArgumentException("Слишком длинная цепочка операторов.");
        }
        Expression = new Parser(tokens).Parse().Simplify();
        CheckTree(Expression);
        Derivative = Expression.Differentiate("z").Simplify();
        CheckTree(Derivative);
        if (Derivative is NumberNode { Value: var value } && value == Complex.Zero)
            throw new ArgumentException("Производная равна нулю: задайте функцию, зависящую от z.");
        Compiled = CompiledComplexExpression.Compile(Expression);
        CompiledDerivative = CompiledComplexExpression.Compile(Derivative);
        if (Compiled.InstructionCount > 512 || CompiledDerivative.InstructionCount > 512)
            throw new ArgumentException("Формула или её производная превышает 512 операций.");
    }

    public static Newton3DFormula Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512) throw new ArgumentException("Формула: от 1 до 512 символов.");
        try { return Programs.GetOrAdd(text, static s => new(s)); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
        { throw new ArgumentException($"Ошибка формулы: {exception.Message}", exception); }
    }

    private static void CheckTree(ExpressionNode node, int depth = 0)
    {
        if (depth > 48) throw new ArgumentException("Слишком глубокое дерево выражения.");
        switch (node)
        {
            case NumberNode number when !double.IsFinite(number.Value.Real) || !double.IsFinite(number.Value.Imaginary) ||
                Math.Abs(number.Value.Real) > 1e30 || Math.Abs(number.Value.Imaginary) > 1e30:
                throw new ArgumentException("Константа формулы выходит за диапазон вычислений GPU.");
            case VariableNode variable when variable.Name != "z":
                throw new ArgumentException($"Неизвестная переменная '{variable.Name}'. Используйте z, i, pi, e.");
            case UnaryOpNode unary: CheckTree(unary.Operand, depth + 1); break;
            case BinaryOpNode binary:
                CheckTree(binary.Left, depth + 1); CheckTree(binary.Right, depth + 1); break;
            case FunctionNode function: CheckTree(function.Argument, depth + 1); break;
        }
    }

    public static Newton3DSettings Prepare(Newton3DSettings settings, CancellationToken token = default)
    {
        settings.Validate(); token.ThrowIfCancellationRequested();
        if (settings.Formula is null) return settings;
        var program = Parse(settings.Formula);
        if (NewtonRootFinder.TryGetPolynomialCoefficients(program.Expression, out var coefficients) && coefficients.Length > 33)
            throw new ArgumentException("Для 3D поддерживаются полиномы степени не выше 32.");
        if (settings.Roots is not null)
        {
            foreach (var root in settings.Roots.Values)
            {
                var residual = program.Compiled.Evaluate(root.Value);
                if (!double.IsFinite(residual.Magnitude) || residual.Magnitude > 1e-3)
                    throw new ArgumentException("Сохранённые корни не соответствуют формуле. Примените формулу заново.");
            }
            return settings;
        }
        IReadOnlyList<Complex> roots;
        if (program._rootCatalogs.TryGetValue(settings.RootSearchRadius, out var cached))
            return settings with { Roots = cached, PlusRoot = Math.Clamp(settings.PlusRoot, 0, cached.Values.Count - 1),
                MinusRoot = Math.Clamp(settings.MinusRoot, 0, cached.Values.Count - 1) };
        if (NewtonRootFinder.TryFindPolynomialRoots(program.Expression, 1e-7, out roots, out int degree))
        {
            if (degree > 32) throw new ArgumentException("Для 3D поддерживаются полиномы степени не выше 32.");
        }
        else
        {
            // The shared solver is synchronous; check cancellation at every expression evaluation.
            Complex Evaluate(Complex z) { token.ThrowIfCancellationRequested(); return program.Compiled.Evaluate(z); }
            Complex Differentiate(Complex z) { token.ThrowIfCancellationRequested(); return program.CompiledDerivative.Evaluate(z); }
            roots = NewtonRootFinder.FindAdaptiveRoots(Evaluate, Differentiate, 0, 0, settings.RootSearchRadius, 1e-7);
        }
        token.ThrowIfCancellationRequested();
        if (roots.Count == 0) throw new ArgumentException("Корни не найдены. Измените формулу или увеличьте радиус поиска.");
        if (roots.Count > 32) throw new ArgumentException("Найдено больше 32 корней. Уменьшите радиус поиска.");
        var catalog = new Newton3DRootCatalog(roots.Select(z => new Newton3DRoot(z.Real, z.Imaginary)).ToArray());
        program._rootCatalogs.TryAdd(settings.RootSearchRadius, catalog);
        return settings with { Roots = catalog,
            PlusRoot = Math.Clamp(settings.PlusRoot, 0, roots.Count - 1), MinusRoot = Math.Clamp(settings.MinusRoot, 0, roots.Count - 1) };
    }
}
