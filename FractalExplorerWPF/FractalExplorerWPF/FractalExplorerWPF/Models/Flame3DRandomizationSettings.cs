namespace FractalExplorerWPF.Models;

public sealed class Flame3DRandomizationSettings
{
    public const int MinimumAllowedTransforms = 1;
    public const int MaximumAllowedTransforms = 25;

    public int MinimumTransforms { get; set; } = 3;
    public int MaximumTransforms { get; set; } = 6;
    public List<Flame3DVariation> Variations { get; set; } = [.. Enum.GetValues<Flame3DVariation>()];

    public Flame3DRandomizationSettings Normalize()
    {
        MinimumTransforms = Math.Clamp(MinimumTransforms, MinimumAllowedTransforms, MaximumAllowedTransforms);
        MaximumTransforms = Math.Clamp(MaximumTransforms, MinimumAllowedTransforms, MaximumAllowedTransforms);
        if (MinimumTransforms > MaximumTransforms)
            (MinimumTransforms, MaximumTransforms) = (MaximumTransforms, MinimumTransforms);

        Variations = [.. (Variations ?? [])
            .Where(variation => Enum.IsDefined(variation))
            .Distinct()
            .OrderBy(variation => (int)variation)];
        return this;
    }

    public Flame3DRandomizationSettings Clone() => new()
    {
        MinimumTransforms = MinimumTransforms,
        MaximumTransforms = MaximumTransforms,
        Variations = [.. (Variations ?? [])]
    };
}
