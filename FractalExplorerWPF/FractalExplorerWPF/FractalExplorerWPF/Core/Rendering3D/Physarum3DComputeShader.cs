using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>3D sensory/motor swarm inspired by Jones. Integer deposits make collisions order independent.</summary>
internal static class Physarum3DComputeShader
{
    public const int GroupX = 8, GroupY = 8, GroupZ = 4;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "MoveAgents", "Diffuse", "Publish" }.Select(e => new ShaderCacheEntry("physarum3d-"+e, Source, e, "cs_5_0")).ToArray();
    public const string Source = """
        cbuffer Parameters : register(b0) {
            float4 Grid; // side, count, step low 24 bits, seed
            float4 Sense; // distance, cone angle, turn angle, speed
            float4 Trail; // deposit, diffusion, evaporation, reserved
        };
        struct Agent { float4 position; float4 heading; };
        StructuredBuffer<float> Input : register(t0);
        RWStructuredBuffer<float> Output : register(u0);
        RWTexture3D<float> Display : register(u1);
        RWStructuredBuffer<Agent> Agents : register(u2);
        RWStructuredBuffer<uint> Deposits : register(u3);
        uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); }
        float Random(inout uint x) { x=Hash(x); return (x & 0x00ffffffu)/16777216.0; }
        uint Index(int3 p) { uint n=(uint)Grid.x; p=clamp(p,0,(int)n-1); return (p.z*n+p.y)*n+p.x; }
        float ReadTrail(float3 p) {
            int3 q=(int3)floor(p-.5); float3 t=frac(p-.5);
            float a=lerp(lerp(Input[Index(q)],Input[Index(q+int3(1,0,0))],t.x),
                         lerp(Input[Index(q+int3(0,1,0))],Input[Index(q+int3(1,1,0))],t.x),t.y);
            float b=lerp(lerp(Input[Index(q+int3(0,0,1))],Input[Index(q+int3(1,0,1))],t.x),
                         lerp(Input[Index(q+int3(0,1,1))],Input[Index(q+int3(1,1,1))],t.x),t.y);
            return lerp(a,b,t.z);
        }
        // Crowded channels cease to attract. This disperses clumps without an ordering-dependent occupancy map.
        float Signal(float3 p) { float v=ReadTrail(p); return v/(1+v*v*.001); }
        [numthreads(128,1,1)]
        void MoveAgents(uint3 id : SV_DispatchThreadID) {
            uint i=id.x; if(i>=(uint)Grid.y) return;
            Agent agent=Agents[i]; float3 p=agent.position.xyz, d=normalize(agent.heading.xyz);
            uint rng=Hash(i ^ Hash((uint)Grid.z) ^ asuint(Grid.w));
            float3 up=abs(d.y)<.9 ? float3(0,1,0) : float3(1,0,0);
            float3 right=normalize(cross(d,up)); up=cross(right,d);
            float offset=Random(rng)*6.2831853;
            float forward=Signal(p+d*Sense.x), best=forward; float3 chosen=d;
            [unroll] for(int k=0;k<8;k++) {
                float a=offset+k*.78539816;
                float3 candidate=d*cos(Sense.y)+(right*cos(a)+up*sin(a))*sin(Sense.y);
                float value=Signal(p+candidate*Sense.x);
                if(value>best+1e-5) {best=value; chosen=normalize(d*cos(Sense.z)+(right*cos(a)+up*sin(a))*sin(Sense.z));}
            }
            // Local exploration keeps a homogeneous trail from trapping particles in straight rays.
            if(best<=forward+1e-5 && Random(rng)<.12)
                chosen=normalize(d*cos(Sense.z)+(right*cos(offset)+up*sin(offset))*sin(Sense.z));
            float3 next=p+chosen*Sense.w;
            float3 radial=next-Grid.x*.5; float radius=Grid.x*.44;
            if(dot(radial,radial)>radius*radius) {
                float3 normal=normalize(radial); chosen=normalize(reflect(chosen,normal));
                next=Grid.x*.5+normal*(radius-.1)+chosen*Sense.w;
            }
            next=clamp(next, .5, Grid.x-.5);
            agent.position.xyz=next; agent.heading.xyz=chosen; Agents[i]=agent;
            // Maximum count*deposit*256 = 536870912: no uint overflow even in one voxel.
            InterlockedAdd(Deposits[Index((int3)next)],(uint)round(Trail.x*256));
        }
        [numthreads(8,8,4)]
        void Diffuse(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint i=Index(p); float value=Input[i]; int3 q=p;
            float mean=(Input[Index(q+int3(1,0,0))]+Input[Index(q-int3(1,0,0))]+
                        Input[Index(q+int3(0,1,0))]+Input[Index(q-int3(0,1,0))]+
                        Input[Index(q+int3(0,0,1))]+Input[Index(q-int3(0,0,1))])/6;
            Output[i]=min(1000,(lerp(value,mean,Trail.y)+Deposits[i]/256.0)*(1-Trail.z));
            Deposits[i]=0;
        }
        [numthreads(8,8,4)]
        void Publish(uint3 p : SV_DispatchThreadID) {
            if(any(p>=(uint)Grid.x)) return;
            Display[p]=Input[Index(p)];
        }
        """;
}
