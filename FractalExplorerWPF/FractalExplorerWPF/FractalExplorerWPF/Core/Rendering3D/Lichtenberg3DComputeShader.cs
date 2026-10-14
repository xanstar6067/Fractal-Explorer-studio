using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lichtenberg3DComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("lichtenberg3d-relax", Source, "Relax", "cs_5_0");
    public static string Source => ShaderSource.Load("Lichtenberg3DCompute.hlsl");
}
