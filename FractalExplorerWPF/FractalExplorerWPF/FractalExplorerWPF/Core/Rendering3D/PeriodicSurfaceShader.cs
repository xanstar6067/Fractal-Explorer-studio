namespace FractalExplorerWPF.Core.Rendering3D;

internal static class PeriodicSurfaceShader
{
    // Nodal equations from https://doi.org/10.1016/j.jmrt.2024.05.023 (equations 2–4).
    // These are approximations of TPMS, not exact minimal surfaces.
    internal static string Source => ShaderSource.Load("PeriodicSurfaces.hlsl");
}
