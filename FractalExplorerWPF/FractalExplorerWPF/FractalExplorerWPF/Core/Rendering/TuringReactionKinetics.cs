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

    // Shared by the 2D and 3D DirectCompute implementations; P[8]/P[9] are reserved for kinetics.
    public const string Shader = """
        float2 Reaction(float2 uv) {
            float u = uv.x, v = uv.y, uu = u*u, a = P[8].y, b = P[8].z;
            if (P[8].x == 1) return float2(a-(b+1)*u+uu*v,b*u-uu*v);
            if (P[8].x == 2) return float2(a-u+uu*v,b-uu*v);
            return float2(a-u+uu/max(v,1e-6),uu-b*v);
        }
        float ReactionDisplayValue(float u) { return (u-P[9].z)/(u+P[9].z); }
        """;
}
