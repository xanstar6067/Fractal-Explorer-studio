cbuffer HybridProgram : register(b1)
{
    float4 HybridInfo; // stage count, total primitive operations, reserved
    float4 HybridStages[32]; // four vectors per stage, up to eight stages
};

float3 HybridRotate(float3 z, float3 angles)
{
    float3 s = sin(angles), c = cos(angles);
    z.yz = float2(c.x * z.y - s.x * z.z, s.x * z.y + c.x * z.z);
    z.xz = float2(c.y * z.x + s.y * z.z, -s.y * z.x + c.y * z.z);
    z.xy = float2(c.z * z.x - s.z * z.y, s.z * z.x + c.z * z.y);
    return z;
}

float HybridMap(float3 p, out float4 trap)
{
    float3 z = p;
    float dr = 1, trapRadius2 = 1e20, trapAxis = 1e20, trapIndex = 0;
    int iterations = (int)March.w, nonlinear = 0;
    int stageCount = (int)HybridInfo.x, total = (int)HybridInfo.y;
    // There is at least one nonlinear operation per cycle (validated on the CPU).
    // Modifiers after the last nonlinear operation also belong to the displayed orbit.
    [loop] for (int primitive = 0; primitive < (iterations + 1) * total; primitive++)
    {
        int phase = primitive % total, index = 0;
        [loop] for (int j = 0; j < stageCount; j++)
        {
            int count = (int)HybridStages[j * 4].y;
            if (phase < count) { index = j; break; }
            phase -= count;
        }
        float4 a = HybridStages[index * 4], b = HybridStages[index * 4 + 1];
        float4 c = HybridStages[index * 4 + 2], d = HybridStages[index * 4 + 3];
        int kind = (int)a.x;
        if (kind < 2)
        {
            if (nonlinear >= iterations) break;
            float r = length(z);
            trapIndex = (float)nonlinear;
            trapRadius2 = min(trapRadius2, r * r);
            trapAxis = min(trapAxis, MinAxis(z));
            if (r > ShapeA.w) break;
            nonlinear++;
            float power = a.z;
            float theta = acos(clamp(z.z / max(r, 1e-12), -1, 1));
            float phi = atan2(z.y, z.x);
            float bulbGain = power * pow(max(r, 1e-12), power - 1);
            float zr = pow(r, power);
            theta *= power; phi *= power;
            float3 bulb = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
            float3 box = clamp(z, -b.y, b.y) * 2 - z;
            float foldGain;
            float factor = ShapeInversionFactorFor(box, b.x, float4(b.w, c.w, d.x, 0), foldGain);
            box *= a.w * factor;
            float weight = kind == 0 ? 1 - b.z : b.z;
            z = lerp(box, bulb, weight) + p;
            dr = dr * lerp(abs(a.w) * foldGain, bulbGain, weight) + 1;
        }
        else if (kind == 2) z = HybridRotate(z, c.xyz); // orthogonal: derivative norm unchanged
        else z += c.xyz; // translation: derivative norm unchanged
    }
    float radius = length(z);
    trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, radius);
    StepScale = HYBRID_STEP;
    return min(.5 * log(max(radius, 1.000001)), 1) * radius / max(dr, 1e-9);
}
