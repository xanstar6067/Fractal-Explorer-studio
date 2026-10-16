using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FractalExplorerWPF.Core.PercolationMath;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

internal static class PercolationLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state,
        CancellationToken token, IProgress<int>? progress, IProgress<PercolationStatistics>? statistics)
    {
        int n = Math.Clamp(state.PrimaryValue, 16, 768);
        var lattice = state.Mode == 1 ? PercolationLattice.Triangular : PercolationLattice.Square;
        PercolationResult field = Percolation.Generate(n, state.Parameter, state.TertiaryValue, lattice, token);
        progress?.Report(35);
        var colors = new Color[field.ClusterSizes.Length];
        Color dim = Mix(state.BackgroundColor, state.PrimaryColor, 0.16);
        for (int label = 1; label < colors.Length; label++)
            colors[label] = state.SecondaryValue switch
            {
                1 => label == field.LargestLabel ? state.PrimaryColor : dim,
                2 => field.IsSpanning(label) ? state.AccentColor : dim,
                3 => Mix(state.SecondaryColor, state.PrimaryColor,
                    Math.Log(1 + field.ClusterSizes[label]) / Math.Log(1 + Math.Max(1, field.Statistics.LargestSize))),
                _ => Palette(state, label * 0.6180339887498949)
            };
        var path = new bool[n * n];
        if (state.Filled) foreach (int index in field.Path) path[index] = true;
        Color pathColor = Mix(state.AccentColor, Colors.White, 0.55);
        double step = 1.8 / n, rowHeight = lattice == PercolationLattice.Triangular ? Math.Sqrt(3) / 2 : 1;
        // Аффинное обратное преобразование один раз на кадр, а не тригонометрия для каждого пикселя.
        var origin = canvas.Unmap(0.5, 0.5);
        var right = canvas.Unmap(1.5, 0.5);
        var down = canvas.Unmap(0.5, 1.5);
        bool grid = state.ShowGuides && step * state.Zoom * canvas.Scale >= 6;
        for (int py = 0; py < canvas.Height; py++)
        {
            token.ThrowIfCancellationRequested();
            double wx = origin.x + py * (down.x - origin.x), wy = origin.y + py * (down.y - origin.y);
            for (int px = 0; px < canvas.Width; px++, wx += right.x - origin.x, wy += right.y - origin.y)
            {
                double gx = (wx + 0.9) / step - 0.5;
                double gy = (0.9 * rowHeight - wy) / (step * rowHeight) - 0.5;
                int x, y; bool edge;
                if (lattice == PercolationLattice.Square)
                {
                    x = (int)Math.Floor(gx + 0.5); y = (int)Math.Floor(gy + 0.5);
                    edge = Math.Abs(gx - x) > 0.44 || Math.Abs(gy - y) > 0.44;
                }
                else
                {
                    int row = (int)Math.Floor(gy + 0.5);
                    x = y = 0;
                    double best = double.MaxValue, second = double.MaxValue;
                    for (int r = row - 1; r <= row + 1; r++)
                    {
                        double offset = (r & 1) * 0.5;
                        int col = (int)Math.Floor(gx - offset + 0.5);
                        // Два ближайших столбца нужны и для границ шестиугольных ячеек Вороного.
                        for (int c = col - 1; c <= col + 1; c++)
                        {
                            double dx = gx - c - offset, dy = (gy - r) * rowHeight;
                            double distance = dx * dx + dy * dy;
                            if (distance < best) { second = best; best = distance; x = c; y = r; }
                            else if (distance < second) second = distance;
                        }
                    }
                    edge = second - best < 0.09;
                }
                if (x < 0 || x >= n || y < 0 || y >= n) continue;
                int index = y * n + x, label = field.Labels[index];
                if (label == 0 || (grid && edge)) continue;
                canvas.SetPixel(px, py, path[index] ? pathColor : colors[label]);
            }
            if ((py & 31) == 0) progress?.Report(35 + 60 * py / canvas.Height);
        }
        statistics?.Report(field.Statistics);
    }
}
