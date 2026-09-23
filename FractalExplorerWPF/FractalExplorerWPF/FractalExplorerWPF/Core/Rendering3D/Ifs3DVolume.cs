using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Builds the geometry for the GPU volume renderer. Camera changes reuse this volume; only the
/// affine maps or orbit length require another chaos-game pass.
/// </summary>
internal static class Ifs3DVolume
{
    public const int Side = 256;

    public static byte[] Build(Fractal3DState state, CancellationToken token)
    {
        int count = Math.Clamp(state.Iterations, 10_000, 10_000_000);
        IReadOnlyList<Ifs3DTransform> transforms = state.IfsTransforms;
        if (transforms.Count == 0) throw new InvalidOperationException("Добавьте хотя бы одно преобразование IFS.");

        double total = transforms.Sum(transform => Math.Max(0, transform.Probability));
        if (!double.IsFinite(total) || total <= 0)
            throw new InvalidOperationException("Сумма вероятностей IFS должна быть положительной.");
        double[] weights = new double[transforms.Count];
        double cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += Math.Max(0, transforms[i].Probability) / total;
            weights[i] = cumulative;
        }
        weights[^1] = 1;

        var points = new Vector3[count];
        var random = new Random(12345);
        double x = 0, y = 0, z = 0;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = -100; i < count; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            double draw = random.NextDouble();
            int selected = Array.BinarySearch(weights, draw);
            if (selected < 0) selected = ~selected;
            Ifs3DTransform transform = transforms[Math.Min(selected, transforms.Count - 1)];
            double nx = transform.M11 * x + transform.M12 * y + transform.M13 * z + transform.Tx;
            double ny = transform.M21 * x + transform.M22 * y + transform.M23 * z + transform.Ty;
            double nz = transform.M31 * x + transform.M32 * y + transform.M33 * z + transform.Tz;
            x = double.IsFinite(nx) && Math.Abs(nx) < 1e12 ? nx : 0;
            y = double.IsFinite(ny) && Math.Abs(ny) < 1e12 ? ny : 0;
            z = double.IsFinite(nz) && Math.Abs(nz) < 1e12 ? nz : 0;
            if (i < 0) continue;
            Vector3 point = new((float)x, (float)y, (float)z);
            points[i] = point;
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        Vector3 center = (min + max) * .5f;
        Vector3 extent = max - min;
        float scale = 1.8f / Math.Max(Math.Max(extent.X, extent.Y), Math.Max(extent.Z, 1e-6f));
        byte[] voxels = new byte[Side * Side * Side];
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            Vector3 point = (points[i] - center) * scale;
            int vx = Math.Clamp((int)((point.X + 1) * (Side / 2d)), 1, Side - 2);
            int vy = Math.Clamp((int)((point.Y + 1) * (Side / 2d)), 1, Side - 2);
            int vz = Math.Clamp((int)((point.Z + 1) * (Side / 2d)), 1, Side - 2);
            int index = (vz * Side + vy) * Side + vx;
            Add(voxels, index, 96);
            Add(voxels, index - 1, 32); Add(voxels, index + 1, 32);
            Add(voxels, index - Side, 32); Add(voxels, index + Side, 32);
            Add(voxels, index - Side * Side, 32); Add(voxels, index + Side * Side, 32);
        }
        return voxels;
    }

    private static void Add(byte[] voxels, int index, int value) =>
        voxels[index] = (byte)Math.Min(255, voxels[index] + value);
}
