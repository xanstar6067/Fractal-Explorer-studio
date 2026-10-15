using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Newton3DComputeShader
{
    internal static ShaderCacheEntry CacheEntry =>
        new("newton3d-field", ShaderSource.Load("Newton3DCompute.hlsl"), "Compute", "cs_5_0");
}
