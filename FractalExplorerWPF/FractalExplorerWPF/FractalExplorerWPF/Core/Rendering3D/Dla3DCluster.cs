using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Six-neighbor 3D lattice DLA, with isotropic empty-space jumps when there is no drift.
/// A capped Chebyshev distance field gives a conservative free sphere in O(1).
/// Close to the cluster and with drift we use elementary steps (no tunneling through branches).
/// Each particle has its own random stream, so batching/cancellation never changes the result.
/// </summary>
internal sealed class Dla3DCluster
{
    private const int Side = 256, Half = Side / 2, DistanceCap = 8;
    internal const int RadiusLimit = 112;
    private static readonly (int X, int Y, int Z)[] Directions =
        [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];
    private readonly byte[] _distance = new byte[Side * Side * Side];
    private readonly List<Vector3> _points = [];
    private readonly HashSet<int> _occupied = [];
    private readonly double[] _weights = new double[6];
    private double _radius;
    private int _seedCount;
    public Dla3DSettings Settings { get; }
    public IReadOnlyList<Vector3> Points => _points;
    public int SeedCount => _seedCount;
    public int Count => _points.Count - _seedCount;
    public double Radius => _radius;
    public bool BoundaryReached { get; private set; }

    public Dla3DCluster(Dla3DSettings settings)
    {
        Settings = settings.Normalized();
        Array.Fill(_distance, (byte)DistanceCap);
        Vector3 flow = Settings.FlowDirection;
        double total = 0;
        for (int i = 0; i < Directions.Length; i++)
        {
            var d = Directions[i];
            total += Math.Exp(3 * Settings.FlowStrength * Vector3.Dot(flow, new(d.X, d.Y, d.Z)));
            _weights[i] = total;
        }
        for (int i = 0; i < 6; i++) _weights[i] /= total;
        int size = Settings.SeedSize;
        switch (Settings.SeedShape)
        {
            case Dla3DSeedShape.Line:
                for (int y = -size; y <= size; y++) Add(0, y, 0);
                break;
            case Dla3DSeedShape.Ring:
                // A connected digital annulus in XZ; unlike angular sampling it has no gaps.
                for (int z = -size; z <= size; z++)
                for (int x = -size; x <= size; x++)
                    if (Math.Abs(Math.Sqrt(x * x + z * z) - size) <= .75) Add(x, 0, z);
                break;
            case Dla3DSeedShape.Plane:
                for (int z = -size; z <= size; z++)
                for (int x = -size; x <= size; x++)
                    if (x * x + z * z <= size * size) Add(x, 0, z);
                break;
            default: Add(0, 0, 0); break;
        }
        _seedCount = _points.Count;
    }

    public void GrowTo(int count, CancellationToken token)
    {
        count = Math.Clamp(count, 0, Dla3DSettings.MaxParticles);
        while (Count < count && !BoundaryReached)
        {
            token.ThrowIfCancellationRequested();
            // This seed derivation is part of the persisted algorithm; keep it stable.
            var random = new Random(unchecked(Settings.Seed * 73856093 ^ (Count + 1) * 19349663));
            bool attached = false;
            for (int attempt = 0; attempt < 200_000 && !attached; attempt++)
            {
                if ((attempt & 31) == 0) token.ThrowIfCancellationRequested();
                double launch = _radius + 5;
                Vector3 u = RandomDirection(random);
                if (Settings.FlowStrength > 0)
                {
                    // Spawn on the upstream hemisphere; arrows mean particle travel, not growth.
                    Vector3 flow = Settings.FlowDirection;
                    if (Vector3.Dot(u, flow) > 0) u = -u;
                }
                int x = (int)Math.Round(u.X * launch), y = (int)Math.Round(u.Y * launch), z = (int)Math.Round(u.Z * launch);
                double killSquared = Math.Pow(launch + Math.Max(12, _radius * .5), 2);
                for (int step = 0; step < 24_000; step++)
                {
                    if ((step & 255) == 0) token.ThrowIfCancellationRequested();
                    if (x * x + y * y + z * z > killSquared || !Inside(x, y, z)) break;
                    int index = Index(x, y, z);
                    if (!_occupied.Contains(index) && Touches(x, y, z) && random.NextDouble() < Settings.Stickiness)
                    {
                        // Commit only complete particles. Cancellation retries the same stream.
                        Add(x, y, z);
                        attached = true;
                        BoundaryReached = _radius >= RadiusLimit;
                        break;
                    }
                    int distance = _distance[index];
                    if (Settings.FlowStrength == 0 && distance >= 4)
                    {
                        Vector3 jump = RandomDirection(random) * (distance - 2);
                        x += (int)Math.Round(jump.X); y += (int)Math.Round(jump.Y); z += (int)Math.Round(jump.Z);
                    }
                    else
                    {
                        double draw = random.NextDouble();
                        int direction = 0;
                        while (direction < 5 && draw > _weights[direction]) direction++;
                        var d = Directions[direction];
                        if (!_occupied.Contains(Index(x + d.X, y + d.Y, z + d.Z)))
                        { x += d.X; y += d.Y; z += d.Z; }
                    }
                }
            }
            if (!attached) throw new InvalidOperationException("Рост DLA замедлился. Уменьшите силу потока или увеличьте вероятность прилипания.");
        }
    }

    private bool Touches(int x, int y, int z)
    {
        foreach (var d in Directions)
            if (_occupied.Contains(Index(x + d.X, y + d.Y, z + d.Z))) return true;
        return false;
    }

    private void Add(int x, int y, int z)
    {
        if (!_occupied.Add(Index(x, y, z))) return;
        _points.Add(new(x, y, z));
        _radius = Math.Max(_radius, Math.Sqrt(x * x + y * y + z * z));
        // Updates are local and monotone. Outside this cube the old cap is still a lower bound.
        for (int dz = -DistanceCap + 1; dz < DistanceCap; dz++)
        for (int dy = -DistanceCap + 1; dy < DistanceCap; dy++)
        for (int dx = -DistanceCap + 1; dx < DistanceCap; dx++)
        {
            int index = Index(x + dx, y + dy, z + dz);
            byte d = (byte)Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
            if (_distance[index] > d) _distance[index] = d;
        }
    }

    private static bool Inside(int x, int y, int z) => Math.Abs(x) < Half - 2 && Math.Abs(y) < Half - 2 && Math.Abs(z) < Half - 2;
    private static int Index(int x, int y, int z) => ((z + Half) * Side + y + Half) * Side + x + Half;
    private static Vector3 RandomDirection(Random random)
    {
        double y = 2 * random.NextDouble() - 1, angle = 2 * Math.PI * random.NextDouble();
        double r = Math.Sqrt(1 - y * y);
        return new((float)(r * Math.Cos(angle)), (float)y, (float)(r * Math.Sin(angle)));
    }
}
