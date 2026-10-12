using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

public readonly record struct AbcSectionPoint(Vector2 Position, float Lyapunov, int Seed, double Time, float Speed);

/// <summary>Display geometry only. The ODE and its variational equation are integrated on the GPU.</summary>
internal sealed class AbcFlowGeometry
{
    public required Vector4[] Nodes { get; init; }
    public required Vector4[] Capsules { get; init; }
    public required AbcSectionPoint[] Sections { get; init; }
    public int SegmentCount => Capsules.Length / 4;

    public static AbcFlowGeometry Build(AbcFlowSettings s, Vector4[] samples, CancellationToken token)
    {
        var capsules = new List<Vector4>();
        var sections = new List<AbcSectionPoint>();
        float radius = (float)s.Thickness;
        float maxSpeed = (float)Math.Sqrt(Math.Pow(Math.Abs(s.A) + Math.Abs(s.C), 2) +
            Math.Pow(Math.Abs(s.A) + Math.Abs(s.B), 2) + Math.Pow(Math.Abs(s.B) + Math.Abs(s.C), 2));
        int axis = (int)s.SectionAxis;
        for (int seed = 0; seed < s.SeedCount; seed++)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < s.Samples; i++)
            {
                if ((i & 127) == 0) token.ThrowIfCancellationRequested();
                int index = (seed * (s.Samples + 1) + i) * 2;
                var v0 = samples[index]; var v1 = samples[index + 2];
                Vector3 p = new(v0.X, v0.Y, v0.Z), end = new(v1.X, v1.Y, v1.Z);
                // Minimal displacement is unambiguous: max sample displacement < π under validation.
                Vector3 delta = AbcFlowSettings.Wrap(end - p);
                float lambda = .5f + .5f * v1.W / (float)s.LyapunovRange;
                Vector4 velocity = samples[index + 3];
                float speed = new Vector3(velocity.X, velocity.Y, velocity.Z).Length() / Math.Max(maxSpeed, 1e-9f);
                var color = new Vector4(Math.Clamp(lambda, 0, 1), speed, seed / (float)Math.Max(s.SeedCount - 1, 1), 0);
                // Intersect all copies of the section plane in the unwrapped sample segment.
                float d = delta[axis];
                if (Math.Abs(d) > 1e-7 && (!s.PositiveCrossings || d > 0))
                {
                    double plane = s.SectionPosition + Math.Tau * Math.Ceiling((Math.Min(p[axis], p[axis] + d) - s.SectionPosition) / Math.Tau);
                    float f = (float)((plane - p[axis]) / d);
                    // Half-open interval counts a crossing at a sample endpoint once.
                    if (f > 0 && f <= 1)
                    {
                        Vector3 point = AbcFlowSettings.Wrap(p + f * delta);
                        point[axis] = (float)s.SectionPosition;
                        Vector2 uv = axis switch { 0 => new(point.Y, point.Z), 1 => new(point.X, point.Z), _ => new(point.X, point.Y) };
                        double time = (i + f) * AbcFlowSettings.Substeps * s.TimeStep;
                        Vector4 before = samples[index + 1];
                        Vector3 flow = Vector3.Lerp(new(before.X, before.Y, before.Z), new(velocity.X, velocity.Y, velocity.Z), f);
                        sections.Add(new(uv, v0.W + f * (v1.W - v0.W), seed, time, flow.Length() / Math.Max(maxSpeed, 1e-9f)));
                        if (s.SectionOnly) Add(point, point, radius * 1.8f, color, (i + f) / s.Samples, (i + f) / s.Samples, 1);
                    }
                }
                if (s.SectionOnly || delta.LengthSquared() < 1e-14f) continue;
                // Split at every periodic face: no long chord across the cube and no missing seam end.
                var cuts = new List<float> { 0, 1 };
                for (int k = 0; k < 3; k++)
                {
                    float e = p[k] + delta[k];
                    if (e > Math.PI || e < -Math.PI)
                    {
                        float f = ((e > Math.PI ? (float)Math.PI : -(float)Math.PI) - p[k]) / delta[k];
                        if (f > 0 && f < 1) cuts.Add(f);
                    }
                }
                cuts.Sort();
                for (int j = 0; j < cuts.Count - 1; j++)
                {
                    float a = cuts[j], b = cuts[j + 1];
                    if (b - a < 1e-7) continue;
                    Vector3 mid = p + delta * ((a + b) / 2);
                    Vector3 shift = mid - AbcFlowSettings.Wrap(mid);
                    Add(p + delta * a - shift, p + delta * b - shift, radius, color,
                        (i + a) / s.Samples, (i + b) / s.Samples, 0);
                }
            }
        }
        // A stationary field still has visible seeds; section-only with no crossings stays empty.
        if (!s.SectionOnly && capsules.Count == 0)
            foreach (var p in s.Seeds()) Add(p, p, radius * 2, new(.5f, 0, 0, 0), 0, 1, 0);
        // One animated leaf per seed. It samples the cached GPU trajectory directly, instead of
        // duplicating every trail segment. Its conservative bound is the entire periodic cube.
        if (!s.SectionOnly)
            for (int seed = 0; seed < s.SeedCount; seed++)
                Add(new(-(float)Math.PI), new((float)Math.PI), radius, Vector4.Zero, 0, seed, 2);
        if (capsules.Count == 0) Add(new(50), new(50), .001f, Vector4.Zero, 0, 1, 1);

        Vector4[] data = capsules.ToArray();
        int[] indices = Enumerable.Range(0, data.Length / 4).ToArray();
        var nodes = new List<Vector4>(indices.Length * 4);
        if (!s.SectionOnly)
        {
            // Separate subtrees let the shader skip all trails when only particles are visible.
            // Broad particle bounds never contaminate the tight trail BVH.
            Vector3 margin = new(1 + radius * 3);
            nodes.Add(default); nodes.Add(new(margin, -1));
            BuildGroup(0, indices.Length - s.SeedCount, -2);
            BuildGroup(indices.Length - s.SeedCount, s.SeedCount, -3);
            nodes[0] = new(-margin, nodes.Count / 2);
        }
        else BuildNode(0, indices.Length);
        return new() { Nodes = nodes.ToArray(), Capsules = data, Sections = sections.ToArray() };

        void BuildGroup(int first, int count, int marker)
        {
            int node = nodes.Count;
            Vector3 margin = new(1 + radius * 3);
            nodes.Add(default); nodes.Add(new(margin, marker));
            BuildNode(first, count);
            nodes[node] = new(-margin, nodes.Count / 2);
        }

        void Add(Vector3 a, Vector3 b, float r, Vector4 color, float start, float end, int type)
        {
            capsules.Add(new(a / (float)Math.PI, r));
            capsules.Add(new(b / (float)Math.PI, 0));
            capsules.Add(color); capsules.Add(new(start, end, type, 0));
        }
        void BuildNode(int first, int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int i = first; i < first + count; i++)
            {
                int id = indices[i] * 4;
                Vector3 a = new(data[id].X, data[id].Y, data[id].Z), b = new(data[id + 1].X, data[id + 1].Y, data[id + 1].Z);
                Vector3 margin = new(data[id].W * (data[id + 3].Z > 1.5f ? 3 : 1));
                lo = Vector3.Min(lo, Vector3.Min(a, b) - margin); hi = Vector3.Max(hi, Vector3.Max(a, b) + margin);
            }
            int node = nodes.Count;
            nodes.Add(default); nodes.Add(new(hi, count == 1 ? indices[first] : -1));
            if (count > 1)
            {
                Vector3 size = hi - lo;
                int k = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                Array.Sort(indices, first, count, Comparer<int>.Create((a, b) =>
                    (data[a * 4][k] + data[a * 4 + 1][k]).CompareTo(data[b * 4][k] + data[b * 4 + 1][k])));
                int left = count / 2; BuildNode(first, left); BuildNode(first + left, count - left);
            }
            nodes[node] = new(lo, nodes.Count / 2);
        }
    }
}
