using System.Numerics;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Компактное постфиксное представление комплексного выражения для горячего цикла рендера.
/// Не использует словари, рекурсию AST и выделения памяти для обычных формул.
/// </summary>
internal sealed partial class CompiledComplexExpression
{
    private const int StackAllocationLimit = 128;
    private readonly Instruction[] _instructions;
    private readonly int _maxStackDepth;

    private CompiledComplexExpression(Instruction[] instructions, int maxStackDepth)
    {
        _instructions = instructions;
        _maxStackDepth = Math.Max(1, maxStackDepth);
        InitializePerturbationLayout(out _left, out _right, out _auxiliary, out _slotCount);
    }

    public int InstructionCount => _instructions.Length;
    public int MaxStackDepth => _maxStackDepth;

    public static CompiledComplexExpression Compile(ExpressionNode expression)
    {
        var compiler = new Compiler();
        compiler.Emit(expression);
        if (compiler.StackDepth != 1)
            throw new InvalidOperationException("Некорректный стек скомпилированного выражения.");
        return new CompiledComplexExpression(compiler.Instructions.ToArray(), compiler.MaxStackDepth);
    }

    public Complex Evaluate(Complex z)
    {
        Span<Complex> stack = _maxStackDepth <= StackAllocationLimit
            ? stackalloc Complex[_maxStackDepth]
            : new Complex[_maxStackDepth];
        int top = 0;

        for (int index = 0; index < _instructions.Length; index++)
        {
            ref readonly Instruction instruction = ref _instructions[index];
            switch (instruction.OpCode)
            {
                case OpCode.PushConstant:
                    stack[top++] = instruction.Operand;
                    break;
                case OpCode.PushZ:
                    stack[top++] = z;
                    break;
                case OpCode.Negate:
                    stack[top - 1] = -stack[top - 1];
                    break;
                case OpCode.Add:
                    stack[top - 2] += stack[top - 1];
                    top--;
                    break;
                case OpCode.Subtract:
                    stack[top - 2] -= stack[top - 1];
                    top--;
                    break;
                case OpCode.Multiply:
                    stack[top - 2] *= stack[top - 1];
                    top--;
                    break;
                case OpCode.Divide:
                    stack[top - 2] /= stack[top - 1];
                    top--;
                    break;
                case OpCode.Power:
                    stack[top - 2] = Complex.Pow(stack[top - 2], stack[top - 1]);
                    top--;
                    break;
                case OpCode.PowerConstant:
                    stack[top - 1] = Pow(stack[top - 1], instruction.Operand);
                    break;
                case OpCode.Sin:
                    stack[top - 1] = Complex.Sin(stack[top - 1]);
                    break;
                case OpCode.Cos:
                    stack[top - 1] = Complex.Cos(stack[top - 1]);
                    break;
                case OpCode.Tan:
                    stack[top - 1] = Complex.Tan(stack[top - 1]);
                    break;
                case OpCode.Asin:
                    stack[top - 1] = Complex.Asin(stack[top - 1]);
                    break;
                case OpCode.Acos:
                    stack[top - 1] = Complex.Acos(stack[top - 1]);
                    break;
                case OpCode.Atan:
                    stack[top - 1] = Complex.Atan(stack[top - 1]);
                    break;
                case OpCode.Sinh:
                    stack[top - 1] = Complex.Sinh(stack[top - 1]);
                    break;
                case OpCode.Cosh:
                    stack[top - 1] = Complex.Cosh(stack[top - 1]);
                    break;
                case OpCode.Tanh:
                    stack[top - 1] = Complex.Tanh(stack[top - 1]);
                    break;
                case OpCode.Exp:
                    stack[top - 1] = Complex.Exp(stack[top - 1]);
                    break;
                case OpCode.Log:
                    stack[top - 1] = Complex.Log(stack[top - 1]);
                    break;
                case OpCode.Sqrt:
                    stack[top - 1] = Complex.Sqrt(stack[top - 1]);
                    break;
                default:
                    throw new InvalidOperationException($"Неизвестная инструкция {instruction.OpCode}.");
            }
        }

        return stack[0];
    }

