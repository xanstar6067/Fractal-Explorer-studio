using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Последовательность Рекамана.</summary>
internal static class RecamanSequenceLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int requested = Math.Clamp(state.PrimaryValue, 10, 20_000);
        int count = state.Animate
            ? Math.Clamp((int)Math.Round(requested * Math.Max(0.015, state.Phase)), 2, requested)
            : requested;
        long current = Math.Max(0, state.SecondaryValue);
        var used = new HashSet<long> { current };
        var values = new long[count];
        values[0] = current;
        for (int n = 1; n < count; n++)
        {
            if ((n & 1023) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(8 + n * 30 / count);
            }
            long backwards = current - n;
            current = backwards >= 0 && !used.Contains(backwards) ? backwards : current + n;
            used.Add(current);
            values[n] = current;
        }

        long minimum = values.Min();
        long maximum = values.Max();
        double range = Math.Max(1, maximum - minimum);
        double X(long value) => -0.92 + 1.84 * (value - minimum) / range;
        double thickness = Math.Clamp(state.Parameter, 0.25, 10);
        int period = Math.Clamp(state.TertiaryValue, 1, 2_000);

        switch (Math.Clamp(state.Mode, 0, 3))
        {
            case 0:
            case 1:
                if (state.ShowGuides)
                    canvas.Line(-0.96, 0, 0.96, 0, 1, Mix(state.PrimaryColor, state.BackgroundColor, 0.58), 0.8);
                for (int i = 1; i < count; i++)
                {
                    if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                    double x1 = X(values[i - 1]);
                    double x2 = X(values[i]);
                    double center = (x1 + x2) * 0.5;
                    double radius = Math.Abs(x2 - x1) * 0.5;
                    double sign = state.Mode == 1 || (i & 1) == 0 ? 1 : -1;
                    int segments = Math.Clamp((int)(radius * canvas.Scale * Math.PI / 3), 8, 180);
                    Color color = Palette(state, (double)(i % period) / period);
                    double previousX = x1, previousY = 0;
                    for (int segment = 1; segment <= segments; segment++)
                    {
                        double angle = Math.PI * segment / segments;
                        double x = center - (x2 >= x1 ? 1 : -1) * radius * Math.Cos(angle);
                        double y = sign * radius * Math.Sin(angle);
                        canvas.Line(previousX, previousY, x, y, thickness, color, 0.78);
                        previousX = x; previousY = y;
                    }
                }
                break;
            case 2:
                if (state.ShowGuides)
                    canvas.Circle(0, 0, 0.88, 1, Mix(state.PrimaryColor, state.BackgroundColor, 0.6), false, 0.9);
                for (int i = 1; i < count; i++)
                {
                    if ((i & 511) == 0) token.ThrowIfCancellationRequested();
                    double a1 = Math.Tau * (values[i - 1] - minimum) / range - Math.PI / 2;
                    double a2 = Math.Tau * (values[i] - minimum) / range - Math.PI / 2;
                    canvas.Line(0.88 * Math.Cos(a1), 0.88 * Math.Sin(a1),
                        0.88 * Math.Cos(a2), 0.88 * Math.Sin(a2), thickness,
                        Palette(state, (double)(i % period) / period), 0.32);
                }
                break;
            default:
                var points = new LaboratoryPoint[count];
                double px = 0, py = 0, maxRadius = 1;
                points[0] = new LaboratoryPoint(0, 0);
                for (int i = 1; i < count; i++)
                {
                    double angle = values[i] * 2.399963229728653;
                    double step = 0.3 + Math.Sqrt(i);
                    px += Math.Cos(angle) * step;
                    py += Math.Sin(angle) * step;
                    points[i] = new LaboratoryPoint(px, py);
                    maxRadius = Math.Max(maxRadius, Math.Max(Math.Abs(px), Math.Abs(py)));
                }
                for (int i = 1; i < count; i++)
                    canvas.Line(points[i - 1].X * 0.9 / maxRadius, points[i - 1].Y * 0.9 / maxRadius,
                        points[i].X * 0.9 / maxRadius, points[i].Y * 0.9 / maxRadius, thickness,
                        Palette(state, (double)(i % period) / period), 0.72);
                break;
        }
        progress?.Report(94);
    }
}
