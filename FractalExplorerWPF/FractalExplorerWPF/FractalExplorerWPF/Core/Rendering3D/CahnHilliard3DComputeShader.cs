using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class CahnHilliard3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Prepare", "Evolve", "Finish", "Publish" }
        .Select(e => new ShaderCacheEntry("cahn-hilliard3d-" + e, Source, e, "cs_5_0")).ToArray();
    public const string Source = """
        // Equation: dt*M, kappa, stabilization S, conserved mean. Grid.x = N; dx = 1.
        cbuffer Simulation : register(b0) { float4 Equation; float4 Grid; float4 Reserved; };
        StructuredBuffer<float> Field : register(t0);
        StructuredBuffer<float4> Spectrum : register(t1);
        RWStructuredBuffer<float> Next : register(u0);
        RWTexture3D<float> Display : register(u1);
        RWStructuredBuffer<float4> Work : register(u2);
        uint Index(uint3 p, uint n) { return (p.z*n+p.y)*n+p.x; }
        [numthreads(16,4,2)]
        void Prepare(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            uint i = Index(p,n); float c = Field[i];
            Work[i] = float4(c,0,c*c*c-c,0);
        }
        [numthreads(16,4,2)]
        void Evolve(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            uint i = Index(p,n);
            float3 k = 6.283185307179586 * float3(min(p,n-p)) / n;
            float q = dot(k,k), a = Equation.x*q;
            float4 v = Spectrum[i];
            // (c_new-c)/dt = M Δ(f(c) - κ Δ c_new + S(c_new-c)).
            float2 result = ((1+a*Equation.z)*v.xy-a*v.zw)/(1+a*(Equation.z+Equation.y*q));
            if (i == 0) result = float2(Equation.w*n*n*n,0);
            Work[i] = float4(result,0,0);
        }
        [numthreads(16,4,2)]
        void Finish(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            // No clipping: clipping would change the mass and the equation.
            uint i = Index(p,n); Next[i] = Spectrum[i].x;
        }
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            // Presentation normalization only. The exact signed field is kept separately.
            Display[p] = .5 + .5*Field[Index(p,n)];
        }
        """;
}
