using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed class GrayScottSimulation
{
    private readonly int _size;
    private readonly double _diffusionU;
    private readonly double _diffusionV;
    private readonly double _feed;
    private readonly double _kill;
    private readonly double _deltaTime;
    private float[] _u;
    private float[] _v;
    private float[] _nextU;
    private float[] _nextV;

    public int Size => _size;
    public long StepCount { get; private set; }

    public GrayScottSimulation(GrayScottState state)
    {
        state.Validate();
        _size = state.GridSize;
        _diffusionU = state.DiffusionU;
        _diffusionV = state.DiffusionV;
        _feed = state.Feed;
        _kill = state.Kill;
        _deltaTime = state.DeltaTime;
        int length = _size * _size;
        _u = new float[length];
        _v = new float[length];
        _nextU = new float[length];
        _nextV = new float[length];
        if (state.Checkpoint is { } cp)
        {
            _u = [.. cp.U]; _v = [.. cp.V]; StepCount = cp.StepCount; return;
        }
        Array.Fill(_u, 1f);
        InitializeSeed(state);
    }

    public void Advance(int steps, CancellationToken token)
    {
        for (int step = 0; step < steps; step++)
        {
            token.ThrowIfCancellationRequested();
            AdvanceOneStep(token);
            (_u, _nextU) = (_nextU, _u);
            (_v, _nextV) = (_nextV, _v);
            StepCount++;
        }
    }

    public void Inject(double normalizedX, double normalizedY, int radius)
    {
        int centerX = Math.Clamp((int)Math.Round(normalizedX * (_size - 1)), 0, _size - 1);
        int centerY = Math.Clamp((int)Math.Round(normalizedY * (_size - 1)), 0, _size - 1);
        PaintCircle(centerX, centerY, Math.Clamp(radius, 1, _size / 3), 0.22f, 0.72f);
    }

    public GrayScottSnapshot Snapshot() => new(_size, [.. _u], [.. _v], StepCount);

    public GrayScottSnapshot CurrentView() => new(_size, _u, _v, StepCount);

    private void AdvanceOneStep(CancellationToken token)
    {
        int size = _size;
        if (size >= 384)
        {
            Parallel.For(0, size, new ParallelOptions { CancellationToken = token }, AdvanceRow);
            return;
        }

        for (int y = 0; y < size; y++)
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            AdvanceRow(y);
        }
    }

    private void AdvanceRow(int y)
    {
        int size = _size;
        int previousY = y == 0 ? size - 1 : y - 1;
        int nextY = y == size - 1 ? 0 : y + 1;
        int row = y * size;
        int previousRow = previousY * size;
        int nextRow = nextY * size;

        for (int x = 0; x < size; x++)
        {
            int previousX = x == 0 ? size - 1 : x - 1;
            int nextX = x == size - 1 ? 0 : x + 1;
            int index = row + x;
            float u = _u[index];
            float v = _v[index];
            double laplacianU = -u
                + 0.2 * (_u[row + previousX] + _u[row + nextX] + _u[previousRow + x] + _u[nextRow + x])
                + 0.05 * (_u[previousRow + previousX] + _u[previousRow + nextX]
                          + _u[nextRow + previousX] + _u[nextRow + nextX]);
            double laplacianV = -v
                + 0.2 * (_v[row + previousX] + _v[row + nextX] + _v[previousRow + x] + _v[nextRow + x])
                + 0.05 * (_v[previousRow + previousX] + _v[previousRow + nextX]
                          + _v[nextRow + previousX] + _v[nextRow + nextX]);
            double reaction = u * v * v;
            _nextU[index] = (float)Math.Clamp(
                u + (_diffusionU * laplacianU - reaction + _feed * (1 - u)) * _deltaTime, 0, 1);
            _nextV[index] = (float)Math.Clamp(
                v + (_diffusionV * laplacianV + reaction - (_feed + _kill) * v) * _deltaTime, 0, 1);
        }
    }

    private void InitializeSeed(GrayScottState state)
    {
        var random = new Random(state.RandomSeed);
        int radius = Math.Clamp(state.SeedRadius, 1, _size / 3);
        switch (state.SeedMode)
        {
            case GrayScottSeedMode.CenterSquare:
                PaintSquare(_size / 2, _size / 2, radius);
                break;
            case GrayScottSeedMode.RandomSpots:
                for (int index = 0; index < state.SeedCount; index++)
                    PaintCircle(random.Next(_size), random.Next(_size), radius, 0.25f, 0.7f);
                break;
            case GrayScottSeedMode.Ring:
            {
                double ringRadius = _size * 0.22;
                double thickness = Math.Max(2, radius);
                double center = (_size - 1) * 0.5;
                for (int y = 0; y < _size; y++)
                for (int x = 0; x < _size; x++)
                {
                    double distance = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    if (Math.Abs(distance - ringRadius) <= thickness)
                        SetSeedCell(y * _size + x, 0.28f, 0.68f);
                }
                break;
            }
            case GrayScottSeedMode.Noise:
                for (int index = 0; index < _v.Length; index++)
                {
                    if (random.NextDouble() > 0.12) continue;
                    float v = (float)(0.3 + random.NextDouble() * 0.45);
                    SetSeedCell(index, 1 - v, v);
                }
                break;
        }

        for (int index = 0; index < _u.Length; index++)
        {
            double perturbation = (random.NextDouble() - 0.5) * 0.025;
            _u[index] = (float)Math.Clamp(_u[index] + perturbation, 0, 1);
            _v[index] = (float)Math.Clamp(_v[index] - perturbation, 0, 1);
        }
    }

    private void PaintSquare(int centerX, int centerY, int radius)
    {
        for (int y = centerY - radius; y <= centerY + radius; y++)
        for (int x = centerX - radius; x <= centerX + radius; x++)
            SetSeedCell(WrappedIndex(x, y), 0.25f, 0.7f);
    }

    private void PaintCircle(int centerX, int centerY, int radius, float u, float v)
    {
        int squaredRadius = radius * radius;
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y <= squaredRadius)
                SetSeedCell(WrappedIndex(centerX + x, centerY + y), u, v);
        }
    }

    private int WrappedIndex(int x, int y)
    {
        x = (x % _size + _size) % _size;
        y = (y % _size + _size) % _size;
        return y * _size + x;
    }

    private void SetSeedCell(int index, float u, float v)
    {
        _u[index] = u;
        _v[index] = v;
    }
}

