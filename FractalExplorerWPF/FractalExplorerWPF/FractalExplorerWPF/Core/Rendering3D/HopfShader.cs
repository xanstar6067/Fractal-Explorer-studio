namespace FractalExplorerWPF.Core.Rendering3D;

internal static class HopfShader
{
    public const string Source = """
        StructuredBuffer<float4> HopfNodes : register(t2);
        StructuredBuffer<float4> HopfRings : register(t3);

        float HopfMap(float3 p, out float4 trap)
        {
            trap = 0;
            if (ShapeA.y < .5) return 1e10;
            uint count, stride; HopfNodes.GetDimensions(count, stride);
            float best = 1e20;
            int node = 0;
            [loop] while (node * 2 < (int)count)
            {
                float4 lo = HopfNodes[node*2], hi = HopfNodes[node*2+1];
                float3 q = abs(p-(lo.xyz+hi.xyz)*.5)-(hi.xyz-lo.xyz)*.5;
                float box = length(max(q,0))+min(max(q.x,max(q.y,q.z)),0);
                if (box > best) { node=(int)lo.w; continue; }
                if (hi.w >= 0)
                {
                    int id=(int)hi.w;
                    float4 circle=HopfRings[id*3], plane=HopfRings[id*3+1], color=HopfRings[id*3+2];
                    float distance;
                    if (color.w > .5)
                        distance=length(p-plane.xyz*dot(p,plane.xyz))-plane.w;
                    else
                    {
                        float height=dot(p,plane.xyz);
                        float3 radial=p-plane.xyz*height-circle.xyz;
                        // |C|²-R²=-Scale². Rationalizing rho-R avoids catastrophic cancellation
                        // when a circle approaches the stereographic pole and its radius tends to infinity.
                        float delta=(dot(p,p)-height*height-2*dot(p,circle.xyz)-ShapeA.z)/(length(radial)+circle.w);
                        distance=length(float2(delta,height))-plane.w;
                    }
                    distance=max(distance,length(p)-ShapeA.x);
                    if(distance<best)
                    {
                        best=distance;
                        trap=float4(color.x,color.y,color.z*max(March.w-1,1),color.z);
                    }
                }
                node++;
            }
            return best;
        }
        """;
}
