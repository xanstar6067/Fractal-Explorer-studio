using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GrayScott3DComputeShader
{
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Evolve", "Inject" }
        .Select(entry => new ShaderCacheEntry("gray-scott3d-" + entry, Source, entry, "cs_5_0")).ToArray();
    public const string Source = """
        cbuffer Simulation : register(b0) { float4 Equation; float4 Grid; float4 Brush; };
        StructuredBuffer<float2> Current : register(t0);
        RWStructuredBuffer<float2> Next : register(u0);
        uint Index(uint3 p) { uint n = (uint)Grid.x; return (p.z * n + p.y) * n + p.x; }
        [numthreads(4,4,4)]
        void Evolve(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            uint3 m = (p + n - 1) % n, q = (p + 1) % n;
            uint i = Index(p); float2 uv = Current[i];
            float2 lap = Current[Index(uint3(m.x,p.y,p.z))] + Current[Index(uint3(q.x,p.y,p.z))] +
                Current[Index(uint3(p.x,m.y,p.z))] + Current[Index(uint3(p.x,q.y,p.z))] +
                Current[Index(uint3(p.x,p.y,m.z))] + Current[Index(uint3(p.x,p.y,q.z))] - 6.0 * uv;
            float reaction = uv.x * uv.y * uv.y;
            Next[i] = saturate(uv + float2(Equation.x * lap.x - reaction + Equation.z * (1.0 - uv.x),
                Equation.y * lap.y + reaction - (Equation.z + Equation.w) * uv.y));
        }
        [numthreads(4,4,4)]
        void Inject(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            float3 delta = abs((float3(p) + .5) / n - Brush.xyz); delta = min(delta, 1.0 - delta);
            if (dot(delta,delta) <= Brush.w * Brush.w) Next[Index(p)] = float2(.5,.3);
        }
        """;
}
