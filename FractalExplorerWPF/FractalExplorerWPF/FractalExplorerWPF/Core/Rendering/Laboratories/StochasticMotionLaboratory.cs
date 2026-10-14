using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Броуновское движение и полёты Леви.</summary>
internal static class StochasticMotionLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int requestedSteps = Math.Clamp(state.PrimaryValue, 20, 200_000);
        int steps = state.Animate
            ? Math.Clamp((int)Math.Round(requestedSteps * Math.Max(0.01, state.Phase)), 2, requestedSteps)
            : requestedSteps;
        int paths = Math.Clamp(state.SecondaryValue, 1, 1_000);
        int mode = Math.Clamp(state.Mode, 0, 4);
        const int pointBudget = 1_800_000;
        if (mode != 3 && (long)steps * paths > pointBudget)
            paths = Math.Max(1, pointBudget / steps);
        double parameter = Math.Clamp(state.Parameter, 0.2, 2);
        var allPaths = new List<List<LaboratoryPoint>>(mode == 3 ? 0 : paths);
        var endpoints = new List<LaboratoryPoint>(paths);
        double startX = state.AnchorX;
        double startY = state.AnchorY;

        for (int pathIndex = 0; pathIndex < paths; pathIndex++)
        {
            if ((pathIndex & 7) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(8 + pathIndex * 62 / Math.Max(1, paths));
            }
            var random = new Random(unchecked(state.TertiaryValue + pathIndex * 1_000_003));
            double x = startX, y = startY;
            if (mode == 3)
            {
                double elapsedFraction = Math.Sqrt((double)steps / requestedSteps);
                x += NextGaussian(random) * 0.72 * parameter * elapsedFraction;
                y += NextGaussian(random) * 0.72 * parameter * elapsedFraction;
                endpoints.Add(new LaboratoryPoint(x, y));
                continue;
            }
            double brownianScale = 0.72 * parameter / Math.Sqrt(requestedSteps);
            double levyScale = 0.68 / Math.Pow(requestedSteps, 1 / Math.Max(0.35, parameter));
            double direction = random.NextDouble() * Math.Tau;
            List<LaboratoryPoint>? points = mode == 3 ? null : new List<LaboratoryPoint>(steps + 1)
            {
                new(startX, startY)
            };
            for (int step = 1; step <= steps; step++)
            {
                if ((step & 8191) == 0) token.ThrowIfCancellationRequested();
                switch (mode)
                {
                    case 1:
                        double levyAngle = random.NextDouble() * Math.Tau;
                        double length = levyScale * Math.Pow(Math.Max(1e-12, random.NextDouble()), -1 / parameter);
                        length = Math.Min(length, 0.42);
                        x += Math.Cos(levyAngle) * length;
                        y += Math.Sin(levyAngle) * length;
                        break;
                    case 4:
                        double inertia = Math.Clamp(parameter / 2, 0.1, 0.985);
                        direction += NextGaussian(random) * (1 - inertia) * 2.8;
                        x += Math.Cos(direction) * 0.82 / Math.Sqrt(requestedSteps);
                        y += Math.Sin(direction) * 0.82 / Math.Sqrt(requestedSteps);
                        break;
                    default:
                        x += NextGaussian(random) * brownianScale;
                        y += NextGaussian(random) * brownianScale;
                        break;
                }
                points?.Add(new LaboratoryPoint(x, y));
            }

            if (mode == 2 && points is not null)
            {
                double endDx = points[^1].X - startX;
                double endDy = points[^1].Y - startY;
                for (int i = 1; i < points.Count; i++)
                {
                    double amount = (double)i / (points.Count - 1);
                    points[i] = new LaboratoryPoint(points[i].X - endDx * amount, points[i].Y - endDy * amount);
                }
                x = points[^1].X; y = points[^1].Y;
            }
            if (points is not null) allPaths.Add(points);
            endpoints.Add(new LaboratoryPoint(x, y));
        }

        if (state.ShowGuides)
        {
            canvas.Circle(startX, startY, 0.025, 1.5, state.AccentColor, false, 1);
            canvas.Circle(startX, startY, 0.01, 1, state.AccentColor, true, 1);
            canvas.Circle(startX, startY, 0.72 * (mode == 1 ? 1 : parameter), 1,
                Mix(state.PrimaryColor, state.BackgroundColor, 0.68), false, 0.55);
        }

        if (mode == 3)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                Color color = Palette(state, (double)i / Math.Max(1, endpoints.Count));
                canvas.Circle(endpoints[i].X, endpoints[i].Y, 0.008, 1, color, true, 0.34);
            }
        }
        else
        {
            for (int pathIndex = 0; pathIndex < allPaths.Count; pathIndex++)
            {
                List<LaboratoryPoint> points = allPaths[pathIndex];
                Color color = Palette(state, (double)pathIndex / Math.Max(1, allPaths.Count));
                double opacity = Math.Clamp(1.1 / Math.Sqrt(allPaths.Count), 0.16, 0.86);
                for (int i = 1; i < points.Count; i++)
                    canvas.Line(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y,
                        mode == 1 ? 1.35 : 1.05, color, opacity);
            }
        }
        progress?.Report(95);
    }

    private static double NextGaussian(Random random)
    {
        double u1 = Math.Max(1e-12, random.NextDouble());
        double u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(Math.Tau * u2);
    }
}
