namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Physarum3DShader
{
    public static string Source { get; } = ShaderSource.Patch(GrayScott3DShader.Source, "Physarum 3D",
        ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
            "return 1-exp(-max(Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0),0)*ShapeB.z);"),
        ("value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);",
            "value = SampleDensity(surfacePoint);"),
        ("int style = (int)Style.x;", Lenia3DShader.GlowStyle));
}
