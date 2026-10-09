namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Common volume styles with solid fraction geometry and temperature colouring.</summary>
internal static class Kobayashi3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        string s = GrayScott3DShader.Source;
        s = Replace(s, "Texture3D<float> Density", "Texture3D<float2> Density");
        s = Replace(s, "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
            "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).x;");
        s = Replace(s, "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);",
            "if (mode == 2) value = saturate((Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).y + ShapeB.z) / max(ShapeB.w, .001));");
        return s;
    }
    private static string Replace(string s, string find, string value)
    {
        if (!s.Contains(find, StringComparison.Ordinal)) throw new InvalidOperationException("Шейдер Кобаяси: отсутствует фрагмент " + find);
        return s.Replace(find, value, StringComparison.Ordinal);
    }
}
