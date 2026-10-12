namespace FractalExplorerWPF.Core.Rendering3D;

internal static class AbcFlowShader
{
    // Reuse the exact capsule intersections and threaded BVH, including probes and reflections.
    public static string Source
    {
        get
        {
            string source = LSystem3DShader.Source;
            int first = source.IndexOf("bool LCapsule", StringComparison.Ordinal);
            int last = source.IndexOf("float LBoxDistance", StringComparison.Ordinal);
            string result = "StructuredBuffer<float4> AbcSamples : register(t4);\n" + source[..first] + Capsule + source[last..];
            return result.Replace("float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];", """
                float4 lo = LNodes[node * 2], hi = LNodes[node * 2 + 1];
                if((hi.w == -2 && ShapeA.y < .5) || (hi.w == -3 && ShapeA.z < .5)) {
                    node=(int)lo.w; continue;
                }
                """);
        }
    }
    private const string Capsule = """
        bool LCapsule(int id, out float3 a, out float3 b, out float r, out float4 color)
        {
            float4 first=LCapsules[id*4], last=LCapsules[id*4+1];
            float4 values=LCapsules[id*4+2], times=LCapsules[id*4+3];
            a=first.xyz; b=last.xyz; r=first.w;
            color=float4(values.x, values.y/3, values.z*max(March.w-1,1), exp(2*times.y)-1);
            if(times.z>1.5) {
                float phase=min(ShapeA.x,.999999);
                if(ShapeA.z<.5) return false;
                uint seed=(uint)times.y, n=(uint)ShapeB.x;
                float sample=phase*n; uint step=(uint)sample;
                uint index=(seed*(n+1)+step)*2;
                float4 p=AbcSamples[index], q=AbcSamples[index+2];
                float3 delta=q.xyz-p.xyz;
                delta-=6.28318530718*floor((delta+3.14159265359)/6.28318530718);
                a=p.xyz+frac(sample)*delta;
                a-=6.28318530718*floor((a+3.14159265359)/6.28318530718);
                a/=3.14159265359; b=a; r*=3;
                float3 velocity=AbcSamples[index+1].xyz;
                color=float4(saturate(.5+.5*lerp(p.w,q.w,frac(sample))/ShapeB.z),
                    length(velocity)/max(ShapeB.w,1e-9)/3,
                    seed/max(ShapeB.y-1,1)*max(March.w-1,1),exp(2*phase)-1);
            }
            else if(times.z<.5 && ShapeA.y<.5) return false;
            return true;
        }

        """;
}
