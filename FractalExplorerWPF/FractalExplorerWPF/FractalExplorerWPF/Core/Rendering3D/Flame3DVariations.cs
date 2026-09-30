using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Flame3DVariations
{
    public static Vector3 Apply(Vector3 p, Flame3DVariation variation, Random random)
    {
        float r2 = p.LengthSquared();
        switch (variation)
        {
            case Flame3DVariation.Linear: return p;
            case Flame3DVariation.Sinusoidal: return new(MathF.Sin(p.X), MathF.Sin(p.Y), MathF.Sin(p.Z));
            case Flame3DVariation.Spherical: return p / MathF.Max(r2, 1e-8f);
            case Flame3DVariation.Bubble: return p * (4 / (4 + r2));
            case Flame3DVariation.Twist:
                float a = 1.8f * p.Y + .7f * r2, c = MathF.Cos(a), s = MathF.Sin(a);
                return new(c * p.X - s * p.Z, p.Y, s * p.X + c * p.Z);
            case Flame3DVariation.Curl:
                return new(p.X + .32f * (MathF.Sin(2 * p.Z) - MathF.Cos(2 * p.Y)),
                    p.Y + .32f * (MathF.Sin(2 * p.X) - MathF.Cos(2 * p.Z)),
                    p.Z + .32f * (MathF.Sin(2 * p.Y) - MathF.Cos(2 * p.X)));
            case Flame3DVariation.Julia:
                float r = MathF.Sqrt(MathF.Sqrt(r2));
                float theta = .5f * MathF.Atan2(p.Z, p.X) + random.Next(2) * MathF.PI;
                float phi = .5f * MathF.Acos(Math.Clamp(p.Y / MathF.Max(MathF.Sqrt(r2), 1e-8f), -1, 1)) + random.Next(2) * MathF.PI;
                return new(r * MathF.Sin(phi) * MathF.Cos(theta), r * MathF.Cos(phi), r * MathF.Sin(phi) * MathF.Sin(theta));
            case Flame3DVariation.Waves:
                return new(p.X + .3f * MathF.Sin(3 * p.Y + p.Z),
                    p.Y + .3f * MathF.Sin(3 * p.Z + p.X), p.Z + .3f * MathF.Sin(3 * p.X + p.Y));
            default: throw new ArgumentOutOfRangeException(nameof(variation));
        }
    }
}
