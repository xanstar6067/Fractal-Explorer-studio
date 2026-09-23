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
        SamplerState DensitySampler : register(s0);

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
            return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);
        }

        float3 SkyAt(float3 direction)
        {
            return lerp(BackgroundBottom.rgb, BackgroundTop.rgb,
                        saturate(direction.y * 0.5 + 0.5));
        }

        float3 SamplePalette(float value, int repeat)
        {
            int count = clamp((int)PaletteInfo.x, 1, 16);
            float t = repeat == 1 ? frac(value) :
                repeat == 2 ? 1.0 - abs(2.0 * frac(value) - 1.0) : saturate(value);
            if (repeat == 2) t = t * t * (3.0 - 2.0 * t);
            t = pow(saturate(t), max(PaletteInfo.w, 1e-3));
            if (count == 1) return Palette[0].rgb;
            if (PaletteInfo.z > 0.5)
                return Palette[clamp((int)(t * count), 0, count - 1)].rgb;
            if (repeat == 1)
            {
                float scaled = t * count;
                int low = clamp((int)scaled, 0, count - 1);
                int high = low + 1 == count ? 0 : low + 1;
                return lerp(Palette[low].rgb, Palette[high].rgb, saturate(scaled - low));
            }
            float span = t * (count - 1);
            int first = clamp((int)span, 0, count - 2);
            return lerp(Palette[first].rgb, Palette[first + 1].rgb, saturate(span - first));
        }

        float3 SurfaceAlbedo(float3 normal, float3 surfacePoint, float3 rayDirection,
                             float depth, float occlusion, float stepsRatio)
        {
            int mode = (int)Flags.x;
            if (mode == 0) return Surface.rgb;
            if (mode == 1) return abs(normal);
            float value;
            if (mode == 3) value = depth * 3.0;
            else if (mode == 7) value = surfacePoint.y;
            else if (mode == 8) value = 1.0 - occlusion;
            else if (mode == 9) value = 1.0 - saturate(dot(normal, -rayDirection));
            else value = stepsRatio;
            return SamplePalette(value * ShapeC.y + ShapeC.z, (int)PaletteInfo.y);
        }

        float3 EstimateNormal(float3 p, float cell, float3 rayDirection)
        {
            float3 gradient = float3(
                SampleDensity(p + float3(cell, 0, 0)) - SampleDensity(p - float3(cell, 0, 0)),
                SampleDensity(p + float3(0, cell, 0)) - SampleDensity(p - float3(0, cell, 0)),
                SampleDensity(p + float3(0, 0, cell)) - SampleDensity(p - float3(0, 0, cell)));
            return dot(gradient, gradient) > 1e-8 ? normalize(-gradient) : -rayDirection;
        }

        float LocalOcclusion(float3 p, float3 normal, float cell)
        {
            // Sample the neighborhood around the outward side of the surface. A single
            // normal ray misses most of the adjacent IFS branches and gives a flat AO map.
            float3 q = p + normal * cell * 2.0;
            float nearRadius = cell * 8.0;
            float farRadius = cell * 24.0;
            float nearDensity =
                SampleDensity(q + float3(nearRadius, 0, 0)) + SampleDensity(q - float3(nearRadius, 0, 0)) +
                SampleDensity(q + float3(0, nearRadius, 0)) + SampleDensity(q - float3(0, nearRadius, 0)) +
                SampleDensity(q + float3(0, 0, nearRadius)) + SampleDensity(q - float3(0, 0, nearRadius));
            float farDensity =
                SampleDensity(q + float3(farRadius, 0, 0)) + SampleDensity(q - float3(farRadius, 0, 0)) +
                SampleDensity(q + float3(0, farRadius, 0)) + SampleDensity(q - float3(0, farRadius, 0)) +
                SampleDensity(q + float3(0, 0, farRadius)) + SampleDensity(q - float3(0, 0, farRadius));
            return saturate(1.0 - nearDensity * 0.45 - farDensity * 0.2);
        }

        float ShadowVisibility(float3 p, float3 lightDirection, float cell)
        {
            float occupied = 0.0;
            [loop]
            for (int i = 1; i <= 32; i++)
                occupied += SampleDensity(p + lightDirection * (cell * (i * 4.0 + 2.0)));
            return exp(-occupied * cell * 1.5 * Flags.y);
        }

        float BackThickness(float3 p, float3 normal, float cell)
        {
            float occupied = 0.0;
            [unroll]
            for (int i = 1; i <= 12; i++)
                occupied += SampleDensity(p - normal * cell * (i * 2.0));
            return exp(-occupied * cell * 12.0);
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
            float3 sky = SkyAt(direction);
            if (entry >= exitDistance)
                return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(sky), 1.0);

            // The traversal must cover the whole box. A fixed user ray-step limit used to
            // terminate halfway through it, leaving camera-centered circular silhouettes.
            float cell = 2.0 / 512.0;
            float stepLength = cell * 0.65;
            const float threshold = 0.075;
            float distance = entry;
            float previousDensity = SampleDensity(origin + direction * distance);
            float integratedDensity = 0.0;
            float glowPath = 0.0;
            float firstHit = -1.0;
            int usedSteps = 0;
            int style = (int)Style.x;
            float strength = max(Style.y, 0.0);
            [loop]
            for (int i = 0; i < 1536 && distance < exitDistance; i++)
            {
                float nextDistance = min(distance + stepLength, exitDistance);
                float density = SampleDensity(origin + direction * nextDistance);
                float segmentDensity = 0.5 * (previousDensity + density);
                integratedDensity += segmentDensity * (nextDistance - distance);
                if (style == 3 && (i & 3) == 0)
                {
                    float3 samplePosition = origin + direction * nextDistance;
                    float reach = cell * 8.0;
                    float nearby = max(segmentDensity,
                        max(max(SampleDensity(samplePosition + CameraRight.xyz * reach),
                                SampleDensity(samplePosition - CameraRight.xyz * reach)),
                            max(SampleDensity(samplePosition + CameraUp.xyz * reach),
                                SampleDensity(samplePosition - CameraUp.xyz * reach))));
                    glowPath += nearby * (nextDistance - distance) * 4.0;
                }
                if (firstHit < 0.0 && (previousDensity >= threshold || density >= threshold))
                {
                    // Interpolate the crossing to keep both shading and the camera probe
                    // stable as the ray sampling phase changes during movement.
                    firstHit = previousDensity >= threshold ? distance :
                        lerp(distance, nextDistance,
                             saturate((threshold - previousDensity) /
                                      max(density - previousDensity, 1e-6)));
                    usedSteps = i + 1;
                    if (Probe.x > 0.5) return PackFloat(firstHit);
                    if (style != 4) break;
                }
                previousDensity = density;
                distance = nextDistance;
            }
            if (Probe.x > 0.5) return PackFloat(-1.0);

            if (style == 4)
            {
                // This style uses the actual orbit density, unlike the DE-based modes.
                float opacity = 1.0 - exp(-integratedDensity * max(strength, 1e-3) * 8.0);
                float3 tint = SamplePalette(opacity * ShapeC.y + ShapeC.z, 0);
                return float4(LinearToSrgb(lerp(sky, tint, opacity)), 1.0);
            }

            float glow = style == 3 ? saturate(glowPath * 24.0 * strength) : 0.0;
            float3 glowTint = style == 3 ? SamplePalette(glow * ShapeC.y + ShapeC.z, 0) : 0.0;
            if (firstHit < 0.0)
                return float4(LinearToSrgb(sky + glowTint * glow), 1.0);

            float3 surfacePoint = origin + direction * firstHit;
            float3 normal = EstimateNormal(surfacePoint, cell, direction);
            float3 lightDirection = normalize(Light.xyz);
            float3 lightTint = LightColor.rgb;
            float diffuse = saturate(dot(normal, lightDirection));
            float shadow = Flags.y > 0.0 && diffuse > 0.0
                ? ShadowVisibility(surfacePoint + normal * cell * 2.0, lightDirection, cell)
                : 1.0;
            float rawOcclusion = ((int)Flags.x == 8 || Flags.z > 0.5)
                ? LocalOcclusion(surfacePoint, normal, cell) : 1.0;
            float occlusion = Flags.z > 0.5
                ? lerp(1.0, rawOcclusion, saturate(Surface.a)) : 1.0;
            float3 ambientTint = lerp(float3(1.0, 1.0, 1.0), SkyAt(normal) * 3.0,
                                       saturate(Style.z));
            float3 ambient = Flags.w * ambientTint;
            float3 albedo = SurfaceAlbedo(normal, surfacePoint, direction,
                firstHit - entry, rawOcclusion, saturate((float)usedSteps / 1536.0));
            float3 halfVector = normalize(lightDirection - direction);
            float specular = Light.w * pow(saturate(dot(normal, halfVector)), 32.0) * shadow;

            float3 color;
            if (style == 1)
            {
                float wrapped = saturate(dot(normal, lightDirection) * 0.5 + 0.5);
                color = albedo * (ambient * 0.8 + wrapped * shadow * lightTint) *
                    occlusion * occlusion;
            }
            else if (style == 2)
            {
                float3 reflected = reflect(direction, normal);
                float fresnel = pow(1.0 - saturate(dot(normal, -direction)), 5.0);
                float sheen = saturate(dot(reflected, lightDirection));
                float3 environment = SkyAt(reflected) * 2.0 +
                    lightTint * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
                color = albedo * (ambient + diffuse * shadow * lightTint * 0.25) * occlusion * 0.6 +
                    albedo * environment * lerp(0.35, 1.0, fresnel) * shadow * strength;
            }
            else if (style == 5)
            {
                float2 screenNormal = float2(dot(normal, CameraRight.xyz), dot(normal, CameraUp.xyz));
                float key = saturate(dot(screenNormal, normalize(float2(-0.55, 0.62))) * 0.6 + 0.55);
                float fill = saturate(dot(screenNormal, normalize(float2(0.7, -0.3))) * 0.5 + 0.5) * 0.25;
                float rim = pow(saturate(length(screenNormal)), 6.0) * 0.55 * strength;
                color = albedo * (key + fill + Flags.w * 0.3) * occlusion + rim * lightTint;
            }
            else if (style == 6)
            {
                float levels = max(2.0, floor(2.0 + strength * 2.0));
                float lit = floor(saturate(diffuse * shadow) * levels) / max(levels - 1.0, 1.0);
                float edge = 1.0 - smoothstep(0.12, 0.42,
                    saturate(dot(normal, -direction)));
                color = albedo * (ambient + lit * lightTint) * occlusion * (1.0 - edge);
            }
            else if (style == 7)
            {
                float thickness = BackThickness(surfacePoint, normal, cell);
                float back = pow(saturate(dot(-normal, lightDirection)) * 0.6 + 0.4, 2.0);
                color = albedo * (ambient + diffuse * shadow * lightTint * 0.6) * occlusion +
                    albedo * back * thickness * strength * lightTint * 2.5 + specular * lightTint;
            }
            else
            {
                color = albedo * (ambient + diffuse * shadow * lightTint) * occlusion +
                    specular * lightTint;
            }
            if (style == 3) color += glowTint * glow * 1.2;
            return float4(LinearToSrgb(color), 1.0);
        }
        """;
}
