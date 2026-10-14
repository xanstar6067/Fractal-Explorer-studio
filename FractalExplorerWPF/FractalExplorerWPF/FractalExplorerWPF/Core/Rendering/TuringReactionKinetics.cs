using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

internal static class TuringReactionKinetics
{
    public const float MinConcentration = 1e-6f, MaxConcentration = 1000;

    public static (double U, double V) Evaluate(double u, double v, TuringReactionSettings settings)
    {
        double squared = u * u;
        return settings.Model switch
        {
            TuringReactionModel.Brusselator => (settings.A - (settings.B + 1) * u + squared * v, settings.B * u - squared * v),
            TuringReactionModel.Schnakenberg => (settings.A - u + squared * v, settings.B - squared * v),
            TuringReactionModel.GiererMeinhardt => (settings.A - u + squared / Math.Max(v, MinConcentration), squared - settings.B * v),
            _ => throw new ArgumentException("Для Маккейба используется многомасштабное правило.")
        };
    }

    public static float Display(double u, double equilibrium) => (float)((u - equilibrium) / (u + equilibrium));
}
