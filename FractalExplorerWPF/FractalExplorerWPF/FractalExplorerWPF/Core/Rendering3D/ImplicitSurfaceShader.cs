using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class ImplicitSurfaceShader
{
    internal static string Build(ImplicitSurfaceSettings settings)
    {
        var expression = settings.Validate();
        return Helpers + "\nfloat ImplicitField(float3 p) { float3 q=p*ShapeA.x; return " + expression.Hlsl +
            "-ShapeA.y; }\nfloat2 ImplicitInterval(float3 lo,float3 hi) { float2 ix=float2(lo.x,hi.x),iy=float2(lo.y,hi.y),iz=float2(lo.z,hi.z); return ISub(" +
            expression.IntervalHlsl + ",float2(ShapeA.y,ShapeA.y)); }\n" + Geometry;
    }

    private static string Helpers => ShaderSource.Load("ImplicitSurface.Helpers.hlsl");

    private static string Geometry => ShaderSource.Load("ImplicitSurface.Geometry.hlsl");
}
