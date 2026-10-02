namespace FractalExplorerWPF.Core.Rendering3D;

internal static class LSystem3DShader
{
    public const string Source = """
        StructuredBuffer<float4> LNodes : register(t2);
        StructuredBuffer<float4> LCapsules : register(t3);

        bool LCapsule(int id, out float3 a, out float3 b, out float r, out float4 color)
        {
            float4 first = LCapsules[id * 3];
            float4 last = LCapsules[id * 3 + 1];
            float amount = saturate(ShapeA.x * ShapeA.z - last.w);
            a = first.xyz; b = lerp(a, last.xyz, amount); r = first.w;
            float3 values = LCapsules[id * 3 + 2].xyz;
            float value = ShapeA.y < .5 ? values.x : ShapeA.y < 1.5 ? values.y : values.z;
            color = float4(value, value, value * max(March.w - 1.0, 1.0), value);
            return amount > 0;
        }

        float LBoxDistance(float3 p, float3 lo, float3 hi)
        {
            float3 q = abs(p - (lo + hi) * .5) - (hi - lo) * .5;
            return length(max(q, 0)) + min(max(q.x, max(q.y, q.z)), 0);
        }

        float LMap(float3 p, out float4 trap)
        {
            uint count, stride; LNodes.GetDimensions(count, stride);
            float best = 1e20; trap = 0;
            int node = 0;
            [loop] while (node * 2 < (int)count)
            {
                float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];
                if (LBoxDistance(p, lo.xyz, hi.xyz) > best) { node = (int)lo.w; continue; }
                if (hi.w >= 0)
                {
                    float3 a, b; float r; float4 color;
                    if (LCapsule((int)hi.w, a, b, r, color))
                    {
                        float3 ba = b - a, pa = p - a;
                        float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-20));
                        float d = length(pa - ba * h) - r;
                        if (d < best) { best = d; trap = color; }
                    }
                }
                node++;
            }
            return best;
        }

        bool LRayBox(float3 o, float3 d, float3 lo, float3 hi, float limit)
        {
            float enter = 0, leave = limit;
            [unroll] for (int k = 0; k < 3; k++)
            {
                if (abs(d[k]) < 1e-12) { if (o[k] < lo[k] || o[k] > hi[k]) return false; }
                else
                {
                    float t0 = (lo[k] - o[k]) / d[k], t1 = (hi[k] - o[k]) / d[k];
                    enter = max(enter, min(t0, t1)); leave = min(leave, max(t0, t1));
                }
            }
            return leave >= enter;
        }

        // Cylinder plus two hemispheres, including exit intersections when the camera is inside.
        float LRayCapsule(float3 o, float3 d, float3 a, float3 b, float r)
        {
            float3 ba = b - a, oa = o - a;
            float baba = dot(ba, ba), bard = dot(ba, d), baoa = dot(ba, oa);
            float best = 1e20;
            float aa = baba - bard * bard;
            float bb = baba * dot(oa, d) - baoa * bard;
            float cc = baba * (dot(oa, oa) - r * r) - baoa * baoa;
            float disc = bb * bb - aa * cc;
            if (aa > 1e-12 && disc >= 0)
            {
                float root = sqrt(disc);
                [unroll] for (int i = 0; i < 2; i++)
                {
                    float t = (-bb + (i == 0 ? -root : root)) / aa;
                    float y = baoa + t * bard;
                    if (t > 1e-7 && y >= 0 && y <= baba) best = min(best, t);
                }
            }
            [unroll] for (int cap = 0; cap < 2; cap++)
            {
                float3 oc = o - (cap == 0 ? a : b);
                float q = dot(oc, d), h = q * q - dot(oc, oc) + r * r;
                if (h >= 0)
                {
                    [unroll] for (int i = 0; i < 2; i++)
                    {
                        float t = -q + (i == 0 ? -sqrt(h) : sqrt(h));
                        float y = baoa + t * bard;
                        if (t > 1e-7 && (cap == 0 ? y <= 0 : y >= baba)) best = min(best, t);
                    }
                }
            }
            return best;
        }

        bool TraceLSystem(float3 o, float3 d, float limit, out float distance, out int steps, out float4 trap)
        {
            uint count, stride; LNodes.GetDimensions(count, stride);
            distance = limit; steps = 0; trap = 0; bool hit = false;
            int node = 0;
            [loop] while (node * 2 < (int)count)
            {
                float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];
                steps++;
                if (!LRayBox(o, d, lo.xyz, hi.xyz, distance)) { node = (int)lo.w; continue; }
                if (hi.w >= 0)
                {
                    float3 a, b; float r; float4 color;
                    if (LCapsule((int)hi.w, a, b, r, color))
                    {
                        float t = LRayCapsule(o, d, a, b, r);
                        if (t < distance) { distance = t; trap = color; hit = true; }
                    }
                }
                node++;
            }
            return hit;
        }
        """;
}
