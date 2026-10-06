namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Common volume lighting and probe, with an optional view of the complementary phase.</summary>
internal static class CahnHilliard3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        const string sample = "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);";
        if (!GrayScott3DShader.Source.Contains(sample, StringComparison.Ordinal))
            throw new InvalidOperationException("Не найдено чтение поля в шейдере Кана–Хиллиарда.");
        return GrayScott3DShader.Source.Replace(sample,
            "float c = Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0); return ShapeB.z > .5 ? 1.0-c : c;",
            StringComparison.Ordinal);
    }
}