public static class GrayScottRenderer
{
    private const int PaletteResolution = 1024;

    public static byte[] RenderFrame(GrayScottSnapshot snapshot, GrayScottState state, CancellationToken token) =>
        RenderFrame(snapshot, state, snapshot.Size, snapshot.Size, token);

    public static byte[] RenderFrame(
        GrayScottSnapshot snapshot,
        GrayScottState state,
        int width,
        int height,
        CancellationToken token, double displayAspect = 0)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        int[] palette = BuildPalette(state.Palette);
        byte[] pixels = new byte[width * height * 4];
        double denominator = state.RangeMaximum - state.RangeMinimum;
        double logicalWidth = displayAspect > 0 ? height * displayAspect : width;
        double side = Math.Min(logicalWidth, height), left = (logicalWidth - side) / 2, top = (height - side) / 2;

        for (int y = 0; y < height; y++)
        {
            if ((y & 31) == 0) token.ThrowIfCancellationRequested();
            double ny = (y + .5 - top) / side;
            int targetRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                double nx = ((x + .5) * logicalWidth / width - left) / side;
                int pixel = targetRow + x * 4;
                if (nx is < 0 or > 1 || ny is < 0 or > 1) { pixels[pixel + 3] = 255; continue; }
                double sx = nx * snapshot.Size - .5, sy = ny * snapshot.Size - .5;
                double value = state.FieldMode switch
                {
                    GrayScottFieldMode.U => GrayScottSnapshot.Sample(snapshot.U, snapshot.Size, sx, sy),
                    GrayScottFieldMode.Difference => GrayScottSnapshot.Sample(snapshot.U, snapshot.Size, sx, sy) - GrayScottSnapshot.Sample(snapshot.V, snapshot.Size, sx, sy),
                    _ => GrayScottSnapshot.Sample(snapshot.V, snapshot.Size, sx, sy)
                };
                double normalized = Math.Clamp((value - state.RangeMinimum) / denominator, 0, 1);
                if (state.ReversePalette) normalized = 1 - normalized;
                int color = palette[Math.Clamp((int)Math.Round(normalized * (PaletteResolution - 1)), 0, PaletteResolution - 1)];
                pixels[pixel] = (byte)color;
                pixels[pixel + 1] = (byte)(color >> 8);
                pixels[pixel + 2] = (byte)(color >> 16);
                pixels[pixel + 3] = (byte)(color >> 24);
            }
        }
        return pixels;
    }

    public static Task<BitmapSource> RenderPreviewAsync(
        GrayScottState state,
        int width,
        int height,
        CancellationToken token,
        IProgress<int>? progress = null) => Task.Run(() =>
    {
        GrayScottState previewState = state.Clone();
        using var simulation = GrayScottEngineFactory.Create(previewState, out _);
        const int totalSteps = 900;
        const int batch = 30;
        for (int completed = 0; previewState.Checkpoint is null && completed < totalSteps; completed += batch)
        {
            int count = Math.Min(batch, totalSteps - completed);
            simulation.Advance(count, token);
            progress?.Report((completed + count) * 90 / totalSteps);
        }
        byte[] pixels = simulation.RenderFrame(previewState, width, height, token);
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        progress?.Report(100);
        return bitmap;
    }, token);

    public static Task<BitmapSource> RenderSnapshotAsync(
        GrayScottSnapshot snapshot,
        GrayScottState state,
        int width,
        int height,
        CancellationToken token,
        IProgress<int>? progress = null) => Task.Run(() =>
    {
        progress?.Report(10);
        var renderState = state.Clone(includeCheckpoint: false); renderState.Checkpoint = snapshot;
        using var engine = GrayScottEngineFactory.Create(renderState, out _);
        byte[] pixels = engine.RenderFrame(renderState, width, height, token);
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        progress?.Report(100);
        return bitmap;
    }, token);

    internal static int[] BuildPalette(GrayScottPalette palette)
    {
        List<Color> colors = palette.Colors.Count == 0 ? [Colors.Black] : palette.Colors;
        var lookup = new int[PaletteResolution];
        for (int index = 0; index < lookup.Length; index++)
        {
            double normalized = index / (double)(lookup.Length - 1);
            normalized = Math.Pow(normalized, 1 / Math.Clamp(palette.Gamma, 0.1, 5));
            Color color;
            if (colors.Count == 1)
            {
                color = colors[0];
            }
            else if (!palette.IsGradient)
            {
                color = colors[Math.Min((int)(normalized * colors.Count), colors.Count - 1)];
            }
            else
            {
                double position = normalized * (colors.Count - 1);
                int left = Math.Min((int)position, colors.Count - 2);
                double amount = position - left;
                color = Color.FromArgb(
                    Lerp(colors[left].A, colors[left + 1].A, amount),
                    Lerp(colors[left].R, colors[left + 1].R, amount),
                    Lerp(colors[left].G, colors[left + 1].G, amount),
                    Lerp(colors[left].B, colors[left + 1].B, amount));
            }
            lookup[index] = color.B | color.G << 8 | color.R << 16 | color.A << 24;
        }
        return lookup;
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * amount);
}
