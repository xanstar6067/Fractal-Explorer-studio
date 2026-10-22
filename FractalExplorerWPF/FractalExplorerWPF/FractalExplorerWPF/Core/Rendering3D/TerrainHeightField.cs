using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Seeded gradient noise with octave synthesis and ridged/hybrid modulation.
/// Heights are in [0, Height], without per-image normalization. Rows run from -Z to +Z.</summary>
public static class TerrainHeightField
{
    public static float[] Build(TerrainSettings settings, CancellationToken token)
    {
        settings.Validate();
        int side = settings.Resolution;
        var heights = new float[side * side];
        // Discard frequencies above the grid Nyquist limit rather than alias fine octaves.
        int octaves = 1;
        double frequency = settings.Scale;
        while (octaves < settings.Octaves && frequency * settings.Lacunarity <= (side - 1) * 0.5)
        {
            frequency *= settings.Lacunarity;
            octaves++;
        }
        Parallel.For(0, side, Options(token), z =>
        {
            for (int x = 0; x < side; x++)
            {
                if ((x & 127) == 0) token.ThrowIfCancellationRequested();
                double px = (double)x / (side - 1) * settings.Scale + 17.13;
                double pz = (double)z / (side - 1) * settings.Scale + 7.73;
                double sum = 0, norm = 0, amplitude = 1, weight = 1;
                for (int octave = 0; octave < octaves; octave++)
                {
                    double noise = Noise(px, pz, unchecked((uint)settings.Seed + (uint)octave * 1013));
                    double value = (noise + 1) * 0.5;
                    if (settings.Type == TerrainKind.Ridged)
                    {
                        value = 1 - Math.Abs(noise);
                        value *= value * weight;
                        weight = Math.Clamp(value * 2, 0, 1);
                    }
                    else if (settings.Type == TerrainKind.Hybrid)
                    {
                        value *= weight;
                        weight = Math.Clamp(value * 2.4, 0, 1);
                    }
                    sum += value * amplitude;
                    norm += amplitude;
                    amplitude *= settings.Roughness;
                    // Rotate the octave domain to avoid aligned lattice ridges.
                    (px, pz) = ((0.8 * px - 0.6 * pz) * settings.Lacunarity,
                        (0.6 * px + 0.8 * pz) * settings.Lacunarity);
                }
                double terrainValue = Math.Clamp(sum / norm, 0, 1);
                if (settings.Shape != TerrainShape.Patch && settings.ShapeStrength > 0)
                {
                    double nx = 2.0 * x / (side - 1) - 1;
                    double nz = 2.0 * z / (side - 1) - 1;
                    double radius = Math.Sqrt(nx * nx + nz * nz) / settings.ShapeRadius;
                    double shaped;
                    if (settings.Shape == TerrainShape.Island)
                        shaped = terrainValue * (1 - SmoothStep(0.55, 1, radius));
                    else
                    {
                        // A shallow floor, raised rim and original terrain outside the crater.
                        double floor = 0.08 + terrainValue * 0.16;
                        double rim = 0.72 + terrainValue * 0.28;
                        double bowl = floor + (rim - floor) * SmoothStep(0.3, 1, radius);
                        shaped = bowl + (terrainValue - bowl) * SmoothStep(1, 1.35, radius);
                    }
                    terrainValue += (shaped - terrainValue) * settings.ShapeStrength;
                }
                heights[z * side + x] = (float)(terrainValue * settings.Height);
            }
        });
        return ApplyThermalErosion(heights, side, settings.Size / (side - 1),
            settings.ThermalIterations, settings.TalusAngle, token);
    }

    private static ParallelOptions Options(CancellationToken token) => new()
    {
        CancellationToken = token,
        MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
    };

    private static double SmoothStep(double low, double high, double value)
    {
        double t = Math.Clamp((value - low) / (high - low), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Closed boundaries; simultaneous, symmetric downhill transfers to eight neighbours.
    /// Each edge exchanges 1/16 of the height difference above the talus threshold.
    /// The update is a convex combination: no overshoot, no clipping or renormalization.
    /// Input is unchanged; zero steps return it directly.</summary>
    internal static float[] ApplyThermalErosion(float[] source, int side, double spacing,
        int iterations, double talusAngle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (iterations == 0) return source;
        var current = (float[])source.Clone();
        var next = new float[source.Length];
        double threshold = spacing * Math.Tan(talusAngle * Math.PI / 180);
        double diagonal = threshold * Math.Sqrt(2);
        for (int step = 0; step < iterations; step++)
        {
            Parallel.For(0, side, Options(token), z =>
            {
                for (int x = 0; x < side; x++)
                {
                    if ((x & 127) == 0) token.ThrowIfCancellationRequested();
                    int index = z * side + x;
                    double height = current[index], transfer = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dz == 0) || x + dx < 0 || x + dx >= side || z + dz < 0 || z + dz >= side)
                            continue;
                        double difference = height - current[(z + dz) * side + x + dx];
                        double excess = Math.Max(0, Math.Abs(difference) - (dx != 0 && dz != 0 ? diagonal : threshold));
                        transfer += Math.CopySign(excess, difference);
                    }
                    next[index] = (float)(height - transfer / 16);
                }
            });
            (current, next) = (next, current);
        }
        return current;
    }

    private static double Noise(double x, double y, uint seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double u = x - ix, v = y - iy;
        double a = Gradient(ix, iy, seed, u, v);
        double b = Gradient(ix + 1, iy, seed, u - 1, v);
        double c = Gradient(ix, iy + 1, seed, u, v - 1);
        double d = Gradient(ix + 1, iy + 1, seed, u - 1, v - 1);
        double fx = Fade(u), fy = Fade(v);
        return Math.Clamp(((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) * 1.414213562, -1, 1);
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Gradient(int x, int y, uint seed, double dx, double dy)
    {
        uint h = unchecked((uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u);
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 7) switch
        {
            0 => dx, 1 => -dx, 2 => dy, 3 => -dy,
            4 => (dx + dy) * 0.707106781, 5 => (dx - dy) * 0.707106781,
            6 => (-dx + dy) * 0.707106781, _ => (-dx - dy) * 0.707106781
        };
    }
}
