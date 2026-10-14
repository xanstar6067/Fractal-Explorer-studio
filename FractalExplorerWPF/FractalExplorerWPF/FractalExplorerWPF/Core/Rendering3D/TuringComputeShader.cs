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
        return ShaderSource.Patch(Source, "размытия Тьюринга",
            ("Prefix[4096]", $"Prefix[{capacity * 2}]"),
            ("ScanCapacity = 2048", $"ScanCapacity = {capacity}"),
            ("2048 - origin", $"{capacity} - origin"),
            ("void Blur(", $"void {name}("));
    }

    // Float4 buffers share a pool. Only the committed field owns value and scale together.
    // Prefix sums perform every blur in O(N²), independent of the selected radius.
    public static string Source => ShaderSource.Load("TuringCompute.hlsl");
}
