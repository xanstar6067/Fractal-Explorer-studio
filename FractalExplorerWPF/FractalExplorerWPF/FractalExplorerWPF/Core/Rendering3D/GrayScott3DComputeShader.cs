using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GrayScott3DComputeShader
{
    /// <summary>Thread group: a wide X row keeps neighbouring reads of a warp in one memory segment.</summary>
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;

    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Evolve", "Inject", "Publish" }
        .Select(entry => new ShaderCacheEntry("gray-scott3d-" + entry, Source, entry, "cs_5_0")).ToArray();

    public const string Source = """
        cbuffer Simulation : register(b0) { float4 Equation; float4 Grid; float4 Brush; };
        StructuredBuffer<float2> Current : register(t0);
        RWStructuredBuffer<float2> Next : register(u0);
        RWTexture3D<float> Display : register(u1);
        uint Index(uint3 p, uint n) { return (p.z * n + p.y) * n + p.x; }
        [numthreads(16,4,2)]
        void Evolve(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            // Periodic neighbours without integer division, which is slow on older GPUs.
            uint3 m = p == 0 ? n - 1 : p - 1, q = p == n - 1 ? 0 : p + 1;
            uint i = Index(p, n); float2 uv = Current[i];
            float2 lap = Current[Index(uint3(m.x,p.y,p.z), n)] + Current[Index(uint3(q.x,p.y,p.z), n)] +
                Current[Index(uint3(p.x,m.y,p.z), n)] + Current[Index(uint3(p.x,q.y,p.z), n)] +
                Current[Index(uint3(p.x,p.y,m.z), n)] + Current[Index(uint3(p.x,p.y,q.z), n)] - 6.0 * uv;
            float reaction = uv.x * uv.y * uv.y;
            Next[i] = saturate(uv + float2(Equation.x * lap.x - reaction + Equation.z * (1.0 - uv.x),
                Equation.y * lap.y + reaction - (Equation.z + Equation.w) * uv.y));
        }
        [numthreads(16,4,2)]
        void Inject(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            float3 delta = abs((float3(p) + .5) / n - Brush.xyz); delta = min(delta, 1.0 - delta);
            if (dot(delta,delta) <= Brush.w * Brush.w) Next[Index(p, n)] = float2(.5,.3);
        }
        // The renderer samples only V: half the texture traffic of the full U/V pair.
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID)
        {
            uint n = (uint)Grid.x; if (any(p >= n)) return;
            Display[p] = Current[Index(p, n)].y;
        }
        """;
}
