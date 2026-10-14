using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class CahnHilliard3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = new[] { "Prepare", "Evolve", "Finish", "Publish" }
        .Select(e => new ShaderCacheEntry("cahn-hilliard3d-" + e, Source, e, "cs_5_0")).ToArray();
    public static string Source => ShaderSource.Load("CahnHilliard3DCompute.hlsl");
}
