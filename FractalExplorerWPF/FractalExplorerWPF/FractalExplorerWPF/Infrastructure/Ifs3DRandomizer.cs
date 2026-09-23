using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Creates contractive affine maps whose anchors occupy actual 3D space.</summary>
public static class Ifs3DRandomizer
{
    private const double MaximumContraction = .88;

    public static List<Ifs3DTransform> Create(Ifs3DRandomizationSettings settings, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Ifs3DRandomizationSettings normalized = settings.Clone().Normalize();
        if (normalized.Families.Count == 0)
            throw new InvalidOperationException("Выберите хотя бы одно семейство преобразований.");

        random ??= Random.Shared;
        int count = random.Next(normalized.MinimumTransforms, normalized.MaximumTransforms + 1);
        List<Ifs3DTransform> result = normalized.PlacementMode == Ifs3DPlacementMode.Bilateral
            ? CreateBilateral(count, normalized, random)
            : CreatePlaced(count, normalized, random);
        NormalizeProbabilities(result);
        return result;
    }

    public static void NormalizeProbabilities(IList<Ifs3DTransform> transforms)
    {
        if (transforms.Count == 0) return;
        double total = transforms.Sum(transform => Math.Max(0, transform.Probability));
        if (!double.IsFinite(total) || total <= 0)
        {
            foreach (Ifs3DTransform transform in transforms) transform.Probability = 1d / transforms.Count;
            return;
        }
        foreach (Ifs3DTransform transform in transforms)
            transform.Probability = Math.Max(0, transform.Probability) / total;
    }

    private static List<Ifs3DTransform> CreatePlaced(int count, Ifs3DRandomizationSettings settings, Random random)
    {
        var result = new List<Ifs3DTransform>(count);
        double phase = Range(random, -Math.PI, Math.PI);
        double helixTurns = Range(random, 1.25, 2);
        for (int index = 0; index < count; index++)
        {
            Vector3 anchor = settings.PlacementMode switch
            {
                Ifs3DPlacementMode.Spherical => SphericalAnchor(index, count, phase, random),
                Ifs3DPlacementMode.Helix => HelixAnchor(index, count, phase, helixTurns, random),
                _ => FreeAnchor(random)
            };
            Ifs3DTransform transform = CreateTransform(PickFamily(settings.Families, random), anchor, random);
            transform.Probability = RawProbability(transform, settings.ProbabilityMode, random);
            result.Add(transform);
        }
        return result;
    }

    private static List<Ifs3DTransform> CreateBilateral(int count, Ifs3DRandomizationSettings settings, Random random)
    {
        var result = new List<Ifs3DTransform>(count);
        if ((count & 1) != 0)
        {
            Ifs3DTransform center = CreateTransform(PickFamily(settings.Families, random),
                new Vector3(0, (float)Range(random, -.45, .45), (float)Range(random, -.5, .5)), random);
            center.Probability = RawProbability(center, settings.ProbabilityMode, random);
            result.Add(center);
        }
        while (result.Count < count)
        {
            Ifs3DTransform right = CreateTransform(PickFamily(settings.Families, random),
                new Vector3((float)Range(random, .25, .85), (float)Range(random, -.75, .75),
                    (float)Range(random, -.7, .7)), random);
            right.Probability = RawProbability(right, settings.ProbabilityMode, random);
            result.Add(right);
            result.Add(MirrorAcrossYZ(right));
        }
        return result;
    }

    private static Vector3 SphericalAnchor(int index, int count, double phase, Random random)
    {
        // Fibonacci sphere distributes even small sets over the surface; jitter prevents a rigid grid.
        double y = 1 - 2 * (index + .5) / count + Range(random, -.12, .12);
        y = Math.Clamp(y, -.98, .98);
        double angle = phase + index * Math.PI * (3 - Math.Sqrt(5)) + Range(random, -.22, .22);
        double radius = Range(random, .5, .86);
        double planar = Math.Sqrt(1 - y * y) * radius;
        return new Vector3((float)(planar * Math.Cos(angle)), (float)(y * radius),
            (float)(planar * Math.Sin(angle)));
    }

    private static Vector3 HelixAnchor(int index, int count, double phase, double turns, Random random)
    {
        double progress = count == 1 ? .5 : index / (double)(count - 1);
        double angle = phase + progress * Math.PI * 2 * turns;
        double radius = Range(random, .35, .75);
        return new Vector3((float)(Math.Cos(angle) * radius),
            (float)(-.8 + progress * 1.6 + Range(random, -.12, .12)),
            (float)(Math.Sin(angle) * radius));
    }

    private static Vector3 FreeAnchor(Random random)
    {
        Vector3 direction;
        do
        {
            direction = new Vector3((float)Range(random, -1, 1),
                (float)Range(random, -1, 1), (float)Range(random, -1, 1));
        } while (direction.LengthSquared() < .01f || direction.LengthSquared() > 1);
        return direction * (float)Range(random, .4, 1);
    }

