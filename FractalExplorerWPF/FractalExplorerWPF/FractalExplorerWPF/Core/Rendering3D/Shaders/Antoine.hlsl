StructuredBuffer<float4> AntoineNodes : register(t2);

float AntoineMap(float3 p, out float4 trap)
{
    uint count, stride;
    AntoineNodes.GetDimensions(count, stride);
    float best = 1e20;
    trap = 0;
    int node = 0;
    [loop] while (node * 3 < (int)count)
    {
        float4 circle = AntoineNodes[node * 3];
        float4 plane = AntoineNodes[node * 3 + 1];
        float4 info = AntoineNodes[node * 3 + 2];
        float3 q = p - circle.xyz;
        float h = dot(q, plane.xyz);
        float radial = length(q - h * plane.xyz) - circle.w;
        float d = length(float2(radial, h)) - plane.w;
        if (d > best) { node = (int)info.x; continue; }
        if (info.y > .5)
        {
            best = d;
            trap = float4(info.z, abs(plane.z) / 3, info.w * max(March.w - 1, 1), 0);
        }
        node++;
    }
    return best;
}
