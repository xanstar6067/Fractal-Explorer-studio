using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class AbcFlowComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("abc-flow-integrate", Source, "Integrate", "cs_5_0");
    public const string Source = """
        cbuffer Parameters : register(b0) {
            float4 Coefficients; // A B C dt
            float4 Counts;       // seeds samples offset batch
        };
        RWStructuredBuffer<float4> States : register(u0); // position/log stretch and unit tangent
        RWStructuredBuffer<float4> Samples : register(u1); // position/FTLE and velocity/time
        float3 Velocity(float3 p) {
            return float3(Coefficients.x*sin(p.z)+Coefficients.z*cos(p.y),
                Coefficients.y*sin(p.x)+Coefficients.x*cos(p.z),
                Coefficients.z*sin(p.y)+Coefficients.y*cos(p.x));
        }
        float3 Variation(float3 p,float3 q) {
            return float3(-Coefficients.z*sin(p.y)*q.y+Coefficients.x*cos(p.z)*q.z,
                Coefficients.y*cos(p.x)*q.x-Coefficients.x*sin(p.z)*q.z,
                -Coefficients.y*sin(p.x)*q.x+Coefficients.z*cos(p.y)*q.y);
        }
        float3 Wrap(float3 p) { return p-6.28318530718*floor((p+3.14159265359)/6.28318530718); }
        [numthreads(64,1,1)]
        void Integrate(uint3 id : SV_DispatchThreadID) {
            uint seed=id.x; if(seed>=(uint)Counts.x) return;
            float4 state=States[seed*2]; float3 p=state.xyz, q=States[seed*2+1].xyz;
            float total=state.w, h=Coefficients.w;
            uint offset=(uint)Counts.z, count=(uint)Counts.w, stride=(uint)Counts.y+1;
            if(offset==0) {
                Samples[seed*stride*2]=float4(p,0);
                Samples[seed*stride*2+1]=float4(Velocity(p),0);
            }
            [loop] for(uint i=0;i<count;i++) {
                [unroll] for(uint sub=0;sub<4;sub++) {
                    float3 k1=Velocity(p), v1=Variation(p,q);
                    float3 k2=Velocity(p+h*.5*k1), v2=Variation(p+h*.5*k1,q+h*.5*v1);
                    float3 k3=Velocity(p+h*.5*k2), v3=Variation(p+h*.5*k2,q+h*.5*v2);
                    float3 k4=Velocity(p+h*k3), v4=Variation(p+h*k3,q+h*v3);
                    p=Wrap(p+h/6*(k1+2*k2+2*k3+k4));
                    q+=h/6*(v1+2*v2+2*v3+v4);
                    float stretch=max(length(q),1e-20); total+=log(stretch); q/=stretch;
                }
                uint index=(seed*stride+offset+i+1)*2;
                float time=(offset+i+1)*4*h;
                Samples[index]=float4(p,total/time);
                Samples[index+1]=float4(Velocity(p),time);
            }
            States[seed*2]=float4(p,total); States[seed*2+1]=float4(q,0);
        }
        """;
}
