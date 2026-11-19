using FractalExplorerWPF.Core.PercolationMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Percolation3DVolume
{
    public static (byte[] Voxels, Percolation3DStatistics Statistics) Build(Percolation3DSettings settings,
        CancellationToken token)
    {
        var occupied = Percolation3D.GenerateOccupancy(settings, token);
        var field = Percolation3D.Analyze(settings, occupied, token);
        // R: тип кластера (бит 0 — занят, 1 — крупнейший, 2 — сквозной).
        // G: стабильный цвет, B: размер в логарифмической шкале, A: позиция на маршруте.
        var clusterData = new (byte Flags, byte Color, byte Size)[field.Sizes.Length];
        int mask = Percolation3D.AxisMask(settings.Axis);
        double scale = 255 / Math.Log2(occupied.Length + 1);
        for (int i = 1; i < clusterData.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            int flags = 1 | (i == field.LargestLabel ? 2 : 0) | ((field.FaceMasks[i] & mask) == mask ? 4 : 0);
            clusterData[i] = ((byte)flags, (byte)(Percolation3D.Mix((uint)i) >> 24),
                (byte)Math.Round(Math.Log2(field.Sizes[i] + 1) * scale));
        }

        var voxels = new byte[field.Labels.Length * 4];
        for (int i = 0; i < field.Labels.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var data = clusterData[field.Labels[i]];
            voxels[i * 4] = data.Flags;
            voxels[i * 4 + 1] = data.Color;
            voxels[i * 4 + 2] = data.Size;
        }
        for (int i = 0; i < field.Path.Length; i++)
            voxels[field.Path[i] * 4 + 3] = (byte)(1 + (long)i * 254 / (field.Path.Length - 1));

        return (voxels, field.Statistics);
    }
}
