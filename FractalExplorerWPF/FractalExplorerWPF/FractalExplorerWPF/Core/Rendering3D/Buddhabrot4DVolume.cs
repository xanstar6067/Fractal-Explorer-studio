using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Buddhabrot4DVolume
{
    public const int Side = Flame3DVolume.Side;

    internal static Matrix4x4 Rotation(Buddhabrot4DSettings settings)
    {
        Matrix4x4 rotation = Matrix4x4.Identity;
        Rotate(0, 1, settings.ZrZi); Rotate(0, 2, settings.ZrCr); Rotate(0, 3, settings.ZrCi);
        Rotate(1, 2, settings.ZiCr); Rotate(1, 3, settings.ZiCi); Rotate(2, 3, settings.CrCi);
        return rotation;

        void Rotate(int a, int b, double angle)
        {
            float c = (float)Math.Cos(angle * Math.PI / 180), s = (float)Math.Sin(angle * Math.PI / 180);
            var plane = Matrix4x4.Identity;
            plane[a, a] = c; plane[b, b] = c; plane[a, b] = s; plane[b, a] = -s;
            rotation *= plane;
        }
    }

    internal static Vector3 Project(Vector4 point, Matrix4x4 rotation, Buddhabrot4DProjection projection)
    {
        Vector4 p = Vector4.Transform(point, rotation);
        return projection switch
        {
            Buddhabrot4DProjection.HideCr => new(p.X, p.Y, p.W),
            Buddhabrot4DProjection.HideZi => new(p.X, p.Z, p.W),
            Buddhabrot4DProjection.HideZr => new(p.Y, p.Z, p.W),
            _ => new(p.X, p.Y, p.Z)
        };
    }

    public static byte[] Build(Buddhabrot4DOrbitCloud cloud, Buddhabrot4DSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); settings.Validate();
        Matrix4x4 rotation = Rotation(settings);
        ReadOnlySpan<Buddhabrot4DPoint> points = cloud.Points.Span;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            Vector3 p = Project(points[i].Position, rotation, settings.Projection);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        Vector3 center = (min + max) * .5f, extent = max - min;
        float scale = 1.75f / MathF.Max(1e-5f, MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)));
        var cells = new Vector4[Side * Side * Side];
        int accepted = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var sample = points[i];
            Vector3 color = new(sample.EscapeIteration <= settings.RedLimit ? 1 : 0,
                sample.EscapeIteration <= settings.GreenLimit ? 1 : 0,
                sample.EscapeIteration <= settings.BlueLimit ? 1 : 0);
            if (color == Vector3.Zero) continue;
            Vector3 p = ((Project(sample.Position, rotation, settings.Projection) - center) * scale + Vector3.One) * ((Side - 1) * .5f);
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
            if (x < 1 || y < 1 || z < 1 || x >= Side - 2 || y >= Side - 2 || z >= Side - 2) continue;
            accepted++;
            Vector3 f = p - new Vector3(x, y, z);
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                float weight = (dx == 0 ? 1 - f.X : f.X) * (dy == 0 ? 1 - f.Y : f.Y) * (dz == 0 ? 1 - f.Z : f.Z);
                cells[((z + dz) * Side + y + dy) * Side + x + dx] += new Vector4(color, 1) * weight;
            }
        }
        if (accepted < 100) throw new InvalidOperationException("В цветовые каналы попало мало точек. Увеличьте пределы R/G/B или число затравок.");
        Flame3DVolume.Smooth(cells, token);
        // Robust per-channel normalization retains faint long-lived orbits beside bright short ones.
        Vector3 norms = new(Normalization(0), Normalization(1), Normalization(2));
        float densityNorm = Normalization(3);
        var packed = new Half[cells.Length * 4];
        for (int i = 0; i < cells.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            Vector4 v = cells[i];
            if (v.W <= 0) continue;
            // Keep the sums linear until the ray has integrated depth. Compressing every
            // voxel first would promote isolated noise and turn the projection into fog.
            float density = MathF.Min(60000, v.W / densityNorm);
            Vector3 rgb = Vector3.Min(new(60000), new Vector3(v.X / norms.X, v.Y / norms.Y, v.Z / norms.Z));
            packed[i * 4] = (Half)rgb.X; packed[i * 4 + 1] = (Half)rgb.Y;
            packed[i * 4 + 2] = (Half)rgb.Z; packed[i * 4 + 3] = (Half)density;
        }
        return MemoryMarshal.AsBytes(packed.AsSpan()).ToArray();

        float Normalization(int channel)
        {
            var occupied = new List<float>();
            for (int i = 0; i < cells.Length; i++)
            {
                if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                float value = cells[i][channel];
                if (value > 0) occupied.Add(value);
            }
            token.ThrowIfCancellationRequested(); occupied.Sort(); token.ThrowIfCancellationRequested();
            return occupied.Count == 0 ? 1 : MathF.Max(1e-5f, occupied[(int)((occupied.Count - 1) * .99)]);
        }
    }
}
