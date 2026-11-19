using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Samples the equation domain and builds a stackless BVH for exact GPU triangle intersections.</summary>
internal sealed class ParametricSurfaceMesh
{
    private const int LeafSize = 4;
    private readonly record struct Sample(double X, double Y, double Z)
    {
        public bool IsValid => Valid(X) && Valid(Y) && Valid(Z);
        private static bool Valid(double value) => double.IsFinite(value) && Math.Abs(value) <= ParametricSurfaceSettings.CoordinateLimit;
        public Vector3 ToVector() => new((float)X, (float)Y, (float)Z);
    }

    private readonly record struct Triangle(int A, int B, int C, Vector3 Min, Vector3 Max);

    public required Vector4[] Vertices { get; init; } // position/u, normal/v
    public required Vector4[] Triangles { get; init; }
    public required Vector4[] Nodes { get; init; } // min/escape index, max/leaf offset (or negative for a branch)
    public required float Radius { get; init; }
    public required int SkippedSamples { get; init; }

    public static ParametricSurfaceMesh Build(ParametricSurfaceSettings settings, CancellationToken token)
    {
        settings.Validate();
        int nu = settings.USegments, nv = settings.VSegments, stride = nu + 1;
        var samples = new Sample[stride * (nv + 1)];
        var domain = settings.ActiveDomain;
        var formulas = settings.Family == ParametricSurfaceFamily.Equations ? settings.Equations.Compile() : null;
        int skipped = 0;
        Sample low = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Sample high = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

        // A superformula separates u and v, so compute each angular profile only once per axis.
        var longitude = formulas is null ? AngularProfile(nu, domain.UMin, domain.UMax, settings.Longitude) : null;
        var latitude = formulas is null ? AngularProfile(nv, domain.VMin, domain.VMax,
            settings.LinkProfiles ? settings.Longitude : settings.Latitude) : null;
        for (int j = 0; j <= nv; j++)
        {
            token.ThrowIfCancellationRequested();
            double v = At(j, nv, domain.VMin, domain.VMax);
            for (int i = 0; i <= nu; i++)
            {
                Sample point;
                if (formulas is not null)
                {
                    double u = At(i, nu, domain.UMin, domain.UMax);
                    point = new(Evaluate(formulas[0], u, v), Evaluate(formulas[1], u, v), Evaluate(formulas[2], u, v));
                }
                else
                {
                    var (x, z) = longitude![i];
                    var (r, y) = latitude![j];
                    point = new(x * r, y, z * r);
                }
                samples[j * stride + i] = point;
                if (!point.IsValid) { skipped++; continue; }
                low = new(Math.Min(low.X, point.X), Math.Min(low.Y, point.Y), Math.Min(low.Z, point.Z));
                high = new(Math.Max(high.X, point.X), Math.Max(high.Y, point.Y), Math.Max(high.Z, point.Z));
            }
        }
        if (skipped == samples.Length)
            throw new ArgumentException("В выбранной области нет допустимых точек поверхности. Проверьте формулы, границы и коэффициенты.");

        var center = new Sample((low.X + high.X) * .5, (low.Y + high.Y) * .5, (low.Z + high.Z) * .5);
        double width = Math.Max(high.X - low.X, Math.Max(high.Y - low.Y, high.Z - low.Z));
        if (width == 0) throw new ArgumentException("Все точки поверхности совпадают.");
        var points = new Vector3[samples.Length];
        var normals = new Vector3[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            Sample p = samples[i];
            if (!p.IsValid) continue;
            points[i] = settings.Fit
                ? new(Fit(p.X, center.X), Fit(p.Y, center.Y), Fit(p.Z, center.Z))
                : p.ToVector();
        }

        var triangles = new List<Triangle>(2 * nu * nv);
        for (int j = 0; j < nv; j++)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < nu; i++)
            {
                int a = j * stride + i, b = a + 1, c = a + stride, d = c + 1;
                // Omit the whole cell around an undefined sample, without stretching a triangle across the hole.
                if (!samples[a].IsValid || !samples[b].IsValid || !samples[c].IsValid || !samples[d].IsValid) continue;
                Add(a, c, b);
                Add(b, c, d);
            }
        }
        if (triangles.Count == 0)
            throw new ArgumentException("Уравнения вырождаются в линию или точку либо не дают связных участков. Проверьте формулы и область.");

        float tolerance = (float)((settings.Fit ? 2.25 : width) * 1e-6);
        for (int j = 0; j <= nv; j++) Weld(j * stride, j * stride + nu);
        for (int i = 0; i <= nu; i++) Weld(i, nv * stride + i);

        var vertices = new Vector4[samples.Length * 2];
        float radius = 0;
        for (int j = 0; j <= nv; j++)
        for (int i = 0; i <= nu; i++)
        {
            int id = j * stride + i;
            vertices[id * 2] = new(points[id], i / (float)nu);
            vertices[id * 2 + 1] = new(Unit(normals[id]), j / (float)nv);
            if (samples[id].IsValid) radius = Math.Max(radius, points[id].Length());
        }

        Triangle[] ordered = triangles.ToArray();
        var nodes = new List<Vector4>(ordered.Length * 2);
        var leafEnds = new bool[ordered.Length];
        BuildNode(0, ordered.Length);
        var packed = new Vector4[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            Triangle triangle = ordered[i];
            packed[i] = new(triangle.A, triangle.B, triangle.C, leafEnds[i] ? 1 : 0);
        }
        return new() { Vertices = vertices, Triangles = packed, Nodes = nodes.ToArray(), Radius = radius, SkippedSamples = skipped };

        double Evaluate(Core.NewtonMath.CompiledRealSpaceExpression formula, double u, double v) =>
            formula.Evaluate(u, v, 0, settings.A, settings.B, settings.C);

        float Fit(double value, double middle) => (float)((value - middle) / width * 2.25);

        void Add(int a, int b, int c)
        {
            Vector3 p = points[a], q = points[b], r = points[c];
            Vector3 cross = Vector3.Cross(Unit(q - p), Unit(r - p));
            if (cross.LengthSquared() < 1e-14f) return;
            Vector3 normal = Vector3.Normalize(cross);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
            triangles.Add(new(a, b, c, Vector3.Min(p, Vector3.Min(q, r)), Vector3.Max(p, Vector3.Max(q, r))));
        }

        void Weld(int a, int b)
        {
            // Coincident ends are smoothed only if their tangent planes agree. The mesh
            // always retains the original domain and UVs, including open or twisted seams.
            if (samples[a].IsValid && samples[b].IsValid && Vector3.Distance(points[a], points[b]) <= tolerance &&
                Vector3.Dot(Unit(normals[a]), Unit(normals[b])) > .8f)
                normals[a] = normals[b] = normals[a] + normals[b];
        }

        void BuildNode(int start, int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int i = start; i < start + count; i++)
            {
                min = Vector3.Min(min, ordered[i].Min);
                max = Vector3.Max(max, ordered[i].Max);
            }
            int node = nodes.Count;
            var padding = Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)) * 2e-7f;
            nodes.Add(default);
            nodes.Add(new(max + padding, count <= LeafSize ? start : -1));
            if (count > LeafSize)
            {
                Vector3 size = max - min;
                int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                Array.Sort(ordered, start, count, AxisComparers[axis]);
                int left = count / 2;
                BuildNode(start, left);
                BuildNode(start + left, count - left);
            }
            else leafEnds[start + count - 1] = true;
            nodes[node] = new(min - padding, nodes.Count / 2);
        }
    }

    private static readonly IComparer<Triangle>[] AxisComparers = Enumerable.Range(0, 3)
        .Select(axis => Comparer<Triangle>.Create((a, b) =>
            (a.Min[axis] + a.Max[axis]).CompareTo(b.Min[axis] + b.Max[axis]))).ToArray();

    private static double At(int i, int count, double min, double max) =>
        i == count ? max : min + (max - min) * (i / (double)count);

    private static (double Cos, double Sin)[] AngularProfile(int count, double min, double max, SuperformulaProfile profile)
    {
        var result = new (double, double)[count + 1];
        for (int i = 0; i <= count; i++)
        {
            double t = At(i, count, min, max);
            double r = profile.Radius(t);
            var (sin, cos) = Math.SinCos(t);
            result[i] = (r * (Math.Abs(cos) < 1e-15 ? 0 : cos), r * (Math.Abs(sin) < 1e-15 ? 0 : sin));
        }
        return result;
    }

    private static Vector3 Unit(Vector3 value)
    {
        Vector3 a = Vector3.Abs(value);
        float max = Math.Max(a.X, Math.Max(a.Y, a.Z));
        return max > 0 ? Vector3.Normalize(value / max) : Vector3.Zero;
    }
}
