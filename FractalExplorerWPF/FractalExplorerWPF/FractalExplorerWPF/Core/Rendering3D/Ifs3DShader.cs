namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>GPU ray marcher for the density volume produced by the IFS chaos game.</summary>
internal static class Ifs3DShader
{
    public static string Source => ShaderSource.Load("Ifs3D.hlsl");
}
