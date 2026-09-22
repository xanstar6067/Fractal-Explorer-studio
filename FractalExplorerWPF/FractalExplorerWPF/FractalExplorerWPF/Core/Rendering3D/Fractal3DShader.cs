using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходник пиксельного шейдера трассировки лучей по дистанционной оценке (distance estimation).
/// Вид фрактала подставляется препроцессором: для каждого <see cref="Fractal3DKind"/> компилируется
/// свой шейдер, поэтому в горячем цикле нет ветвления по режиму.
/// </summary>
internal static class Fractal3DShader
{
    public static string Build(Fractal3DKind kind) =>
        $"#define FRACTAL_KIND {(int)kind}\n" + Source;

    private const string Source = """
        struct PSInput
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        cbuffer FrameConstants : register(b0)
        {
            float4 Resolution;        // xy — размер полного кадра, zw — смещение текущей полосы
            float4 CameraPosition;    // xyz — положение камеры, w — масштаб поля зрения
            float4 CameraRight;
            float4 CameraUp;
            float4 CameraForward;
            float4 March;             // x — шаги, y — детализация, z — дальность, w — итерации
            float4 ShapeA;            // x — степень/масштаб, y — мин. радиус², z — предел свёртки, w — радиус вылета
            float4 ShapeB;            // константа C
            float4 ShapeC;            // x — срез w, y — масштаб окраски, z — сдвиг окраски
            float4 Light;             // xyz — направление на источник, w — сила бликов
            float4 Surface;           // rgb — цвет материала, a — сила затенения
            float4 ColorA;
            float4 ColorB;
            float4 BackgroundTop;
            float4 BackgroundBottom;
            float4 Flags;             // x — режим окраски, y — жёсткость теней (0 — выкл), z — затенение, w — фоновый свет
        };

        PSInput VSMain(uint id : SV_VertexID)
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            PSInput output;
            output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            output.uv = uv;
            return output;
        }

        float BoxDistance(float3 p, float3 b)
        {
            float3 q = abs(p) - b;
            return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
        }

        float3 RepeatSpace(float3 v, float period)
        {
            return v - period * floor(v / period);
        }

        float4 QuaternionSquare(float4 a)
        {
            return float4(a.x * a.x - dot(a.yzw, a.yzw), 2.0 * a.x * a.yzw);
        }

        // Дистанционная оценка до поверхности фрактала. trap — минимум орбиты, он же источник
        // цвета в режиме орбитальной ловушки.
        float Map(float3 p, out float trap)
        {
            int iterations = (int)March.w;

        #if FRACTAL_KIND == 2

            float scale = ShapeA.x;
            float minRadius2 = ShapeA.y;
            float foldingLimit = ShapeA.z;
            float bailout = ShapeA.w;
            const float fixedRadius2 = 1.0;

            float3 z = p;
            float dr = 1.0;
            trap = dot(z, z);
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                z = clamp(z, -foldingLimit, foldingLimit) * 2.0 - z;
                float r2 = dot(z, z);
                trap = min(trap, r2);
                if (r2 < minRadius2)
                {
                    float factor = fixedRadius2 / max(minRadius2, 1e-6);
                    z *= factor;
                    dr *= factor;
                }
                else if (r2 < fixedRadius2)
                {
                    float factor = fixedRadius2 / r2;
                    z *= factor;
                    dr *= factor;
                }
                z = scale * z + p;
                dr = dr * abs(scale) + 1.0;
                if (dot(z, z) > bailout) break;
            }
            return length(z) / max(abs(dr), 1e-9);

        #elif FRACTAL_KIND == 3

            float d = BoxDistance(p, float3(1.0, 1.0, 1.0));
            trap = 3.0;
            float s = 1.0;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                float3 a = RepeatSpace(p * s, 2.0) - 1.0;
                s *= 3.0;
                float3 r = abs(1.0 - 3.0 * abs(a));
                float da = max(r.x, r.y);
                float db = max(r.y, r.z);
                float dc = max(r.z, r.x);
                float c = (min(da, min(db, dc)) - 1.0) / s;
                trap = min(trap, dot(a, a));
                d = max(d, c);
            }
            return d;

        #elif FRACTAL_KIND == 4

            float scale = ShapeA.x;
            const float3 a1 = float3(1.0, 1.0, 1.0);
            const float3 a2 = float3(-1.0, -1.0, 1.0);
            const float3 a3 = float3(1.0, -1.0, -1.0);
            const float3 a4 = float3(-1.0, 1.0, -1.0);

            float3 z = p;
            trap = dot(z, z);
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                float3 c = a1;
                float nearest = length(z - a1);
                float d = length(z - a2);
                if (d < nearest) { c = a2; nearest = d; }
                d = length(z - a3);
                if (d < nearest) { c = a3; nearest = d; }
                d = length(z - a4);
                if (d < nearest) { c = a4; nearest = d; }
                z = scale * z - c * (scale - 1.0);
                trap = min(trap, dot(z, z));
            }
            return length(z) * pow(max(abs(scale), 1.0001), -float(iterations));

        #elif FRACTAL_KIND == 5

            float bailout = ShapeA.w;
            float4 z = float4(p, ShapeC.x);
            float4 c = ShapeB;
            float md2 = 1.0;
            float mz2 = dot(z, z);
            trap = mz2;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                md2 *= 4.0 * mz2;
                z = QuaternionSquare(z) + c;
                mz2 = dot(z, z);
                trap = min(trap, mz2);
                if (mz2 > bailout) break;
            }
            return 0.25 * sqrt(mz2 / max(md2, 1e-12)) * log(max(mz2, 1.000001));

        #else

            float power = ShapeA.x;
            float bailout = ShapeA.w;
        #if FRACTAL_KIND == 1
            float3 c = ShapeB.xyz;
        #else
            float3 c = p;
        #endif

            float3 z = p;
            float dr = 1.0;
            float r = length(z);
            trap = r;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                r = length(z);
                trap = min(trap, r);
                if (r > bailout) break;

                float invR = 1.0 / max(r, 1e-12);
                float theta = acos(clamp(z.z * invR, -1.0, 1.0));
                float phi = atan2(z.y, z.x);
                dr = pow(r, power - 1.0) * power * dr + 1.0;

                float zr = pow(r, power);
                theta *= power;
                phi *= power;
                float sinTheta = sin(theta);
                z = zr * float3(sinTheta * cos(phi), sinTheta * sin(phi), cos(theta)) + c;
            }
            r = length(z);
            return 0.5 * log(max(r, 1.000001)) * r / max(dr, 1e-9);

        #endif
        }

        float3 EstimateNormal(float3 p, float epsilon)
        {
            float2 k = float2(1.0, -1.0);
            float trap;
            return normalize(
                k.xyy * Map(p + k.xyy * epsilon, trap) +
                k.yyx * Map(p + k.yyx * epsilon, trap) +
                k.yxy * Map(p + k.yxy * epsilon, trap) +
                k.xxx * Map(p + k.xxx * epsilon, trap));
        }

        float SoftShadow(float3 origin, float3 direction, float minDistance, float maxDistance, float sharpness)
        {
            float result = 1.0;
            float travelled = minDistance;
            float trap;
            [loop]
            for (int i = 0; i < 48; i++)
            {
                float stepDistance = Map(origin + direction * travelled, trap);
                result = min(result, sharpness * stepDistance / travelled);
                travelled += clamp(stepDistance, minDistance, 0.25);
                if (result < 0.004 || travelled > maxDistance) break;
            }
            return saturate(result);
        }

        float Occlusion(float3 p, float3 normal)
        {
            float occluded = 0.0;
            float weight = 1.0;
            float trap;
            [unroll]
            for (int i = 0; i < 5; i++)
            {
                float offset = 0.01 + 0.12 * float(i) / 4.0;
                occluded += (offset - Map(p + normal * offset, trap)) * weight;
                weight *= 0.75;
            }
            return saturate(1.0 - 3.0 * occluded);
        }

        float3 BaseColor(float3 normal, float trap, float travelled)
        {
            int mode = (int)Flags.x;
            if (mode == 1) return abs(normal);
            if (mode == 2)
            {
                float t = frac(sqrt(max(trap, 0.0)) * ShapeC.y + ShapeC.z);
                return lerp(ColorA.rgb, ColorB.rgb, smoothstep(0.0, 1.0, 1.0 - abs(2.0 * t - 1.0)));
            }
            if (mode == 3)
            {
                float t = saturate(travelled / max(March.z, 1e-3) * 6.0 * ShapeC.y + ShapeC.z);
                return lerp(ColorA.rgb, ColorB.rgb, t);
            }
            return Surface.rgb;
        }

        float3 LinearToSrgb(float3 color)
        {
            color = saturate(color);
            float3 low = color * 12.92;
            float3 high = 1.055 * pow(max(color, 1e-8), 1.0 / 2.4) - 0.055;
            return lerp(low, high, step(0.0031308, color));
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float2 pixel = input.position.xy + Resolution.zw;
            // По вертикали кадр занимает ровно ±1, поэтому масштаб поля зрения 1/tg(FOV/2)
            // задаёт именно заданный угол обзора.
            float2 plane = (pixel - 0.5 * Resolution.xy) / (0.5 * Resolution.y);
            plane.y = -plane.y;

            float3 rayDirection = normalize(
                CameraForward.xyz * CameraPosition.w + CameraRight.xyz * plane.x + CameraUp.xyz * plane.y);
            float3 rayOrigin = CameraPosition.xyz;

            float pixelRadius = 2.0 * March.y / max(Resolution.y * CameraPosition.w, 1.0);
            int maxSteps = (int)March.x;
            float maxDistance = March.z;

            float travelled = 0.0;
            float trap = 0.0;
            float epsilon = 1e-6;
            bool hit = false;

            [loop]
            for (int i = 0; i < maxSteps; i++)
            {
                float stepTrap;
                float3 samplePoint = rayOrigin + rayDirection * travelled;
                float stepDistance = Map(samplePoint, stepTrap);
                epsilon = max(pixelRadius * travelled, 1e-7);
                if (stepDistance < epsilon)
                {
                    trap = stepTrap;
                    hit = true;
                    break;
                }
                travelled += stepDistance;
                if (travelled > maxDistance) break;
            }

            float3 sky = lerp(BackgroundBottom.rgb, BackgroundTop.rgb, saturate(rayDirection.y * 0.5 + 0.5));
            if (!hit) return float4(LinearToSrgb(sky), 1.0);

            float3 surfacePoint = rayOrigin + rayDirection * travelled;
            float3 normal = EstimateNormal(surfacePoint, max(epsilon, 1e-6));
            float3 lightDirection = Light.xyz;

            float diffuse = saturate(dot(normal, lightDirection));
            float shadow = 1.0;
            if (Flags.y > 0.0 && diffuse > 0.0)
            {
                shadow = SoftShadow(surfacePoint + normal * epsilon * 4.0, lightDirection,
                    max(epsilon * 8.0, 1e-5), maxDistance * 0.5, Flags.y);
            }

            float occlusion = 1.0;
            if (Flags.z > 0.5) occlusion = lerp(1.0, Occlusion(surfacePoint, normal), saturate(Surface.a));

            float3 albedo = BaseColor(normal, trap, travelled);
            float3 halfVector = normalize(lightDirection - rayDirection);
            float specular = Light.w * pow(saturate(dot(normal, halfVector)), 32.0) * shadow;

            float3 color = albedo * (Flags.w + diffuse * shadow) * occlusion + specular;
            color = lerp(color, sky, saturate(travelled / max(maxDistance, 1e-3)));
            return float4(LinearToSrgb(color), 1.0);
        }
        """;
}
