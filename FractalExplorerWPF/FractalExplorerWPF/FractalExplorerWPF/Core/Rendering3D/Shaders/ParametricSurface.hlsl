StructuredBuffer<float4> ParametricNodes : register(t2);
StructuredBuffer<float4> ParametricTriangles : register(t3);
StructuredBuffer<float4> ParametricVertices : register(t4);

bool ParameterBoxHit(float3 origin, float3 inverse, float3 lo, float3 hi, float limit)
{
    float3 a = (lo - origin) * inverse, b = (hi - origin) * inverse;
    float3 near = min(a, b), far = max(a, b);
    return max(max(near.x, near.y), max(near.z, 0)) <= min(min(far.x, far.y), min(far.z, limit));
}

bool TraceParametric(float3 origin, float3 ray, float limit, out float distance,
                     out int steps, out float4 trap, out float3 hitNormal)
{
    // Preserve the sign of near-parallel rays and avoid 0 * infinity on thin boxes.
    float3 inverse = lerp(-1, 1, step(0, ray)) / max(abs(ray), 1e-20);
    uint count, stride;
    ParametricNodes.GetDimensions(count, stride);
    distance = limit;
    steps = 0;
    trap = 0;
    hitNormal = 0;
    bool hit = false;
    int node = 0;
    [loop]
    while (node * 2 < (int)count)
    {
        float4 lo = ParametricNodes[node * 2], hi = ParametricNodes[node * 2 + 1];
        if (!ParameterBoxHit(origin, inverse, lo.xyz, hi.xyz, distance))
        {
            node = (int)lo.w;
            continue;
        }
        ++node;
        ++steps;
        if (hi.w < 0) continue;
        [loop]
        for (int index = (int)hi.w; ; ++index)
        {
            float4 indices = ParametricTriangles[index];
            int3 ids = (int3)indices.xyz * 2;
            float4 a = ParametricVertices[ids.x], b = ParametricVertices[ids.y], c = ParametricVertices[ids.z];
            float3 e1 = b.xyz - a.xyz, e2 = c.xyz - a.xyz;
            float3 extent = max(abs(e1), abs(e2));
            float scale = max(extent.x, max(extent.y, extent.z));
            e1 /= scale;
            e2 /= scale;
            float3 p = cross(ray, e2);
            float determinant = dot(e1, p);
            if (abs(determinant) > 1e-10)
            {
                float3 offset = (origin - a.xyz) / scale;
                float3 q = cross(offset, e1);
                float u = dot(offset, p) / determinant, v = dot(ray, q) / determinant;
                float t = dot(e2, q) / determinant * scale;
                if (u >= -1e-6 && v >= -1e-6 && u + v <= 1.000001 && t > 0 && t < distance)
                {
                    float w = 1 - u - v;
                    float4 na = ParametricVertices[ids.x + 1], nb = ParametricVertices[ids.y + 1], nc = ParametricVertices[ids.z + 1];
                    float3 n = w * na.xyz + u * nb.xyz + v * nc.xyz;
                    if (dot(n,n) < 1e-6) n = cross(e1, e2);
                    hitNormal = normalize(n);
                    trap.xy = float2(dot(float3(w,u,v), float3(a.w,b.w,c.w)), dot(float3(w,u,v), float3(na.w,nb.w,nc.w)));
                    distance = t;
                    hit = true;
                }
            }
            if (indices.w > 0) break;
        }
    }
    if (dot(hitNormal, ray) > 0) hitNormal = -hitNormal;
    return hit;
}

float ParametricGrid(float2 uv)
{
    float2 separation = abs(frac(uv * ShapeB.x + .5) - .5);
    return ShapeB.x > 0 ? 1 - smoothstep(.018, .055, min(separation.x, separation.y)) : 0;
}
