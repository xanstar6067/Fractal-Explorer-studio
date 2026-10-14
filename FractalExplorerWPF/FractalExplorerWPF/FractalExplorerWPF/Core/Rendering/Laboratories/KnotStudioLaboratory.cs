using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Узлы и косы с псевдотрёхмерной проекцией.</summary>
internal static class KnotStudioLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int p = Math.Clamp(state.PrimaryValue, 1, 32);
        int q = Math.Clamp(state.SecondaryValue, 1, 64);
        int samples = Math.Clamp(state.TertiaryValue, 200, 30_000);
        samples = Math.Min(samples, Math.Max(800, canvas.Width * 5));
        double spatialRotation = state.Phase * Math.Tau;
        var segments = new List<KnotSegment>(samples * Math.Min(p, 8));

        if (state.Mode == 2)
        {
            int strands = Math.Clamp(p, 2, 16);
            int perStrand = Math.Max(160, samples / strands);
            for (int strand = 0; strand < strands; strand++)
            {
                Point3 previous = ProjectKnotPoint(CreateBraidPoint(0, strand, strands, q), spatialRotation);
                for (int i = 1; i <= perStrand; i++)
                {
                    double t = Math.Tau * i / perStrand;
                    Point3 point = ProjectKnotPoint(CreateBraidPoint(t, strand, strands, q), spatialRotation);
                    segments.Add(new KnotSegment(previous, point, (double)i / perStrand + (double)strand / strands));
                    previous = point;
                }
            }
        }
        else
        {
            Point3 previous = ProjectKnotPoint(CreateKnotPoint(0, p, q, state.Mode), spatialRotation);
            for (int i = 1; i <= samples; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(8 + i * 45 / samples);
                }
                double t = Math.Tau * i / samples;
                Point3 point = ProjectKnotPoint(CreateKnotPoint(t, p, q, state.Mode), spatialRotation);
                segments.Add(new KnotSegment(previous, point, (double)i / samples));
                previous = point;
            }
        }

        if (state.ShowGuides)
        {
            canvas.Circle(0, 0, 0.68, 1, Mix(state.PrimaryColor, state.BackgroundColor, 0.68), false, 0.7);
            canvas.Line(-0.84, 0, 0.84, 0, 1, Mix(state.PrimaryColor, state.BackgroundColor, 0.72), 0.55);
            canvas.Line(0, -0.84, 0, 0.84, 1, Mix(state.SecondaryColor, state.BackgroundColor, 0.72), 0.55);
        }

        segments.Sort((left, right) => left.Depth.CompareTo(right.Depth));
        double baseThickness = state.Filled ? Math.Clamp(state.Parameter, 0.4, 20) : 1.25;
        for (int i = 0; i < segments.Count; i++)
        {
            if ((i & 2047) == 0) token.ThrowIfCancellationRequested();
            KnotSegment segment = segments[i];
            double depthAmount = Math.Clamp((segment.Depth + 1.4) / 2.8, 0, 1);
            double thickness = baseThickness * (0.72 + depthAmount * 0.48);
            if (state.Filled)
                canvas.Line(segment.From.X, segment.From.Y, segment.To.X, segment.To.Y,
                    thickness + 3.2, state.BackgroundColor, 0.88);
            canvas.Line(segment.From.X, segment.From.Y, segment.To.X, segment.To.Y,
                thickness, Mix(Palette(state, segment.Parameter), Color.FromRgb(255, 255, 255), depthAmount * 0.28),
                0.94);
        }
        progress?.Report(95);
    }

    private static Point3 CreateKnotPoint(double t, int p, int q, int mode)
    {
        return mode switch
        {
            0 => new Point3(
                (0.58 + 0.26 * Math.Cos(q * t)) * Math.Cos(p * t),
                (0.58 + 0.26 * Math.Cos(q * t)) * Math.Sin(p * t),
                0.26 * Math.Sin(q * t)),
            1 => new Point3(
                0.76 * Math.Sin(p * t + Math.PI / 5),
                0.76 * Math.Sin(q * t),
                0.6 * Math.Sin((p + q + 1) * t + Math.PI / 3)),
            _ => new Point3(
                0.75 * Math.Sin(p * t),
                0.75 * Math.Sin(q * t + Math.PI / 4),
                0.58 * Math.Sin((2 * p + q) * t + Math.PI / 7))
        };
    }

    private static Point3 CreateBraidPoint(double t, int strand, int strands, int turns)
    {
        double phase = Math.Tau * strand / strands;
        double cross = turns * t + phase;
        double radius = 0.56 + 0.17 * Math.Cos(cross);
        return new Point3(radius * Math.Cos(t), radius * Math.Sin(t), 0.22 * Math.Sin(cross));
    }

    private static Point3 ProjectKnotPoint(Point3 point, double rotation)
    {
        double cosY = Math.Cos(rotation), sinY = Math.Sin(rotation);
        double x1 = point.X * cosY + point.Z * sinY;
        double z1 = -point.X * sinY + point.Z * cosY;
        const double tilt = -0.46;
        double cosX = Math.Cos(tilt), sinX = Math.Sin(tilt);
        double y2 = point.Y * cosX - z1 * sinX;
        double z2 = point.Y * sinX + z1 * cosX;
        double perspective = 1 / Math.Max(0.55, 1.25 - z2 * 0.22);
        return new Point3(x1 * perspective, y2 * perspective, z2);
    }

    private readonly record struct Point3(double X, double Y, double Z);

    private readonly record struct KnotSegment(Point3 From, Point3 To, double Parameter)
    {
        public double Depth => (From.Z + To.Z) * 0.5;
    }
}
