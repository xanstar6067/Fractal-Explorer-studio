using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static class SnowCrystalRenderer
{
    public static byte[] RenderFrame(SnowCrystalCheckpoint snapshot, SnowCrystalState state,
        int width, int height, CancellationToken token, SnowCrystalLattice? lattice = null)
    {
        state.Validate();
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        lattice ??= new SnowCrystalLattice(snapshot.Radius);
        if (lattice.Radius != snapshot.Radius) throw new ArgumentException("Несовместимая решётка.");
        if (snapshot.Mass.Length != lattice.Count || snapshot.FrozenAt.Length != lattice.Count)
            throw new ArgumentException("Неверный размер поля.");
        var colors = new Color[lattice.Count];
        int crystalRadius = 1;
        for (int i = 0; i < lattice.Count; i++)
            if (snapshot.FrozenAt[i] >= 0) crystalRadius = Math.Max(crystalRadius, lattice.Sites[i].Q + lattice.Sites[i].R);
        for (int i = 0; i < lattice.Count; i++)
        {
            if (snapshot.FrozenAt[i] >= 0)
            {
                double t = state.Coloring switch
                {
                    SnowCrystalColoring.Mass => 1 / Math.Max(1, snapshot.Mass[i]),
                    SnowCrystalColoring.Radius => (lattice.Sites[i].Q + lattice.Sites[i].R) / (double)crystalRadius,
                    _ => snapshot.FrozenAt[i] / (double)Math.Max(1, snapshot.StepCount)
                };
                colors[i] = Mix(state.CenterColor, state.TipColor, Math.Clamp(t, 0, 1));
            }
            else colors[i] = state.ShowVapor
                ? Mix(state.BackgroundColor, state.CenterColor, Math.Clamp(snapshot.Mass[i], 0, 1) * .22)
                : state.BackgroundColor;
        }
        var pixels = new byte[checked(width * height * 4)];
        double scale = Math.Min(width, height) * .46 * state.Zoom / snapshot.Radius;
        // Two samples per axis soften the hexagonal cell edges without distorting the lattice.
        for (int y = 0; y < height; y++)
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int red = 0, green = 0, blue = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    double wx = (x + .25 + sx * .5 - width * .5) / scale + state.PanX;
                    double wy = -(y + .25 + sy * .5 - height * .5) / scale + state.PanY;
                    double r = wy / (Math.Sqrt(3) * .5), q = wx - r * .5;
                    (int iq, int ir) = RoundAxial(q, r);
                    int site = lattice.Index(iq, ir);
                    Color c = site < 0 ? state.BackgroundColor : colors[site];
                    red += c.R; green += c.G; blue += c.B;
                }
                int p = (y * width + x) * 4;
                pixels[p] = (byte)(blue / 4); pixels[p + 1] = (byte)(green / 4);
                pixels[p + 2] = (byte)(red / 4); pixels[p + 3] = 255;
            }
        }
        return pixels;
    }

    public static Task<BitmapSource> RenderStateAsync(SnowCrystalState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null) => Task.Run(() =>
    {
        var simulation = new SnowCrystalSimulation(state);
        if (state.Checkpoint is null)
        {
            int steps = SnowCrystalPresets.All.FirstOrDefault(p => p.Id == state.PresetId)?.PreviewSteps ?? 2200;
            for (int done = 0; done < steps && !simulation.BoundaryReached; done += 32)
            {
                simulation.Advance(Math.Min(32, steps - done), state, token);
                progress?.Report(Math.Min(90, (done + 32) * 90 / steps));
            }
        }
        byte[] pixels = RenderFrame(simulation.Snapshot(), state, width, height, token, simulation.Lattice);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); progress?.Report(100); return bitmap;
    }, token);

    private static (int Q, int R) RoundAxial(double q, double r)
    {
        int iq = (int)Math.Round(q), ir = (int)Math.Round(r), iz = (int)Math.Round(-q - r);
        double dq = Math.Abs(iq - q), dr = Math.Abs(ir - r), dz = Math.Abs(iz + q + r);
        if (dq > dr && dq > dz) iq = -ir - iz;
        else if (dr > dz) ir = -iq - iz;
        return (iq, ir);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
