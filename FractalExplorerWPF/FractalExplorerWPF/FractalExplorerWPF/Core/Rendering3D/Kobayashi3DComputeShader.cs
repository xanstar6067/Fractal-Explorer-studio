using FractalExplorerWPF.Infrastructure;
namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Kobayashi3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Flux", "Evolve", "Inject", "Publish" }
        .Select(e => new ShaderCacheEntry("kobayashi3d-" + e, Source, e, "cs_5_0")).ToArray();
    public const string Source = """
        cbuffer Simulation : register(b0) { float4 Equation; float4 Thermal; float4 Grid; float4 Brush; };
        StructuredBuffer<float2> Current : register(t0);
        StructuredBuffer<float4> FaceFlux : register(t1);
        // Flux and Evolve bind different structured UAV strides to the same register.
        RWStructuredBuffer<float4> FluxOutput : register(u0);
        RWStructuredBuffer<float2> Next : register(u0);
        RWTexture3D<float2> Display : register(u1);
        uint Index(int3 p) { uint n = (uint)Grid.x; return (p.z * n + p.y) * n + p.x; }
        float2 Value(int3 p) { return Current[Index(clamp(p, 0, (int)Grid.x - 1))]; }
        // d[epsilon(g)^2 |g|^2 / 2]/dg. Includes the orientation derivative, not just epsilon² Δφ.
        float3 EnergyFlux(float3 g)
        {
            float r2 = dot(g,g); if (r2 < 1e-18) return 0;
            // Normalize before taking fourth powers: r^6 underflows near a diffuse interface tail.
            float3 n2 = g*g/r2; float sum4=dot(n2,n2);
            float a=1-3*Equation.y+4*Equation.y*sum4;
            return Equation.x*Equation.x*(a*a*g+16*Equation.y*a*g*(n2-sum4));
        }
        [numthreads(16,4,2)]
        void Flux(uint3 id : SV_DispatchThreadID)
        {
            int n = (int)Grid.x; if (any(id >= (uint)n)) return; int3 p = id;
            const int3 ex=int3(1,0,0), ey=int3(0,1,0), ez=int3(0,0,1);
            float center=Value(p).x;
            float3 gx=float3(Value(p+ex).x-center,
                .25*(Value(p+ey).x-Value(p-ey).x+Value(p+ex+ey).x-Value(p+ex-ey).x),
                .25*(Value(p+ez).x-Value(p-ez).x+Value(p+ex+ez).x-Value(p+ex-ez).x));
            float3 gy=float3(.25*(Value(p+ex).x-Value(p-ex).x+Value(p+ey+ex).x-Value(p+ey-ex).x),
                Value(p+ey).x-center,
                .25*(Value(p+ez).x-Value(p-ez).x+Value(p+ey+ez).x-Value(p+ey-ez).x));
            float3 gz=float3(.25*(Value(p+ex).x-Value(p-ex).x+Value(p+ez+ex).x-Value(p+ez-ex).x),
                .25*(Value(p+ey).x-Value(p-ey).x+Value(p+ez+ey).x-Value(p+ez-ey).x),Value(p+ez).x-center);
            FluxOutput[Index(p)]=float4(p.x==n-1?0:EnergyFlux(gx).x,
                p.y==n-1?0:EnergyFlux(gy).y,p.z==n-1?0:EnergyFlux(gz).z,0);
        }
        uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
        [numthreads(16,4,2)]
        void Evolve(uint3 id : SV_DispatchThreadID)
        {
            int n = (int)Grid.x; if (any(id >= (uint)n)) return; int3 p = id; uint i=Index(p);
            float2 v=Current[i]; float div=0, lap=0;
            const int3 ex=int3(1,0,0),ey=int3(0,1,0),ez=int3(0,0,1);
            float3 f=FaceFlux[i].xyz;
            div=f.x+f.y+f.z-(p.x==0?0:FaceFlux[Index(p-ex)].x)
                -(p.y==0?0:FaceFlux[Index(p-ey)].y)-(p.z==0?0:FaceFlux[Index(p-ez)].z);
            lap=Value(p+ex).y+Value(p-ex).y+Value(p+ey).y+Value(p-ey).y+Value(p+ez).y+Value(p-ez).y-6*v.y;
            float noise = (Hash(i ^ asuint(Thermal.w) ^ Hash(asuint(Grid.y)) ^ Hash(asuint(Grid.z))) / 4294967295.0 - .5)*2;
            float m = .9/3.14159265359*atan(-10*v.y);
            float phi = saturate(v.x + Thermal.y*Equation.z*(div + v.x*(1-v.x)*(v.x-.5+m+Thermal.z*noise)));
            // Use the actual phase increment (including roundoff/projection) for exact latent-heat balance.
            Next[i] = float2(phi, v.y + Thermal.y*Equation.w*lap + Thermal.x*(phi-v.x));
        }
        [numthreads(16,4,2)]
        void Inject(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if (any(p>=n)) return; uint i=Index(p); float2 v=Current[i];
            float3 d=(float3(p)+.5)/n-Brush.xyz;
            if (dot(d,d)<=Brush.w*Brush.w) { v.y += Thermal.x*(1-v.x); v.x=1; }
            Next[i]=v;
        }
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if (any(p>=n)) return;
            Display[p]=Current[Index(p)];
        }
        """;
}
