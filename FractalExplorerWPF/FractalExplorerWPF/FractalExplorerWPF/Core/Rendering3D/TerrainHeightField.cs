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
        Parallel.For(0, side, new ParallelOptions { CancellationToken = token }, z =>
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
                heights[z * side + x] = (float)(Math.Clamp(sum / norm, 0, 1) * settings.Height);
            }
        });
        return heights;
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
