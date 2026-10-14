namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Common volume lighting and probe, with an optional view of the complementary phase.</summary>
internal static class CahnHilliard3DShader
{
    public static string Source { get; } = ShaderSource.Patch(GrayScott3DShader.Source, "Кана–Хиллиарда",
        ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
            "float c = Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0); return ShapeB.z > .5 ? 1.0-c : c;"));
}
