using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Диаграммы Вороного и релаксация Ллойда.</summary>
internal static class VoronoiLloydLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        List<VoronoiSite> sites = CreateVoronoiSites(state);
        if (sites.Count == 0) return;

        int relaxationIterations = state.Mode is 1 or 4 ? Math.Clamp(state.SecondaryValue, 0, 80) : 0;
        if (state.Animate)
            relaxationIterations = (int)Math.Round(relaxationIterations * Math.Clamp(state.Phase, 0, 1));
        RelaxSites(sites, relaxationIterations, state.Mode, token, progress);

        long area = (long)canvas.Width * canvas.Height;
        int stride = area switch
        {
            > 32_000_000 => 4,
            > 8_000_000 => 2,
            _ => 1
        };
        int gridWidth = (canvas.Width + stride - 1) / stride;
        int gridHeight = (canvas.Height + stride - 1) / stride;
        var labels = new ushort[checked(gridWidth * gridHeight)];

        for (int gy = 0; gy < gridHeight; gy++)
        {
            if ((gy & 15) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(22 + gy * 54 / Math.Max(1, gridHeight));
            }
            int py = Math.Min(canvas.Height - 1, gy * stride + stride / 2);
            for (int gx = 0; gx < gridWidth; gx++)
            {
                int px = Math.Min(canvas.Width - 1, gx * stride + stride / 2);
                (double x, double y) = canvas.Unmap(px, py);
                int nearest = FindNearestSite(sites, x, y, state.Mode);
                labels[gy * gridWidth + gx] = (ushort)nearest;
                Color cellColor = state.Filled
                    ? Mix(state.BackgroundColor, sites[nearest].Color, 0.58)
                    : Mix(state.BackgroundColor, sites[nearest].Color, 0.08);
                canvas.FillScreenRect(gx * stride, gy * stride, stride, stride, cellColor);
            }
        }

        var neighbours = state.Mode == 4 ? new HashSet<long>() : null;
        Color boundary = Mix(state.PrimaryColor, Color.FromRgb(245, 248, 255), 0.58);
        double thickness = Math.Clamp(state.Parameter, 0.25, 8);
        for (int gy = 0; gy < gridHeight; gy++)
        {
            if ((gy & 31) == 0) token.ThrowIfCancellationRequested();
            for (int gx = 0; gx < gridWidth; gx++)
            {
                int index = gy * gridWidth + gx;
                int current = labels[index];
                if (gx > 0 && labels[index - 1] != current)
                {
                    AddNeighbour(neighbours, current, labels[index - 1]);
                    canvas.ScreenLine(gx * stride, gy * stride, gx * stride, (gy + 1) * stride,
                        thickness, boundary, 0.92);
                }
                if (gy > 0 && labels[index - gridWidth] != current)
                {
                    AddNeighbour(neighbours, current, labels[index - gridWidth]);
                    canvas.ScreenLine(gx * stride, gy * stride, (gx + 1) * stride, gy * stride,
                        thickness, boundary, 0.92);
                }
            }
        }

        if (neighbours is not null && state.ShowGuides)
        {
            foreach (long pair in neighbours)
            {
                int first = (int)(pair >> 32);
                int second = (int)pair;
                canvas.Line(sites[first].X, sites[first].Y, sites[second].X, sites[second].Y,
                    Math.Max(0.7, thickness * 0.7), state.AccentColor, 0.44);
            }
        }

        if (state.ShowGuides)
        {
            double radius = Math.Clamp(0.011 / Math.Sqrt(Math.Max(0.2, state.Zoom)), 0.003, 0.018);
            foreach (VoronoiSite site in sites)
            {
                canvas.Circle(site.X, site.Y, radius * 1.8, 1, state.BackgroundColor, true, 0.82);
                canvas.Circle(site.X, site.Y, radius, 1, state.AccentColor, true, 1);
            }
        }
    }

    private static List<VoronoiSite> CreateVoronoiSites(MathematicalLaboratoryState state)
    {
        int count = Math.Clamp(state.PrimaryValue, 3, 256);
        var random = new Random(state.TertiaryValue);
        var sites = new List<VoronoiSite>(count);
        if (state.InputPoints.Count > 0)
        {
            foreach (LaboratoryPoint point in state.InputPoints.Take(256))
                sites.Add(new VoronoiSite(point.X, point.Y, random.NextDouble() * 0.16 - 0.08,
                    Palette(state, random.NextDouble())));
            return sites;
        }

        double shiftX = random.NextDouble();
        double shiftY = random.NextDouble();
        for (int i = 0; i < count; i++)
        {
            double x = -0.94 + 1.88 * ((Halton(i + 1, 2) + shiftX) % 1);
            double y = -0.94 + 1.88 * ((Halton(i + 1, 3) + shiftY) % 1);
            x += (random.NextDouble() - 0.5) * 0.035;
            y += (random.NextDouble() - 0.5) * 0.035;
            sites.Add(new VoronoiSite(x, y, random.NextDouble() * 0.16 - 0.08,
                Palette(state, (i * 0.61803398875 + random.NextDouble() * 0.08) % 1)));
        }
        return sites;
    }

    private static void RelaxSites(
        List<VoronoiSite> sites,
        int iterations,
        int mode,
        CancellationToken token,
        IProgress<int>? progress)
    {
        const int resolution = 150;
        var sumX = new double[sites.Count];
        var sumY = new double[sites.Count];
        var counts = new int[sites.Count];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            token.ThrowIfCancellationRequested();
            Array.Clear(sumX); Array.Clear(sumY); Array.Clear(counts);
            for (int yIndex = 0; yIndex < resolution; yIndex++)
            {
                double y = -0.98 + 1.96 * (yIndex + 0.5) / resolution;
                for (int xIndex = 0; xIndex < resolution; xIndex++)
                {
                    double x = -0.98 + 1.96 * (xIndex + 0.5) / resolution;
                    int nearest = FindNearestSite(sites, x, y, mode);
                    sumX[nearest] += x;
                    sumY[nearest] += y;
                    counts[nearest]++;
                }
            }
            for (int i = 0; i < sites.Count; i++)
                if (counts[i] > 0)
                    sites[i] = sites[i] with
                    {
                        X = sumX[i] / counts[i],
                        Y = sumY[i] / counts[i]
                    };
            progress?.Report(8 + (iteration + 1) * 12 / Math.Max(1, iterations));
        }
    }

    private static int FindNearestSite(IReadOnlyList<VoronoiSite> sites, double x, double y, int mode)
    {
        int nearest = 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < sites.Count; i++)
        {
            double dx = x - sites[i].X;
            double dy = y - sites[i].Y;
            double distance = mode switch
            {
                2 => dx * dx + dy * dy - sites[i].Weight,
                3 => Math.Abs(dx) + Math.Abs(dy),
                _ => dx * dx + dy * dy
            };
            if (distance >= best) continue;
            best = distance;
            nearest = i;
        }
        return nearest;
    }

    private static void AddNeighbour(HashSet<long>? neighbours, int left, int right)
    {
        if (neighbours is null || left == right) return;
        int first = Math.Min(left, right);
        int second = Math.Max(left, right);
        neighbours.Add(((long)first << 32) | (uint)second);
    }

    private static double Halton(int index, int radix)
    {
        double result = 0;
        double fraction = 1d / radix;
        while (index > 0)
        {
            result += fraction * (index % radix);
            index /= radix;
            fraction /= radix;
        }
        return result;
    }

    private readonly record struct VoronoiSite(double X, double Y, double Weight, Color Color);
}
