using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static class HopalongRenderer
{
    public static (double X, double Y) ViewSpans(DynamicSystemState state, double width, double height)
    {
        double span = state.Hopalong.Span / state.Zoom;
        double shortSide = Math.Max(1, Math.Min(width, height));
        return (span * width / shortSide, span * height / shortSide);
    }

    public static byte[] RenderBuffer(DynamicSystemState state, int width, int height, DynamicPalette? palette,
        CancellationToken token, IProgress<int>? progress = null, Action<byte[]>? frameReady = null)
    {
        token.ThrowIfCancellationRequested();
        HopalongSettings settings = state.Hopalong ?? throw new InvalidOperationException("Не заданы параметры Hopalong.");
        settings.Validate();
        if (width < 1 || height < 1 || state.Iterations is < 1 or > 100_000_000 ||
            !double.IsFinite(state.Zoom) || state.Zoom <= 0 ||
            !double.IsFinite(state.CenterX) || !double.IsFinite(state.CenterY) ||
            !double.IsFinite(state.DensityGamma) || state.DensityGamma is < .05 or > 8)
            throw new InvalidOperationException("Проверьте размер, центр и масштаб кадра. Число точек: 1–100 млн; гамма: 0,05–8.");
        (double spanX, double spanY) = ViewSpans(state, width, height);
        if (!double.IsFinite(spanX) || !double.IsFinite(spanY) || spanX <= 0 || spanY <= 0)
            throw new InvalidOperationException("Масштаб выходит за поддерживаемый диапазон.");
        double minX = state.CenterX - spanX * .5, maxY = state.CenterY + spanY * .5;
        double angle = settings.Rotation % 360 * Math.PI / 180;
        double cosine = Math.Cos(angle), sine = Math.Sin(angle);
        double x = settings.StartX, y = settings.StartY;
        int[] density = new int[checked(width * height)];
        int lastProgress = -1, nextFrame = 10;

        // One uninterrupted orbit, with no warm-up, restarts or jittered workers:
        // changing the initial condition can select a different invariant region.
        for (int i = 0; i < state.Iterations; i++)
        {
            if ((i & 4095) == 0)
            {
                token.ThrowIfCancellationRequested();
                int value = (int)((long)i * 94 / state.Iterations);
                if (value > lastProgress) { lastProgress = value; progress?.Report(value); }
                if (frameReady is not null && value >= nextFrame)
                {
                    frameReady(Colorize());
                    nextFrame += 25;
                    token.ThrowIfCancellationRequested();
                }
            }
            HopalongMap.Iterate(settings, ref x, ref y);
            double fx = (x * cosine - y * sine - minX) / spanX;
            double fy = (maxY - x * sine - y * cosine) / spanY;
            if (!double.IsFinite(fx) || !double.IsFinite(fy) || fx < 0 || fx > 1 || fy < 0 || fy > 1) continue;
            int px = (int)Math.Round(fx * (width - 1)), py = (int)Math.Round(fy * (height - 1));
            density[py * width + px]++;
        }
        byte[] pixels = Colorize(); progress?.Report(100); return pixels;

        byte[] Colorize() => AttractorDensityColorizer.Colorize(density, state.BackgroundColor,
            state.FractalColor, palette, state.DensityGamma, token);
    }
}
