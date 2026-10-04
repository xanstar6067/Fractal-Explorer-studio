using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Deterministic colored chaos game; linear RGB sums and unsaturated density, then RGBA16F.</summary>
internal static class Flame3DVolume
{
    public const int Side = 192;

    public static byte[] Build(Fractal3DState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = state.Flame;
        settings.Validate();
        int count = Math.Clamp(state.Iterations, 50_000, 10_000_000);
        var transforms = settings.Transforms.Where(t => t.Weight > 0).ToArray();
        double total = transforms.Sum(t => t.Weight), cumulative = 0;
        double[] weights = transforms.Select(t => cumulative += t.Weight / total).ToArray();
        weights[^1] = 1;
        Vector3[] colors = transforms.Select(t => new Vector3(Linear(t.Color.R), Linear(t.Color.G), Linear(t.Color.B))).ToArray();

        // A separate pilot pass fixes robust bounds without storing millions of points.
        var pilot = new List<Vector3>(32768);
        Walk(Math.Min(count, 32768), (p, _) => pilot.Add(p));
        if (pilot.Count < 100) throw new InvalidOperationException("Орбита Flame расходится. Уменьшите масштаб матриц или силу вариаций.");
        float[] xs = pilot.Select(p => p.X).Order().ToArray();
        float[] ys = pilot.Select(p => p.Y).Order().ToArray();
        float[] zs = pilot.Select(p => p.Z).Order().ToArray();
        int low = pilot.Count / 200, high = pilot.Count - 1 - low;
        Vector3 min = new(xs[low], ys[low], zs[low]), max = new(xs[high], ys[high], zs[high]);
        Vector3 center = (min + max) * .5f, extent = max - min;
        float size = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        if (size < 1e-5f) throw new InvalidOperationException("Flame сжался в точку. Добавьте переносы или другие вариации.");
        float scale = 1.75f / size;
        var sums = new Vector4[Side * Side * Side];
        int accepted = 0;
        Walk(count, (p, color) =>
        {
            p = ((p - center) * scale + Vector3.One) * ((Side - 1) * .5f);
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
            if (x < 1 || y < 1 || z < 1 || x >= Side - 2 || y >= Side - 2 || z >= Side - 2) return;
            accepted++;
            Vector3 f = p - new Vector3(x, y, z);
            // Trilinear splats preserve density and avoid holes when viewing from another angle.
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                float w = (dx == 0 ? 1 - f.X : f.X) * (dy == 0 ? 1 - f.Y : f.Y) * (dz == 0 ? 1 - f.Z : f.Z);
                sums[((z + dz) * Side + y + dy) * Side + x + dx] += new Vector4(color, 1) * w;
            }
        });
        if (accepted < 100) throw new InvalidOperationException("В объёме слишком мало точек Flame. Измените преобразования.");

        Smooth(sums, token);

        // Use the 99th percentile of occupied cells, so a hot spot does not darken the entire cloud.
        float[] occupied = sums.Where(v => v.W > 0).Select(v => v.W).Order().ToArray();
        float normalization = MathF.Log(1 + occupied[(int)((occupied.Length - 1) * .99)]);
        var packed = new Half[sums.Length * 4];
        for (int i = 0; i < sums.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            Vector4 v = sums[i];
            if (v.W <= 0) continue;
            float density = MathF.Min(4, MathF.Log(1 + v.W) / MathF.Max(normalization, 1e-5f));
            Vector3 rgb = new Vector3(v.X, v.Y, v.Z) * (density / v.W);
            packed[i * 4] = (Half)rgb.X; packed[i * 4 + 1] = (Half)rgb.Y;
            packed[i * 4 + 2] = (Half)rgb.Z; packed[i * 4 + 3] = (Half)density;
        }
        return MemoryMarshal.AsBytes(packed.AsSpan()).ToArray();

        void Walk(int steps, Action<Vector3, Vector3> visit)
        {
            var random = new Random(settings.Seed);
            Vector3 p = new(.13f, -.17f, .11f), color = Vector3.Zero;
            int warmup = settings.Warmup;
            for (int i = 0; i < steps + settings.Warmup; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                int selected = Array.BinarySearch(weights, random.NextDouble());
                if (selected < 0) selected = ~selected;
                selected = Math.Min(selected, transforms.Length - 1);
                var t = transforms[selected]; var m = t.Map;
                Vector3 affine = new((float)(m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.Tx),
                    (float)(m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.Ty),
                    (float)(m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.Tz));
                p = Vector3.Lerp(affine, Flame3DVariations.Apply(affine, t.Variation, random), (float)t.Amount);
                color = Vector3.Lerp(color, colors[selected], (float)t.ColorSpeed);
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || p.LengthSquared() > 10000)
                {
                    p = new((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f);
                    color = Vector3.Zero; warmup = settings.Warmup;
                    continue;
                }
                if (warmup > 0) { warmup--; continue; }
                visit(p, color);
            }
        }
    }

    internal static void Smooth(Vector4[] cells, CancellationToken token)
    {
        // Separable [1,2,1] footprint suppresses grain and stabilizes sculpture normals.
        // Copy one line before overwriting it: no second full-size HDR volume is needed.
        var line = new Vector4[Side];
        for (int axis = 0; axis < 3; axis++)
        for (int a = 0; a < Side; a++)
        {
            token.ThrowIfCancellationRequested();
            for (int b = 0; b < Side; b++)
            {
                int stride = axis == 0 ? 1 : axis == 1 ? Side : Side * Side;
                int start = axis == 0 ? (a * Side + b) * Side : axis == 1 ? a * Side * Side + b : a * Side + b;
                for (int k = 0; k < Side; k++) line[k] = cells[start + k * stride];
                for (int k = 1; k < Side - 1; k++)
                    cells[start + k * stride] = (line[k - 1] + line[k] * 2 + line[k + 1]) * .25f;
            }
        }
    }

    private static float Linear(byte b)
    {
        float c = b / 255f;
        return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    }
}
