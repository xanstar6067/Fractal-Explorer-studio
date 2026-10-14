float PeriodicField(float3 p)
{
    float3 q = p * (6.28318530718 / (ShapeB.w * ShapeB.xyz));
    float3 s = sin(q), c = cos(q);
    int family = (int)ShapeA.x;
    if (family == 1) return c.x + c.y + c.z;
    if (family == 2) return c.x*c.y*c.z - s.x*s.y*s.z;
    return s.x*c.y + s.y*c.z + s.z*c.x;
}

float PeriodicMap(float3 p, out float4 trap)
{
    float f = PeriodicField(p) - ShapeA.y;
    float3 frequency = 6.28318530718 / (ShapeB.w * ShapeB.xyz);
    // Global gradient bound: |∂F/∂qᵢ| ≤ 2 for G, ≤ 1 for P and D.
    // Dividing by this bound gives a conservative step even at critical points;
    // a local |F|/|gradient| estimate could skip an entire wall there.
    float lipschitz = length(frequency) * (ShapeA.x < .5 ? 2.0 : 1.0);
    int region = (int)BoxInversion.z;
    float fieldDistance = region == 0 ? abs(f) - ShapeA.z*.5
        : region == 1 ? abs(f) : region == 2 ? -f : f;
    float3 box = abs(p) - 1.0;
    float boundary = length(max(box, 0.0)) + min(max(box.x, max(box.y, box.z)), 0.0);
    int cut = (int)BoxInversion.x;
    if (cut > 0) boundary = max(boundary, p[cut - 1] - BoxInversion.y);
    trap = float4(length(p), min(abs(p.x), min(abs(p.y), abs(p.z))), 0, abs(f));
    return max(fieldDistance / lipschitz, boundary);
}
