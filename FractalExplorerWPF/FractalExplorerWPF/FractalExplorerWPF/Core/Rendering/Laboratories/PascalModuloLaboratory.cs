using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Треугольник Паскаля по модулю N.</summary>
internal static class PascalModuloLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int rows = Math.Clamp(state.PrimaryValue, 8, 2_000);
        int modulus = Math.Clamp(state.SecondaryValue, 2, 256);
        int cellSize = Math.Clamp(state.TertiaryValue, 1, 8);
        int[] previous = new int[rows + 1];
        int[] current = new int[rows + 1];
        previous[0] = 1;
        int rowStride = Math.Max(1, rows / Math.Max(64, canvas.Height * 2));

        for (int row = 0; row < rows; row++)
        {
            if ((row & 31) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(8 + row * 86 / rows);
            }
            if (row % rowStride == 0 || row == rows - 1)
            {
                double y = -0.92 + 1.84 * row / Math.Max(1, rows - 1d);
                for (int column = 0; column <= row; column++)
                {
                    int residue = previous[column];
                    if (state.Mode == 1 && residue != 0) continue;
                    double x = row == 0 ? 0 : 0.92 * (2d * column - row) / rows;
                    Color color = state.Mode switch
                    {
                        1 => state.AccentColor,
                        _ => residue == 0
                            ? Mix(state.BackgroundColor, state.PrimaryColor, 0.18)
                            : Palette(state, (double)residue / modulus)
                    };
                    (double sx, double sy) = canvas.Map(x, y);
                    int size = Math.Max(1, (int)Math.Round(cellSize * Math.Max(0.55, state.Zoom)));
                    canvas.PixelSquare((int)Math.Round(sx), (int)Math.Round(sy), size, color,
                        Math.Clamp(state.Parameter, 0.2, 2.5) / 2.5);
                }
            }

            Array.Clear(current);
            current[0] = current[row + 1] = 1 % modulus;
            for (int column = 1; column <= row; column++)
                current[column] = (previous[column - 1] + previous[column]) % modulus;
            (previous, current) = (current, previous);
        }

        if (state.Mode == 2 && state.ShowGuides)
        {
            for (long power = modulus; power < rows; power *= modulus)
            {
                double y = -0.92 + 1.84 * power / rows;
                canvas.Line(-0.94, y, 0.94, y, 1, state.AccentColor, 0.35);
                if (power > rows / Math.Max(2, modulus)) break;
            }
        }
    }
}
