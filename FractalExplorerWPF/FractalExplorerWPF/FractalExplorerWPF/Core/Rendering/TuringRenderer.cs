using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public static class TuringRenderer
{
    public static readonly Color[] ScaleColors =
    [Color.FromRgb(72, 208, 199), Color.FromRgb(68, 137, 229), Color.FromRgb(170, 110, 220),
     Color.FromRgb(232, 149, 84), Color.FromRgb(231, 208, 103), Color.FromRgb(220, 93, 129), Color.FromRgb(135, 199, 93), Color.FromRgb(219, 218, 227)];

    public static byte[] RenderFrame(TuringCheckpoint cp, TuringState state, int width, int height, CancellationToken token, double displayAspect = 0)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        byte[] pixels = new byte[checked(width * height * 4)];
        Color[] palette = Enumerable.Range(0, 1024).Select(i => PaletteColor(state.Palette.Colors, i / 1023d)).ToArray();
        double logicalWidth = displayAspect > 0 ? height * displayAspect : width;
        double side = Math.Min(logicalWidth, height), viewSide = side * state.Zoom;
        double left = (logicalWidth - viewSide) * .5 - state.PanX * viewSide, top = (height - viewSide) * .5 - state.PanY * viewSide;
        for (int y = 0; y < height; y++)
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                double nx = ((x + .5) * logicalWidth / width - left) / viewSide, ny = (y + .5 - top) / viewSide;
                Color color = Color.FromRgb(10, 15, 22);
                if (nx is >= 0 and <= 1 && ny is >= 0 and <= 1 && (state.Symmetry == 1 || (nx - .5) * (nx - .5) + (ny - .5) * (ny - .5) <= .25))
                {
                    double sx = nx * (cp.Size - 1), sy = ny * (cp.Size - 1);
                    double value = TuringSimulation.Sample(cp.Field, cp.Size, sx, sy, state.Boundary);
                    double t = Math.Clamp(value * .5 * state.Contrast + .5, 0, 1);
                    if (state.ReversePalette) t = 1 - t;
                    color = state.Coloring == TuringColoring.Scales
                        ? ScaleColors[cp.Scales[Math.Clamp((int)Math.Round(sy), 0, cp.Size - 1) * cp.Size + Math.Clamp((int)Math.Round(sx), 0, cp.Size - 1)] % ScaleColors.Length]
                        : palette[(int)Math.Round(t * 1023)];
                    if (state.Coloring == TuringColoring.Relief)
                    {
                        double dx = (TuringSimulation.Sample(cp.Field, cp.Size, sx + 1, sy, state.Boundary) - TuringSimulation.Sample(cp.Field, cp.Size, sx - 1, sy, state.Boundary)) * state.Relief * cp.Size / 32;
                        double dy = (TuringSimulation.Sample(cp.Field, cp.Size, sx, sy + 1, state.Boundary) - TuringSimulation.Sample(cp.Field, cp.Size, sx, sy - 1, state.Boundary)) * state.Relief * cp.Size / 32;
                        double light = Math.Clamp(.55 + .55 * (.5 * dx + .6 * dy + .62) / Math.Sqrt(dx * dx + dy * dy + 1), .25, 1.1);
                        color = Color.FromRgb((byte)Math.Clamp(color.R * light, 0, 255), (byte)Math.Clamp(color.G * light, 0, 255), (byte)Math.Clamp(color.B * light, 0, 255));
                    }
                }
                int p = (y * width + x) * 4;
                pixels[p] = color.B; pixels[p + 1] = color.G; pixels[p + 2] = color.R; pixels[p + 3] = 255;
            }
        }
        return pixels;
    }

    public static Task<BitmapSource> RenderStateAsync(TuringState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) => Task.Run(() =>
    {
        state.Validate();
        using var simulation = TuringEngineFactory.Create(state, out _);
        if (state.Checkpoint is null)
            for (int i = 0; i < state.WarmupSteps; i++)
            {
                simulation.Advance(1, state, token);
                if (i % 10 == 0) progress?.Report(i * 90 / Math.Max(1, state.WarmupSteps));
            }
        byte[] pixels = simulation.RenderFrame(state, width, height, token);
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); progress?.Report(100); return bitmap;
    }, token);

    internal static Color PaletteColor(List<Color> colors, double t)
    {
        double position = t * (colors.Count - 1); int left = Math.Min((int)position, colors.Count - 2); double f = position - left;
        Color a = colors[left], b = colors[left + 1];
        return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f), (byte)Math.Round(a.B + (b.B - a.B) * f));
    }
}
