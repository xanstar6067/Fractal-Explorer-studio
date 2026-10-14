namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Shared nine volume styles with activator fronts, discrete phase winding filaments and phase colour.</summary>
internal static class Excitable3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        return ShaderSource.Patch(GrayScott3DShader.Source, "возбудимой среды",
            ("Texture3D<float> Density", "Texture3D<float4> Density"),
            ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
                float4 field = Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0);
                if (ShapeB.z > .5 && ShapeB.z < 1.5) return field.w;
                return ShapeB.z > 1.5 ? max(field.x, field.w) : field.x;
                """),
            ("if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
                if (mode == 2) {
                    float4 field = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);
                    float phase = (atan2(field.z, field.y) + 3.14159265359) / 6.28318530718;
                    return SamplePalette(phase * ShapeC.y + ShapeC.z, 1);
                }
                else if (mode == 4) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).x;
                """));
    }
}
