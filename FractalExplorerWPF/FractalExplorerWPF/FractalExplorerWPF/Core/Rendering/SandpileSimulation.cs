using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Integer chip firing on a finite graph with a sink outside its boundary. Single worker owns the field.</summary>
public sealed class SandpileSimulation
{
    public const long MaxAdded = 1_000_000_000_000;
    private readonly long[] _grains, _topplings;
    private readonly Queue<int> _active = new();
    private readonly bool[] _queued;
    private readonly (int X, int Y)[] _directions;
    public int Size { get; }
    public int Degree => _directions.Length;
    public SandpileLattice Lattice { get; }
    public long Added { get; private set; }
    public long Lost { get; private set; }
    public long Operations { get; private set; }
    public int ActiveCount => _active.Count;
    public bool IsStable => _active.Count == 0;

    public SandpileSimulation(SandpileState state)
    {
        state.Validate(); Size = state.Size; Lattice = state.Lattice;
        _directions = Lattice == SandpileLattice.Square
            ? [(1, 0), (0, 1), (-1, 0), (0, -1)]
            : [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
        _queued = new bool[Size * Size];
        if (state.Checkpoint is { } saved)
        {
            ValidateCheckpoint(saved);
            _grains = [.. saved.Grains]; _topplings = [.. saved.Topplings];
            Added = saved.Added; Lost = saved.Lost; Operations = saved.Operations;
            foreach (int i in saved.Queue) { _active.Enqueue(i); _queued[i] = true; }
        }
        else
        {
            _grains = new long[Size * Size]; _topplings = new long[_grains.Length];
            int c = Size / 2;
            switch (state.Seed)
            {
                case SandpileSeed.Center: Add(c, c, state.InitialGrains); break;
                case SandpileSeed.FourPiles:
                    int d = Size / 8;
                    for (int i = 0; i < 4; i++) Add(c + (i % 2 == 0 ? -d : d), c + (i < 2 ? -d : d),
                        state.InitialGrains / 4 + (i < state.InitialGrains % 4 ? 1 : 0));
                    break;
                case SandpileSeed.Filled:
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) Add(x, y, Degree);
                    break;
            }
        }
    }

    public void Add(int x, int y, long amount)
    {
        if ((uint)x >= Size || (uint)y >= Size) throw new ArgumentOutOfRangeException(nameof(x));
        if (amount < 0 || amount > MaxAdded - Added) throw new ArgumentException("Достигнут предел подсыпки: 10¹² песчинок. Создайте новое поле.");
        Added += amount; int i = y * Size + x;
        _grains[i] += amount; Enqueue(i);
    }

    public void AddBrush(int x, int y, int radius, int amount)
    {
        if (radius is < 0 or > 20 || amount is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(radius));
        var sites = new List<int>();
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            double distance = Lattice == SandpileLattice.Square ? dx * dx + dy * dy : dx * dx + dx * dy + dy * dy;
            if (distance <= radius * radius && (uint)(x + dx) < Size && (uint)(y + dy) < Size)
                sites.Add((y + dy) * Size + x + dx);
        }
        if ((long)sites.Count * amount > MaxAdded - Added) throw new ArgumentException("Достигнут предел подсыпки. Создайте новое поле.");
        foreach (int i in sites) Add(i % Size, i / Size, amount);
    }

    /// <summary>One operation exhausts a vertex's currently legal topplings. Budget changes only animation speed.</summary>
    public void Advance(int budget, CancellationToken token)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
        token.ThrowIfCancellationRequested();
        for (int done = 0; done < budget && _active.Count > 0; done++)
        {
            if ((done & 1023) == 0) token.ThrowIfCancellationRequested();
            int i = _active.Dequeue(); _queued[i] = false;
            long count = _grains[i] / Degree;
            _grains[i] %= Degree; _topplings[i] += count; Operations++;
            int x = i % Size, y = i / Size;
            foreach (var (dx, dy) in _directions)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= Size || (uint)ny >= Size) { Lost += count; continue; }
                int neighbor = ny * Size + nx;
                _grains[neighbor] += count; Enqueue(neighbor);
            }
        }
    }

    public SandpileCheckpoint Snapshot() => new()
    {
        Grains = [.. _grains], Topplings = [.. _topplings], Queue = _active.ToArray(),
        Added = Added, Lost = Lost, Operations = Operations
    };

    private void Enqueue(int i)
    {
        if (_grains[i] < Degree || _queued[i]) return;
        _queued[i] = true; _active.Enqueue(i);
    }

    private void ValidateCheckpoint(SandpileCheckpoint saved)
    {
        int n = Size * Size;
        if (saved.Grains is null || saved.Topplings is null || saved.Queue is null ||
            saved.Grains.Length != n || saved.Topplings.Length != n || saved.Queue.Length > n ||
            saved.Added is < 0 or > MaxAdded || saved.Lost < 0 || saved.Lost > saved.Added || saved.Operations < 0)
            throw new ArgumentException("Повреждённый снимок песочной кучи.");
        long mass = 0;
        // A grain's expected residence time is bounded by O(Size²); this also prevents malicious counters overflowing later.
        long maxTopplings = MaxAdded * Size * Size;
        for (int i = 0; i < n; i++)
        {
            long value = saved.Grains[i];
            if (value < 0 || value > saved.Added - mass || saved.Topplings[i] < 0 || saved.Topplings[i] > maxTopplings)
                throw new ArgumentException("Некорректная масса или счётчик обрушений.");
            mass += value;
        }
        if (mass != saved.Added - saved.Lost || saved.Operations > maxTopplings)
            throw new ArgumentException("Нарушен баланс массы снимка.");
        var seen = new bool[n];
        foreach (int i in saved.Queue)
        {
            if ((uint)i >= n || seen[i] || saved.Grains[i] < Degree) throw new ArgumentException("Повреждена очередь лавины.");
            seen[i] = true;
        }
        for (int i = 0; i < n; i++)
            if (seen[i] != (saved.Grains[i] >= Degree)) throw new ArgumentException("В очереди отсутствует активная ячейка.");
    }
}
