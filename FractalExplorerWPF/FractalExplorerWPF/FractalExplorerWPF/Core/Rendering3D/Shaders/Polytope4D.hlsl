StructuredBuffer<float4> PolytopeNodes : register(t2);
StructuredBuffer<float4> PolytopePrimitives : register(t3);

float PolytopeSegmentDistance(float3 p, float3 a, float3 b)
{
    float3 ab=b-a;
    return length(p-a-ab*saturate(dot(p-a,ab)/max(dot(ab,ab),1e-20)));
}

float PolytopeTriangleDistance(float3 p, float3 a, float3 b, float3 c)
{
    float3 ab=b-a, bc=c-b, ca=a-c;
    float3 ap=p-a, bp=p-b, cp=p-c;
    float3 n=cross(ab,ca);
    float signs=sign(dot(cross(ab,n),ap))+sign(dot(cross(bc,n),bp))+sign(dot(cross(ca,n),cp));
    if (signs < 2)
        return min(PolytopeSegmentDistance(p,a,b),min(PolytopeSegmentDistance(p,b,c),PolytopeSegmentDistance(p,c,a)));
    return abs(dot(n,ap))*rsqrt(max(dot(n,n),1e-20));
}

float PolytopeMap(float3 p, out float4 trap)
{
    trap=0;
    uint count,stride; PolytopeNodes.GetDimensions(count,stride);
    float best=1e10;
    int node=0;
    [loop] while (node*2 < (int)count)
    {
        float4 lo=PolytopeNodes[node*2], hi=PolytopeNodes[node*2+1];
        float3 q=abs(p-(lo.xyz+hi.xyz)*.5)-(hi.xyz-lo.xyz)*.5;
        float box=length(max(q,0))+min(max(q.x,max(q.y,q.z)),0);
        if (box>best) { node=(int)lo.w; continue; }
        if (hi.w>=0)
        {
            int id=(int)hi.w;
            float4 a=PolytopePrimitives[id*4], b=PolytopePrimitives[id*4+1], c=PolytopePrimitives[id*4+2];
            float d=b.w>.5 ? PolytopeTriangleDistance(p,a.xyz,b.xyz,c.xyz) : PolytopeSegmentDistance(p,a.xyz,b.xyz);
            d-=a.w;
            if (d<best)
            {
                best=d;
                trap=float4(c.w,0,PolytopePrimitives[id*4+3].x*max(March.w-1,1),0);
            }
        }
        node++;
    }
    return best;
}
