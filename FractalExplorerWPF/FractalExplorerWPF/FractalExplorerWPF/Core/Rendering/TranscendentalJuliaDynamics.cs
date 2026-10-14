using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public enum TranscendentalOrbitOutcome { Converged, IterationLimit, NumericalLimit }
public sealed record TranscendentalCycle(Complex[] Points, double Multiplier);
public sealed record TranscendentalOrbit(TranscendentalOrbitOutcome Outcome, int Iterations, int Attractor, Complex Last);

/// <summary>Ни радиус, ни переполнение не являются доказательством ухода на бесконечность.</summary>
public sealed class TranscendentalJuliaDynamics
{
    private readonly TranscendentalJuliaState _state;
    private readonly Complex _lambda;
    private readonly double _logLambda, _lambdaArgument;
    public TranscendentalJuliaDynamics(TranscendentalJuliaState state)
    {
        state.Validate();
        _state = state.Clone();
        _lambda = new(state.LambdaReal, state.LambdaImaginary);
        _logLambda = Math.Log(_lambda.Magnitude);
        _lambdaArgument = Math.Atan2(_lambda.Imaginary, _lambda.Real);
    }
    public static bool IsFinite(Complex z) => double.IsFinite(z.Real) && double.IsFinite(z.Imaginary);

    public Complex Evaluate(Complex z)
    {
        if (_lambda == Complex.Zero) return Complex.Zero;
        // Масштаб λ включён до exp: промежуточное exp(z) может переполниться,
        // хотя λ·exp(z) ещё представимо (и наоборот, не должно раньше времени обнуляться).
        if (_state.Kind == TranscendentalJuliaKind.Exponential)
            return Complex.FromPolarCoordinates(Math.Exp(z.Real + _logLambda), z.Imaginary + _lambdaArgument);
        return Math.Abs(z.Imaginary) <= 20 ? _lambda * Complex.Sin(z) : ScaledSine(z, cosine: false);
    }
    public Complex Derivative(Complex z) => _lambda == Complex.Zero ? Complex.Zero :
        _state.Kind == TranscendentalJuliaKind.Exponential ? Evaluate(z) :
        Math.Abs(z.Imaginary) <= 20 ? _lambda * Complex.Cos(z) : ScaledSine(z, cosine: true);

    private Complex ScaledSine(Complex z, bool cosine)
    {
        // При |Im z|>20 поправка exp(-2|Im z|) меньше машинной точности.
        double scale = Math.Exp(Math.Abs(z.Imaginary) + _logLambda - Math.Log(2));
        double sin = Math.Sin(z.Real), cos = Math.Cos(z.Real), sign = Math.Sign(z.Imaginary);
        Complex phase = cosine ? new(cos, -sign * sin) : new(sin, sign * cos);
        return phase * Complex.FromPolarCoordinates(scale, _lambdaArgument);
    }

    // За пределом надёжного аргумента тригонометрии не угадываем фазу.
    public bool TryStep(Complex z, out Complex next)
    {
        next = default;
        if (!IsFinite(z)) return false;
        if (_lambda == Complex.Zero) return true;
        // Здесь модуль результата уже меньше минимального double при любой фазе.
        // Проверка до ограничения аргумента позволяет огромной отрицательной экспоненте вернуться к 0.
        if (_state.Kind == TranscendentalJuliaKind.Exponential && z.Real + _logLambda < -745.2)
            return true;
        if ((_state.Kind == TranscendentalJuliaKind.Exponential && Math.Abs(z.Imaginary) > 1e14) ||
            (_state.Kind == TranscendentalJuliaKind.Sine && Math.Abs(z.Real) > 1e14)) return false;
        next = Evaluate(z);
        return IsFinite(next);
    }

    public Complex Preimage(Complex w, long branch)
    {
        if (_lambda == Complex.Zero) return new(double.NaN, double.NaN);
        Complex q = w / _lambda;
        return _state.Kind == TranscendentalJuliaKind.Exponential
            ? Complex.Log(q) + new Complex(0, 2 * Math.PI * branch)
            : new Complex(branch * Math.PI, 0) + ((branch & 1) == 0 ? 1 : -1) * Complex.Asin(q);
    }

