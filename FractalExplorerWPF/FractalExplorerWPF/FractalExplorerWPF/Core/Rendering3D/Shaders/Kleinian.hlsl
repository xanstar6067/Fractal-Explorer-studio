// Finite cylinder-ball approximation of a sphere reflection group's limit set.
// Nodes are a threaded preorder BVH; all distances refer to the actual balls.
StructuredBuffer<float4> KleinianNodes : register(t2);
StructuredBuffer<float4> KleinianSpheres : register(t3);

float KleinianMap(float3 p, out float4 trap)
{
    uint count, stride;
    KleinianNodes.GetDimensions(count, stride);
    float best = 1e20;
    trap = 0;
    int node = 0;
    [loop] while (node * 2 < (int)count)
    {
        float4 lo = KleinianNodes[node * 2], hi = KleinianNodes[node * 2 + 1];
        float3 q = abs(p - (lo.xyz + hi.xyz) * .5) - (hi.xyz - lo.xyz) * .5 - ShapeA.y;
        float bound = length(max(q, 0)) + min(max(q.x, max(q.y, q.z)), 0);
        if (bound > best) { node = (int)lo.w; continue; }
        if (hi.w >= 0)
        {
            int id = (int)hi.w;
            float4 ball = KleinianSpheres[id * 2];
            float distance = length(p - ball.xyz) - ball.w - ShapeA.y;
            if (distance < best) { best = distance; trap = KleinianSpheres[id * 2 + 1]; }
        }
        node++;
    }
    return best;
}

float2 KleinianRayBox(float3 origin, float3 reciprocal, float3 lo, float3 hi)
{
    float3 a = (lo - ShapeA.y - origin) * reciprocal;
    float3 b = (hi + ShapeA.y - origin) * reciprocal;
    float3 near = min(a, b), far = max(a, b);
    return float2(max(near.x, max(near.y, near.z)), min(far.x, min(far.y, far.z)));
}

float2 KleinianRayBall(float3 origin, float3 direction, float4 ball)
{
    float3 delta = origin - ball.xyz;
    float along = -dot(delta, direction);
    float3 perpendicular = delta + along * direction;
    float radius = ball.w + ShapeA.y;
    float discriminant = radius * radius - dot(perpendicular, perpendicular);
    if (discriminant < 0) return float2(1e20, -1e20);
    float root = sqrt(discriminant);
    return float2(along - root, along + root);
}

// For a camera inside a thickened covering, merge overlapping intervals until the
// union exits. Returning the first individual sphere exit would expose internal seams.
bool TraceKleinianExit(float3 origin, float3 direction, float3 reciprocal, float limit,
    out float distance, out int steps, out float4 trap)
{
    uint count, stride;
    KleinianNodes.GetDimensions(count, stride);
    distance = 0; steps = 0; trap = 0;
    float previous;
    [loop] do
    {
        previous = distance;
        int node = 0;
        [loop] while (node * 2 < (int)count)
        {
            steps++;
            float4 lo = KleinianNodes[node * 2], hi = KleinianNodes[node * 2 + 1];
            float2 bound = KleinianRayBox(origin, reciprocal, lo.xyz, hi.xyz);
            if (bound.x > distance + 1e-7 || bound.y <= distance) { node = (int)lo.w; continue; }
            if (hi.w >= 0)
            {
                int id = (int)hi.w;
                float2 interval = KleinianRayBall(origin, direction, KleinianSpheres[id * 2]);
                if (interval.x <= distance + 1e-7 && interval.y > distance)
                {
                    distance = interval.y;
                    trap = KleinianSpheres[id * 2 + 1];
                }
            }
            node++;
        }
    } while (distance > previous && distance < limit);
    return distance > 0 && distance < limit;
}

bool TraceKleinian(float3 origin, float3 direction, float limit,
    out float distance, out int steps, out float4 trap)
{
    uint count, stride;
    KleinianNodes.GetDimensions(count, stride);
    distance = limit; steps = 0; trap = 0;
    bool hit = false;
    // A finite reciprocal keeps slab tests well-defined for axis-aligned rays.
    float3 reciprocal = float3(direction.x >= 0 ? 1 : -1, direction.y >= 0 ? 1 : -1, direction.z >= 0 ? 1 : -1)
        / max(abs(direction), 1e-20);
    float4 originTrap;
    if (KleinianMap(origin, originTrap) < 0)
        return TraceKleinianExit(origin, direction, reciprocal, limit, distance, steps, trap);
    int node = 0;
    [loop] while (node * 2 < (int)count)
    {
        steps++;
        float4 lo = KleinianNodes[node * 2], hi = KleinianNodes[node * 2 + 1];
        float2 bound = KleinianRayBox(origin, reciprocal, lo.xyz, hi.xyz);
        float entry = max(0, bound.x);
        float exit = min(distance, bound.y);
        if (entry > exit) { node = (int)lo.w; continue; }
        if (hi.w >= 0)
        {
            int id = (int)hi.w;
            float2 interval = KleinianRayBall(origin, direction, KleinianSpheres[id * 2]);
            float t = interval.x;
            if (t > 0 && t < distance)
            {
                distance = t; hit = true; trap = KleinianSpheres[id * 2 + 1];
            }
        }
        node++;
    }
    return hit;
}
