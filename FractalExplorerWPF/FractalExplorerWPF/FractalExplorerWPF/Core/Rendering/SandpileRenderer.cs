using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static class SandpileRenderer
{
    // Shared by drawing and pointer mapping. Coordinates are centered lattice coordinates, y points down.
    public static (double X, double Y) ToLattice(double x, double y, double width, double height, SandpileState state)
    {
        double extent = state.Lattice == SandpileLattice.Square ? 1 : 1.5;
        double scale = Math.Min(width, height) * .92 * state.Zoom / (state.Size * extent);
        double wy = (y - height / 2) / scale + state.PanY;
        double wx = (x - width / 2) / scale + state.PanX;
        if (state.Lattice == SandpileLattice.Square) return (wx, wy);
        double r = wy / (Math.Sqrt(3) / 2);
        return (wx - .5 * r, r);
    }

    public static (int X, int Y) CellAt(double x, double y, double width, double height, SandpileState state)
    {
        var (q, r) = ToLattice(x, y, width, height, state);
        int iq = (int)Math.Round(q), ir = (int)Math.Round(r);
        if (state.Lattice == SandpileLattice.Triangular)
        {
            int iz = (int)Math.Round(-q - r);
            double dq = Math.Abs(iq - q), dr = Math.Abs(ir - r), dz = Math.Abs(iz + q + r);
            if (dq > dr && dq > dz) iq = -ir - iz;
            else if (dr > dz) ir = -iq - iz;
        }
        return (iq + state.Size / 2, ir + state.Size / 2);
    }

    public static byte[] RenderFrame(SandpileCheckpoint field, SandpileState state, int width, int height, CancellationToken token)
    {
        state.Validate();
        if (width <= 0 || height <= 0 || field.Grains.Length != state.Size * state.Size || field.Topplings.Length != field.Grains.Length)
            throw new ArgumentException("Некорректный размер кадра или поля.");
        token.ThrowIfCancellationRequested();
        var colors = new Color[field.Grains.Length];
        double logMax = Math.Log(1 + (double)field.Topplings.Max());
        for (int i = 0; i < colors.Length; i++)
        {
            long grains = field.Grains[i];
            colors[i] = state.Coloring switch
            {
                SandpileColoring.Topplings => Mix(state.Colors[0], state.Colors[3], logMax == 0 ? 0 : Math.Log(1 + (double)field.Topplings[i]) / logMax),
                SandpileColoring.Activity => grains >= state.Degree ? Color.FromRgb(255, 100, 60) : Mix(state.Colors[0], state.Colors[2], grains / (double)(state.Degree - 1) * .4),
                _ => grains >= state.Degree ? Color.FromRgb(255, 100, 60) : state.Colors[(int)grains]
            };
        }
        var pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                var (cx, cy) = CellAt(x + .5, y + .5, width, height, state);
                Color c = (uint)cx < state.Size && (uint)cy < state.Size ? colors[cy * state.Size + cx] : state.Colors[0];
                int p = (y * width + x) * 4;
                pixels[p] = c.B; pixels[p + 1] = c.G; pixels[p + 2] = c.R; pixels[p + 3] = 255;
            }
        }
        return pixels;
    }

    public static Task<BitmapSource> RenderStateAsync(SandpileState state, int width, int height, CancellationToken token,
        IProgress<int>? progress = null) => Task.Run<BitmapSource>(() =>
    {
        // Saved fields render exactly as captured. Parameter-only states are settled for built-in previews.
        var simulation = new SandpileSimulation(state);
        if (state.Checkpoint is null)
            while (!simulation.IsStable) simulation.Advance(100_000, token);
        byte[] pixels = RenderFrame(simulation.Snapshot(), state, width, height, token);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); progress?.Report(100); return bitmap;
    }, token);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
