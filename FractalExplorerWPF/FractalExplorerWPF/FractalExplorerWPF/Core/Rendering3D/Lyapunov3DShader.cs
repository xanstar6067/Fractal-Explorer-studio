namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Общий свет и девять стилей объёма, но поле содержит настоящую экспоненту λ.</summary>
internal static class Lyapunov3DShader
{
    public static string Source { get; } = Build();

    private static string Build()
    {
        string source = GrayScott3DShader.Source.Replace("\r\n", "\n", StringComparison.Ordinal);
        source = Patch(source, "float SampleDensity(float3 samplePosition)", """
            float ExponentAt(float3 p) {
                return Density.SampleLevel(DensitySampler, saturate(p*.5+.5), 0);
            }
            float ExponentColor(float3 p) {
                return saturate(.5+ExponentAt(p)/(2*ShapeA.z));
            }
            float SampleDensity(float3 samplePosition)
            """);
        source = Patch(source,
            "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            float delta = ExponentAt(samplePosition)-BoxInversion.x;
            int region = (int)ShapeB.z;
            if (region == 2) return saturate(1-abs(delta)/(2*ShapeB.w));
            return saturate(.5+(region == 1 ? delta : -delta)/(2*ShapeA.z));
            """);
        source = Patch(source,
            "if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);", """
            if (mode == 2) value = ExponentColor(surfacePoint);
            else if (mode == 4) value = surfacePoint.x*.5+.5;
            else if (mode == 5) value = surfacePoint.y*.5+.5;
            else if (mode == 6) value = surfacePoint.z*.5+.5;
            """);
        source = Patch(source, "if (entry >= exitDistance)\n", """
            if (BoxInversion.y > .5) {
                int axis = (int)ShapeB.x-1;
                if (axis < 0 || abs(direction[axis]) < 1e-7)
                    return Probe.x > .5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
                float hit = (ShapeB.y-origin[axis])/direction[axis];
                float3 p = origin+direction*hit;
                if (hit < 0 || any(abs(p)>1.0001))
                    return Probe.x > .5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
                if (Probe.x > .5) return PackFloat(hit);
                float3 tint = SamplePalette(ExponentColor(p)*ShapeC.y+ShapeC.z,(int)PaletteInfo.y);
                return float4(LinearToSrgb(tint),1);
            }
            if (entry >= exitDistance)

            """);
        source = Patch(source, "int style = (int)Style.x;", """
            if ((int)Style.x == 4 && Probe.x < .5) {
                float3 emission = 0; float transmission = 1;
                float ds = cell*.65;
                [loop] for (int j=0; j<1536 && distance<exitDistance && transmission>.005; j++) {
                    float3 p = origin+direction*min(distance+ds*.5,exitDistance);
                    float occupancy = saturate((SampleDensity(p)-.5)*2);
                    float alpha = 1-exp(-occupancy*ds*8*max(Style.y,.001));
                    float3 tint = SamplePalette(ExponentColor(p)*ShapeC.y+ShapeC.z,(int)PaletteInfo.y);
                    emission += transmission*alpha*tint;
                    transmission *= 1-alpha;
                    distance += ds;
                }
                return float4(LinearToSrgb(sky*transmission+emission),1);
            }
            int style = (int)Style.x;
            """);
        return source;
    }

    private static string Patch(string source, string find, string replacement)
    {
        if (!source.Contains(find, StringComparison.Ordinal))
            throw new InvalidOperationException($"Шейдер Ляпунова 3D: не найден фрагмент «{find}».");
        return source.Replace(find, replacement, StringComparison.Ordinal);
    }
}
