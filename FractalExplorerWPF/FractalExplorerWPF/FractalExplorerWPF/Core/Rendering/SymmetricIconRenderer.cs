using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static class SymmetricIconRenderer
{
    public static byte[] RenderBuffer(DynamicSystemState state, int width, int height,
        DynamicPalette? palette, CancellationToken token, IProgress<int>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        SymmetricIconSettings settings = state.SymmetricIcon;
        settings.Validate();
        if (width < 1 || height < 1 || state.Iterations < 1 || state.DiscardIterations < 0 ||
            !double.IsFinite(state.Zoom) || state.Zoom <= 0 ||
            !double.IsFinite(state.CenterX) || !double.IsFinite(state.CenterY) ||
            !double.IsFinite(state.X0) || !double.IsFinite(state.Y0) ||
            !double.IsFinite(state.DensityGamma) || state.DensityGamma is < .05 or > 8)
            throw new InvalidOperationException("Проверьте размер, число точек, начало орбиты и параметры кадра.");
        int[] density = new int[checked(width * height)];
        // Fixed chains preserve the image when the CPU thread count changes. Cap histogram memory.
        int chains = (int)Math.Min(4, Math.Max(1, 128L * 1024 * 1024 / (density.Length * 4L)));
        chains = Math.Min(chains, state.Iterations);
        double spanX = settings.Span / state.Zoom * Math.Max(1, (double)width / height);
        double spanY = spanX * height / width;
        if (!double.IsFinite(spanX) || !double.IsFinite(spanY) || spanX <= 0 || spanY <= 0)
            throw new InvalidOperationException("Масштаб находится вне доступного диапазона.");
        double minX = state.CenterX - spanX / 2, maxY = state.CenterY + spanY / 2;
        double angle = settings.Rotation % 360 * Math.PI / 180;
        var rotations = Enumerable.Range(0, settings.Degree)
            .Select(i => (Cos: Math.Cos(angle + i * Math.Tau / settings.Degree),
                Sin: Math.Sin(angle + i * Math.Tau / settings.Degree))).ToArray();
        long completedPoints = 0;
        object progressLock = new();
        object mergeLock = new();
        Parallel.For(0, chains, new ParallelOptions { CancellationToken = token,
            MaxDegreeOfParallelism = Math.Max(1, state.Threads) }, chain =>
        {
            int[] local = new int[density.Length];
            double x = state.X0 + chain * 1e-9, y = state.Y0 - chain * 7.31e-10;
            for (int i = 0; i < state.DiscardIterations; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                Step();
            }
            int count = state.Iterations / chains + (chain < state.Iterations % chains ? 1 : 0);
            int reported = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    ReportPoints(i - reported);
                    reported = i;
                }
                Step();
                // Average over the symmetry group, so finite sampling cannot break the ornament.
                foreach (var rotation in rotations)
                {
                    Plot(x * rotation.Cos - y * rotation.Sin, x * rotation.Sin + y * rotation.Cos);
                    if (settings.Mirror)
                        Plot(x * rotation.Cos + y * rotation.Sin, x * rotation.Sin - y * rotation.Cos);
                }
            }
            ReportPoints(count - reported);
            lock (mergeLock)
            {
                for (int i = 0; i < density.Length; i++)
                {
                    if ((i & 65_535) == 0) token.ThrowIfCancellationRequested();
                    density[i] = (int)Math.Min(int.MaxValue, (long)density[i] + local[i]);
                }
            }

            void Step()
            {
                SymmetricIconMap.Iterate(settings, ref x, ref y);
                if (!SymmetricIconMap.IsBounded(x, y))
                    throw new InvalidOperationException("Орбита вышла за рабочую область. Измените параметры или выберите готовую форму.");
            }
            void Plot(double pointX, double pointY)
            {
                double px = (pointX - minX) / spanX * (width - 1);
                double py = (maxY - pointY) / spanY * (height - 1);
                if (px < -.5 || py < -.5 || px >= width - .5 || py >= height - .5) return;
                int index = (int)Math.Round(py) * width + (int)Math.Round(px);
                if (local[index] < int.MaxValue) local[index]++;
            }
        });
        token.ThrowIfCancellationRequested();
        progress?.Report(94);
        byte[] pixels = AttractorDensityColorizer.Colorize(density, state.BackgroundColor,
            state.FractalColor, palette, state.DensityGamma, token);
        progress?.Report(100);
        return pixels;

        void ReportPoints(int count)
        {
            if (progress is null) return;
            lock (progressLock)
            {
                completedPoints += count;
                progress.Report((int)(completedPoints * 90 / state.Iterations));
            }
        }
    }
}
