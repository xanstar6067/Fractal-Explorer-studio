using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>McCabe-style multiscale competition with separable square neighbourhood means.</summary>
public sealed class TuringSimulation
{
    private float[] _field, _next, _symmetric;
    private byte[] _scales, _nextScales;
    private readonly float[] _temporary, _blurBuffer, _activator, _inhibitor, _difference, _variation, _best, _delta;
    public int Size { get; }
    public long StepCount { get; private set; }

    public TuringSimulation(TuringState state)
    {
        state.Validate(); Size = state.GridSize;
        int count = Size * Size;
        _field = new float[count]; _next = new float[count]; _symmetric = new float[count];
        _scales = new byte[count]; _nextScales = new byte[count];
        _temporary = new float[count]; _blurBuffer = new float[count]; _activator = new float[count]; _inhibitor = new float[count];
        _difference = new float[count]; _variation = new float[count]; _best = new float[count]; _delta = new float[count];
        if (state.Checkpoint is { } cp)
        {
            cp.Field.CopyTo(_field, 0); cp.Scales.CopyTo(_scales, 0); StepCount = cp.StepCount;
        }
        else
        {
            var random = new Random(state.RandomSeed);
            for (int i = 0; i < count; i++) _field[i] = (float)(random.NextDouble() * 2 - 1);
            Symmetrize(_field, _symmetric, state, CancellationToken.None);
            (_field, _symmetric) = (_symmetric, _field);
            Normalize(_field);
        }
    }

    public TuringCheckpoint Snapshot() => new() { Size = Size, StepCount = StepCount, Field = [.. _field], Scales = [.. _scales] };

    public void Advance(int steps, TuringState state, CancellationToken token)
    {
        if (steps is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(steps));
        state.Validate();
        for (int step = 0; step < steps; step++)
        {
            token.ThrowIfCancellationRequested();
            Array.Fill(_best, float.PositiveInfinity);
            for (int layer = 0; layer < state.Layers.Count; layer++)
            {
                TuringScale scale = state.Layers[layer];
                if (!scale.Enabled) continue;
                int radius = Math.Clamp((int)Math.Round(scale.Radius * state.DetailSize * Size / 256), 1, Size - 1);
                int inhibitorRadius = Math.Clamp((int)Math.Round(radius * state.InhibitorRatio), radius + 1, Size);
                NeighbourhoodMean(_field, _activator, radius, state.Boundary, token);
                NeighbourhoodMean(_field, _inhibitor, inhibitorRadius, state.Boundary, token);
                for (int i = 0; i < _field.Length; i++) _difference[i] = Math.Abs(_activator[i] - _inhibitor[i]);
                NeighbourhoodMean(_difference, _variation, radius, state.Boundary, token);
                float amount = (float)(scale.Amount * state.EvolutionRate);
                for (int i = 0; i < _field.Length; i++)
                {
                    if (_variation[i] >= _best[i]) continue;
                    _best[i] = _variation[i]; _nextScales[i] = (byte)layer;
                    _delta[i] = _activator[i] > _inhibitor[i] ? amount : -amount;
                }
            }
            for (int i = 0; i < _field.Length; i++) _next[i] = _field[i] + _delta[i];
            Symmetrize(_next, _symmetric, state, token);
            Normalize(_symmetric);
            // A cancellation cannot publish a partially updated step or its scale map.
            token.ThrowIfCancellationRequested();
            (_field, _symmetric) = (_symmetric, _field);
            (_scales, _nextScales) = (_nextScales, _scales);
            StepCount++;
        }
    }

