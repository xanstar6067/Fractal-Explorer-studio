using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Pickover's simultaneous real-plane map. Unlike a dissipative attractor,
/// Popcorn retains the choice of starting points: never replace a seed grid
/// with jittered copies of a single orbit or discard a transient.
/// </summary>
public static class PopcornRenderer
{
    public static void Iterate(double h, double k, ref double x, ref double y)
    {
        double oldX = x, oldY = y;
        x = oldX - h * Math.Sin(oldY + Math.Tan(k * oldY));
        y = oldY - h * Math.Sin(oldX + Math.Tan(k * oldX));
    }

    public static (double X, double Y) ViewSpans(DynamicSystemState state, double width, double height)
    {
        double span = state.Popcorn.Span / state.Zoom;
        double shortSide = Math.Max(1, Math.Min(width, height));
        return (span * width / shortSide, span * height / shortSide);
    }

    public static byte[] RenderBuffer(DynamicSystemState state, int width, int height,
        DynamicPalette? palette, CancellationToken token, IProgress<int>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        PopcornSettings settings = state.Popcorn ?? throw new InvalidOperationException("Не заданы параметры Popcorn.");
        settings.Validate();
        if (width < 1 || height < 1 || !double.IsFinite(state.Zoom) || state.Zoom <= 0 ||
            !double.IsFinite(state.CenterX) || !double.IsFinite(state.CenterY) ||
            !double.IsFinite(state.DensityGamma) || state.DensityGamma is < .05 or > 8)
            throw new InvalidOperationException("Проверьте размер кадра, центр, масштаб и гамму плотности (0,05–8).");

        int[] density = new int[checked(width * height)];
        (double spanX, double spanY) = ViewSpans(state, width, height);
        if (!double.IsFinite(spanX) || !double.IsFinite(spanY) || spanX <= 0 || spanY <= 0)
            throw new InvalidOperationException("Масштаб выходит за поддерживаемый диапазон.");
        double minX = state.CenterX - spanX * .5, maxY = state.CenterY + spanY * .5;
        int side = settings.PlotMode == PopcornPlotMode.GridOrbits ? settings.GridSize : 1 << settings.HilbertOrder;
        int count = side * side;
        bool curve = settings.PlotMode == PopcornPlotMode.HilbertCurve;
        var vertices = curve ? new (double X, double Y)[count] : null;
        int completed = 0, reported = -1;
        object progressLock = new();

        // The fixed blocks and exact integer hit sums make the frame independent
        // of the number of CPU threads, with only one full-sized density buffer.
        Parallel.For(0, (count + 255) / 256, new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Clamp(state.Threads, 1, Environment.ProcessorCount)
        }, block =>
        {
            int end = Math.Min(count, (block + 1) * 256);
            for (int index = block * 256; index < end; index++)
            {
                token.ThrowIfCancellationRequested();
                (int sx, int sy) = curve ? HilbertPoint(side, index) : (index % side, index / side);
                double x = ((sx + .5) / side - .5) * settings.SeedSpan;
                double y = ((sy + .5) / side - .5) * settings.SeedSpan;
                for (int step = 0; step < settings.OrbitIterations; step++)
                {
                    if ((step & 63) == 0) token.ThrowIfCancellationRequested();
                    Iterate(settings.H, settings.K, ref x, ref y);
                    if (!double.IsFinite(x) || !double.IsFinite(y)) break;
                    if (!curve) PlotPoint(x, y);
                }
                if (vertices is not null) vertices[index] = (x, y);
            }
            int done = Interlocked.Add(ref completed, end - block * 256);
            if (progress is not null)
                lock (progressLock)
                {
                    int value = done * (curve ? 75 : 94) / count;
                    if (value > reported) { reported = value; progress.Report(value); }
                }
        });

        if (vertices is not null)
            for (int index = 1; index < vertices.Length; index++)
            {
                if ((index & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(75 + index * 19 / vertices.Length);
                }
                var from = vertices[index - 1];
                var to = vertices[index];
                PlotLine((from.X - minX) / spanX * (width - 1), (maxY - from.Y) / spanY * (height - 1),
                    (to.X - minX) / spanX * (width - 1), (maxY - to.Y) / spanY * (height - 1), density, width, height, token);
            }

        byte[] pixels = AttractorDensityColorizer.Colorize(density, state.BackgroundColor,
            state.FractalColor, palette, state.DensityGamma, token);
        progress?.Report(100);
        return pixels;

        void PlotPoint(double x, double y)
        {
            double fx = (x - minX) / spanX, fy = (maxY - y) / spanY;
            if (fx < 0 || fx > 1 || fy < 0 || fy > 1) return;
            int px = (int)Math.Round(fx * (width - 1)), py = (int)Math.Round(fy * (height - 1));
            Interlocked.Increment(ref density[py * width + px]);
        }
    }

    public static (int X, int Y) HilbertPoint(int side, int index)
    {
        int x = 0, y = 0;
        for (int scale = 1, remaining = index; scale < side; scale *= 2, remaining /= 4)
        {
            int rx = 1 & (remaining / 2), ry = 1 & (remaining ^ rx);
            if (ry == 0)
            {
                if (rx == 1) { x = scale - 1 - x; y = scale - 1 - y; }
                (x, y) = (y, x);
            }
            x += scale * rx; y += scale * ry;
        }
        return (x, y);
    }

    private static void PlotLine(double x0, double y0, double x1, double y1,
        int[] density, int width, int height, CancellationToken token)
    {
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)) return;
        double dx = x1 - x0, dy = y1 - y0, first = 0, last = 1;
        // Liang–Barsky clipping bounds work even when zoomed far into a curve.
        if (!Clip(-dx, x0) || !Clip(dx, width - 1 - x0) || !Clip(-dy, y0) || !Clip(dy, height - 1 - y0)) return;
        x1 = x0 + last * dx; y1 = y0 + last * dy;
        x0 += first * dx; y0 += first * dy;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
        for (int step = 0; step <= steps; step++)
        {
            if ((step & 255) == 0) token.ThrowIfCancellationRequested();
            double t = steps == 0 ? 0 : (double)step / steps;
            int x = Math.Clamp((int)Math.Round(x0 + (x1 - x0) * t), 0, width - 1);
            int y = Math.Clamp((int)Math.Round(y0 + (y1 - y0) * t), 0, height - 1);
            density[y * width + x]++;
        }
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double ratio = q / p;
            if (p < 0) { if (ratio > last) return false; first = Math.Max(first, ratio); }
            else { if (ratio < first) return false; last = Math.Min(last, ratio); }
            return true;
        }
    }
}
