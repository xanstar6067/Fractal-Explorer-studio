using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lyapunov3DComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("lyapunov3d-field", Source, "Compute", "cs_5_0");
    public const string Source = """
        cbuffer Parameters : register(b0) {
            float4 Counts; // size, transient, samples, sequence length
            float4 Lower; // minimum A/B/C, x0
            float4 Upper; // maximum A/B/C, first z slice
            float4 Sequence[16];
        };
        RWTexture3D<float> Exponents : register(u0);
        [numthreads(8,8,1)]
        void Compute(uint3 id : SV_DispatchThreadID) {
            id.z += (uint)Upper.w;
            uint n = (uint)Counts.x;
            if (any(id >= n)) return;
            // Texel centres match the trilinear sampling used by the volume renderer.
            float3 r = lerp(Lower.xyz, Upper.xyz, (float3(id)+.5)/n);
            float x = Lower.w, sum = 0;
            uint phase = 0, length = (uint)Counts.w;
            uint total = (uint)(Counts.y+Counts.z);
            [loop] for (uint i=0; i<total; i++) {
                uint symbol = (uint)Sequence[phase/4][phase%4];
                float rate = r[symbol];
                // The derivative belongs to x_n, before computing x_(n+1).
                if (i >= (uint)Counts.y) sum += log(max(abs(rate*(1-2*x)),1e-30));
                x = rate*x*(1-x);
                phase++; if (phase == length) phase = 0;
            }
            Exponents[id] = sum/Counts.z;
        }
        """;
}
