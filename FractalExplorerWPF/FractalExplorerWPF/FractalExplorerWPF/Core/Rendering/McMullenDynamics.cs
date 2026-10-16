using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public enum McMullenOutcome { IterationLimit, Escaped, Pole, Periodic, NumericalLimit }
public readonly record struct McMullenOrbit(McMullenOutcome Outcome, int Iterations, double Smooth,
    double Phase, double TrapDistance, int Period);

/// <summary>f(z) = z^p + λ/z^q. Infinity and the pole are handled before evaluating powers.</summary>
public sealed class McMullenDynamics
{
    private readonly McMullenState _state;
    public Complex Lambda { get; }
    public double EscapeRadius { get; }

    public McMullenDynamics(McMullenState state, Complex? lambda = null)
    {
        _state = state;
        Lambda = lambda ?? new(state.LambdaReal, state.LambdaImaginary);
        // For r > R: |f(z)| >= r^p - |λ|/r^q >= r^p/2 > r.
        EscapeRadius = Math.Max(state.EscapeRadius, Math.Max(2, Math.Pow(2 * Lambda.Magnitude, 1.0 / (state.P + state.Q))));
    }

    public static Complex Power(Complex z, int n)
    {
        Complex result = Complex.One;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= z;
            n >>= 1;
            if (n > 0) z *= z;
        }
        return result;
    }

    public Complex Evaluate(Complex z)
    {
        if (Lambda == Complex.Zero) return Power(z, _state.P);
        if (z == Complex.Zero) return new(double.PositiveInfinity, 0);
        double logDenominator = _state.Q * Math.Log(z.Magnitude);
        // A tiny denominator can underflow although λ/z^q is finite (especially with subnormal λ).
        Complex pole = logDenominator < -650 ? Complex.FromPolarCoordinates(
            Math.Exp(Math.Log(Lambda.Magnitude) - logDenominator), Lambda.Phase - _state.Q * z.Phase) :
            Lambda / Power(z, _state.Q);
        return Power(z, _state.P) + pole;
    }

    public Complex CriticalPoint(int index = 0)
    {
        if (Lambda == Complex.Zero) return Complex.Zero;
        int degree = _state.P + _state.Q;
        return Complex.FromPolarCoordinates(Math.Exp((Math.Log(Lambda.Magnitude) + Math.Log((double)_state.Q / _state.P)) / degree),
            (Lambda.Phase + 2 * Math.PI * index) / degree);
    }

    public McMullenOrbit Analyze(Complex initial, CancellationToken token = default)
    {
        Complex z = initial;
        double trap = double.PositiveInfinity;
        // Only period coloring needs the rolling history. Two consecutive repeats reject a transient near-return.
        Complex[]? history = _state.Coloring == McMullenColoring.Period ? new Complex[2 * _state.MaxPeriod + 1] : null;
        double phase = 0;
        for (int n = 0; n <= _state.MaxIterations; n++)
        {
            if ((n & 31) == 0) token.ThrowIfCancellationRequested();
            double r = z.Magnitude;
            if (!double.IsFinite(r)) return Result(McMullenOutcome.NumericalLimit, n);
            phase = (z.Phase + Math.PI) / (2 * Math.PI);
            trap = Math.Min(trap, _state.Coloring == McMullenColoring.CrossTrap ?
                Math.Min(Math.Abs(z.Real), Math.Abs(z.Imaginary)) : Math.Abs(r - _state.TrapRadius));
            if (r > EscapeRadius)
            {
                double smooth = n - Math.Log(Math.Log(r) / Math.Log(EscapeRadius)) / Math.Log(_state.P);
                return new(McMullenOutcome.Escaped, n, smooth, phase, trap, 0);
            }
            if (Lambda != Complex.Zero)
            {
                if (r == 0) return Result(McMullenOutcome.Pole, n);
                // Certified entry through the pole: |λ|/r^q - r^p > R. Logarithms avoid 0/0 and overflow.
                double logPole = Math.Log(Lambda.Magnitude) - _state.Q * Math.Log(r);
                double power = Math.Pow(r, _state.P);
                if (logPole > Math.Log(EscapeRadius + power))
                {
                    double logNext = logPole + Math.Log(1 - power * Math.Exp(-logPole));
                    double smooth = n + 1 - Math.Log(logNext / Math.Log(EscapeRadius)) / Math.Log(_state.P);
                    return new(McMullenOutcome.Pole, n + 1, smooth, phase, trap, 0);
                }
            }
            if (history is not null)
            {
                for (int period = 1; period <= Math.Min(_state.MaxPeriod, n / 2); period++)
                {
                    double tolerance = _state.Tolerance * (1 + r);
                    if ((z - history[(n - period) % history.Length]).Magnitude < tolerance &&
                        (z - history[(n - 2 * period) % history.Length]).Magnitude < tolerance)
                        return new(McMullenOutcome.Periodic, n, n, phase, trap, period);
                }
                history[n % history.Length] = z;
            }
            if (n == _state.MaxIterations) break;
            z = Evaluate(z);
        }
        return Result(McMullenOutcome.IterationLimit, _state.MaxIterations);

        McMullenOrbit Result(McMullenOutcome outcome, int n) => new(outcome, n, n, phase, trap, 0);
    }

    public IReadOnlyList<Complex> Trace(Complex z, int limit = 128)
    {
        var points = new List<Complex>();
        for (int i = 0; i < limit; i++)
        {
            if (!double.IsFinite(z.Real) || !double.IsFinite(z.Imaginary)) break;
            points.Add(z);
            if (z.Magnitude > EscapeRadius || z == Complex.Zero && Lambda != Complex.Zero) break;
            z = Evaluate(z);
        }
        return points;
    }
}
