namespace FractalExplorerWPF.Core.Rendering3D;

internal static class PhaseFieldCrystal3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        string s=GrayScott3DShader.Source;
        s=Patch(s,"Texture3D<float> Density","Texture3D<float2> Density");
        s=Patch(s,"return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            float2 field=Density.SampleLevel(DensitySampler,saturate(samplePosition*.5+.5),0);
            if(ShapeB.z>1.5) return field.y*2.5/ShapeA.z;
            float delta=(field.x-ShapeB.w)/ShapeA.z;
            return max(ShapeB.z>.5 ? -delta : delta,0);
            """);
        s=Patch(s,"if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
            if(mode==2) value=.5+.5*(Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).x-ShapeB.w)/ShapeA.z;
            else if(mode==4) value=Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).y*2.5/ShapeA.z;
            """);
        return s;
    }
    private static string Patch(string s,string find,string replacement)
    {
        if(!s.Contains(find,StringComparison.Ordinal)) throw new InvalidOperationException("Изменился базовый шейдер PFC: "+find);
        return s.Replace(find,replacement,StringComparison.Ordinal);
    }
}
