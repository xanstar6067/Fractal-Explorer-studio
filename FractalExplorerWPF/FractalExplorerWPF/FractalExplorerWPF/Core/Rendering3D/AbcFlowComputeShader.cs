using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class AbcFlowComputeShader
{
    public static ShaderCacheEntry CacheEntry => new("abc-flow-integrate", Source, "Integrate", "cs_5_0");
    public static string Source => ShaderSource.Load("AbcFlowCompute.hlsl");
}
