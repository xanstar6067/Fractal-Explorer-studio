using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class MandelbrotPreviewShader
{
    internal static ShaderCacheEntry CacheEntry => new("julia-preview-metrics", Source, "Render", "cs_5_0");
    // Coordinates and the orbit use doubles. Transcendental functions only affect colour metrics.
    private static string Source => ShaderSource.Load("MandelbrotPreview.hlsl");
}
