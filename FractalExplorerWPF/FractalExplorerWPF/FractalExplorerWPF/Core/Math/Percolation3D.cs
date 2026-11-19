using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.PercolationMath;

internal sealed record Percolation3DStatistics(int Occupied, int ClusterCount, int LargestSize, int CrossingAxes,
    int SpanningClusters, int PathSteps)
{
    public bool Crosses(Percolation3DAxis axis) => (CrossingAxes & (1 << (int)axis)) != 0;
}

internal sealed record Percolation3DResult(int[] Labels, int[] Sizes, int[] FaceMasks, int LargestLabel,
    int[] Path, Percolation3DStatistics Statistics);

/// <summary>Узловая перколяция на кубической решётке: шесть соседей по граням, открытые границы.</summary>
internal static class Percolation3D
{
    public static bool[] GenerateOccupancy(Percolation3DSettings settings, CancellationToken token)
    {
        settings.Validate();
        int side = settings.Size;
        var occupied = new bool[side * side * side];
        uint seed = unchecked((uint)settings.Seed);
        bool correlated = settings.Medium == Percolation3DMedium.Correlated;
        for (int z = 0, i = 0; z < side; z++)
        {
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++, i++)
            {
                double threshold = correlated
                    ? 0.75 * Noise(x, y, z, 12, seed) + 0.25 * Noise(x, y, z, 5, seed ^ 0x85ebca6bu)
                    : Unit(unchecked(seed + (uint)(i + 1) * 0x9e3779b9u));
                occupied[i] = threshold < settings.Probability;
            }
        }
        return occupied;
    }

    public static Percolation3DResult Analyze(Percolation3DSettings settings, ReadOnlySpan<bool> occupied,
        CancellationToken token)
    {
        settings.Validate();
        int side = settings.Size;
        int plane = side * side;
        if (occupied.Length != side * plane) throw new ArgumentException("Неверный размер решётки.", nameof(occupied));
        token.ThrowIfCancellationRequested();

        var labels = new int[occupied.Length];
        var queue = new int[occupied.Length];
        var sizes = new List<int> { 0 };
        var faces = new List<int> { 0 };
        int total = 0, largest = 0, axes = 0, spanning = 0;
        int selectedMask = AxisMask(settings.Axis);
        Span<int> neighbors = stackalloc int[6];
        for (int start = 0; start < occupied.Length; start++)
        {
            if ((start & 4095) == 0) token.ThrowIfCancellationRequested();
            if (!occupied[start] || labels[start] != 0) continue;

            int label = sizes.Count;
            int head = 0, tail = 1, mask = 0;
            queue[0] = start;
            labels[start] = label;
            while (head < tail)
            {
                if ((head & 4095) == 0) token.ThrowIfCancellationRequested();
                int index = queue[head++];
                int count = Neighbors(index, side, plane, neighbors, out int boundary);
                mask |= boundary;
                for (int k = 0; k < count; k++)
                {
                    int next = neighbors[k];
                    if (!occupied[next] || labels[next] != 0) continue;
                    labels[next] = label;
                    queue[tail++] = next;
                }
            }

            sizes.Add(tail);
            faces.Add(mask);
            total += tail;
            if (tail > sizes[largest]) largest = label;
            int pairs = mask & (mask >> 1);
            axes |= (pairs & 1) | ((pairs >> 1) & 2) | ((pairs >> 2) & 4);
            if ((mask & selectedMask) == selectedMask) spanning++;
        }

        int[] path = spanning == 0 ? [] : FindPath(settings, labels, faces, queue, token);
        return new(labels, sizes.ToArray(), faces.ToArray(), largest, path,
            new(total, sizes.Count - 1, sizes[largest], axes, spanning, Math.Max(0, path.Length - 1)));
    }

    public static int AxisMask(Percolation3DAxis axis) => 3 << (2 * (int)axis);

    private static int Neighbors(int index, int side, int plane, Span<int> neighbors, out int mask)
    {
        int x = index % side, y = index / side % side, z = index / plane;
        int count = 0;
        mask = 0;
        if (x > 0) neighbors[count++] = index - 1; else mask |= 1;
        if (x < side - 1) neighbors[count++] = index + 1; else mask |= 2;
        if (y > 0) neighbors[count++] = index - side; else mask |= 4;
        if (y < side - 1) neighbors[count++] = index + side; else mask |= 8;
        if (z > 0) neighbors[count++] = index - plane; else mask |= 16;
        if (z < side - 1) neighbors[count++] = index + plane; else mask |= 32;
        return count;
    }

    private static int[] FindPath(Percolation3DSettings settings, int[] labels, List<int> faces, int[] queue,
        CancellationToken token)
    {
        int side = settings.Size, plane = side * side;
        int mask = AxisMask(settings.Axis);
        var parent = new int[labels.Length]; // 0 — не посещён, −1 — начало, иначе индекс предка + 1.
        int head = 0, tail = 0;
        for (int v = 0; v < side; v++)
        for (int u = 0; u < side; u++)
        {
            int index = settings.Axis switch
            {
                Percolation3DAxis.X => u * side + v * plane,
                Percolation3DAxis.Y => u + v * plane,
                _ => u + v * side
            };
            if ((faces[labels[index]] & mask) != mask) continue;
            parent[index] = -1;
            queue[tail++] = index;
        }

        Span<int> neighbors = stackalloc int[6];
        while (head < tail)
        {
            if ((head & 4095) == 0) token.ThrowIfCancellationRequested();
            int index = queue[head++];
            int count = Neighbors(index, side, plane, neighbors, out int boundary);
            if ((boundary & (mask & 42)) != 0)
            {
                var path = new List<int>();
                for (int current = index; current >= 0; current = parent[current] - 1) path.Add(current);
                path.Reverse();
                return path.ToArray();
            }
            for (int k = 0; k < count; k++)
            {
                int next = neighbors[k];
                if (labels[next] == 0 || parent[next] != 0) continue;
                parent[next] = index + 1;
                queue[tail++] = next;
            }
        }
        throw new InvalidOperationException("Не найден путь в сквозном кластере.");
    }

    private static double Noise(int x, int y, int z, int scale, uint seed)
    {
        double fx = Smooth((x % scale) / (double)scale);
        double fy = Smooth((y % scale) / (double)scale);
        double fz = Smooth((z % scale) / (double)scale);
        x /= scale; y /= scale; z /= scale;
        return Lerp(Plane(z), Plane(z + 1), fz);

        double Plane(int k) => Lerp(Row(y, k), Row(y + 1, k), fy);
        double Row(int j, int k) => Lerp(Corner(x, j, k), Corner(x + 1, j, k), fx);
        double Corner(int i, int j, int k) =>
            Unit(unchecked(seed ^ ((uint)i * 0x9e3779b9u) ^ ((uint)j * 0x85ebca6bu) ^ ((uint)k * 0xc2b2ae35u)));
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Unit(uint value) => (Mix(value) + 0.5) / 4294967296.0;

    // Фиксированная перестановка 32-битных чисел обеспечивает воспроизводимость по seed.
    internal static uint Mix(uint value)
    {
        value = unchecked((value ^ (value >> 16)) * 0x21f0aaadu);
        value = unchecked((value ^ (value >> 15)) * 0x735a2d97u);
        return value ^ (value >> 15);
    }
}
