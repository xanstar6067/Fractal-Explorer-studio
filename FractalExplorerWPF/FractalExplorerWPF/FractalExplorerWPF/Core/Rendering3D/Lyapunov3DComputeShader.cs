using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lyapunov3DComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("lyapunov3d-field", Source, "Compute", "cs_5_0");
    public static string Source => ShaderSource.Load("Lyapunov3DCompute.hlsl");
}
