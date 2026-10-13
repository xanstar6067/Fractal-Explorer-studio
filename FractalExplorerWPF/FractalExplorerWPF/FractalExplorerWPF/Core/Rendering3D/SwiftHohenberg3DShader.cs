namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>The common volume styles and probe, interpreting a raw signed scalar field.</summary>
internal static class SwiftHohenberg3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        string source = GrayScott3DShader.Source;
        source = Patch(source, "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            float u=Density.SampleLevel(DensitySampler,saturate(samplePosition*.5+.5),0)/ShapeA.z;
            if(ShapeB.z>1.5) return saturate(1-abs(u));
            return max(ShapeB.z>.5 ? -u : u,0);
            """);
        source = Patch(source, "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
            if (mode == 2) value=.5+.5*Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0)/ShapeA.z;
            else if (mode == 4) value=abs(Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0))/ShapeA.z;
            """);
        return source;
    }
    private static string Patch(string source, string find, string replacement)
    {
        if (!source.Contains(find, StringComparison.Ordinal)) throw new InvalidOperationException("Изменился базовый шейдер Свифта–Хоэнберга: " + find);
        return source.Replace(find, replacement, StringComparison.Ordinal);
    }
}
