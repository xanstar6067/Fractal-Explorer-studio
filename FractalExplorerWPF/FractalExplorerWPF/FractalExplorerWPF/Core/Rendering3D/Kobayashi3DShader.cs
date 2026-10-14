namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Common volume styles with solid fraction geometry and temperature colouring.</summary>
internal static class Kobayashi3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        return ShaderSource.Patch(GrayScott3DShader.Source, "Кобаяси",
            ("Texture3D<float> Density", "Texture3D<float2> Density"),
            ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
                "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).x;"),
            ("if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);",
                "if (mode == 2) value = saturate((Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).y + ShapeB.z) / max(ShapeB.w, .001));"));
    }
}
