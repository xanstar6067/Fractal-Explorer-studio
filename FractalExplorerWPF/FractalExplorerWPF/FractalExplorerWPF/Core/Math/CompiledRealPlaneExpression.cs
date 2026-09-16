using System.Linq.Expressions;
using LinqExpression = System.Linq.Expressions.Expression;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Тот же разбор и символьное дифференцирование, что у комплексных функций,
/// но x/y — независимые double. Делегат компилируется один раз, без словарей на пиксель.
/// </summary>
internal static class CompiledRealPlaneExpression
{
    public static ExpressionNode Parse(string text, bool polynomialOnly = false)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048)
            throw new InvalidOperationException("Введите выражение длиной от 1 до 2048 символов.");
        ExpressionNode node = new Parser(new Tokenizer(text).Tokenize()).Parse().Simplify();
        _ = Compile(node); // Проверяет переменные, функции и вещественные константы.
        if (polynomialOnly && PolynomialDegree(node) is < 0 or > 32)
            throw new InvalidOperationException("Векторное поле: требуется полином от x и y степени не выше 32; деление — только на ненулевую константу.");
        return node;
    }

    public static Func<double, double, double> Compile(ExpressionNode node)
    {
        var x = LinqExpression.Parameter(typeof(double), "x");
        var y = LinqExpression.Parameter(typeof(double), "y");
        int count = 0;
        return LinqExpression.Lambda<Func<double, double, double>>(Build(node), x, y).Compile();

        LinqExpression Build(ExpressionNode current)
        {
            if (++count > 12000) throw new InvalidOperationException("Выражение или его производная слишком сложны.");
            return current switch
            {
                NumberNode n when n.Value.Imaginary == 0 && double.IsFinite(n.Value.Real) => LinqExpression.Constant(n.Value.Real),
                VariableNode { Name: "x" } => x,
                VariableNode { Name: "y" } => y,
                VariableNode { Name: "pi" } => LinqExpression.Constant(Math.PI),
                VariableNode { Name: "e" } => LinqExpression.Constant(Math.E),
                UnaryOpNode u => u.Operator == "-" ? LinqExpression.Negate(Build(u.Operand)) : Build(u.Operand),
                BinaryOpNode b => Binary(b),
                FunctionNode f => LinqExpression.Call(typeof(Math).GetMethod(f.Name switch
                {
                    "sin" => "Sin", "cos" => "Cos", "tan" => "Tan", "asin" => "Asin", "acos" => "Acos", "atan" => "Atan",
                    "sinh" => "Sinh", "cosh" => "Cosh", "tanh" => "Tanh", "exp" => "Exp", "log" => "Log", "sqrt" => "Sqrt",
                    _ => throw new InvalidOperationException($"Функция {f.Name} не поддерживается.")
                }, [typeof(double)])!, Build(f.Argument)),
                _ => throw new InvalidOperationException("Допустимы только вещественные числа, x, y, pi, e и вещественные функции. Умножение: x*(y+1).")
            };
        }

        LinqExpression Binary(BinaryOpNode b)
        {
            LinqExpression left = Build(b.Left), right = Build(b.Right);
            return b.Operator switch
            {
                "+" => LinqExpression.Add(left, right), "-" => LinqExpression.Subtract(left, right),
                "*" => LinqExpression.Multiply(left, right), "/" => LinqExpression.Divide(left, right),
                "^" => LinqExpression.Power(left, right),
                _ => throw new InvalidOperationException("Неизвестная операция.")
            };
        }
    }

    private static int PolynomialDegree(ExpressionNode node) => node switch
    {
        NumberNode => 0,
        VariableNode { Name: "pi" or "e" } => 0,
        VariableNode { Name: "x" or "y" } => 1,
        UnaryOpNode u => PolynomialDegree(u.Operand),
        BinaryOpNode b when b.Operator is "+" or "-" && PolynomialDegree(b.Left) >= 0 && PolynomialDegree(b.Right) >= 0 =>
            Math.Max(PolynomialDegree(b.Left), PolynomialDegree(b.Right)),
        BinaryOpNode { Operator: "*" } b when PolynomialDegree(b.Left) >= 0 && PolynomialDegree(b.Right) >= 0 =>
            Math.Min(33, PolynomialDegree(b.Left) + PolynomialDegree(b.Right)),
        BinaryOpNode { Operator: "/", Right: NumberNode n } b when n.Value.Real != 0 => PolynomialDegree(b.Left),
        BinaryOpNode { Operator: "^", Right: NumberNode n } b when n.Value.Real >= 0 && n.Value.Real <= 32 &&
            n.Value.Real == Math.Truncate(n.Value.Real) && PolynomialDegree(b.Left) >= 0 =>
            Math.Min(33, PolynomialDegree(b.Left) * (int)n.Value.Real),
        _ => -1
    };
}