    public void Paint(double x, double y, double radius, double strength, TuringBrush brush, TuringState state)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || radius <= 0 || !double.IsFinite(radius)) return;
        int arms = state.Symmetry;
        double dx = x - .5, dy = y - .5;
        for (int arm = 0; arm < arms; arm++)
        {
            double angle = arm * Math.Tau / arms, c = Math.Cos(angle), s = Math.Sin(angle);
            PaintCircle(.5 + dx * c - dy * s, .5 + dx * s + dy * c);
            if (state.Mirror) PaintCircle(.5 + dx * c + dy * s, .5 + dx * s - dy * c);
        }
        void PaintCircle(double cx, double cy)
        {
            double px = cx * (Size - 1), py = cy * (Size - 1), r = radius * Size;
            for (int iy = Math.Max(0, (int)Math.Floor(py - r)); iy <= Math.Min(Size - 1, py + r); iy++)
            for (int ix = Math.Max(0, (int)Math.Floor(px - r)); ix <= Math.Min(Size - 1, px + r); ix++)
            {
                double distance = Math.Sqrt((ix - px) * (ix - px) + (iy - py) * (iy - py));
                if (distance >= r) continue;
                int i = iy * Size + ix;
                uint hash = unchecked((uint)(i * 374761393 + state.RandomSeed) ^ (uint)StepCount);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                double target = brush switch { TuringBrush.Dark => -1, TuringBrush.Noise => (hash >> 8) / (double)0xFFFFFF * 2 - 1, _ => 1 };
                _field[i] = (float)(_field[i] + (target - _field[i]) * strength * (1 - distance / r));
            }
        }
    }

    public static TuringCheckpoint Resize(TuringCheckpoint source, int size)
    {
        var cp = new TuringCheckpoint { Size = size, StepCount = source.StepCount, Field = new float[size * size], Scales = new byte[size * size] };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double sx = x * (source.Size - 1d) / (size - 1), sy = y * (source.Size - 1d) / (size - 1);
            cp.Field[y * size + x] = Sample(source.Field, source.Size, sx, sy, TuringBoundary.Reflect);
            cp.Scales[y * size + x] = source.Scales[(int)Math.Round(sy) * source.Size + (int)Math.Round(sx)];
        }
        return cp;
    }

    // Sliding sums keep the cost independent of the neighbourhood's area.
    public static void BoxMean(float[] source, float[] target, float[] temporary, int size, int radius, TuringBoundary boundary, CancellationToken token)
    {
        if (radius == 0) { source.CopyTo(target, 0); return; }
        int width = radius * 2 + 1;
        int[] enter = new int[size], leave = new int[size], initial = new int[width];
        for (int i = 0; i < size; i++) { enter[i] = Edge(i + radius + 1, size, boundary); leave[i] = Edge(i - radius, size, boundary); }
        for (int i = 0; i < width; i++) initial[i] = Edge(i - radius, size, boundary);
        for (int y = 0; y < size; y++)
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            int row = y * size; double sum = 0;
            foreach (int k in initial) sum += source[row + k];
            for (int x = 0; x < size; x++)
            {
                temporary[row + x] = (float)(sum / width);
                sum += (double)source[row + enter[x]] - source[row + leave[x]];
            }
        }
        for (int x = 0; x < size; x++)
        {
            if ((x & 15) == 0) token.ThrowIfCancellationRequested();
            double sum = 0;
            foreach (int k in initial) sum += temporary[k * size + x];
            for (int y = 0; y < size; y++)
            {
                target[y * size + x] = (float)(sum / width);
                sum += (double)temporary[enter[y] * size + x] - temporary[leave[y] * size + x];
            }
        }
    }

    private void NeighbourhoodMean(float[] source, float[] target, int radius, TuringBoundary boundary, CancellationToken token)
    {
        // Three box filters approximate a radial Gaussian and avoid rectangular-looking channels.
        int[] widths = GaussianBoxWidths(radius * .75);
        BoxMean(source, target, _temporary, Size, widths[0] / 2, boundary, token);
        BoxMean(target, _blurBuffer, _temporary, Size, widths[1] / 2, boundary, token);
        BoxMean(_blurBuffer, target, _temporary, Size, widths[2] / 2, boundary, token);
    }

    public static int[] GaussianBoxWidths(double sigma)
    {
        int lower = Math.Max(1, (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)));
        if ((lower & 1) == 0) lower--;
        int count = Math.Clamp((int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4d * lower - 4)), 0, 3);
        return Enumerable.Range(0, 3).Select(i => i < count ? lower : lower + 2).ToArray();
    }

    private void Symmetrize(float[] source, float[] target, TuringState state, CancellationToken token)
    {
        if (state.Symmetry == 1 && !state.Mirror) { source.CopyTo(target, 0); return; }
        double center = (Size - 1) * .5;
        var rotations = Enumerable.Range(0, state.Symmetry).Select(i => (C: Math.Cos(i * Math.Tau / state.Symmetry), S: Math.Sin(i * Math.Tau / state.Symmetry))).ToArray();
        for (int y = 0; y < Size; y++)
        {
            if ((y & 7) == 0) token.ThrowIfCancellationRequested();
            for (int x = 0; x < Size; x++)
            {
                double dx = x - center, dy = y - center, sum = 0;
                foreach (var (c, s) in rotations)
                {
                    sum += Sample(source, Size, center + dx * c - dy * s, center + dx * s + dy * c, TuringBoundary.Reflect);
                    if (state.Mirror) sum += Sample(source, Size, center + dx * c + dy * s, center + dx * s - dy * c, TuringBoundary.Reflect);
                }
                target[y * Size + x] = (float)(sum / (rotations.Length * (state.Mirror ? 2 : 1)));
            }
        }
    }

    private static void Normalize(float[] field)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (float value in field) { min = Math.Min(min, value); max = Math.Max(max, value); }
        double range = max - min;
        if (range < 1e-12) { Array.Fill(field, 0); return; }
        for (int i = 0; i < field.Length; i++) field[i] = (float)((field[i] - min) / range * 2 - 1);
    }

    public static float Sample(float[] field, int size, double x, double y, TuringBoundary boundary)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double tx = x - ix, ty = y - iy;
        int x0 = Edge(ix, size, boundary), x1 = Edge(ix + 1, size, boundary), y0 = Edge(iy, size, boundary), y1 = Edge(iy + 1, size, boundary);
        double a = field[y0 * size + x0] * (1 - tx) + field[y0 * size + x1] * tx;
        double b = field[y1 * size + x0] * (1 - tx) + field[y1 * size + x1] * tx;
        return (float)(a * (1 - ty) + b * ty);
    }

    private static int Edge(int position, int size, TuringBoundary boundary)
    {
        if ((uint)position < (uint)size) return position;
        if (boundary == TuringBoundary.Wrap) return (position % size + size) % size;
        int period = size * 2; int p = (position % period + period) % period;
        return p < size ? p : period - 1 - p;
    }
}
