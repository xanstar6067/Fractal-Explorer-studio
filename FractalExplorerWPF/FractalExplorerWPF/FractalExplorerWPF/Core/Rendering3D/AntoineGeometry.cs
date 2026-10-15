using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// A finite stage of Antoine's necklace. Internal solid tori enclose their entire
/// subtree, so their exact signed distances are conservative bounds for GPU traversal.
/// They are bounds only: the image contains exclusively the final generation.
/// </summary>
internal sealed class AntoineGeometry
{
    internal readonly record struct Dimensions(double Scale, double TubeRatio);
    internal readonly record struct Ring(Vector3 Center, Vector3 U, Vector3 V, double Radius)
    {
        public Vector3 Normal => Vector3.Normalize(Vector3.Cross(U, V));
        public Vector3 Point(double angle) => Center + (U * (float)Math.Cos(angle) + V * (float)Math.Sin(angle)) * (float)Radius;
    }

    public required Vector4[] Nodes { get; init; }
    public required int RingCount { get; init; }
    public const int MaxDepth = 3;

    // Work in double when certifying the generator; subtract a Lipschitz bound for
    // the unsampled arcs. This is a lower bound, not an optimistic sampled clearance.
    internal static Dimensions Measure(AntoineSettings settings)
    {
        settings.Validate();
        int n = settings.Links;
        double scale = settings.ScaleFactor * Math.Sin(Math.PI / n);
        double tilt = settings.Tilt * Math.PI / 180;
        double clearance = double.PositiveInfinity;
        const int samples = 2048;
        for (int parity = 0; parity < 2; parity++)
        {
            double h0 = parity * 2 * Math.PI / n, h1 = (parity + 1) * 2 * Math.PI / n;
            double a0 = parity == 0 ? 0 : tilt, a1 = parity == 0 ? tilt : 0;
            double nx = Math.Cos(h1) * Math.Sin(a1), ny = Math.Sin(h1) * Math.Sin(a1), nz = -Math.Cos(a1);
            double minimum = double.PositiveInfinity;
            for (int i = 0; i < samples; i++)
            {
                double t = i * 2 * Math.PI / samples, c = Math.Cos(t), s = Math.Sin(t);
                double x = Math.Cos(h0) + scale * (-Math.Sin(h0) * c + Math.Cos(h0) * Math.Cos(a0) * s) - Math.Cos(h1);
                double y = Math.Sin(h0) + scale * ( Math.Cos(h0) * c + Math.Sin(h0) * Math.Cos(a0) * s) - Math.Sin(h1);
                double z = scale * Math.Sin(a0) * s;
                double height = x * nx + y * ny + z * nz;
                double radial = Math.Sqrt(Math.Max(0, x * x + y * y + z * z - height * height));
                minimum = Math.Min(minimum, Math.Sqrt((radial - scale) * (radial - scale) + height * height));
            }
            clearance = Math.Min(clearance, minimum - scale * Math.PI / samples);
        }
        // All non-neighbours are separated already by their bounding spheres.
        clearance = Math.Min(clearance, 2 * Math.Sin(2 * Math.PI / n) - 2 * scale);
        double low = scale / (1 - scale) * 1.01; // scale * (1 + tube) < tube: nesting
        double high = Math.Min(.42, clearance / (2 * scale) * .96);
        if (high <= low) throw new ArgumentException("Эти параметры не оставляют зазор между вложенными звеньями.");
        return new(scale, low + (high - low) * settings.Thickness);
    }

    internal static Ring Child(Ring parent, int index, AntoineSettings settings, double scale)
    {
        double angle = index * 2 * Math.PI / settings.Links;
        Vector3 radial = parent.U * (float)Math.Cos(angle) + parent.V * (float)Math.Sin(angle);
        Vector3 tangent = -parent.U * (float)Math.Sin(angle) + parent.V * (float)Math.Cos(angle);
        double tilt = index % 2 == 0 ? 0 : settings.Tilt * Math.PI / 180;
        Vector3 v = radial * (float)Math.Cos(tilt) + parent.Normal * (float)Math.Sin(tilt);
        return new(parent.Center + radial * (float)parent.Radius, Vector3.Normalize(tangent), Vector3.Normalize(v), parent.Radius * scale);
    }

    internal static AntoineGeometry Build(AntoineSettings settings, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Dimensions dimensions = Measure(settings);
        depth = Math.Clamp(depth, 0, MaxDepth);
        var nodes = new List<Vector4>();
        int leaves = 0;
        Add(new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 1), 0, 0, 0);
        return new() { Nodes = nodes.ToArray(), RingCount = leaves };

        void Add(Ring ring, int level, int branch, int link)
        {
            token.ThrowIfCancellationRequested();
            int start = nodes.Count;
            nodes.Add(new(ring.Center, (float)ring.Radius));
            // Rounding slack keeps the bounding torus conservative at every depth.
            nodes.Add(new(ring.Normal, (float)(ring.Radius * dimensions.TubeRatio + (level < depth ? 2e-6 : 0))));
            nodes.Add(default);
            if (level < depth)
                for (int i = 0; i < settings.Links; i++)
                    Add(Child(ring, i, settings, dimensions.Scale), level + 1, level == 0 ? i : branch, i);
            else leaves++;
            nodes[start + 2] = new(nodes.Count / 3, level == depth ? 1 : 0,
                branch / (float)(settings.Links - 1), link / (float)(settings.Links - 1));
        }
    }
}
