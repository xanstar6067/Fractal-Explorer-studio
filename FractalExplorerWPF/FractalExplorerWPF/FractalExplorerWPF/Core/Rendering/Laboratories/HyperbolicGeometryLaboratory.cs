using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Гиперболическая геометрия в модели диска Пуанкаре.</summary>
internal static class HyperbolicGeometryLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        canvas.Circle(0, 0, 0.94, 2, state.AccentColor, false);
        if (state.Mode == 4)
        {
            int count = Math.Clamp(state.SecondaryValue * state.TertiaryValue, 8, 120);
            for (int i = 0; i < count; i++)
            {
                double midpoint = Math.Tau * i / count + state.Rotation;
                double delta = 0.18 + 1.18 * ((i * 37) % count) / count;
                DrawPoincareGeodesic(canvas, midpoint, delta, Palette(state, (double)i / count));
            }
            return;
        }

        int depth = Math.Clamp(state.PrimaryValue, 1, 9);
        (int p, int q) = state.Mode switch
        {
            0 => (3, 7),
            1 => (4, 5),
            2 => (6, 4),
            _ => (Math.Clamp(state.SecondaryValue, 3, 12), Math.Clamp(state.TertiaryValue, 3, 12))
        };
        double curvature = Math.Clamp(state.Parameter, 0.15, 1.5);
        var previousRing = new List<LaboratoryPoint> { new(0, 0) };
        DrawRegularPolygon(canvas, 0, 0, 0.16, p, 0, Palette(state, 0), state.Filled);

        for (int layer = 1; layer <= depth; layer++)
        {
            int count = Math.Min(3_000, p * (int)Math.Pow(Math.Max(2, q - 2), layer - 1));
            double radius = 0.94 * Math.Tanh(curvature * layer / Math.Max(1, depth) * 2.1) / Math.Tanh(curvature * 2.1);
            double tileRadius = Math.Clamp(0.38 * (1 - radius * radius) / Math.Sqrt(layer + 1), 0.006, 0.16);
            var ring = new List<LaboratoryPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.Tau * (i + (layer & 1) * 0.5) / count;
                var point = new LaboratoryPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
                ring.Add(point);
                LaboratoryPoint parent = previousRing[Math.Min(previousRing.Count - 1,
                    (int)((long)i * previousRing.Count / count))];
                canvas.Line(parent.X, parent.Y, point.X, point.Y, 0.75,
                    Palette(state, (double)layer / depth), 0.34);
                if (i > 0)
                    canvas.Line(ring[i - 1].X, ring[i - 1].Y, point.X, point.Y, 0.7,
                        Palette(state, (double)layer / depth), 0.28);
                DrawRegularPolygon(canvas, point.X, point.Y, tileRadius, p, angle,
                    Palette(state, (layer + (double)i / count) / (depth + 1)), state.Filled);
            }
            if (ring.Count > 2)
                canvas.Line(ring[^1].X, ring[^1].Y, ring[0].X, ring[0].Y, 0.7,
                    Palette(state, (double)layer / depth), 0.28);
            previousRing = ring;
            token.ThrowIfCancellationRequested();
        }
    }

    private static void DrawPoincareGeodesic(LaboratoryCanvas canvas, double midpoint, double delta, Color color)
    {
        delta = Math.Clamp(delta, 0.08, Math.PI / 2 - 0.04);
        double centerDistance = 0.94 / Math.Cos(delta);
        double radius = 0.94 * Math.Tan(delta);
        double cx = centerDistance * Math.Cos(midpoint);
        double cy = centerDistance * Math.Sin(midpoint);
        LaboratoryPoint? previous = null;
        for (int i = 0; i <= 360; i++)
        {
            double angle = Math.Tau * i / 360;
            var point = new LaboratoryPoint(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
            bool inside = point.X * point.X + point.Y * point.Y <= 0.94 * 0.94 + 1e-5;
            if (inside && previous is { } last)
                canvas.Line(last.X, last.Y, point.X, point.Y, 1.1, color, 0.74);
            previous = inside ? point : null;
        }
    }

    private static void DrawRegularPolygon(
        LaboratoryCanvas canvas, double x, double y, double radius, int sides, double angle,
        Color color, bool filled)
    {
        var points = new LaboratoryPoint[sides];
        for (int i = 0; i < sides; i++)
        {
            double theta = angle + Math.Tau * i / sides;
            points[i] = new LaboratoryPoint(x + radius * Math.Cos(theta), y + radius * Math.Sin(theta));
        }
        if (filled) canvas.Polygon(points, color, true, 0.42);
        canvas.Polygon(points, color, false, 0.8);
    }
}
