namespace FractalExplorerWPF.Core.Rendering3D;

internal static class TerrainShader
{
    // ShapeA: patch width, maximum height, grid resolution. Each cell has two triangles.
    public static string Source => ShaderSource.Load("Terrain.hlsl");
}
