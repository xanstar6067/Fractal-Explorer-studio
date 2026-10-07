using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private Lichtenberg3DGpuSimulation? _lichtenberg;
    private Lichtenberg3DField? _lichtenbergOrigin, _lichtenbergAnchor;
    private ID3D11PixelShader? _lichtenbergPixelShader;
    public Lichtenberg3DField? LichtenbergDisplayedField { get; private set; }
    public bool LichtenbergBoundaryReached { get; private set; }

    private byte[] BuildLichtenbergVolume(Fractal3DState state, CancellationToken token)
    {
        var settings = state.Lichtenberg; settings.Validate();
        if (_lichtenberg is null || !_lichtenberg.Settings.SameGrowth(settings) ||
            _lichtenberg.Count > settings.SegmentCount || !_lichtenberg.HasPrefix(settings.Field) ||
            settings.Field is null && _lichtenbergOrigin is not null ||
            settings.Field is not null && !ReferenceEquals(settings.Field, _lichtenbergOrigin) &&
                !ReferenceEquals(settings.Field, _lichtenbergAnchor))
        {
            _lichtenberg?.Dispose(); _lichtenberg = null;
            _lichtenberg = new(_host, settings);
            _lichtenbergOrigin = _lichtenbergAnchor = settings.Field;
        }
        _lichtenberg.GrowTo(settings.SegmentCount, token);
        var field = _lichtenberg.Snapshot();
        const int side = Dla3DVolume.Side;
        byte[] voxels = new byte[side * side * side * 2];
        int n = settings.Size;
        double scale = 220d / n, radius = 1.8;
        int width = (int)Math.Ceiling(radius);
        var occupied = new HashSet<int>();
        for (int birth = 0; birth < field.Cells.Length; birth++)
        {
            token.ThrowIfCancellationRequested();
            int cell = field.Cells[birth];
            double x = side / 2d + (cell % n - n / 2) * scale;
            double y = side / 2d + (cell / n % n - n / 2) * scale;
            double z = side / 2d + (cell / (n * n) - n / 2) * scale;
            Splat(x,y,z,birth);
            foreach (int neighbor in Lichtenberg3DField.Neighbors(cell,n))
            {
                if (!occupied.Contains(neighbor)) continue;
                double nx = side/2d+(neighbor%n-n/2)*scale;
                double ny = side/2d+(neighbor/n%n-n/2)*scale;
                double nz = side/2d+(neighbor/(n*n)-n/2)*scale;
                int samples = (int)Math.Ceiling(scale*2);
                for (int step=1; step<=samples; step++)
                {
                    double t = (double)step/samples;
                    Splat(x+(nx-x)*t,y+(ny-y)*t,z+(nz-z)*t,birth);
                }
            }
            occupied.Add(cell);
        }
        return voxels;

        void Splat(double x, double y, double z, int birth)
        {
            for (int zz = (int)z - width; zz <= z + width; zz++)
            for (int yy = (int)y - width; yy <= y + width; yy++)
            for (int xx = (int)x - width; xx <= x + width; xx++)
            {
                double r2 = (xx - x) * (xx - x) + (yy - y) * (yy - y) + (zz - z) * (zz - z);
                if (r2 > radius * radius || xx < 0 || yy < 0 || zz < 0 || xx >= side || yy >= side || zz >= side) continue;
                int density = (int)(255 * Math.Exp(-1.4 * r2 / (radius * radius)));
                int index = ((zz * side + yy) * side + xx) * 2;
                if (density > voxels[index])
                { voxels[index] = (byte)density; voxels[index + 1] = (byte)(density * birth / Math.Max(1, field.Count)); }
            }
        }
    }
}
