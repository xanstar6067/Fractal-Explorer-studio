using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class SwiftHohenberg3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "Prepare", "Evolve", "Finish", "Publish", "Inject", "Filter" }
            .Select(e => new ShaderCacheEntry("swift-hohenberg3d-" + e, Source, e, "cs_5_0")).ToArray();

    public const string Source = """
        cbuffer Simulation : register(b0) {
            float4 Equation; // r, b, q0², dt
            float4 Grid;     // N, 2pi/(N*dx), brush target, reserved
            float4 Brush;
        };
        StructuredBuffer<float> Field : register(t0);
        StructuredBuffer<float4> Spectrum : register(t1);
        RWStructuredBuffer<float> Next : register(u0);
        RWTexture3D<float> Display : register(u1);
        RWStructuredBuffer<float4> Work : register(u2);
        uint Index(uint3 p,uint n) { return (p.z*n+p.y)*n+p.x; }
        bool Retained(uint3 p,uint n) { return all(min(p,n-p)<n/4); }
        [numthreads(16,4,2)]
        void Prepare(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint i=Index(p,n); float u=Field[i]; Work[i]=float4(u,0,Equation.y*u*u-u*u*u,0);
        }
        [numthreads(16,4,2)]
        void Evolve(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint i=Index(p,n); if(!Retained(p,n)) { Work[i]=0; return; }
            float3 k=Grid.y*float3(min(p,n-p)); float d=Equation.z-dot(k,k);
            float lambda=Equation.x-d*d, x=Equation.w*lambda, growth=exp(x);
            // ETD1. Series avoids catastrophic cancellation around the neutral wave number.
            float weight=abs(x)<.001 ? Equation.w*(1+x*(.5+x*(1.0/6+x/24))) : (growth-1)/lambda;
            Work[i]=float4(growth*Spectrum[i].xy+weight*Spectrum[i].zw,0,0);
        }
        [numthreads(16,4,2)]
        void Filter(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint i=Index(p,n); Work[i]=Retained(p,n) ? float4(Spectrum[i].xy,0,0) : 0;
        }
        [numthreads(16,4,2)]
        void Finish(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            Next[Index(p,n)]=Spectrum[Index(p,n)].x; // No amplitude clipping.
        }
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return; Display[p]=Field[Index(p,n)];
        }
        [numthreads(16,4,2)]
        void Inject(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            float3 delta=abs((float3(p)+.5)/n-Brush.xyz); delta=min(delta,1-delta);
            float radius=length(delta)/Brush.w; if(radius>=1) return;
            uint i=Index(p,n); Next[i]=lerp(Next[i],Grid.z,1-smoothstep(0,1,radius));
        }
        """;
}
