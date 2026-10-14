using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GinzburgLandau3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "React", "Diffuse", "Inject", "Publish" }
            .Select(e => new ShaderCacheEntry("ginzburg-landau3d-" + e, Source, e, "cs_5_0")).ToArray();

    public static string Source => ShaderSource.Load("GinzburgLandau3DCompute.hlsl");
}
