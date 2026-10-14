namespace FractalExplorerWPF.Core.Rendering3D;

internal static class PhaseFieldCrystal3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        return ShaderSource.Patch(GrayScott3DShader.Source, "PFC",
            ("Texture3D<float> Density","Texture3D<float2> Density"),
            ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
                float2 field=Density.SampleLevel(DensitySampler,saturate(samplePosition*.5+.5),0);
                if(ShapeB.z>1.5) return field.y*2.5/ShapeA.z;
                float delta=(field.x-ShapeB.w)/ShapeA.z;
                return max(ShapeB.z>.5 ? -delta : delta,0);
                """),
            ("if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
                if(mode==2) value=.5+.5*(Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).x-ShapeB.w)/ShapeA.z;
                else if(mode==4) value=Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).y*2.5/ShapeA.z;
                """));
    }
}
