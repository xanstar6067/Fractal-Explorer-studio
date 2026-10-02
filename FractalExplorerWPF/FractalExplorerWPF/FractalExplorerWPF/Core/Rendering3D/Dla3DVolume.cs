namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Density and density-weighted birth index, independent of palette and camera.</summary>
internal static class Dla3DVolume
{
    public const int Side = 256;
    public static byte[] Build(Dla3DCluster cluster, CancellationToken token)
    {
        // R8G8: two compact channels, not a 1 GiB RGBA16F color volume.
        byte[] voxels = new byte[Side * Side * Side * 2];
        for (int i = 0; i < cluster.Points.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            var p = cluster.Points[i];
            // Fixed coordinates keep the seed still while branches grow.
            int x = (int)(Side / 2 + p.X), y = (int)(Side / 2 + p.Y), z = (int)(Side / 2 + p.Z);
            int age = i < cluster.SeedCount ? 0 : Math.Clamp((int)Math.Round(255d * (i - cluster.SeedCount + 1) / Math.Max(cluster.Count, 1)), 1, 255);
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int r2 = dx * dx + dy * dy + dz * dz;
                int density = r2 switch { 0 => 255, 1 => 90, 2 => 30, _ => 10 };
                int index = (((z + dz) * Side + y + dy) * Side + x + dx) * 2;
                // Max splat preserves thickness and the age of the nearest particle.
                if (density > voxels[index])
                { voxels[index] = (byte)density; voxels[index + 1] = (byte)(density * age / 255); }
            }
        }
        return voxels;
    }
}
