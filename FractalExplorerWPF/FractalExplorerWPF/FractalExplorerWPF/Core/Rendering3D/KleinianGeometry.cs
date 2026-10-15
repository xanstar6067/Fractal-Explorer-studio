using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Nested cylinder balls I_i1 … I_i(n-1)(B_in), with adjacent equal letters excluded.
/// Their intersection over all depths is the limit set of the sphere reflection group.
/// Inversions are evaluated in double precision; GPU leaves are exact finite-level balls.
/// </summary>
internal sealed class KleinianGeometry
{
    internal const int MaxBalls = 65_536;
    internal readonly record struct Ball(double X, double Y, double Z, double Radius)
    {
        internal Vector3 Center => new((float)X, (float)Y, (float)Z);
    }
    private readonly record struct Cylinder(Ball Ball, ulong Word, int Depth, int Generator);

    internal required Vector4[] Nodes { get; init; }
    /// <summary>Two float4 per leaf: center/radius, then generator color/size/depth/unused.</summary>
    internal required Vector4[] Spheres { get; init; }
    internal required bool BudgetLimited { get; init; }
    internal int BallCount => Spheres.Length / 2;

    internal static Ball[] Generators(KleinianSettings settings)
    {
        settings.Validate();
        Vector3[] vertices = settings.Configuration switch
        {
            KleinianConfiguration.Tetrahedron => [new(1, 1, 1), new(-1, -1, 1), new(1, -1, -1), new(-1, 1, -1)],
            KleinianConfiguration.Cube => [new(1, 1, 1), new(-1, 1, 1), new(1, -1, 1), new(-1, -1, 1),
                new(1, 1, -1), new(-1, 1, -1), new(1, -1, -1), new(-1, -1, -1)],
            _ => [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ]
        };
        // All centers lie on the unit sphere. Compute radii analytically in double precision:
        // touching balls must not accidentally overlap because of float normalization.
        double norm = settings.Configuration == KleinianConfiguration.Octahedron ? 1 : Math.Sqrt(3);
        double radius = settings.Configuration switch
        {
            KleinianConfiguration.Tetrahedron => Math.Sqrt(2.0 / 3),
            KleinianConfiguration.Cube => 1 / Math.Sqrt(3),
            _ => 1 / Math.Sqrt(2)
        };
        // The central generator is essential: the outer spheres alone preserve a common
        // orthogonal sphere and would produce a limit set confined to that spherical surface.
        // It is disjoint from the outer balls (tangent when RadiusRatio = 1), so discreteness
        // is retained while the common invariant sphere is removed.
        return vertices.Select(v => new Ball(v.X / norm, v.Y / norm, v.Z / norm, radius * settings.RadiusRatio))
            .Append(new Ball(0, 0, 0, (1 - radius) * settings.RadiusRatio)).ToArray();
    }

    /// <summary>Exact sphere image under c + R²(x−c)/|x−c|², away from its pole.</summary>
    internal static Ball Invert(Ball ball, Ball inversion)
    {
        double x = ball.X - inversion.X, y = ball.Y - inversion.Y, z = ball.Z - inversion.Z;
        double denominator = x * x + y * y + z * z - ball.Radius * ball.Radius;
        if (denominator <= 0) throw new InvalidOperationException("Сфера пересекла полюс инверсии.");
        double factor = inversion.Radius * inversion.Radius / denominator;
        return new(inversion.X + factor * x, inversion.Y + factor * y, inversion.Z + factor * z, factor * ball.Radius);
    }

    internal static KleinianGeometry Build(KleinianSettings settings, int depth, CancellationToken token)
    {
        settings.Validate();
        if (depth is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(depth));
        Ball[] generators = Generators(settings);
        var queue = new PriorityQueue<Cylinder, (double Radius, long Order)>();
        var leaves = new List<Cylinder>();
        long order = 0;
        for (int i = 0; i < generators.Length; i++)
            queue.Enqueue(new(generators[i], (ulong)i, 1, i), (-generators[i].Radius, order++));
        bool limited = false;
        int processed = 0;
        while (queue.TryDequeue(out Cylinder parent, out _))
        {
            if ((processed++ & 127) == 0) token.ThrowIfCancellationRequested();
            if (parent.Depth >= depth || parent.Ball.Radius <= settings.MinRadius)
            {
                leaves.Add(parent);
                continue;
            }
            // Refine largest balls first. At the memory limit retain a complete covering,
            // rather than silently discarding branches (which would change the limit set).
            if (leaves.Count + queue.Count + generators.Length - 1 > MaxBalls)
            {
                limited = true;
                leaves.Add(parent);
                while (queue.TryDequeue(out var remaining, out _)) leaves.Add(remaining);
                break;
            }
            int previous = (int)((parent.Word >> (4 * (parent.Depth - 1))) & 15);
            for (int next = 0; next < generators.Length; next++)
            {
                if (next == previous) continue; // I_i² = identity: no immediate inverse.
                Ball ball = generators[next];
                for (int letter = parent.Depth - 1; letter >= 0; letter--)
                    ball = Invert(ball, generators[(int)((parent.Word >> (letter * 4)) & 15)]);
                ulong word = parent.Word | ((ulong)next << (4 * parent.Depth));
                queue.Enqueue(new(ball, word, parent.Depth + 1, parent.Generator), (-ball.Radius, order++));
            }
        }
        token.ThrowIfCancellationRequested();
        var spheres = new Vector4[leaves.Count * 2];
        var nodes = new List<Vector4>(leaves.Count * 4);
        var array = leaves.ToArray();
        BuildNode(0, array.Length);
        return new() { Nodes = nodes.ToArray(), Spheres = spheres, BudgetLimited = limited };

        void BuildNode(int start, int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 lo = new(float.PositiveInfinity), hi = new(float.NegativeInfinity);
            for (int i = start; i < start + count; i++)
            {
                Ball ball = array[i].Ball;
                // Padding is for double→float rounding, not visual thickening.
                Vector3 radius = new((float)ball.Radius + 2e-6f);
                lo = Vector3.Min(lo, ball.Center - radius);
                hi = Vector3.Max(hi, ball.Center + radius);
            }
            int node = nodes.Count;
            nodes.Add(default); nodes.Add(default);
            if (count == 1)
            {
                Cylinder leaf = array[start];
                spheres[start * 2] = new(leaf.Ball.Center, (float)leaf.Ball.Radius);
                spheres[start * 2 + 1] = new((float)leaf.Generator / generators.Length,
                    (float)(-Math.Log10(leaf.Ball.Radius) / 9), leaf.Depth - 1, 0);
            }
            else
            {
                Vector3 size = hi - lo;
                int axis = size.X >= Math.Max(size.Y, size.Z) ? 0 : size.Y >= size.Z ? 1 : 2;
                Array.Sort(array, start, count, Comparer<Cylinder>.Create((a, b) =>
                    (axis == 0 ? a.Ball.X : axis == 1 ? a.Ball.Y : a.Ball.Z)
                    .CompareTo(axis == 0 ? b.Ball.X : axis == 1 ? b.Ball.Y : b.Ball.Z)));
                int half = count / 2;
                BuildNode(start, half);
                BuildNode(start + half, count - half);
            }
            // Preorder threading: skip the complete subtree on an AABB miss.
            nodes[node] = new(lo, nodes.Count / 2);
            nodes[node + 1] = new(hi, count == 1 ? start : -1);
        }
    }
}
