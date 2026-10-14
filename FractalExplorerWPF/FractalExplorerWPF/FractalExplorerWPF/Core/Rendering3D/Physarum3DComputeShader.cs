using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>3D sensory/motor swarm inspired by Jones. Integer deposits make collisions order independent.</summary>
internal static class Physarum3DComputeShader
{
    public const int GroupX = 8, GroupY = 8, GroupZ = 4;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "MoveAgents", "Diffuse", "Publish" }.Select(e => new ShaderCacheEntry("physarum3d-"+e, Source, e, "cs_5_0")).ToArray();
    public static string Source => ShaderSource.Load("Physarum3DCompute.hlsl");
}
