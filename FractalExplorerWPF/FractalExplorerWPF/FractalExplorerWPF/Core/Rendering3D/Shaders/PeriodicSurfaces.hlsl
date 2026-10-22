float PeriodicFamily(float3 s, float3 c, int family)
{
    if (family == 1) return c.x + c.y + c.z;
    if (family == 2) return c.x*c.y*c.z - s.x*s.y*s.z;
    return s.x*c.y + s.y*c.z + s.z*c.x;
}

float PeriodicField(float3 p)
{
    float3 q = p * (6.28318530718 / (ShapeB.w * ShapeB.xyz));
    float3 s = sin(q), c = cos(q);
    int packed = (int)ShapeA.x;
    float first = PeriodicFamily(s, c, packed & 3);
    if (ShapeC.w <= 0.0) return first;
    float second = PeriodicFamily(s, c, (packed >> 2) & 3);
    if (ShapeC.w >= 1.0) return second;
    return lerp(first, second, ShapeC.w);
}

float PeriodicMap(float3 p, out float4 trap)
{
    float f = PeriodicField(p) - ShapeA.y;
    float3 frequency = 6.28318530718 / (ShapeB.w * ShapeB.xyz);
    // Global gradient bound: |∂F/∂qᵢ| ≤ 2 for G, ≤ 1 for P and D.
    // Dividing by this bound gives a conservative step even at critical points;
    // a local |F|/|gradient| estimate could skip an entire wall there.
    int packed = (int)ShapeA.x;
    float factor = lerp((packed & 3) == 0 ? 2.0 : 1.0,
        ((packed >> 2) & 3) == 0 ? 2.0 : 1.0, ShapeC.w);
    float lipschitz = length(frequency) * factor;
    int region = (int)BoxInversion.z;
    int axis = packed >> 4;
    float halfWidth = ShapeA.z * .5;
    if ((region == 0 || region == 4) && axis > 0)
    {
        halfWidth *= 1.0 + BoxInversion.w * clamp(p[axis - 1], -1.0, 1.0);
        // The spatially varying width adds its own gradient to the bound.
        lipschitz += ShapeA.z * .5 * abs(BoxInversion.w);
    }
    float fieldDistance = region == 0 ? abs(f) - halfWidth
        : region == 1 ? abs(f) : region == 2 ? -f : region == 3 ? f : halfWidth - abs(f);
    float3 box = abs(p) - 1.0;
    float boundary = length(max(box, 0.0)) + min(max(box.x, max(box.y, box.z)), 0.0);
    int cut = (int)BoxInversion.x;
    if (cut > 0) boundary = max(boundary, p[cut - 1] - BoxInversion.y);
    trap = float4(f >= 0.0 ? .8 : .2, min(abs(p.x), min(abs(p.y), abs(p.z))), 0, abs(f));
    return max(fieldDistance / lipschitz, boundary);
}
