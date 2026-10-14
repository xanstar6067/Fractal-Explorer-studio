using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Аналитическое отображение без DTO/палитры: пригодно для отдельной λ каждого пикселя.</summary>
public readonly struct TranscendentalMap
{
    public TranscendentalJuliaKind Kind { get; }
    public Complex Lambda { get; }
    private readonly double _logLambda, _argument;
    public TranscendentalMap(TranscendentalJuliaKind kind, Complex lambda)
    {
        Kind = kind; Lambda = lambda;
        _logLambda = Math.Log(lambda.Magnitude); _argument = Math.Atan2(lambda.Imaginary, lambda.Real);
    }
    public Complex Evaluate(Complex z)
    {
        if (Lambda == Complex.Zero) return Complex.Zero;
        if (Kind == TranscendentalJuliaKind.Exponential)
            return Complex.FromPolarCoordinates(Math.Exp(z.Real + _logLambda), z.Imaginary + _argument);
        return Math.Abs(z.Imaginary) <= 20 ? Lambda * Complex.Sin(z) : ScaledSine(z, false);
    }
    public Complex Derivative(Complex z) => Lambda == Complex.Zero ? Complex.Zero :
        Kind == TranscendentalJuliaKind.Exponential ? Evaluate(z) :
        Math.Abs(z.Imaginary) <= 20 ? Lambda * Complex.Cos(z) : ScaledSine(z, true);
    private Complex ScaledSine(Complex z, bool cosine)
    {
        double scale = Math.Exp(Math.Abs(z.Imaginary) + _logLambda - Math.Log(2));
        double sin = Math.Sin(z.Real), cos = Math.Cos(z.Real), sign = Math.Sign(z.Imaginary);
        Complex phase = cosine ? new(cos, -sign * sin) : new(sin, sign * cos);
        return phase * Complex.FromPolarCoordinates(scale, _argument);
    }
    public bool TryStep(Complex z, out Complex next)
    {
        next = default;
        if (!TranscendentalJuliaDynamics.IsFinite(z) || !TranscendentalJuliaDynamics.IsFinite(Lambda)) return false;
        if (Lambda == Complex.Zero) return true;
        if (Kind == TranscendentalJuliaKind.Exponential && z.Real + _logLambda < -745.2) return true;
        if ((Kind == TranscendentalJuliaKind.Exponential && Math.Abs(z.Imaginary) > 1e14) ||
            (Kind == TranscendentalJuliaKind.Sine && Math.Abs(z.Real) > 1e14)) return false;
        next = Evaluate(z);
        return TranscendentalJuliaDynamics.IsFinite(next);
    }
    public Complex Preimage(Complex w, long branch)
    {
        if (Lambda == Complex.Zero) return new(double.NaN, double.NaN);
        Complex q = w / Lambda;
        return Kind == TranscendentalJuliaKind.Exponential ? Complex.Log(q) + new Complex(0, 2 * Math.PI * branch) :
            new Complex(branch * Math.PI, 0) + ((branch & 1) == 0 ? 1 : -1) * Complex.Asin(q);
    }
}
