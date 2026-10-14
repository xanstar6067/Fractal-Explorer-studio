using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class TuringComputeShader
{
    public static readonly string[] EntryPoints = ["Blur", "Blur256", "Blur512", "Blur1024", "Difference", "Choose", "Compose", "Symmetry", "Reduce", "Normalize", "Paint", "Render", "ReactionStep", "ReactionSymmetry"];
    public static IEnumerable<ShaderCacheEntry> CacheEntries => EntryPoints.Select(name =>
        new ShaderCacheEntry("turing-" + name, BlurSource(name), name, "cs_5_0"));

    private static string BlurSource(string name)
    {
        if (!name.StartsWith("Blur", StringComparison.Ordinal) || name == "Blur") return Source;
        int capacity = int.Parse(name.AsSpan(4));
        return $"#define BLUR_CAPACITY {capacity}\n#define BLUR_ENTRY {name}\n" + Source;
    }

    // Float4 buffers share a pool. Only the committed field owns value and scale together.
    // Prefix sums perform every blur in O(N²), independent of the selected radius.
    public static string Source => ShaderSource.Load("TuringCompute.hlsl");
}
