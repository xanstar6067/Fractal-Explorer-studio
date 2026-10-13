namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>The common volume renderer, lighting and probe, with amplitude/phase/filament channels.</summary>
internal static class GinzburgLandau3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        string s = GrayScott3DShader.Source;
        s = Patch(s, "Texture3D<float> Density", "Texture3D<float4> Density");
        s = Patch(s, "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            float4 field = Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0);
            if (ShapeB.z < .5) return field.w;
            return ShapeB.z < 1.5 ? saturate(1-field.x) : field.x;
            """);
        s = Patch(s, "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
            if (mode == 2) {
                float4 field = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);
                float phase = (atan2(field.z, field.y) + 3.14159265359) / 6.28318530718;
                return SamplePalette(phase * ShapeC.y + ShapeC.z, 1);
            }
            else if (mode == 4) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).x;
            """);
        return s;
    }

    private static string Patch(string source, string find, string replacement)
    {
        if (!source.Contains(find, StringComparison.Ordinal))
            throw new InvalidOperationException("Изменился базовый шейдер Гинзбурга–Ландау: " + find);
        return source.Replace(find, replacement, StringComparison.Ordinal);
    }
}
