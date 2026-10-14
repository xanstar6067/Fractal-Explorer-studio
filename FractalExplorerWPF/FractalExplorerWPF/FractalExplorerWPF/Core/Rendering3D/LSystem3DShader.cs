namespace FractalExplorerWPF.Core.Rendering3D;

internal static class LSystem3DShader
{
    public static string Source => ShaderSource.Load("LSystem3D.hlsl");
}
