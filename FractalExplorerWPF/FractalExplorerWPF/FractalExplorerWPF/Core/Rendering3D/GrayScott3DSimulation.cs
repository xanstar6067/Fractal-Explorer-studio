using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

public interface IGrayScott3DEngine : IDisposable
{
    string DeviceName { get; }
    void Advance(int steps, CancellationToken token);
    void Inject(double x, double y, double z, double radius);
    GrayScott3DField Snapshot();
}

public static class GrayScott3DEngineFactory
{
    internal static Func<GrayScott3DSettings, IGrayScott3DEngine>? GpuFactoryOverrideForTests;
    public static IGrayScott3DEngine Create(GrayScott3DSettings settings, out string? fallbackReason)
    {
        fallbackReason = null;
        if (settings.Backend == GrayScottBackend.Gpu)
        {
            try { return GpuFactoryOverrideForTests?.Invoke(settings) ?? new GrayScott3DGpuEngine(settings); }
            catch (Exception exception) { fallbackReason = exception.Message; }
        }
        return new GrayScott3DSimulation(settings);
    }
}

/// <summary>Explicit Euler, unit cell/time step, six-neighbor 3D Laplacian, periodic boundaries.</summary>
public sealed class GrayScott3DSimulation : IGrayScott3DEngine
{
    private readonly GrayScott3DSettings _settings;
    private float[] _field, _next;
    private long _step;
    public string DeviceName => "ЦП · 3D-сетка";

    public GrayScott3DSimulation(GrayScott3DSettings settings)
    {
        settings.Validate(); _settings = settings;
        _field = settings.Field is null ? InitialField(settings) : (float[])settings.Field.Values.Clone();
        _next = new float[_field.Length]; _step = settings.Field?.Step ?? 0;
    }

    internal static float[] InitialField(GrayScott3DSettings s)
    {
        int n = s.Size; var field = new float[n * n * n * 2]; var random = new Random(s.Seed);
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = (x + .5) / n * 2 - 1, py = (y + .5) / n * 2 - 1, pz = (z + .5) / n * 2 - 1;
            double r = Math.Sqrt(px * px + py * py + pz * pz);
            bool active = s.SeedShape switch
            {
                GrayScott3DSeed.Noise => Math.Abs(px) < .72 && Math.Abs(py) < .72 && Math.Abs(pz) < .72,
                GrayScott3DSeed.Ring => Math.Pow(Math.Sqrt(px * px + pz * pz) - .4, 2) + py * py < .0144,
                GrayScott3DSeed.Spheres => Math.Pow(Math.Abs(px) - .25, 2) + Math.Pow(Math.Abs(py) - .25, 2) + Math.Pow(Math.Abs(pz) - .25, 2) < .0225,
                _ => false
            };
            int i = ((z * n + y) * n + x) * 2;
            field[i] = active ? .5f : 1f;
            field[i + 1] = active ? (float)(.25 + (random.NextDouble() - .5) * .12) : 0;
        }
        return field;
    }

    public void Advance(int steps, CancellationToken token)
    {
        if (steps is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(steps));
        int n = _settings.Size, plane = n * n;
        float du = (float)_settings.DiffusionU, dv = (float)_settings.DiffusionV;
        float feed = (float)_settings.Feed, kill = (float)_settings.Kill;
        for (int step = 0; step < steps; step++)
        {
            token.ThrowIfCancellationRequested();
            Parallel.For(0, n, new ParallelOptions { CancellationToken = token }, z =>
            {
                int zm = z == 0 ? n - 1 : z - 1, zp = z == n - 1 ? 0 : z + 1;
                for (int y = 0; y < n; y++)
                {
                    int ym = y == 0 ? n - 1 : y - 1, yp = y == n - 1 ? 0 : y + 1;
                    for (int x = 0; x < n; x++)
                    {
                        int xm = x == 0 ? n - 1 : x - 1, xp = x == n - 1 ? 0 : x + 1;
                        int i = (z * plane + y * n + x) * 2;
                        int a = (z * plane + y * n + xm) * 2, b = (z * plane + y * n + xp) * 2;
                        int c = (z * plane + ym * n + x) * 2, d = (z * plane + yp * n + x) * 2;
                        int e = (zm * plane + y * n + x) * 2, f = (zp * plane + y * n + x) * 2;
                        float u = _field[i], v = _field[i + 1], reaction = u * v * v;
                        float lu = _field[a] + _field[b] + _field[c] + _field[d] + _field[e] + _field[f] - 6 * u;
                        float lv = _field[a + 1] + _field[b + 1] + _field[c + 1] + _field[d + 1] + _field[e + 1] + _field[f + 1] - 6 * v;
                        _next[i] = Math.Clamp(u + du * lu - reaction + feed * (1 - u), 0, 1);
                        _next[i + 1] = Math.Clamp(v + dv * lv + reaction - (feed + kill) * v, 0, 1);
                    }
                }
            });
            (_field, _next) = (_next, _field); _step++;
        }
    }

    public void Inject(double x, double y, double z, double radius)
    {
        int n = _settings.Size;
        ValidateBrush(x, y, z, radius);
        for (int iz = 0; iz < n; iz++) for (int iy = 0; iy < n; iy++) for (int ix = 0; ix < n; ix++)
        {
            double dx = Math.Abs((ix + .5) / n - x), dy = Math.Abs((iy + .5) / n - y), dz = Math.Abs((iz + .5) / n - z);
            dx = Math.Min(dx, 1 - dx); dy = Math.Min(dy, 1 - dy); dz = Math.Min(dz, 1 - dz);
            if (dx * dx + dy * dy + dz * dz > radius * radius) continue;
            int i = ((iz * n + iy) * n + ix) * 2; _field[i] = .5f; _field[i + 1] = .3f;
        }
    }

    internal static void ValidateBrush(double x, double y, double z, double radius)
    {
        if (!double.IsFinite(x + y + z + radius) || x is < 0 or > 1 || y is < 0 or > 1 || z is < 0 or > 1 || radius is <= 0 or > .3)
            throw new ArgumentOutOfRangeException(nameof(radius));
    }

    public GrayScott3DField Snapshot() => new(_settings.Size, _step, (float[])_field.Clone(), true);
    public void Dispose() { }
}
