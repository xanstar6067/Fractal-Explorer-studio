using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Арифметика по модулю: таблицы умножения и функциональные графы на окружности.</summary>
internal static class ModularArithmeticLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        int modulus = Math.Clamp(state.PrimaryValue, 10, 2_000);
        int a = state.SecondaryValue;
        int value = state.TertiaryValue;
        double radius = 0.9;
        if (state.ShowGuides)
            canvas.Circle(0, 0, radius, 1.2, Mix(state.PrimaryColor, state.BackgroundColor, 0.55), false);

        for (int x = 0; x < modulus; x++)
        {
            if ((x & 127) == 0) token.ThrowIfCancellationRequested();
            int target = state.Mode switch
            {
                0 => Mod((long)a * x, modulus),
                1 => Mod((long)a * x + value, modulus),
                2 => Mod((long)x * x + value, modulus),
                3 => PowMod(x, Math.Clamp(Math.Abs(value), 2, 31), modulus),
                4 => x % 2 == 0 ? x / 2 : Mod(3L * x + 1, modulus),
                _ => Mod((long)a * x, modulus)
            };
            double angle1 = -Math.PI / 2 + Math.Tau * x / modulus;
            double angle2 = -Math.PI / 2 + Math.Tau * target / modulus;
            Color color = Palette(state, (double)x / modulus);
            canvas.Line(radius * Math.Cos(angle1), radius * Math.Sin(angle1),
                radius * Math.Cos(angle2), radius * Math.Sin(angle2),
                Math.Clamp(state.Parameter, 0.25, 8), color, modulus > 700 ? 0.42 : 0.72);
        }

        if (modulus <= 500)
        {
            double pointRadius = Math.Clamp(4.5 / Math.Sqrt(modulus), 0.004, 0.018);
            for (int x = 0; x < modulus; x++)
            {
                double angle = -Math.PI / 2 + Math.Tau * x / modulus;
                canvas.Circle(radius * Math.Cos(angle), radius * Math.Sin(angle), pointRadius,
                    1, state.AccentColor, true);
            }
        }
    }

    private static int Mod(long value, int modulus) => (int)((value % modulus + modulus) % modulus);

    private static int PowMod(int value, int exponent, int modulus)
    {
        long result = 1, factor = Mod(value, modulus);
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = result * factor % modulus;
            factor = factor * factor % modulus;
            exponent >>= 1;
        }
        return (int)result;
    }
}