    public TranscendentalOrbit Classify(Complex z, IReadOnlyList<TranscendentalCycle> cycles, CancellationToken token = default)
    {
        for (int iteration = 0; iteration <= _state.MaxIterations; iteration++)
        {
            if ((iteration & 31) == 0) token.ThrowIfCancellationRequested();
            if (!IsFinite(z)) return new(TranscendentalOrbitOutcome.NumericalLimit, iteration, -1, z);
            for (int owner = 0; owner < cycles.Count; owner++)
                foreach (Complex point in cycles[owner].Points)
                    if ((z - point).Magnitude <= _state.Tolerance * Math.Max(1, point.Magnitude))
                        return new(TranscendentalOrbitOutcome.Converged, iteration, owner, z);
            if (iteration == _state.MaxIterations) break;
            if (!TryStep(z, out Complex next)) return new(TranscendentalOrbitOutcome.NumericalLimit, iteration + 1, -1, z);
            z = next;
        }
        return new(TranscendentalOrbitOutcome.IterationLimit, _state.MaxIterations, -1, z);
    }

    public IReadOnlyList<Complex> Trace(Complex z, CancellationToken token = default)
    {
        var points = new List<Complex>();
        for (int i = 0; i <= _state.MaxIterations; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsFinite(z)) break;
            points.Add(z);
            if (!TryStep(z, out z)) break;
        }
        return points;
    }

    public IReadOnlyList<TranscendentalCycle> FindAttractors(CancellationToken token)
    {
        if (_lambda == Complex.Zero) return [new([Complex.Zero], 0)];
        var cycles = new List<TranscendentalCycle>();
        // Полный набор конечных сингулярных значений этих двух семейств.
        Complex[] seeds = _state.Kind == TranscendentalJuliaKind.Exponential ? [Complex.Zero] : [_lambda, -_lambda];
        foreach (Complex seed in seeds)
        {
            Complex z = seed;
            var history = new Complex[2 * _state.MaxPeriod + 1];
            int limit = Math.Clamp(_state.MaxIterations * 8, 2000, 20000);
            for (int i = 0; i < limit; i++)
            {
                if ((i & 31) == 0) token.ThrowIfCancellationRequested();
                history[i % history.Length] = z;
                if (i >= 2 * _state.MaxPeriod && i % 8 == 0)
                {
                    for (int period = 1; period <= _state.MaxPeriod; period++)
                    {
                        Complex previous = history[(i - period) % history.Length];
                        Complex earlier = history[(i - 2 * period) % history.Length];
                        double tolerance = _state.Tolerance * Math.Max(1, z.Magnitude);
                        if ((z - previous).Magnitude > tolerance || (z - earlier).Magnitude > tolerance) continue;
                        if (!RefinePeriodic(z, period, out Complex root, out Complex multiplier)) continue;
                        if (multiplier.Magnitude >= 1 - 1e-8) continue;
                        var points = new Complex[period];
                        Complex q = root;
                        for (int j = 0; j < period; j++) { points[j] = q; q = Evaluate(q); }
                        if (!cycles.Any(c => c.Points.Any(p => (p - root).Magnitude < 1e-5)))
                            cycles.Add(new(points, multiplier.Magnitude));
                        goto NextSeed;
                    }
                }
                if (!TryStep(z, out z)) break;
            }
            NextSeed:;
        }
        return cycles;
    }

    private bool Iterate(Complex z, int period, out Complex value, out Complex derivative)
    {
        derivative = Complex.One;
        value = z;
        for (int i = 0; i < period; i++)
        {
            derivative *= Derivative(value);
            if (!IsFinite(derivative) || !TryStep(value, out value)) return false;
        }
        return true;
    }

    private bool RefinePeriodic(Complex seed, int period, out Complex root, out Complex multiplier)
    {
        root = seed; multiplier = default;
        for (int i = 0; i < 70; i++)
        {
            if (!Iterate(root, period, out Complex value, out multiplier)) return false;
            if ((value - root).Magnitude <= 1e-11 * Math.Max(1, root.Magnitude)) return true;
            Complex divisor = multiplier - Complex.One;
            if (divisor.Magnitude < 1e-14) return false;
            Complex step = (value - root) / divisor;
            // Ограничение шага удерживает Ньютон в области вычислимых exp/sin.
            if (step.Magnitude > 4) step *= 4 / step.Magnitude;
            root -= step;
            if (!IsFinite(root)) return false;
        }
        return false;
    }

    public IReadOnlyList<Complex> FindRepellingSeeds(CancellationToken token)
    {
        if (_lambda == Complex.Zero) return [];
        var roots = new List<Complex>();
        for (int period = 1; period <= 2; period++)
            for (int x = -4; x <= 4; x++)
                for (int y = -6; y <= 6; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Complex seed = new(x * 2.0, y * 2.0);
                    if (!RefinePeriodic(seed, period, out Complex root, out Complex multiplier) ||
                        multiplier.Magnitude <= 1 + 1e-7) continue;
                    if (roots.All(p => (p - root).Magnitude > 1e-6)) roots.Add(root);
                    if (roots.Count >= 48) return roots;
                }
        return roots;
    }
}
