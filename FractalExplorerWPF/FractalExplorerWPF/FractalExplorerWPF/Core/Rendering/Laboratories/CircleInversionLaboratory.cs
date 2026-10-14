using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Инверсия окружностей и преобразования Мёбиуса.</summary>
internal static class CircleInversionLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        double inversionRadius = Math.Clamp(state.Parameter, 0.05, 2);
        double cx = state.AnchorX, cy = state.AnchorY;
        canvas.Circle(cx, cy, inversionRadius, 1.5, state.AccentColor, false);
        canvas.Circle(cx, cy, 0.012, 1, state.AccentColor, true);

        if (state.Mode == 1)
        {
            int count = Math.Clamp(state.PrimaryValue, 3, 500);
            int symmetry = Math.Clamp(state.TertiaryValue, 2, 24);
            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                double angle = Math.Tau * i / symmetry;
                double ring = 0.22 + 0.55 * ((i / symmetry) % Math.Max(1, count / symmetry)) / Math.Max(1d, count / symmetry);
                double radius = 0.025 + 0.045 * (0.5 + 0.5 * Math.Sin(i * 1.73));
                double ox = ring * Math.Cos(angle), oy = ring * Math.Sin(angle);
                canvas.Circle(ox, oy, radius, 1, state.PrimaryColor, state.Filled);
                if (TryInvertCircle(ox, oy, radius, cx, cy, inversionRadius, out double ix, out double iy, out double ir))
                    canvas.Circle(ix, iy, ir, 1, Palette(state, (double)i / count), state.Filled);
            }
            return;
        }

        List<LaboratoryPoint> points = state.InputPoints.Count > 0
            ? state.InputPoints
            : CreateInversionSeed(Math.Clamp(state.PrimaryValue, 3, 2_000), Math.Clamp(state.TertiaryValue, 2, 24));
        if (state.Mode == 3)
        {
            Complex a = new(1, 0.18), b = new(-0.18, 0.24), c = new(0.22, -0.17), d = Complex.One;
            for (int i = 0; i < points.Count; i++)
            {
                Complex z = new(points[i].X, points[i].Y);
                Complex denominator = c * z + d;
                if (denominator.Magnitude < 1e-8) continue;
                Complex mapped = (a * z + b) / denominator;
                canvas.Line(z.Real, z.Imaginary, mapped.Real, mapped.Imaginary, 1,
                    Palette(state, (double)i / points.Count), 0.5);
                canvas.Circle(mapped.Real, mapped.Imaginary, 0.009, 1,
                    Palette(state, (double)i / points.Count), true);
            }
            return;
        }

        int repetitions = state.Mode == 2 ? Math.Clamp(state.SecondaryValue, 1, 12) : 1;
        int symmetryCount = Math.Clamp(state.TertiaryValue, 2, 24);
        for (int i = 0; i < points.Count; i++)
        {
            if ((i & 127) == 0) token.ThrowIfCancellationRequested();
            LaboratoryPoint current = points[i];
            canvas.Circle(current.X, current.Y, 0.007, 1, state.PrimaryColor, true);
            for (int iteration = 0; iteration < repetitions; iteration++)
            {
                double localCx = cx, localCy = cy;
                if (state.Mode == 2 && iteration > 0)
                {
                    double angle = Math.Tau * iteration / symmetryCount;
                    localCx += 0.42 * Math.Cos(angle);
                    localCy += 0.42 * Math.Sin(angle);
                }
                if (!TryInvertPoint(current.X, current.Y, localCx, localCy, inversionRadius,
                        out double ix, out double iy)) break;
                canvas.Line(current.X, current.Y, ix, iy, 1,
                    Palette(state, (double)(i + iteration) / (points.Count + repetitions)), 0.4);
                current = new LaboratoryPoint(ix, iy);
                canvas.Circle(ix, iy, 0.009, 1,
                    Palette(state, (double)i / points.Count), true);
            }
        }
    }

    private static List<LaboratoryPoint> CreateInversionSeed(int count, int symmetry)
    {
        var result = new List<LaboratoryPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = Math.Tau * i / symmetry;
            double radial = 0.18 + 0.68 * (i / symmetry + 1d) / (count / symmetry + 1d);
            radial += 0.04 * Math.Sin(i * 2.399963229728653);
            result.Add(new LaboratoryPoint(radial * Math.Cos(angle), radial * Math.Sin(angle)));
        }
        return result;
    }

    private static bool TryInvertPoint(
        double x, double y, double centerX, double centerY, double radius,
        out double resultX, out double resultY)
    {
        double dx = x - centerX, dy = y - centerY;
        double squared = dx * dx + dy * dy;
        if (squared < 1e-10)
        {
            resultX = resultY = 0;
            return false;
        }
        double factor = radius * radius / squared;
        resultX = centerX + dx * factor;
        resultY = centerY + dy * factor;
        return double.IsFinite(resultX) && double.IsFinite(resultY);
    }

    private static bool TryInvertCircle(
        double x, double y, double circleRadius,
        double centerX, double centerY, double inversionRadius,
        out double resultX, out double resultY, out double resultRadius)
    {
        double dx = x - centerX, dy = y - centerY;
        double denominator = dx * dx + dy * dy - circleRadius * circleRadius;
        if (Math.Abs(denominator) < 1e-8)
        {
            resultX = resultY = resultRadius = 0;
            return false;
        }
        double factor = inversionRadius * inversionRadius / denominator;
        resultX = centerX + dx * factor;
        resultY = centerY + dy * factor;
        resultRadius = Math.Abs(factor * circleRadius);
        return resultRadius < 20 && double.IsFinite(resultX) && double.IsFinite(resultY);
    }
}
