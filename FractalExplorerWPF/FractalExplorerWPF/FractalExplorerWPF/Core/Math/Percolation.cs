namespace FractalExplorerWPF.Core.PercolationMath;

public enum PercolationLattice { Square, Triangular }

public sealed record PercolationStatistics(int Occupied, int ClusterCount, int LargestSize,
    bool HorizontalCrossing, bool VerticalCrossing, int PathLength);

public sealed record PercolationResult(int Size, PercolationLattice Lattice, int[] Labels,
    int[] ClusterSizes, byte[] Boundaries, int LargestLabel, int[] Path, PercolationStatistics Statistics)
{
    public bool IsSpanning(int label) => label > 0 &&
        ((Boundaries[label] & 3) == 3 || (Boundaries[label] & 12) == 12);
}

/// <summary>Независимая перколяция узлов с открытыми границами. Индивидуальные пороги
/// узлов зависят только от seed и индекса: увеличение p никогда не удаляет узлы.</summary>
public static class Percolation
{
    public static double CriticalProbability(PercolationLattice lattice) =>
        lattice == PercolationLattice.Triangular ? 0.5 : 0.592746050792;

    public static PercolationResult Generate(int size, double probability, int seed,
        PercolationLattice lattice, CancellationToken token = default)
    {
        Validate(size, lattice);
        if (!double.IsFinite(probability) || probability < 0 || probability > 1)
            throw new ArgumentOutOfRangeException(nameof(probability));
        var occupied = new bool[size * size];
        uint random = unchecked((uint)seed);
        for (int i = 0; i < occupied.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            // SplitMix32; фиксированный алгоритм сохраняет реализации между запусками и версиями .NET.
            random = unchecked(random + 0x9e3779b9u);
            uint value = random;
            value = unchecked((value ^ (value >> 16)) * 0x21f0aaadu);
            value = unchecked((value ^ (value >> 15)) * 0x735a2d97u);
            value ^= value >> 15;
            occupied[i] = (value + 0.5) / 4294967296.0 < probability;
        }
        return Analyze(size, occupied, lattice, token);
    }

    public static PercolationResult Analyze(int size, ReadOnlySpan<bool> occupied,
        PercolationLattice lattice, CancellationToken token = default)
    {
        Validate(size, lattice);
        if (occupied.Length != size * size) throw new ArgumentException("Неверный размер решётки.", nameof(occupied));
        token.ThrowIfCancellationRequested();
        var labels = new int[occupied.Length];
        var queue = new int[occupied.Length];
        var sizes = new List<int> { 0 };
        var boundaries = new List<byte> { 0 };
        int total = 0, largest = 0;
        bool horizontal = false, vertical = false;
        Span<int> neighbors = stackalloc int[6];
        for (int start = 0; start < occupied.Length; start++)
        {
            if ((start & 4095) == 0) token.ThrowIfCancellationRequested();
            if (!occupied[start] || labels[start] != 0) continue;
            int label = sizes.Count, head = 0, tail = 1;
            byte mask = 0;
            queue[0] = start; labels[start] = label;
            while (head < tail)
            {
                if ((head & 4095) == 0) token.ThrowIfCancellationRequested();
                int index = queue[head++], x = index % size, y = index / size;
                if (x == 0) mask |= 1;
                if (x == size - 1) mask |= 2;
                if (y == 0) mask |= 4;
                if (y == size - 1) mask |= 8;
                int count = GetNeighbors(index, size, lattice, neighbors);
                for (int k = 0; k < count; k++)
                {
                    int next = neighbors[k];
                    if (!occupied[next] || labels[next] != 0) continue;
                    labels[next] = label;
                    queue[tail++] = next;
                }
            }
            sizes.Add(tail); boundaries.Add(mask); total += tail;
            if (tail > sizes[largest]) largest = label;
            horizontal |= (mask & 3) == 3;
            vertical |= (mask & 12) == 12;
        }
        int[] path = horizontal || vertical ? FindPath(size, labels, lattice, vertical, queue, token) : [];
        return new(size, lattice, labels, sizes.ToArray(), boundaries.ToArray(), largest, path,
            new(total, sizes.Count - 1, sizes[largest], horizontal, vertical, Math.Max(0, path.Length - 1)));
    }

    /// <summary>Шесть соседей треугольной решётки со сдвигом нечётных строк вправо на полшага.</summary>
    public static int GetNeighbors(int index, int size, PercolationLattice lattice, Span<int> destination)
    {
        int x = index % size, y = index / size, count = 0;
        if (x > 0) destination[count++] = index - 1;
        if (x + 1 < size) destination[count++] = index + 1;
        if (y > 0) destination[count++] = index - size;
        if (y + 1 < size) destination[count++] = index + size;
        if (lattice == PercolationLattice.Triangular)
        {
            int dx = (y & 1) == 0 ? -1 : 1;
            if (x + dx >= 0 && x + dx < size)
            {
                if (y > 0) destination[count++] = index - size + dx;
                if (y + 1 < size) destination[count++] = index + size + dx;
            }
        }
        return count;
    }

    private static int[] FindPath(int size, int[] labels, PercolationLattice lattice, bool vertical,
        int[] queue, CancellationToken token)
    {
        var parent = new int[labels.Length]; Array.Fill(parent, -2);
        int head = 0, tail = 0;
        for (int i = 0; i < size; i++)
        {
            int index = vertical ? i : i * size;
            if (labels[index] == 0) continue;
            queue[tail++] = index; parent[index] = -1;
        }
        Span<int> neighbors = stackalloc int[6];
        while (head < tail)
        {
            if ((head & 4095) == 0) token.ThrowIfCancellationRequested();
            int index = queue[head++];
            if (vertical ? index / size == size - 1 : index % size == size - 1)
            {
                var path = new List<int>();
                for (int current = index; current >= 0; current = parent[current]) path.Add(current);
                path.Reverse(); return path.ToArray();
            }
            int count = GetNeighbors(index, size, lattice, neighbors);
            for (int k = 0; k < count; k++)
            {
                int next = neighbors[k];
                if (labels[next] == 0 || parent[next] != -2) continue;
                parent[next] = index; queue[tail++] = next;
            }
        }
        return [];
    }

    private static void Validate(int size, PercolationLattice lattice)
    {
        if (size < 1 || size > 768) throw new ArgumentOutOfRangeException(nameof(size));
        if (!Enum.IsDefined(lattice)) throw new ArgumentOutOfRangeException(nameof(lattice));
    }
}
