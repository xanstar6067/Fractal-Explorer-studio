namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Reuse the common nine volume styles, lighting and probe for a float32 U/V field.</summary>
internal static class GrayScott3DShader
{
    public static string Source => Ifs3DShader.Source
        .Replace("Texture3D<float> Density", "Texture3D<float2> Density", StringComparison.Ordinal)
        .Replace("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            if (any(abs(samplePosition) > 1.0001)) return 0.0;
            int axis = (int)ShapeB.x;
            if (axis > 0 && samplePosition[axis - 1] > ShapeB.y + .0001) return 0.0;
            return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).y;
            """, StringComparison.Ordinal)
        .Replace("2.0 / 512.0", "2.0 / ShapeA.x", StringComparison.Ordinal)
        .Replace("const float threshold = 0.075;", "float threshold = ShapeA.y;", StringComparison.Ordinal)
        .Replace("float3 farPlane = (1.0 - origin) / safe;", """
            int cutAxis = (int)ShapeB.x;
            float3 boxMax = float3(cutAxis == 1 ? ShapeB.y : 1.0,
                cutAxis == 2 ? ShapeB.y : 1.0, cutAxis == 3 ? ShapeB.y : 1.0);
            float3 farPlane = (boxMax - origin) / safe;
            """, StringComparison.Ordinal)
        .Replace("float3 gradient = float3(", """
            int axis = (int)ShapeB.x;
            if (axis > 0 && abs(p[axis - 1] - ShapeB.y) < .0002)
                return axis == 1 ? float3(1,0,0) : axis == 2 ? float3(0,1,0) : float3(0,0,1);
            if (abs(abs(p.x) - 1.0) < .0002) return float3(sign(p.x),0,0);
            if (abs(abs(p.y) - 1.0) < .0002) return float3(0,sign(p.y),0);
            if (abs(abs(p.z) - 1.0) < .0002) return float3(0,0,sign(p.z));
            float3 gradient = float3(
            """, StringComparison.Ordinal)
        .Replace("if (mode == 3)", "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).y;\n            else if (mode == 3)", StringComparison.Ordinal);
}
