namespace FractalExplorerWPF.Core.Rendering3D;

internal static class HyperbolicHoneycombShader
{
    internal static string Geometry => ShaderSource.Load("HyperbolicHoneycomb.Geometry.hlsl");

    internal static string Rendering => ShaderSource.Load("HyperbolicHoneycomb.Rendering.hlsl");
}
