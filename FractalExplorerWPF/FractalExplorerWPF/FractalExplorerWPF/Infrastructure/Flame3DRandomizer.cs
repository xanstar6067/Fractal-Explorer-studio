using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class Flame3DRandomizer
{
    public static List<Flame3DTransform> Create(Flame3DRandomizationSettings settings, Random? random = null)
    {
        var s = settings.Clone().Normalize();
        if (s.Variations.Count == 0) throw new InvalidOperationException("Выберите хотя бы одну 3D-вариацию.");
        random ??= Random.Shared;
        int count = random.Next(s.MinimumTransforms, s.MaximumTransforms + 1);
        // Reuse the proven spatial affine generator and the Flame color/weight generator.
        var maps = Ifs3DRandomizer.Create(new()
        {
            MinimumTransforms = count, MaximumTransforms = count,
            Families = [Ifs3DTransformFamily.Similarity, Ifs3DTransformFamily.Anisotropic, Ifs3DTransformFamily.Shear],
            PlacementMode = (Ifs3DPlacementMode)random.Next(3), ProbabilityMode = Ifs3DProbabilityMode.Random
        }, random);
        var colored = FlameRandomizer.Create(new()
        {
            MinimumTransforms = count, MaximumTransforms = count, Variations = [FlameVariation.Linear]
        }, random);
        return maps.Select((map, i) => new Flame3DTransform
        {
            Map = map, Weight = colored[i].Weight, Color = colored[i].Color,
            Variation = s.Variations[random.Next(s.Variations.Count)],
            Amount = .4 + random.NextDouble() * .5, ColorSpeed = .35 + random.NextDouble() * .4
        }).ToList();
    }
}
