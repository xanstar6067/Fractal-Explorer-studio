using System.Linq.Expressions;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>A finite real polynomial graph. Absolute values are explicit nodes, including
/// folds inside polynomial terms. Reference values of every node preserve fold signs.</summary>
internal sealed class FoldedPolynomialProgram
{
    internal enum Op { X, Y, Add, Subtract, Multiply, Scale, Abs }
    internal readonly record struct Node(Op Operation, int A = 0, int B = 0, int Scale = 0);
    internal readonly record struct Value(double Real, double Imaginary, double Xx = 0, double Xy = 0, double Yx = 0, double Yy = 0);
    internal Node[] Nodes { get; }
    internal int Real { get; }
    internal int Imaginary { get; }
    internal int Degree { get; }
    private readonly Func<double, double, Value> _evaluate;
    private readonly Func<double, double, Value> _jacobian;
    private FoldedPolynomialProgram(Builder b, E real, E imaginary, int degree)
    {
        Nodes = b.Nodes.ToArray(); Real = real.Index; Imaginary = imaginary.Index; Degree = degree;
        _evaluate = Compile(false); _jacobian = Compile(true);
    }
    internal Value Evaluate(double x, double y, bool derivative = false) => (derivative ? _jacobian : _evaluate)(x, y);
    private Func<double, double, Value> Compile(bool derivative)
    {
        var x = Expression.Parameter(typeof(double), "x"); var y = Expression.Parameter(typeof(double), "y");
        var values = Nodes.Select((_, i) => Expression.Variable(typeof(double), "v" + i)).ToArray();
        var dx = Nodes.Select((_, i) => Expression.Variable(typeof(double), "a" + i)).ToArray();
        var dy = Nodes.Select((_, i) => Expression.Variable(typeof(double), "b" + i)).ToArray();
        var body = new List<Expression>();
        var abs = typeof(System.Math).GetMethod(nameof(System.Math.Abs), [typeof(double)])!;
        var sign = typeof(FoldedPolynomialProgram).GetMethod(nameof(FiniteSign), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Expression C(double d) => Expression.Constant(d);
        foreach (var (n, i) in Nodes.Select((n, i) => (n, i)))
        {
            Expression v = n.Operation switch
            {
                Op.X => x, Op.Y => y, Op.Add => Expression.Add(values[n.A], values[n.B]),
                Op.Subtract => Expression.Subtract(values[n.A], values[n.B]), Op.Multiply => Expression.Multiply(values[n.A], values[n.B]),
                Op.Scale => Expression.Multiply(values[n.A], C(n.Scale)), Op.Abs => Expression.Call(abs, values[n.A]), _ => throw new InvalidOperationException()
            };
            body.Add(Expression.Assign(values[i], v));
            if (!derivative) continue;
            foreach (var (d, isX) in new[] { (dx, true), (dy, false) })
            {
                Expression dv = n.Operation switch
                {
                    Op.X => C(isX ? 1 : 0), Op.Y => C(isX ? 0 : 1), Op.Add => Expression.Add(d[n.A], d[n.B]),
                    Op.Subtract => Expression.Subtract(d[n.A], d[n.B]),
                    Op.Multiply => Expression.Add(Expression.Multiply(d[n.A], values[n.B]), Expression.Multiply(values[n.A], d[n.B])),
                    Op.Scale => Expression.Multiply(d[n.A], C(n.Scale)),
                    Op.Abs => Expression.Multiply(Expression.Call(sign, values[n.A]), d[n.A]), _ => throw new InvalidOperationException()
                };
                body.Add(Expression.Assign(d[i], dv));
            }
        }
        body.Add(Expression.New(typeof(Value).GetConstructors()[0], values[Real], values[Imaginary],
            derivative ? dx[Real] : C(0), derivative ? dy[Real] : C(0), derivative ? dx[Imaginary] : C(0), derivative ? dy[Imaginary] : C(0)));
        return Expression.Lambda<Func<double, double, Value>>(Expression.Block(derivative ? values.Concat(dx).Concat(dy) : values, body), x, y).Compile();
    }
    private static double FiniteSign(double x) => double.IsNaN(x) ? 0 : System.Math.Sign(x);
    internal (BigFloat Real, BigFloat Imaginary) EvaluateBig(BigFloat x, BigFloat y, Span<BigFloat> values)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            values[i] = n.Operation switch
            {
                Op.X => x, Op.Y => y, Op.Add => values[n.A] + values[n.B], Op.Subtract => values[n.A] - values[n.B],
                Op.Multiply => values[n.A] * values[n.B], Op.Scale => values[n.A] * BigFloat.FromInt(n.Scale),
                Op.Abs => BigFloat.Abs(values[n.A]), _ => throw new InvalidOperationException()
            };
        }
        return (values[Real], values[Imaginary]);
    }
    internal Value EvaluateDecimal(decimal x, decimal y)
    {
        Span<decimal> v = stackalloc decimal[Nodes.Length];
        for (int i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i]; v[i] = n.Operation switch
            {
                Op.X => x, Op.Y => y, Op.Add => v[n.A] + v[n.B], Op.Subtract => v[n.A] - v[n.B],
                Op.Multiply => v[n.A] * v[n.B], Op.Scale => v[n.A] * n.Scale, Op.Abs => decimal.Abs(v[n.A]),
                _ => throw new InvalidOperationException()
            };
        }
        return new((double)v[Real], (double)v[Imaginary]);
    }
    internal (FloatExp Real, FloatExp Imaginary, Value Jacobian) Delta(
        ReadOnlySpan<FloatExp> r, FloatExp x, FloatExp y, bool derivative)
    {
        Span<FloatExp> d = stackalloc FloatExp[Nodes.Length];
        Span<double> dx = stackalloc double[derivative ? Nodes.Length : 0];
        Span<double> dy = stackalloc double[derivative ? Nodes.Length : 0];
        for (int i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            d[i] = n.Operation switch
            {
                Op.X => x, Op.Y => y, Op.Add => d[n.A] + d[n.B], Op.Subtract => d[n.A] - d[n.B],
                Op.Multiply => r[n.A] * d[n.B] + r[n.B] * d[n.A] + d[n.A] * d[n.B],
                Op.Scale => d[n.A] * n.Scale, Op.Abs => FoldDelta(r[n.A], d[n.A]), _ => throw new InvalidOperationException()
            };
            if (!derivative) continue;
            double a = (r[n.A] + d[n.A]).ToDouble(), bb = (r[n.B] + d[n.B]).ToDouble();
            double s = (r[n.A] + d[n.A]).Sign;
            dx[i] = n.Operation switch
            {
                Op.X => 1, Op.Y => 0, Op.Add => dx[n.A] + dx[n.B], Op.Subtract => dx[n.A] - dx[n.B],
                Op.Multiply => dx[n.A] * bb + a * dx[n.B], Op.Scale => dx[n.A] * n.Scale, Op.Abs => s * dx[n.A], _ => 0
            };
            dy[i] = n.Operation switch
            {
                Op.X => 0, Op.Y => 1, Op.Add => dy[n.A] + dy[n.B], Op.Subtract => dy[n.A] - dy[n.B],
                Op.Multiply => dy[n.A] * bb + a * dy[n.B], Op.Scale => dy[n.A] * n.Scale, Op.Abs => s * dy[n.A], _ => 0
            };
        }
        return (d[Real], d[Imaginary], derivative ? new(0, 0, dx[Real], dy[Real], dx[Imaginary], dy[Imaginary]) : default);
    }
    // Bounds over |offset| <= radius. A fold is safe only while its argument
    // cannot change sign; the remainder then has an ordinary Hessian bound.
    internal (Value Jacobian, double Remainder, double FoldRadius) BlaBounds(ReadOnlySpan<FloatExp> reference)
    {
        double inputX = 0, inputY = 0;
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].Operation == Op.X) inputX = reference[i].ToDouble();
            if (Nodes[i].Operation == Op.Y) inputY = reference[i].ToDouble();
        }
        double radius = System.Math.Min(1, System.Math.Sqrt(inputX * inputX + inputY * inputY));
        Span<double> bound = stackalloc double[Nodes.Length], l = stackalloc double[Nodes.Length], h = stackalloc double[Nodes.Length];
        double foldRadius = radius;
        for (int i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            if (n.Operation is Op.X or Op.Y) { bound[i] = System.Math.Abs(reference[i].ToDouble()) + radius; l[i] = 1; h[i] = 0; continue; }
            double a = bound[n.A], aa = l[n.A], ah = h[n.A];
            double bb = bound[n.B], bl = l[n.B], bh = h[n.B];
            switch (n.Operation)
            {
                case Op.Add: case Op.Subtract: bound[i] = a + bb; l[i] = aa + bl; h[i] = ah + bh; break;
                case Op.Multiply: bound[i] = a * bb; l[i] = aa * bb + a * bl; h[i] = ah * bb + 2 * aa * bl + a * bh; break;
                case Op.Scale: double k = System.Math.Abs(n.Scale); bound[i] = k * a; l[i] = k * aa; h[i] = k * ah; break;
                case Op.Abs:
                    bound[i] = a; l[i] = aa; h[i] = ah;
                    if (aa > 0) foldRadius = System.Math.Min(foldRadius, FloatExp.Abs(reference[n.A]).ToDouble() / (2 * aa));
                    break;
            }
        }
        return (Delta(reference, FloatExp.Zero, FloatExp.Zero, true).Jacobian,
            0.5 * System.Math.Sqrt(h[Real] * h[Real] + h[Imaginary] * h[Imaginary]), foldRadius);
    }

    private static FloatExp FoldDelta(FloatExp r, FloatExp d) => r.Sign > 0
        ? d > -r ? d : -(d + r * 2) : r.Sign < 0 ? d < -r ? -d : d + r * 2 : FloatExp.Abs(d);

    private sealed class Builder
    {
        internal readonly List<Node> Nodes = [];
        private readonly Dictionary<Node, int> _indices = [];
        internal E Put(Node n)
        {
            if (!_indices.TryGetValue(n, out int i)) { i = Nodes.Count; Nodes.Add(n); _indices.Add(n, i); }
            return new(this, i);
        }
    }
    private readonly record struct E(Builder Builder, int Index)
    {
        internal E Abs() => Builder.Put(new(Op.Abs, Index));
        public static E operator +(E a, E b) => a.Builder.Put(new(Op.Add, a.Index, b.Index));
        public static E operator -(E a, E b) => a.Builder.Put(new(Op.Subtract, a.Index, b.Index));
        public static E operator *(E a, E b) => a.Builder.Put(new(Op.Multiply, a.Index, b.Index));
        public static E operator *(int a, E b) => b.Builder.Put(new(Op.Scale, b.Index, Scale: a));
        public static E operator -(E a) => -1 * a;
    }
    private static (E R, E I) Power(E x, E y, int n)
    {
        E r = x, i = y;
        for (int k = 1; k < n; k++) (r, i) = (r * x - i * y, r * y + i * x);
        return (r, i);
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(MandelbrotVariant, int), FoldedPolynomialProgram> Cache = new();
    internal static FoldedPolynomialProgram For(MandelbrotVariant variant, int power = 2) => Cache.GetOrAdd(
        (MandelbrotVariantDefinition.ParameterVariant(variant), power), pair => Build(pair.Item1, pair.Item2));
    private static FoldedPolynomialProgram Build(MandelbrotVariant variant, int power)
    {
        var b = new Builder(); E x = b.Put(new(Op.X)), y = b.Put(new(Op.Y));
        var def = FoldedFormulaCatalog.Find(variant);
        int p = def?.Degree ?? (variant is MandelbrotVariant.CubicQuasiBurningShip or MandelbrotVariant.CubicFlyingSquirrel ? 3 : power);
        string kind = def?.Kind ?? variant switch
        {
            MandelbrotVariant.Tricorn => "Mandelbar", MandelbrotVariant.Mandelbrot => "Mandelbrot",
            MandelbrotVariant.BurningShip => "LegacyBurningShip", MandelbrotVariant.Buffalo => "LegacyBuffalo",
            MandelbrotVariant.Celtic => "Celtic", MandelbrotVariant.CelticMandelbar => "CelticMandelbar",
            MandelbrotVariant.PerpendicularMandelbrot => "PartialBurningShipRealMandelbar",
            MandelbrotVariant.PerpendicularBurningShip => "PartialBurningShipImagMandelbar",
            MandelbrotVariant.PerpendicularCeltic => "CelticPartialBurningShipRealMandelbar",
            MandelbrotVariant.PerpendicularBuffalo => "CelticPartialBurningShipImagMandelbar",
            MandelbrotVariant.CubicQuasiBurningShip => "QuasiBurningShip", MandelbrotVariant.CubicFlyingSquirrel => "BuffaloPartialImag",
            _ => throw new ArgumentException("Формула не поддерживается гибридом.")
        };
        E x2 = x * x, y2 = y * y, x4 = x2 * x2, y4 = y2 * y2;
        E a = p == 3 ? x2 - 3 * y2 : x4 + 5 * y4 - 10 * (x2 * y2);
        E c = p == 3 ? 3 * x2 - y2 : 5 * x4 + y4 - 10 * (x2 * y2);
        E u = x4 + y4 - 6 * (x2 * y2), v = x2 - y2;
        (E r, E i) = kind switch
        {
            "Mandelbrot" => Power(x, y, p), "BurningShip" => Power(x.Abs(), y.Abs(), p),
            "LegacyBurningShip" => Power(x.Abs(), -y.Abs(), p), "LegacyBuffalo" => Power(x.Abs(), y.Abs(), p),
            "Mandelbar" => Power(x, -y, p),
            "PartialBurningShipReal" => Power(x.Abs(), y, p),
            "PartialBurningShipImag" => Power(x, y.Abs(), p),
            "PartialBurningShipRealMandelbar" => Power(x.Abs(), -y, p),
            "PartialBurningShipImagMandelbar" => Power(x, -y.Abs(), p),
            "QuasiPerpendicular" => (x.Abs() * a, -(y * c.Abs())),
            "QuasiHeart" => (x.Abs() * a, y * c.Abs()),
            "CelticQuasiPerpendicular" => p == 3 ? ((x * a).Abs(), -(y * c.Abs())) : (x.Abs() * a.Abs(), -(y * c.Abs())),
            "CelticQuasiHeart" => (x.Abs() * a.Abs(), y * c.Abs()),
            "QuasiPerpendicularBurningShip" => (y.Abs() * c, -(x * a.Abs())),
            "QuasiPerpendicularBuffalo" => (y.Abs() * c.Abs(), -((p == 5 ? x.Abs() : x) * a.Abs())),
            "FalseQuasiPerpendicular" => (u, -4 * (x * y * v.Abs())),
            "FalseQuasiHeart" => (u, 4 * (x * y * v.Abs())),
            "CelticFalseQuasiPerpendicular" => (u.Abs(), -4 * (x * y * v.Abs())),
            "CelticFalseQuasiHeart" => (u.Abs(), 4 * (x * y * v.Abs())),
            "ImagQuasi" => (u, 4 * (x * (y * v).Abs())),
            "CelticImagQuasi" => (u.Abs(), 4 * (x * (y * v).Abs())),
            "RealQuasiPerpendicular" => (u, -4 * (y * (x * v).Abs())),
            "RealQuasiHeart" => (u, 4 * (y * (x * v).Abs())),
            "CelticRealQuasiPerpendicular" => (u.Abs(), -4 * (y * (x * v).Abs())),
            "CelticRealQuasiHeart" => (u.Abs(), 4 * (y * (x * v).Abs())),
            _ => Power(kind.Contains("PartialBurningShipReal") ? x.Abs() : x,
                kind.Contains("PartialBurningShipImag") ? (kind.EndsWith("Mandelbar") ? -y.Abs() : y.Abs())
                    : kind.EndsWith("Mandelbar") ? -y : y, p)
        };
        if (kind is "Buffalo") (r, i) = (r.Abs(), i.Abs());
        else if (kind is "Celtic" or "CelticMandelbar" || kind.StartsWith("CelticPartial")) r = r.Abs();
        else if (kind == "BuffaloPartialImag") i = i.Abs();
        else if (kind == "QuasiBurningShip")
        {
            (r, i) = Power(x.Abs(), y, p); i = -i.Abs();
        }
        // Prune unused construction nodes before compilation/reference storage.
        var compact = new Builder(); var mapping = new Dictionary<int, E>();
        E Copy(E e)
        {
            if (mapping.TryGetValue(e.Index, out var found)) return found;
            var n = b.Nodes[e.Index];
            var cn = n.Operation is Op.X or Op.Y ? n : n with { A = Copy(new(b, n.A)).Index,
                B = n.Operation is Op.Add or Op.Subtract or Op.Multiply ? Copy(new(b, n.B)).Index : 0 };
            var result = compact.Put(cn); mapping.Add(e.Index, result); return result;
        }
        E real = Copy(r), imaginary = Copy(i);
        return new(compact, real, imaginary, p);
    }
}
