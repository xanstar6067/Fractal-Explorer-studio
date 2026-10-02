namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Shared volume lighting and navigation, with an additional birth-time channel.</summary>
internal static class Dla3DShader
{
    public static string Source => Ifs3DShader.Source
        .Replace("2.0 / 512.0", "2.0 / 256.0", StringComparison.Ordinal)
        .Replace("const float threshold = 0.075;", "const float threshold = 0.18;", StringComparison.Ordinal)
        .Replace("Texture3D<float> Density", "Texture3D<float2> Density", StringComparison.Ordinal)
        .Replace("saturate(samplePosition * 0.5 + 0.5), 0);", "saturate(samplePosition * 0.5 + 0.5), 0).r;", StringComparison.Ordinal)
        .Replace("float glowPath = 0.0;", "float glowPath = 0.0; float birthSum = 0.0; float birthWeight = 0.0;", StringComparison.Ordinal)
        .Replace("integratedDensity += segmentDensity * (nextDistance - distance);",
            "integratedDensity += segmentDensity * (nextDistance - distance); float2 birth = Density.SampleLevel(DensitySampler, saturate((origin + direction * nextDistance) * .5 + .5), 0); birthSum += birth.g * (nextDistance - distance); birthWeight += birth.r * (nextDistance - distance);", StringComparison.Ordinal)
        .Replace("SamplePalette(opacity * ShapeC.y + ShapeC.z, 0)",
            "SamplePalette(((int)Flags.x == 5 ? birthSum / max(birthWeight, 1e-5) : opacity) * ShapeC.y + ShapeC.z, (int)PaletteInfo.y)", StringComparison.Ordinal)
        .Replace("SamplePalette(glow * ShapeC.y + ShapeC.z, 0)",
            "SamplePalette(((int)Flags.x == 5 ? birthSum / max(birthWeight, 1e-5) : glow) * ShapeC.y + ShapeC.z, (int)PaletteInfo.y)", StringComparison.Ordinal)
        .Replace("else if (mode == 7)", "else if (mode == 5) { float2 birthData = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0); value = birthData.g / max(birthData.r, 1e-5); }\n            else if (mode == 7)", StringComparison.Ordinal);
}
