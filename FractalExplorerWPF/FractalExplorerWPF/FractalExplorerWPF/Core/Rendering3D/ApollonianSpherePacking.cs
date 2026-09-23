namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Конечные поколения трёхмерной аполлоновой упаковки. Пять ориентированных взаимно
/// касающихся сфер образуют конфигурацию Декарта; отражение заменяет одну сферу другой,
/// касающейся остальных четырёх. Отрицательная внешняя сфера ограничивает упаковку,
/// но сама не рисуется.
/// </summary>
internal static class ApollonianSpherePacking
{
    internal const int MaxGeneration = 5;
    internal const int MaxTreeNodes = 4095; // 2048 листьев, один float4 на узел.
    internal const double OuterRadius = 1.1123724356957945; // (1 + sqrt(3/2)) / 2

    private readonly record struct Sphere(double Bend, double BX, double BY, double BZ)
    {
        public double Radius => 1.0 / Bend;
        public double X => BX / Bend;
        public double Y => BY / Bend;
        public double Z => BZ / Bend;
    }

    private readonly record struct Bound(double X, double Y, double Z, double Radius)
    {
        public bool Exists => Radius >= 0;
    }

    /// <summary>
    /// Совершенное двоичное дерево: дети узла i находятся в 2i+1 и 2i+2. Листья — точные
    /// сферы; внутренние узлы — охватывающие сферы для отсечения в пиксельном шейдере.
    /// </summary>
    internal static float[] BuildTree(int generation)
    {
        List<Sphere> spheres = Generate(Math.Clamp(generation, 1, MaxGeneration));
        const int leafCount = (MaxTreeNodes + 1) / 2;
        if (spheres.Count > leafCount)
            throw new InvalidOperationException("Упаковка сфер превышает ёмкость GPU-дерева.");

        var tree = new float[MaxTreeNodes * 4];
        BuildNode(spheres, tree, 0, 0, leafCount);
        return tree;
    }

    private static List<Sphere> Generate(int generation)
    {
        const double radius = 0.5;
        double a = radius / Math.Sqrt(2.0);
        var initial = new[]
        {
            FromCenter(radius, a, a, a),
            FromCenter(radius, -a, -a, a),
            FromCenter(radius, a, -a, -a),
            FromCenter(radius, -a, a, -a),
            FromCenter(-OuterRadius, 0, 0, 0)
        };
        var result = new List<Sphere>(1024);
        var seen = new HashSet<(long X, long Y, long Z, long R)>();
        foreach (Sphere sphere in initial) Add(sphere);

        var frontier = new List<(Sphere[] Config, int Previous)> { (initial, -1) };
        for (int level = 1; level <= generation; level++)
        {
            var next = new List<(Sphere[] Config, int Previous)>(frontier.Count * 4);
            foreach ((Sphere[] config, int previous) in frontier)
            {
                for (int index = 0; index < 5; index++)
                {
                    if (index == previous) continue; // Немедленная обратная замена.
                    var replacement = (Sphere[])config.Clone();
                    Sphere old = config[index];
                    double bend = -old.Bend;
                    double bx = -old.BX, by = -old.BY, bz = -old.BZ;
                    for (int other = 0; other < 5; other++)
                    {
                        if (other == index) continue;
                        bend += config[other].Bend;
                        bx += config[other].BX;
                        by += config[other].BY;
                        bz += config[other].BZ;
                    }
                    Sphere child = new(bend, bx, by, bz);
                    replacement[index] = child;
                    Add(child);
                    if (level < generation) next.Add((replacement, index));
                }
            }
            frontier = next;
        }
        return result;

        void Add(Sphere sphere)
        {
            if (sphere.Bend <= 0 || sphere.Radius < 1e-5) return;
            double radius = sphere.Radius;
            double centerDistance = Math.Sqrt(sphere.X * sphere.X + sphere.Y * sphere.Y + sphere.Z * sphere.Z);
            if (centerDistance + radius > OuterRadius + 1e-6) return;
            const double quantization = 1e6;
            var key = ((long)Math.Round(sphere.X * quantization),
                       (long)Math.Round(sphere.Y * quantization),
                       (long)Math.Round(sphere.Z * quantization),
                       (long)Math.Round(radius * quantization));
            if (seen.Add(key)) result.Add(sphere);
        }
    }

    private static Sphere FromCenter(double radius, double x, double y, double z)
    {
        double bend = 1.0 / radius;
        return new Sphere(bend, bend * x, bend * y, bend * z);
    }

    private static Bound BuildNode(List<Sphere> spheres, float[] tree, int node, int start, int slots)
    {
        Bound bound;
        if (slots == 1)
        {
            bound = start < spheres.Count
                ? new Bound(spheres[start].X, spheres[start].Y, spheres[start].Z, spheres[start].Radius)
                : new Bound(0, 0, 0, -1);
        }
        else
        {
            int count = Math.Max(0, Math.Min(slots, spheres.Count - start));
            if (count > 1)
            {
                double minX = double.PositiveInfinity, minY = minX, minZ = minX;
                double maxX = double.NegativeInfinity, maxY = maxX, maxZ = maxX;
                for (int i = start; i < start + count; i++)
                {
                    Sphere sphere = spheres[i];
                    minX = Math.Min(minX, sphere.X); maxX = Math.Max(maxX, sphere.X);
                    minY = Math.Min(minY, sphere.Y); maxY = Math.Max(maxY, sphere.Y);
                    minZ = Math.Min(minZ, sphere.Z); maxZ = Math.Max(maxZ, sphere.Z);
                }
                int axis = maxX - minX >= Math.Max(maxY - minY, maxZ - minZ) ? 0
                    : maxY - minY >= maxZ - minZ ? 1 : 2;
                spheres.Sort(start, count, Comparer<Sphere>.Create((a, b) =>
                    (axis == 0 ? a.X : axis == 1 ? a.Y : a.Z)
                    .CompareTo(axis == 0 ? b.X : axis == 1 ? b.Y : b.Z)));
            }
            int half = slots / 2;
            Bound left = BuildNode(spheres, tree, node * 2 + 1, start, half);
            Bound right = BuildNode(spheres, tree, node * 2 + 2, start + half, half);
            bound = Enclose(left, right);
        }
        int offset = node * 4;
        tree[offset] = (float)bound.X;
        tree[offset + 1] = (float)bound.Y;
        tree[offset + 2] = (float)bound.Z;
        // Небольшой запас покрывает округление double → float у границы отсечения.
        tree[offset + 3] = bound.Exists ? (float)(bound.Radius + 2e-6) : -1;
        return bound;
    }

    private static Bound Enclose(Bound left, Bound right)
    {
        if (!left.Exists) return right;
        if (!right.Exists) return left;
        double dx = right.X - left.X, dy = right.Y - left.Y, dz = right.Z - left.Z;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (left.Radius >= distance + right.Radius) return left;
        if (right.Radius >= distance + left.Radius) return right;
        double radius = (distance + left.Radius + right.Radius) * 0.5;
        double t = (radius - left.Radius) / distance;
        return new Bound(left.X + t * dx, left.Y + t * dy, left.Z + t * dz, radius);
    }
}
