using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class PhaseFieldCrystal3DComputeShader
{
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "Prepare", "Evolve", "Filter", "Finish", "Inject", "Measure", "OrderFilter", "Publish" }
            .Select(e => new ShaderCacheEntry("phase-field-crystal3d-" + e, Source, e, "cs_5_0")).ToArray();
    public static string Source => ShaderSource.Load("PhaseFieldCrystal3DCompute.hlsl");
}
