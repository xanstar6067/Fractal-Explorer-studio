using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Интегрирует автономный поток и накапливает его траекторию в общей 3D-текстуре.</summary>
internal static class Attractor3DVolume
{
    public static byte[] Build(Fractal3DState state, CancellationToken token)
    {
        Attractor3DSettings settings = state.Attractor ?? Attractor3DSystems.Default(Attractor3DSystem.Lorenz);
        int count = Math.Clamp(state.Iterations, 50_000, 5_000_000);
        if (!double.IsFinite(settings.TimeStep) || settings.TimeStep <= 0 || settings.TimeStep > .05)
            throw new InvalidOperationException("Шаг времени должен быть в пределах (0; 0,05].");

        // Первый проход измеряет границы после переходного процесса. Повторная интеграция
        // позволяет обойтись без массива из миллионов точек, как у IFS.
        Bounds bounds = Trace(settings, count, null, token);
        Vector3 extent = bounds.Max - bounds.Min;
        float largest = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        if (!float.IsFinite(largest) || largest < 1e-5f)
            throw new InvalidOperationException("Траектория сошлась к точке. Выберите другой набор параметров или начальную точку.");
        var voxels = new byte[Ifs3DVolume.Side * Ifs3DVolume.Side * Ifs3DVolume.Side];
        Trace(settings, count, point => Splat(voxels, point, bounds, 1.8f / largest), token);
        return voxels;
    }

    private static Bounds Trace(Attractor3DSettings settings, int count, Action<Vector3>? plot, CancellationToken token)
    {
        Point p = new(settings.StartX, settings.StartY, settings.StartZ);
        if (!p.IsFinite) throw new InvalidOperationException("Начальная точка должна состоять из конечных чисел.");
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        int warmup = Math.Min(10_000, Math.Max(2_000, count / 50));
        for (int i = -warmup; i < count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            p = Step(settings, p);
            if (!p.IsFinite || p.MaxAbs > 1e6)
                throw new InvalidOperationException("Траектория разошлась. Уменьшите шаг времени или измените параметры.");
            if (i < 0) continue;
            // Вертикальная ось камеры — Y; в формулах большинства систем вертикаль — z.
            Vector3 point = new((float)p.X, (float)p.Z, (float)p.Y);
            if (plot is null) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
            else plot(point);
        }
        return new Bounds(min, max);
    }

    private static Point Step(Attractor3DSettings s, Point p)
    {
        double h = s.TimeStep;
        Point k1 = Derivative(s, p);
        Point k2 = Derivative(s, p + k1 * (h * .5));
        Point k3 = Derivative(s, p + k2 * (h * .5));
        Point k4 = Derivative(s, p + k3 * h);
        return p + (k1 + k2 * 2 + k3 * 2 + k4) * (h / 6);
    }

    private static Point Derivative(Attractor3DSettings s, Point p)
    {
        double x = p.X, y = p.Y, z = p.Z;
        double a = s.A, b = s.B, c = s.C, d = s.D, e = s.E, f = s.F;
        return s.System switch
        {
            Attractor3DSystem.Lorenz => new(a * (y - x), x * (b - z) - y, x * y - c * z),
            Attractor3DSystem.Rossler => new(-y - z, x + a * y, b + z * (x - c)),
            Attractor3DSystem.Thomas => new(Math.Sin(y) - a * x, Math.Sin(z) - a * y, Math.Sin(x) - a * z),
            Attractor3DSystem.Halvorsen => new(-a * x - 4 * y - 4 * z - y * y,
                -a * y - 4 * z - 4 * x - z * z, -a * z - 4 * x - 4 * y - x * x),
            Attractor3DSystem.Aizawa => new((z - b) * x - d * y, d * x + (z - b) * y,
                c + a * z - z * z * z / 3 - (x * x + y * y) * (1 + e * z) + f * z * x * x * x),
            Attractor3DSystem.Dadras => new(y - a * x + b * y * z, c * y - x * z + z, d * x * y - e * z),
            _ => throw new ArgumentOutOfRangeException(nameof(s.System))
        };
    }

    private static void Splat(byte[] voxels, Vector3 point, Bounds bounds, float scale)
    {
        const int side = Ifs3DVolume.Side;
        Vector3 q = (point - (bounds.Min + bounds.Max) * .5f) * scale;
        int x = Math.Clamp((int)((q.X + 1) * (side / 2d)), 1, side - 2);
        int y = Math.Clamp((int)((q.Y + 1) * (side / 2d)), 1, side - 2);
        int z = Math.Clamp((int)((q.Z + 1) * (side / 2d)), 1, side - 2);
        int center = (z * side + y) * side + x;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int radius = dx * dx + dy * dy + dz * dz;
            int weight = radius switch { 0 => 96, 1 => 48, 2 => 24, _ => 12 };
            int index = center + (dz * side + dy) * side + dx;
            voxels[index] = (byte)Math.Min(255, voxels[index] + weight);
        }
    }

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);
    private readonly record struct Point(double X, double Y, double Z)
    {
        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
        public double MaxAbs => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point operator *(Point a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    }
}
