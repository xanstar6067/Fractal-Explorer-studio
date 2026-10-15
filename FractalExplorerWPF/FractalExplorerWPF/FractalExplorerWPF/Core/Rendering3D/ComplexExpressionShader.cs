using System.Globalization;
using System.Text;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Генерирует только разрешённые операции из AST общего парсера; исходный текст не вставляется в HLSL.</summary>
internal static class ComplexExpressionShader
{
    internal static string Build(Newton3DFormula formula) =>
        EmitFunction("FormulaValue", formula.Expression) + EmitFunction("FormulaDerivative", formula.Derivative);

    private static string EmitFunction(string name, ExpressionNode expression)
    {
        var body = new StringBuilder();
        var emitted = new Dictionary<ExpressionNode, string>(ReferenceEqualityComparer.Instance);
        string result = Emit(expression);
        return $"float2 {name}(float2 z) {{\n{body}return {result};\n}}\n";

        string Emit(ExpressionNode node)
        {
            if (emitted.TryGetValue(node, out string? cached)) return cached;
            if (emitted.Count >= 512) throw new ArgumentException("Формула или её производная слишком сложна для GPU (512 операций).");
            string value = node switch
            {
                NumberNode n => $"float2({Number(n.Value.Real)},{Number(n.Value.Imaginary)})",
                VariableNode { Name: "z" } => "z",
                UnaryOpNode u => u.Operator == "-" ? $"(-{Emit(u.Operand)})" : Emit(u.Operand),
                BinaryOpNode b => Binary(b),
                FunctionNode f => $"Cx_{f.Name}({Emit(f.Argument)})",
                _ => throw new ArgumentException("Неподдерживаемый элемент формулы GPU.")
            };
            string temporary = $"t{emitted.Count}";
            body.Append("float2 ").Append(temporary).Append(" = ").Append(value).AppendLine(";");
            emitted.Add(node, temporary);
            return temporary;
        }

        string Binary(BinaryOpNode b)
        {
            string left = Emit(b.Left), right = Emit(b.Right);
            return b.Operator switch
            {
                "+" or "-" => $"({left}{b.Operator}{right})",
                "*" => $"Cx_Mul({left},{right})",
                "/" => $"Cx_Div({left},{right})",
                "^" when b.Right is NumberNode n && n.Value.Imaginary == 0 &&
                    n.Value.Real == Math.Truncate(n.Value.Real) && Math.Abs(n.Value.Real) <= 128 =>
                    $"Cx_IntPow({left},{(int)n.Value.Real})",
                "^" => $"Cx_Pow({left},{right})",
                _ => throw new ArgumentException("Неизвестная операция GPU.")
            };
        }
    }

    private static string Number(double value)
    {
        string literal = ((float)value).ToString("R", CultureInfo.InvariantCulture);
        return literal.Contains('.') || literal.Contains('E') ? literal : literal + ".0";
    }
}
