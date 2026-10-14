using FractalExplorerWPF.Infrastructure;
namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Kobayashi3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Flux", "Evolve", "Inject", "Publish" }
        .Select(e => new ShaderCacheEntry("kobayashi3d-" + e, Source, e, "cs_5_0")).ToArray();
    public static string Source => ShaderSource.Load("Kobayashi3DCompute.hlsl");
}
