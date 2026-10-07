using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lichtenberg3DComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("lichtenberg3d-relax", Source, "Relax", "cs_5_0");
    public const string Source = """
        cbuffer Parameters : register(b0) { uint Size; uint Color; float Omega; uint Padding; };
        StructuredBuffer<uint> Mask : register(t0);
        RWStructuredBuffer<float> Potential : register(u0);
        uint Index(uint3 p) { return (p.z * Size + p.y) * Size + p.x; }
        [numthreads(4,4,4)]
        void Relax(uint3 p : SV_DispatchThreadID)
        {
            if (any(p >= Size) || ((p.x + p.y + p.z) & 1) != Color) return;
            uint i = Index(p);
            uint fixedValue = Mask[i];
            if (fixedValue != 0) { Potential[i] = fixedValue == 2 ? 1.0 : 0.0; return; }
            // Clamped ghost cells impose zero normal flux at the uncharged side walls.
            uint3 lo = max(int3(p) - 1, 0), hi = min(p + 1, Size - 1);
            float average = (Potential[Index(uint3(lo.x,p.y,p.z))] + Potential[Index(uint3(hi.x,p.y,p.z))]
                + Potential[Index(uint3(p.x,lo.y,p.z))] + Potential[Index(uint3(p.x,hi.y,p.z))]
                + Potential[Index(uint3(p.x,p.y,lo.z))] + Potential[Index(uint3(p.x,p.y,hi.z))]) / 6.0;
            Potential[i] = saturate(Potential[i] + Omega * (average - Potential[i]));
        }
        """;
}
