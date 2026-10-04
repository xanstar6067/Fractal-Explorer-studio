using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Reiter's coupled lattice map: split receptive/air cells, diffuse only air,
/// then add the retained mass and deposition. A D6 fundamental sector avoids
/// floating-point symmetry drift without altering the local update rule.
/// </summary>
public sealed class SnowCrystalSimulation
{
    public SnowCrystalLattice Lattice { get; }
    private double[] _mass;
    private double[] _next;
    private readonly double[] _air;
    private readonly bool[] _receptive;
    private readonly int[] _frozenAt;
    public int StepCount { get; private set; }
    public bool BoundaryReached { get; private set; }
    public int FrozenCount { get; private set; }
    public int CrystalRadius { get; private set; }

    public SnowCrystalSimulation(SnowCrystalState state)
    {
        state.Validate();
        Lattice = new SnowCrystalLattice(state.Radius);
        _mass = new double[Lattice.Count];
        _next = new double[Lattice.Count];
        _air = new double[Lattice.Count];
        _receptive = new bool[Lattice.Count];
        _frozenAt = new int[Lattice.Count];
        Array.Fill(_mass, state.Vapor);
        Array.Fill(_frozenAt, -1);
        if (state.Checkpoint is { } checkpoint)
        {
            ValidateCheckpoint(checkpoint);
            _mass = [.. checkpoint.Mass];
            _frozenAt = [.. checkpoint.FrozenAt];
            StepCount = checkpoint.StepCount;
        }
        else
        {
            for (int i = 0; i < Lattice.Count; i++)
                if (Lattice.Sites[i].Q + Lattice.Sites[i].R <= state.SeedRadius)
                { _mass[i] = 1; _frozenAt[i] = 0; }
        }
        UpdateStatistics();
    }

    public void Advance(int steps, SnowCrystalState conditions, CancellationToken token)
    {
        conditions.Validate();
        if (steps < 0 || steps > 100_000) throw new ArgumentOutOfRangeException(nameof(steps));
        if (conditions.Radius != Lattice.Radius)
            throw new ArgumentException("Изменение размера поля требует нового кристалла.");
        for (int step = 0; step < steps && !BoundaryReached; step++)
        {
            token.ThrowIfCancellationRequested();
            if (StepCount >= 1_000_000) throw new InvalidOperationException("Достигнут предел миллиона шагов. Начните новый кристалл.");
            for (int i = 0; i < _mass.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                bool receptive = _frozenAt[i] >= 0;
                for (int d = 0; d < 6 && !receptive; d++)
                {
                    int n = Lattice.Neighbors[i * 6 + d];
                    receptive = n >= 0 && _frozenAt[n] >= 0;
                }
                _receptive[i] = receptive;
                _air[i] = Lattice.IsBoundary(i) ? conditions.Vapor : receptive ? 0 : _mass[i];
            }
            for (int i = 0; i < _mass.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                if (Lattice.IsBoundary(i)) { _next[i] = conditions.Vapor; continue; }
                double sum = 0;
                for (int d = 0; d < 6; d++)
                {
                    int n = Lattice.Neighbors[i * 6 + d];
                    sum += n < 0 ? conditions.Vapor : _air[n];
                }
                _next[i] = (_receptive[i] ? _mass[i] + conditions.Deposition : 0)
                    + (1 - conditions.Diffusion / 2) * _air[i] + conditions.Diffusion / 12 * sum;
            }
            // Cancellation before this point leaves a complete, resumable previous step.
            token.ThrowIfCancellationRequested();
            (_mass, _next) = (_next, _mass);
            StepCount++;
            for (int i = 0; i < _mass.Length; i++)
                if (_frozenAt[i] < 0 && _mass[i] >= 1 && !Lattice.IsBoundary(i)) _frozenAt[i] = StepCount;
            UpdateStatistics();
        }
    }

    public SnowCrystalCheckpoint Snapshot() => new()
    {
        Radius = Lattice.Radius, StepCount = StepCount, Mass = [.. _mass], FrozenAt = [.. _frozenAt]
    };

    private void UpdateStatistics()
    {
        FrozenCount = CrystalRadius = 0;
        for (int i = 0; i < _mass.Length; i++)
            if (_frozenAt[i] >= 0)
            {
                FrozenCount += Lattice.Multiplicity[i];
                CrystalRadius = Math.Max(CrystalRadius, Lattice.Sites[i].Q + Lattice.Sites[i].R);
            }
        BoundaryReached = CrystalRadius >= Lattice.Radius - 2;
    }

    private void ValidateCheckpoint(SnowCrystalCheckpoint checkpoint)
    {
        if (checkpoint.Radius != Lattice.Radius || checkpoint.StepCount is < 0 or > 1_000_000 ||
            checkpoint.Mass is null || checkpoint.FrozenAt is null ||
            checkpoint.Mass.Length != Lattice.Count || checkpoint.FrozenAt.Length != Lattice.Count)
            throw new ArgumentException("Повреждено сохранённое поле снежного кристалла.");
        for (int i = 0; i < Lattice.Count; i++)
        {
            double mass = checkpoint.Mass[i]; int age = checkpoint.FrozenAt[i];
            if (!double.IsFinite(mass) || mass < 0 || age < -1 || age > checkpoint.StepCount ||
                (age >= 0) != (mass >= 1) || Lattice.IsBoundary(i) && age >= 0)
                throw new ArgumentException("Некорректная масса или возраст в сохранённом поле.");
        }
    }
}

/// <summary>Axial coordinates, with Euclidean position (q+r/2, sqrt(3)*r/2).</summary>
public sealed class SnowCrystalLattice
{
    private static readonly (int Q, int R)[] Directions = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
    private readonly int[] _lookup;
    private readonly int _size;
    public int Radius { get; }
    public (int Q, int R)[] Sites { get; }
    public int[] Neighbors { get; }
    public int[] Multiplicity { get; }
    public int Count => Sites.Length;

    public SnowCrystalLattice(int radius)
    {
        if (radius is < 1 or > 320) throw new ArgumentOutOfRangeException(nameof(radius));
        Radius = radius; _size = radius * 2 + 1;
        _lookup = new int[_size * _size]; Array.Fill(_lookup, -1);
        var sites = new List<(int Q, int R)>();
        for (int r = 0; r <= radius / 2; r++)
        for (int q = r; q <= radius - r; q++)
        { _lookup[(r + radius) * _size + q + radius] = sites.Count; sites.Add((q, r)); }
        Sites = [.. sites]; Multiplicity = new int[Count];
        for (int r = -radius; r <= radius; r++)
        for (int q = -radius; q <= radius; q++)
        {
            if (Math.Abs(q + r) > radius) continue;
            int a = q, b = r;
            for (int rotation = 0; (a < 0 || b < 0) && rotation < 6; rotation++) (a, b) = (-b, a + b);
            if (a < b) (a, b) = (b, a);
            int index = _lookup[(b + radius) * _size + a + radius];
            _lookup[(r + radius) * _size + q + radius] = index;
            Multiplicity[index]++;
        }
        Neighbors = new int[Count * 6];
        for (int i = 0; i < Count; i++)
        for (int d = 0; d < 6; d++)
            Neighbors[i * 6 + d] = Index(Sites[i].Q + Directions[d].Q, Sites[i].R + Directions[d].R);
    }

    public bool IsBoundary(int i) => Sites[i].Q + Sites[i].R == Radius;
    public int Index(int q, int r) => Math.Abs(q) > Radius || Math.Abs(r) > Radius || Math.Abs(q + r) > Radius
        ? -1 : _lookup[(r + Radius) * _size + q + Radius];
}
