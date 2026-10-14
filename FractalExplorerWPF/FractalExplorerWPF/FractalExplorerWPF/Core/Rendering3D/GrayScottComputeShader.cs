using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GrayScottComputeShader
{
    internal static IReadOnlyList<ShaderCacheEntry> CacheEntries => new[] { "Evolve", "Inject", "Render" }
        .Select(name => new ShaderCacheEntry($"gray-scott-{name}", Source, name, "cs_5_0")).ToArray();

    internal static string Source => ShaderSource.Load("GrayScottCompute.hlsl");
}
