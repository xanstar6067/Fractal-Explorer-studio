using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lenia3DComputeShader
{
    public const uint GroupX=16, GroupY=4, GroupZ=2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Prepare", "Evolve", "Finish", "Publish" }
        .Select(e => new ShaderCacheEntry("lenia3d-"+e,Source,e,"cs_5_0")).ToArray();
    public const string Source = """
        cbuffer Simulation : register(b0) { float4 Equation; float4 Grid; float4 Reserved; };
        StructuredBuffer<float> Field : register(t0);
        StructuredBuffer<float4> Spectrum : register(t1);
        StructuredBuffer<float4> Kernel : register(t2);
        RWStructuredBuffer<float> Next : register(u0);
        RWTexture3D<float> Display : register(u1);
        RWStructuredBuffer<float4> Work : register(u2);
        uint Index(uint3 p,uint n) { return (p.z*n+p.y)*n+p.x; }
        [numthreads(16,4,2)]
        void Prepare(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            Work[Index(p,n)]=float4(Field[Index(p,n)],0,0,0);
        }
        [numthreads(16,4,2)]
        void Evolve(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
            float2 a=Spectrum[i].xy,b=Kernel[i].xy;
            Work[i]=float4(a.x*b.x-a.y*b.y,a.x*b.y+a.y*b.x,0,0);
        }
        [numthreads(16,4,2)]
        void Finish(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
            float d=(Spectrum[i].x-Equation.y)/Equation.z;
            float q=max(0,1-d*d/9);
            float g=Equation.w<.5 ? 2*q*q*q*q-1 : 2*exp(-.5*d*d)-1;
            // In-place is safe: each voxel reads only its own old state; FFT has already finished.
            Next[i]=saturate(Next[i]+Equation.x*g);
        }
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID)
        {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            Display[p]=Field[Index(p,n)];
        }
        """;
}
