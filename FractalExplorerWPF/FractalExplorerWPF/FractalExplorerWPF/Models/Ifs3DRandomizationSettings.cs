namespace FractalExplorerWPF.Models;

public enum Ifs3DTransformFamily
{
    Similarity,
    Anisotropic,
    Shear,
    Reflection,
    Stem,
    Sheet
}

public enum Ifs3DPlacementMode
{
    Free,
    Spherical,
    Helix,
    Bilateral
}

public enum Ifs3DProbabilityMode
{
    VolumeWeighted,
    Uniform,
    Random
}

public sealed class Ifs3DRandomizationSettings
{
    public const int MinimumAllowedTransforms = 1;
    public const int MaximumAllowedTransforms = 25;

    public int MinimumTransforms { get; set; } = 4;
    public int MaximumTransforms { get; set; } = 7;
    public Ifs3DPlacementMode PlacementMode { get; set; } = Ifs3DPlacementMode.Spherical;
    public Ifs3DProbabilityMode ProbabilityMode { get; set; } = Ifs3DProbabilityMode.VolumeWeighted;
    public List<Ifs3DTransformFamily> Families { get; set; } = [.. Enum.GetValues<Ifs3DTransformFamily>()];

    public Ifs3DRandomizationSettings Normalize()
    {
        MinimumTransforms = Math.Clamp(MinimumTransforms, MinimumAllowedTransforms, MaximumAllowedTransforms);
        MaximumTransforms = Math.Clamp(MaximumTransforms, MinimumAllowedTransforms, MaximumAllowedTransforms);
        if (MinimumTransforms > MaximumTransforms)
            (MinimumTransforms, MaximumTransforms) = (MaximumTransforms, MinimumTransforms);
        if (!Enum.IsDefined(PlacementMode)) PlacementMode = Ifs3DPlacementMode.Spherical;
        if (!Enum.IsDefined(ProbabilityMode)) ProbabilityMode = Ifs3DProbabilityMode.VolumeWeighted;
        Families = [.. (Families ?? []).Where(Enum.IsDefined).Distinct().OrderBy(family => family)];
        return this;
    }

    public Ifs3DRandomizationSettings Clone() => new()
    {
        MinimumTransforms = MinimumTransforms,
        MaximumTransforms = MaximumTransforms,
        PlacementMode = PlacementMode,
        ProbabilityMode = ProbabilityMode,
        Families = [.. (Families ?? [])]
    };
}
