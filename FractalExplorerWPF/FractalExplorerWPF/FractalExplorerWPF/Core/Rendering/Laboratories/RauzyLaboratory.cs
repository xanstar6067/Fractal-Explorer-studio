using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Core.RauzyMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Finite prefix approximation of the central Rauzy tile and its lattice translates.</summary>
internal static class RauzyLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state,
        CancellationToken token, IProgress<int>? progress)
    {
        int mode = Math.Clamp(state.Mode, 0, 5);
        int coefficient = Math.Clamp(state.SecondaryValue, 1, 4);
        int radius = mode == 4 ? Math.Clamp(state.TertiaryValue, 1, 4) : 0;
        int copies = (2 * radius + 1) * (2 * radius + 1);
        int count = Math.Max(100, Math.Clamp(state.PrimaryValue, 100, 2_000_000) / copies);
        RauzyFractal.Sample[] samples = RauzyFractal.Generate(count, coefficient, token);
        RauzyFractal.Projection projection = RauzyFractal.CreateProjection(coefficient);
        progress?.Report(25);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var sample in samples)
        {
            minX = Math.Min(minX, sample.Position.Real); maxX = Math.Max(maxX, sample.Position.Real);
            minY = Math.Min(minY, sample.Position.Imaginary); maxY = Math.Max(maxY, sample.Position.Imaginary);
        }
        Complex center = new((minX + maxX) / 2, (minY + maxY) / 2);
        double spanX = maxX - minX + 2 * radius * (Math.Abs(projection.PeriodU.Real) + Math.Abs(projection.PeriodV.Real));
        double spanY = maxY - minY + 2 * radius * (Math.Abs(projection.PeriodU.Imaginary) + Math.Abs(projection.PeriodV.Imaginary));
        double scale = 1.8 / Math.Max(spanX, spanY);
        double phase = double.IsFinite(state.Phase) ? state.Phase - Math.Floor(state.Phase) : 0;
        int visible = state.Animate ? Math.Max(1, (int)(count * (0.02 + 0.98 * phase))) : count;
        // Labels retain tile identity, so outlines include seams between adjacent translates.
        var labels = new byte[canvas.Width * canvas.Height];
        var order = mode == 5 ? new float[labels.Length] : null;
        double pointRadius = Math.Clamp(state.Parameter, 0.3, 2.5);
        (double ox, double oy) = canvas.Map(0, 0);
        (double ux, double uy) = canvas.Map(scale, 0);
        (double vx, double vy) = canvas.Map(0, scale);
        ux -= ox; uy -= oy; vx -= ox; vy -= oy;
        int tile = 0;
        for (int u = -radius; u <= radius; u++)
        for (int v = -radius; v <= radius; v++, tile++)
        {
            Complex shift = u * projection.PeriodU + v * projection.PeriodV - center;
            for (int i = 0; i < visible; i++)
            {
                if ((i & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(25 + (int)(60L * (tile * (long)visible + i) / (copies * (long)visible)));
                }
                var sample = samples[i];
                if (mode is >= 1 and <= 3 && sample.Letter != mode - 1) continue;
                Complex z = sample.Position + shift;
                double sx = ox + z.Real * ux + z.Imaginary * vx;
                double sy = oy + z.Real * uy + z.Imaginary * vy;
                if (sx < -pointRadius || sy < -pointRadius || sx > canvas.Width + pointRadius || sy > canvas.Height + pointRadius) continue;
                int left = Math.Max(0, (int)Math.Floor(sx - pointRadius));
                int right = Math.Min(canvas.Width - 1, (int)Math.Ceiling(sx + pointRadius));
                int top = Math.Max(0, (int)Math.Floor(sy - pointRadius));
                int bottom = Math.Min(canvas.Height - 1, (int)Math.Ceiling(sy + pointRadius));
                for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    double dx = x + 0.5 - sx, dy = y + 0.5 - sy;
                    if (dx * dx + dy * dy > pointRadius * pointRadius) continue;
                    int pixel = y * canvas.Width + x;
                    labels[pixel] = (byte)(1 + tile * 3 + sample.Letter);
                    if (order is not null) order[pixel] = (float)i / count;
                }
            }
        }

        Color[] colors = [state.PrimaryColor, state.SecondaryColor, state.AccentColor];
        for (int y = 0; y < canvas.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < canvas.Width; x++)
            {
                int pixel = y * canvas.Width + x;
                byte label = labels[pixel];
                if (label == 0) continue;
                bool edge = x == 0 || y == 0 || x == canvas.Width - 1 || y == canvas.Height - 1 ||
                    labels[pixel - 1] != label || labels[pixel + 1] != label ||
                    labels[pixel - canvas.Width] != label || labels[pixel + canvas.Width] != label;
                if (!state.Filled && !edge) continue;
                Color color = order is null ? colors[(label - 1) % 3] : LaboratoryHelpers.Palette(state, order[pixel]);
                if (state.ShowGuides && state.Filled && edge)
                    color = Color.FromRgb((byte)(color.R * 0.55), (byte)(color.G * 0.55), (byte)(color.B * 0.55));
                canvas.SetPixel(x, y, color);
            }
        }
        if (state.ShowGuides && mode == 4)
        {
            Complex origin = -center * scale;
            foreach (Complex period in new[] { projection.PeriodU, projection.PeriodV })
            {
                Complex end = origin + period * scale;
                canvas.Line(origin.Real, origin.Imaginary, end.Real, end.Imaginary, 1.3, state.AccentColor);
                canvas.Circle(end.Real, end.Imaginary, 0.012, 1, state.AccentColor, true);
            }
        }
        progress?.Report(95);
    }
}
