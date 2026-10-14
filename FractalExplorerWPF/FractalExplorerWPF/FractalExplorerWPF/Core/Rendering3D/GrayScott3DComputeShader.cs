using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GrayScott3DComputeShader
{
    /// <summary>Thread group: a wide X row keeps neighbouring reads of a warp in one memory segment.</summary>
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;

    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Evolve", "Inject", "Publish" }
        .Select(entry => new ShaderCacheEntry("gray-scott3d-" + entry, Source, entry, "cs_5_0")).ToArray();

    public static string Source => ShaderSource.Load("GrayScott3DCompute.hlsl");
}