    private static Ifs3DTransform CreateTransform(Ifs3DTransformFamily family, Vector3 anchor, Random random)
    {
        (double sx, double sy, double sz, double shear) = family switch
        {
            Ifs3DTransformFamily.Anisotropic => (Range(random, .22, .68), Range(random, .18, .65), Range(random, .2, .7), 0),
            Ifs3DTransformFamily.Shear => (Range(random, .26, .58), Range(random, .24, .6), Range(random, .2, .55), Range(random, -.22, .22)),
            Ifs3DTransformFamily.Reflection => (-Range(random, .26, .65), Range(random, .26, .65), Range(random, .24, .62), 0),
            Ifs3DTransformFamily.Stem => (Range(random, .045, .15), Range(random, .42, .72), Range(random, .045, .16), Range(random, -.04, .04)),
            Ifs3DTransformFamily.Sheet => (Range(random, .38, .66), Range(random, .35, .64), Range(random, .035, .12), Range(random, -.05, .05)),
            _ => Similarity(random)
        };

        Quaternion orientation = Quaternion.CreateFromYawPitchRoll(
            (float)Range(random, -Math.PI, Math.PI),
            (float)Range(random, -Math.PI, Math.PI),
            (float)Range(random, -Math.PI, Math.PI));
        Vector3 x = Vector3.Transform(new Vector3((float)sx, 0, 0), orientation);
        Vector3 y = Vector3.Transform(new Vector3((float)shear, (float)sy, 0), orientation);
        Vector3 z = Vector3.Transform(new Vector3(0, (float)(shear * .65), (float)sz), orientation);
        var result = new Ifs3DTransform
        {
            M11 = x.X, M21 = x.Y, M31 = x.Z,
            M12 = y.X, M22 = y.Y, M32 = y.Z,
            M13 = z.X, M23 = z.Y, M33 = z.Z,
            Tx = anchor.X, Ty = anchor.Y, Tz = anchor.Z
        };
        LimitContraction(result);
        return result;
    }

    private static (double, double, double, double) Similarity(Random random)
    {
        double scale = Range(random, .3, .66);
        return (scale, scale, scale, 0);
    }

    private static Ifs3DTransform MirrorAcrossYZ(Ifs3DTransform t) => new()
    {
        M11 = t.M11, M12 = -t.M12, M13 = -t.M13, Tx = -t.Tx,
        M21 = -t.M21, M22 = t.M22, M23 = t.M23, Ty = t.Ty,
        M31 = -t.M31, M32 = t.M32, M33 = t.M33, Tz = t.Tz,
        Probability = t.Probability
    };

    private static void LimitContraction(Ifs3DTransform t)
    {
        // The Frobenius norm is an upper bound for the largest singular value.
        double bound = Math.Sqrt(t.M11 * t.M11 + t.M12 * t.M12 + t.M13 * t.M13 +
            t.M21 * t.M21 + t.M22 * t.M22 + t.M23 * t.M23 +
            t.M31 * t.M31 + t.M32 * t.M32 + t.M33 * t.M33);
        if (bound <= MaximumContraction) return;
        double scale = MaximumContraction / bound;
        t.M11 *= scale; t.M12 *= scale; t.M13 *= scale;
        t.M21 *= scale; t.M22 *= scale; t.M23 *= scale;
        t.M31 *= scale; t.M32 *= scale; t.M33 *= scale;
    }

    private static Ifs3DTransformFamily PickFamily(IReadOnlyList<Ifs3DTransformFamily> families, Random random)
    {
        double total = families.Sum(FamilyWeight);
        double target = random.NextDouble() * total;
        foreach (Ifs3DTransformFamily family in families)
        {
            target -= FamilyWeight(family);
            if (target <= 0) return family;
        }
        return families[^1];
    }

    private static double FamilyWeight(Ifs3DTransformFamily family) => family switch
    {
        Ifs3DTransformFamily.Anisotropic => 1.2,
        Ifs3DTransformFamily.Reflection => .7,
        Ifs3DTransformFamily.Stem or Ifs3DTransformFamily.Sheet => .5,
        _ => 1
    };

    private static double RawProbability(Ifs3DTransform t, Ifs3DProbabilityMode mode, Random random) => mode switch
    {
        Ifs3DProbabilityMode.Uniform => 1,
        Ifs3DProbabilityMode.Random => Range(random, .2, 1.5),
        _ => Math.Max(.015, Math.Abs(Determinant(t))) * Range(random, .8, 1.3)
    };

    private static double Determinant(Ifs3DTransform t) =>
        t.M11 * (t.M22 * t.M33 - t.M23 * t.M32) -
        t.M12 * (t.M21 * t.M33 - t.M23 * t.M31) +
        t.M13 * (t.M21 * t.M32 - t.M22 * t.M31);

    private static double Range(Random random, double minimum, double maximum) =>
        minimum + random.NextDouble() * (maximum - minimum);
}
