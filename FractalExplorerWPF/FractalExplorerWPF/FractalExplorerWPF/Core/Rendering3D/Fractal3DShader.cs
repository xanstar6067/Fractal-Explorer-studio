using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходник пиксельного шейдера трассировки лучей по дистанционной оценке (distance estimation).
/// Вид фрактала подставляется препроцессором (<c>FRACTAL_KIND</c> против имён <c>KIND_…</c>,
/// <c>Shaders/Fractal3D.hlsl</c>): для каждого <see cref="Fractal3DKind"/> компилируется
/// свой шейдер, поэтому в горячем цикле нет ветвления по режиму. Палитра, источник цвета и стиль
/// освещения приходят константами и ветвятся единообразно для всего кадра, поэтому набор встроенных
/// шейдеров не умножает число компиляций. Своя геометрия и освещение режима встают в места
/// <c>@ModeGeometry</c> и <c>@ModeRendering</c>.
/// </summary>
internal static class Fractal3DShader
{
    public static string Build(Fractal3DKind kind, ImplicitSurfaceSettings? implicitSurface = null)
    {
        string geometry = kind switch
        {
            Fractal3DKind.HyperbolicHoneycomb => HyperbolicHoneycombShader.Geometry,
            Fractal3DKind.ImplicitSurface => ImplicitSurfaceShader.Build(implicitSurface ?? new()),
            Fractal3DKind.PeriodicSurfaces => PeriodicSurfaceShader.Source,
            Fractal3DKind.Hopf => HopfShader.Source,
            Fractal3DKind.Terrain => TerrainShader.Source,
            Fractal3DKind.LSystem3D or Fractal3DKind.AbcFlow => LSystem3DShader.Source,
            _ => ""
        };
        string rendering = kind == Fractal3DKind.HyperbolicHoneycomb ? HyperbolicHoneycombShader.Rendering : "";
        return ShaderSource.KindDefines + $"#define FRACTAL_KIND KIND_{kind}\n" +
            ShaderSource.Load("Fractal3D.hlsl", ("ModeGeometry", geometry), ("ModeRendering", rendering));
    }
}
