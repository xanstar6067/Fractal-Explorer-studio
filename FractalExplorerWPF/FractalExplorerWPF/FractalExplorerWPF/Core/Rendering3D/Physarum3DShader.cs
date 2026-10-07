namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Physarum3DShader
{
    public static string Source { get; } = Build();
    private static string Build()
    {
        string source = GrayScott3DShader.Source;
        string anchor = "int style = (int)Style.x;";
        if (!source.Contains(anchor)) throw new InvalidOperationException("Изменился общий шейдер объёма.");
        source = source.Replace("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
            "return 1-exp(-max(Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0),0)*ShapeB.z);", StringComparison.Ordinal);
        source = source.Replace("value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);",
            "value = SampleDensity(surfacePoint);", StringComparison.Ordinal);
        // Other eight styles and the distance probe use the same surface as the common tracer.
        // Glow integrates emission along the whole ray to reveal filaments deep inside the network.
        return source.Replace(anchor, """
            if ((int)Style.x == 3 && Probe.x < .5) {
                float3 emission = 0; float transmittance = 1;
                float ds = 2.0 / ShapeA.x * .65;
                [loop] for (int j=0; j<640 && distance<exitDistance; j++) {
                    float3 samplePoint=origin+direction*(distance+ds*.5);
                    float density=SampleDensity(samplePoint);
                    float visible=saturate((density-ShapeA.y)/max(1-ShapeA.y,.01));
                    float alpha=1-exp(-visible*visible*ds*12*max(Style.y,.1));
                    float3 tint=SamplePalette(saturate(density*.58+.12)*ShapeC.y+ShapeC.z,0);
                    emission+=transmittance*alpha*tint*1.4;
                    transmittance*=1-alpha*.85;
                    distance+=ds;
                }
                float3 color=sky*transmittance+emission;
                return float4(LinearToSrgb(color/(1+color*.35)),1);
            }
            int style = (int)Style.x;
            """, StringComparison.Ordinal);
    }
}
