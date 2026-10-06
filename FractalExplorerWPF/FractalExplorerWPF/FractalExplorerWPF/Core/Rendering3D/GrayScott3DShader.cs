namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Reuse the common nine volume styles, lighting and probe for the published V volume
/// (R32_Float, written on the GPU by <see cref="GrayScott3DGpuSimulation"/>).
/// </summary>
internal static class GrayScott3DShader
{
    public static string Source { get; } = Patch(Ifs3DShader.Source,
        ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            if (any(abs(samplePosition) > 1.0001)) return 0.0;
            int axis = (int)ShapeB.x;
            if (axis > 0 && samplePosition[axis - 1] > ShapeB.y + .0001) return 0.0;
            return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);
            """),
        ("2.0 / 512.0", "2.0 / ShapeA.x"),
        ("const float threshold = 0.075;", "float threshold = ShapeA.y;"),
        ("float3 farPlane = (1.0 - origin) / safe;", """
            int cutAxis = (int)ShapeB.x;
            float3 boxMax = float3(cutAxis == 1 ? ShapeB.y : 1.0,
                cutAxis == 2 ? ShapeB.y : 1.0, cutAxis == 3 ? ShapeB.y : 1.0);
            float3 farPlane = (boxMax - origin) / safe;
            """),
        ("float3 gradient = float3(", """
            int axis = (int)ShapeB.x;
            if (axis > 0 && abs(p[axis - 1] - ShapeB.y) < .0002)
                return axis == 1 ? float3(1,0,0) : axis == 2 ? float3(0,1,0) : float3(0,0,1);
            if (abs(abs(p.x) - 1.0) < .0002) return float3(sign(p.x),0,0);
            if (abs(abs(p.y) - 1.0) < .0002) return float3(0,sign(p.y),0);
            if (abs(abs(p.z) - 1.0) < .0002) return float3(0,0,sign(p.z));
            float3 gradient = float3(
            """),
        ("if (mode == 3)", "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);\n            else if (mode == 3)"));

    /// <summary>A changed IFS source must fail loudly instead of silently dropping a patch.</summary>
    private static string Patch(string source, params (string Find, string Replace)[] patches)
    {
        foreach (var (find, replace) in patches)
        {
            int count = 0;
            for (int at = source.IndexOf(find, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(find, at + find.Length, StringComparison.Ordinal)) count++;
            if (count == 0) throw new InvalidOperationException($"Шейдер Gray–Scott 3D: не найден фрагмент «{find}».");
            source = source.Replace(find, replace, StringComparison.Ordinal);
        }
        return source;
    }
}
