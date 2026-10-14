using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Филлотаксис и иррациональные вращения.</summary>
internal static class PhyllotaxisLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        int count = Math.Clamp(state.PrimaryValue, 50, 50_000);
        double angle = state.Mode switch
        {
            0 => Math.Tau * (1 - 1 / ((1 + Math.Sqrt(5)) / 2)),
            1 => Math.PI - 3,
            2 => Math.Tau * (Math.Sqrt(2) - 1),
            3 => Math.Tau * state.SecondaryValue / Math.Max(1d, state.TertiaryValue),
            _ => state.Parameter * Math.PI / 180
        };
        double dot = Math.Clamp(0.025 / Math.Pow(count / 500d, 0.28), 0.0015, 0.018);
        for (int n = count - 1; n >= 0; n--)
        {
            if ((n & 1023) == 0) token.ThrowIfCancellationRequested();
            double radius = 0.92 * Math.Sqrt((n + 0.5) / count);
            double theta = n * angle;
            double family = 0.5 + 0.5 * Math.Sin(theta * 0.13 + radius * 28);
            canvas.Circle(radius * Math.Cos(theta), radius * Math.Sin(theta), dot,
                1, Palette(state, state.ShowGuides ? family : (double)n / count), true);
        }
    }
}