    private static Complex Pow(Complex value, Complex exponent)
    {
        if (exponent.Imaginary != 0) return Complex.Pow(value, exponent);
        double realExponent = exponent.Real;
        int integerExponent = (int)Math.Round(realExponent);
        if (Math.Abs(realExponent - integerExponent) > 1e-12 || integerExponent is < -64 or > 64)
            return Complex.Pow(value, realExponent);
        if (integerExponent == 0) return Complex.One;

        bool reciprocal = integerExponent < 0;
        int remaining = Math.Abs(integerExponent);
        Complex factor = value;
        Complex result = Complex.One;
        while (remaining > 0)
        {
            if ((remaining & 1) != 0) result *= factor;
            remaining >>= 1;
            if (remaining > 0) factor *= factor;
        }
        return reciprocal ? Complex.One / result : result;
    }

    private enum OpCode : byte
    {
        PushConstant,
        PushZ,
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Power,
        PowerConstant,
        Sin,
        Cos,
        Tan,
        Asin,
        Acos,
        Atan,
        Sinh,
        Cosh,
        Tanh,
        Exp,
        Log,
        Sqrt
    }

    private readonly record struct Instruction(OpCode OpCode, Complex Operand = default);

    private sealed class Compiler
    {
        public List<Instruction> Instructions { get; } = [];
        public int StackDepth { get; private set; }
        public int MaxStackDepth { get; private set; }

        public void Emit(ExpressionNode node)
        {
            switch (node)
            {
                case NumberNode number:
                    Add(new Instruction(OpCode.PushConstant, number.Value), 1);
                    return;
                case VariableNode { Name: "z" }:
                    Add(new Instruction(OpCode.PushZ), 1);
                    return;
                case VariableNode variable:
                    Add(new Instruction(OpCode.PushConstant, ConstantValue(variable.Name)), 1);
                    return;
                case UnaryOpNode unary:
                    Emit(unary.Operand);
                    if (unary.Operator == "-") Add(new Instruction(OpCode.Negate), 0);
                    else if (unary.Operator != "+") throw new InvalidOperationException($"Оператор '{unary.Operator}' не поддерживается байткодом.");
                    return;
                case BinaryOpNode binary:
                    EmitBinary(binary);
                    return;
                case FunctionNode function:
                    Emit(function.Argument);
                    Add(new Instruction(FunctionOpCode(function.Name)), 0);
                    return;
                default:
                    throw new InvalidOperationException($"Узел {node.GetType().Name} не поддерживается байткодом.");
            }
        }

        private void EmitBinary(BinaryOpNode binary)
        {
            Emit(binary.Left);
            if (binary.Operator == "^" && binary.Right is NumberNode exponent)
            {
                Add(new Instruction(OpCode.PowerConstant, exponent.Value), 0);
                return;
            }

            Emit(binary.Right);
            OpCode opCode = binary.Operator switch
            {
                "+" => OpCode.Add,
                "-" => OpCode.Subtract,
                "*" => OpCode.Multiply,
                "/" => OpCode.Divide,
                "^" => OpCode.Power,
                _ => throw new InvalidOperationException($"Оператор '{binary.Operator}' не поддерживается байткодом.")
            };
            Add(new Instruction(opCode), -1);
        }

        private void Add(Instruction instruction, int stackDelta)
        {
            Instructions.Add(instruction);
            StackDepth += stackDelta;
            if (StackDepth <= 0 && instruction.OpCode is not (OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Power))
                throw new InvalidOperationException("Повреждён стек компилятора выражений.");
            MaxStackDepth = Math.Max(MaxStackDepth, StackDepth);
        }

        private static Complex ConstantValue(string name) => name switch
        {
            "i" => Complex.ImaginaryOne,
            "pi" => new Complex(Math.PI, 0),
            "e" => new Complex(Math.E, 0),
            _ => throw new InvalidOperationException($"Переменная '{name}' не поддерживается. Допустимы z, i, pi и e.")
        };

        private static OpCode FunctionOpCode(string name) => name switch
        {
            "sin" => OpCode.Sin,
            "cos" => OpCode.Cos,
            "tan" => OpCode.Tan,
            "asin" => OpCode.Asin,
            "acos" => OpCode.Acos,
            "atan" => OpCode.Atan,
            "sinh" => OpCode.Sinh,
            "cosh" => OpCode.Cosh,
            "tanh" => OpCode.Tanh,
            "exp" => OpCode.Exp,
            "log" => OpCode.Log,
            "sqrt" => OpCode.Sqrt,
            _ => throw new InvalidOperationException($"Функция '{name}' не поддерживается байткодом.")
        };
    }
}
