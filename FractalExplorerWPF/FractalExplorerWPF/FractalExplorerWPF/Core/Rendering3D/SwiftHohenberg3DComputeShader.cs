using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class SwiftHohenberg3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "Prepare", "Evolve", "Finish", "Publish", "Inject", "Filter" }
            .Select(e => new ShaderCacheEntry("swift-hohenberg3d-" + e, Source, e, "cs_5_0")).ToArray();

    public static string Source => ShaderSource.Load("SwiftHohenberg3DCompute.hlsl");
}
