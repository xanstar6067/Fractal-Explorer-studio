namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>GPU ray marcher for the density volume produced by the IFS chaos game.</summary>
internal static class Ifs3DShader
{
    public const string Source = """
        cbuffer Frame : register(b0)
        {
            float4 Resolution;
            float4 CameraPosition;
            float4 CameraRight;
            float4 CameraUp;
            float4 CameraForward;
            float4 March;
            float4 ShapeA;
            float4 ShapeB;
            float4 ShapeC;
            float4 Light;
            float4 Surface;
            float4 BackgroundTop;
            float4 BackgroundBottom;
            float4 Flags;
            float4 Probe;
            float4 Style;
            float4 LightColor;
            float4 PaletteInfo;
            float4 Palette[16];
        };

        Texture3D<float> Density : register(t0);

        struct PSInput { float4 position : SV_POSITION; };

        PSInput VSMain(uint id : SV_VertexID)
        {
            PSInput output;
            output.position = float4((id == 2 ? 3.0 : -1.0),
                                     (id == 1 ? -3.0 : 1.0), 0.0, 1.0);
            return output;
        }

        float4 PackFloat(float value)
        {
            uint bits = asuint(value);
            return float4((bits >> 16) & 255, (bits >> 8) & 255,
                          bits & 255, (bits >> 24) & 255) / 255.0;
        }

        float3 LinearToSrgb(float3 color)
        {
            color = saturate(color);
            float3 low = color * 12.92;
            float3 high = 1.055 * pow(max(color, 1e-8), 1.0 / 2.4) - 0.055;
            return lerp(low, high, step(0.0031308, color));
        }

        float SampleDensity(float3 samplePosition)
        {
            int3 cell = clamp((int3)((samplePosition + 1.0) * 128.0), int3(0, 0, 0), int3(255, 255, 255));
            return Density.Load(int4(cell, 0));
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float2 pixel = input.position.xy + Resolution.zw;
            float2 plane = (pixel - 0.5 * Resolution.xy) / (0.5 * Resolution.y);
            plane.y = -plane.y;
            float3 direction = normalize(CameraForward.xyz * CameraPosition.w +
                                         CameraRight.xyz * plane.x + CameraUp.xyz * plane.y);
            float3 origin = CameraPosition.xyz;
            float3 safe = direction + (abs(direction) < 1e-6) * 1e-6;
            float3 nearPlane = (-1.0 - origin) / safe;
            float3 farPlane = (1.0 - origin) / safe;
            float3 lo = min(nearPlane, farPlane);
            float3 hi = max(nearPlane, farPlane);
            float entry = max(0.0, max(lo.x, max(lo.y, lo.z)));
            float exitDistance = min(hi.x, min(hi.y, hi.z));
            float3 sky = lerp(BackgroundBottom.rgb, BackgroundTop.rgb,
                              saturate(direction.y * 0.5 + 0.5));
            if (entry >= exitDistance)
                return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(sky), 1.0);

            float stepLength = 2.0 / 256.0 * clamp(March.y, 0.5, 2.0);
            float distance = entry;
            [loop]
            for (int i = 0; i < (int)March.x && distance < exitDistance; i++)
            {
                float3 samplePosition = origin + direction * distance;
                float density = SampleDensity(samplePosition);
                if (density > 0.025)
                {
                    if (Probe.x > 0.5) return PackFloat(distance);
                    float cell = 2.0 / 256.0;
                    float3 gradient = float3(
                        SampleDensity(samplePosition + float3(cell, 0, 0)) - SampleDensity(samplePosition - float3(cell, 0, 0)),
                        SampleDensity(samplePosition + float3(0, cell, 0)) - SampleDensity(samplePosition - float3(0, cell, 0)),
                        SampleDensity(samplePosition + float3(0, 0, cell)) - SampleDensity(samplePosition - float3(0, 0, cell)));
                    float3 normal = -normalize(gradient + 1e-5);
                    float lit = 0.4 + 0.6 * abs(dot(normal, normalize(Light.xyz)));
                    float depthShade = saturate(1.2 - distance * 0.16);
                    float3 color = Surface.rgb * lit * depthShade;
                    return float4(LinearToSrgb(color), 1.0);
                }
                distance += stepLength;
            }
            return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(sky), 1.0);
        }
        """;
}
