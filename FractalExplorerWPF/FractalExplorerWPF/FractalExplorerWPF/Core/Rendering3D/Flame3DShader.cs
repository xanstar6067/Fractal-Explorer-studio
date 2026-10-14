namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Colored emission/absorption, sharing the IFS camera, probe and sculpture lighting.</summary>
internal static class Flame3DShader
{
    public static readonly string Source = CreateSource();

    private static string CreateSource()
    {
        // Retain the shared frame layout, ray/probe and nine lighting implementations.
        string source = ShaderSource.Patch(Ifs3DShader.Source, "Flame 3D",
            ("Texture3D<float> Density", "Texture3D<float4> Density"),
            ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);",
                "return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).a;"),
            ("float3 LinearToSrgb(float3 color)", Helpers + "\nfloat3 LinearToSrgb(float3 color)"),
            ("float cell = 2.0 / 512.0;", $"float cell = 2.0 / {Flame3DVolume.Side}.0;"),
            ("float strength = max(Style.y, 0.0);", "float strength = max(Style.y, 0.0);\n" + VolumeMarch),
            ("return float4(LinearToSrgb(color), 1.0);", "return float4(LinearToSrgb(FlameTone(color)), 1.0);"));
        int start = source.IndexOf("float3 SurfaceAlbedo(", StringComparison.Ordinal);
        int end = source.IndexOf("float3 EstimateNormal(", start, StringComparison.Ordinal);
        return source[..start] + """
            float3 SurfaceAlbedo(float3 normal, float3 surfacePoint, float3 rayDirection,
                                 float depth, float occlusion, float stepsRatio)
            {
                return FlameColor(surfacePoint);
            }

            """ + source[end..];
    }

    private static string Helpers => ShaderSource.Load("Flame3D.Helpers.hlsl");

    private const string VolumeMarch = """
        if (style == 3 || style == 4)
        {
            float3 radiance = 0;
            float transmission = 1;
            float probeHit = -1;
            [loop]
            for (int j = 0; j < 1536 && distance < exitDistance; j++)
            {
                float dt = min(stepLength, exitDistance - distance);
                float3 p = origin + direction * (distance + dt * 0.5);
                float4 v = Density.SampleLevel(DensitySampler, saturate(p * 0.5 + 0.5), 0);
                if (probeHit < 0 && v.a >= threshold) probeHit = distance + dt * 0.5;
                float alpha = 1 - exp(-v.a * dt * ShapeA.z * (style == 3 ? 18 : 48));
                float3 tint = v.rgb / max(v.a, 1e-6);
                radiance += transmission * alpha * tint * (style == 3 ? 3.0 : 1.5) * max(strength, 0.001);
                transmission *= 1 - alpha;
                distance += dt;
                if (transmission < 0.002) break;
            }
            if (Probe.x > 0.5) return PackFloat(probeHit);
            return float4(LinearToSrgb(FlameTone(radiance) + sky * transmission), 1);
        }
        """;
}
