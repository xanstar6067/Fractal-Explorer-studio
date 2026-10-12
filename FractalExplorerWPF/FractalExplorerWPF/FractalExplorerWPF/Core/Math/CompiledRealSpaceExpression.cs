using System.Globalization;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>Bounded real expression tree. Only validated nodes can emit shader source.</summary>
public sealed class CompiledRealSpaceExpression
{
    private readonly Node _root;
    private CompiledRealSpaceExpression(Node root) => _root = root;
    public double Evaluate(double x, double y, double z, double a = 1, double b = .35, double c = 0) =>
        _root.Evaluate([x, y, z, a, b, c]);
    public string Hlsl => _root.Emit(false);
    public string IntervalHlsl => _root.Emit(true);

    public static CompiledRealSpaceExpression Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048)
            throw new ArgumentException("Введите формулу до 2048 символов.");
        var parser = new Parser(text);
        return new(parser.Parse());
    }

    private abstract record Node
    {
        public abstract double Evaluate(double[] values);
        public abstract string Emit(bool interval);
    }
    private sealed record Number(double Value) : Node
    {
        public override double Evaluate(double[] values) => Value;
        public override string Emit(bool interval)
        {
            string literal = ((float)Value).ToString("R", CultureInfo.InvariantCulture);
            if (!literal.Contains('.') && !literal.Contains('E')) literal += ".0";
            return interval ? $"float2({literal},{literal})" : literal;
        }
    }
    private sealed record Variable(int Index) : Node
    {
        public override double Evaluate(double[] values) => values[Index];
        public override string Emit(bool interval) => Index < 3
            ? (interval ? new[] { "ix", "iy", "iz" }[Index] : "q." + "xyz"[Index])
            : interval ? $"float2(ShapeB.{"xyz"[Index - 3]},ShapeB.{"xyz"[Index - 3]})" : "ShapeB." + "xyz"[Index - 3];
    }
    private sealed record Binary(char Op, Node Left, Node Right) : Node
    {
        public override double Evaluate(double[] v) => Op switch
        {
            '+' => Left.Evaluate(v) + Right.Evaluate(v), '-' => Left.Evaluate(v) - Right.Evaluate(v),
            '*' => Left.Evaluate(v) * Right.Evaluate(v), _ => Left.Evaluate(v) / Right.Evaluate(v)
        };
        public override string Emit(bool interval) => interval
            ? $"I{(Op switch { '+' => "Add", '-' => "Sub", '*' => "Mul", _ => "Div" })}({Left.Emit(true)},{Right.Emit(true)})"
            : $"({Left.Emit(false)}{Op}{Right.Emit(false)})";
    }
    private sealed record Power(Node Base, int Exponent) : Node
    {
        public override double Evaluate(double[] v) => System.Math.Pow(Base.Evaluate(v), Exponent);
        public override string Emit(bool interval) => $"{(interval ? "IPow" : "RealPow")}({Base.Emit(interval)},{Exponent})";
    }
    private sealed record Function(string Name, Node Argument, Node? Second = null) : Node
    {
        public override double Evaluate(double[] v)
        {
            double t = Argument.Evaluate(v);
            return Name switch
            {
                "sin" => System.Math.Sin(t), "cos" => System.Math.Cos(t), "abs" => System.Math.Abs(t),
                "sqrt" => System.Math.Sqrt(t), "exp" => System.Math.Exp(t), "log" => System.Math.Log(t),
                "min" => System.Math.Min(t, Second!.Evaluate(v)), _ => System.Math.Max(t, Second!.Evaluate(v))
            };
        }
        public override string Emit(bool interval) =>
            $"{(interval ? "I" + char.ToUpperInvariant(Name[0]) + Name[1..] : Name)}({Argument.Emit(interval)}{(Second is null ? "" : "," + Second.Emit(interval))})";
    }

    private sealed class Parser(string text)
    {
        private readonly string _text = text.ToLowerInvariant().Replace('−', '-').Replace('·', '*');
        private int _pos, _nodes, _depth;
        public Node Parse()
        {
            Node n = Sum();
            if (Take('=')) n = Add(new Binary('-', n, Sum()));
            Skip();
            if (_pos != _text.Length) throw Error("Неожиданный символ. Умножение записывается как x*y.");
            return n;
        }
        private Node Add(Node n)
        {
            if (++_nodes > 128) throw Error("Формула слишком сложна: максимум 128 операций и значений.");
            return n;
        }
        private Node Sum()
        {
            Node n = Product();
            while (true)
            {
                if (Take('+')) n = Add(new Binary('+', n, Product()));
                else if (Take('-')) n = Add(new Binary('-', n, Product()));
                else return n;
            }
        }
        private Node Product()
        {
            Node n = Unary();
            while (true)
            {
                if (Take('*')) n = Add(new Binary('*', n, Unary()));
                else if (Take('/')) n = Add(new Binary('/', n, Unary()));
                else return n;
            }
        }
        private Node Unary()
        {
            if (++_depth > 32) throw Error("Слишком много вложенных скобок или знаков.");
            try
            {
                if (Take('+')) return Unary();
                if (Take('-')) return Add(new Binary('-', Add(new Number(0)), Unary()));
                Node n = Atom();
                if (Take('^'))
                {
                    bool bracket = Take('(');
                    int sign = Take('-') ? -1 : 1; if (sign == 1) Take('+');
                    Skip(); int start = _pos;
                    while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos])) _pos++;
                    if (start == _pos || !int.TryParse(_text[start.._pos], out int exponent) || exponent > 16)
                        throw Error("Степень должна быть целым числом от −16 до 16.");
                    if (bracket) Need(')');
                    n = Add(new Power(n, sign * exponent));
                }
                return n;
            }
            finally { _depth--; }
        }
        private Node Atom()
        {
            Skip();
            if (Take('(')) { Node n = Sum(); Need(')'); return n; }
            int start = _pos;
            if (_pos < _text.Length && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                while (_pos < _text.Length && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
                if (_pos < _text.Length && _text[_pos] == 'e')
                {
                    _pos++; if (_pos < _text.Length && _text[_pos] is '+' or '-') _pos++;
                    while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos])) _pos++;
                }
                if (!double.TryParse(_text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                    !float.IsFinite((float)value) || (value != 0 && (float)value == 0)) throw Error("Недопустимое число для формулы.");
                return Add(new Number(value));
            }
            while (_pos < _text.Length && char.IsAsciiLetter(_text[_pos])) _pos++;
            string name = _text[start.._pos];
            if (name == "pi") return Add(new Number(System.Math.PI));
            if (name == "e") return Add(new Number(System.Math.E));
            if (name.Length == 1 && "xyzabc".IndexOf(name[0]) is int index && index >= 0)
                return Add(new Variable(index));
            if (name is "sin" or "cos" or "abs" or "sqrt" or "exp" or "log" or "min" or "max")
            {
                Need('('); Node arg = Sum(); Node? second = null;
                if (name is "min" or "max") { Need(','); second = Sum(); }
                Need(')'); return Add(new Function(name, arg, second));
            }
            throw Error("Ожидается число, x/y/z, a/b/c или поддерживаемая функция.");
        }
        private void Skip() { while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++; }
        private bool Take(char c) { Skip(); if (_pos >= _text.Length || _text[_pos] != c) return false; _pos++; return true; }
        private void Need(char c) { if (!Take(c)) throw Error($"Ожидается «{c}»."); }
        private ArgumentException Error(string message) => new($"{message} Позиция {_pos + 1}.");
    }
}
