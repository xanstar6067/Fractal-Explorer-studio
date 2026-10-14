using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходник пиксельного шейдера трассировки лучей по дистанционной оценке (distance estimation).
/// Вид фрактала подставляется препроцессором: для каждого <see cref="Fractal3DKind"/> компилируется
/// свой шейдер, поэтому в горячем цикле нет ветвления по режиму. Палитра, источник цвета и стиль
/// освещения приходят константами и ветвятся единообразно для всего кадра, поэтому набор встроенных
/// шейдеров не умножает число компиляций.
/// </summary>
internal static class Fractal3DShader
{
    public static string Build(Fractal3DKind kind, ImplicitSurfaceSettings? implicitSurface = null)
    {
        // Геометрия режима, у которого она своя, встаёт перед первой общей функцией.
        string? geometry = kind switch
        {
            Fractal3DKind.HyperbolicHoneycomb => HyperbolicHoneycombShader.Geometry,
            Fractal3DKind.ImplicitSurface => ImplicitSurfaceShader.Build(implicitSurface ?? new()),
            Fractal3DKind.PeriodicSurfaces => PeriodicSurfaceShader.Source,
            Fractal3DKind.Hopf => HopfShader.Source,
            Fractal3DKind.Terrain => TerrainShader.Source,
            Fractal3DKind.AbcFlow => AbcFlowShader.Source,
            Fractal3DKind.LSystem3D => LSystem3DShader.Source,
            _ => null
        };
        string source = geometry is null ? Source
            : ShaderSource.Patch(Source, "трассировки 3D", ("float BoxDistance", geometry + "\nfloat BoxDistance"));
        if (kind == Fractal3DKind.HyperbolicHoneycomb)
            source = ShaderSource.Patch(source, "трассировки 3D",
                ("float4 PSMain", HyperbolicHoneycombShader.Rendering + "\nfloat4 PSMain"));
        return $"#define FRACTAL_KIND {(int)kind}\n#define ABC_KIND {(int)Fractal3DKind.AbcFlow}\n#define HYPERBOLIC_KIND {(int)Fractal3DKind.HyperbolicHoneycomb}\n#define IMPLICIT_KIND {(int)Fractal3DKind.ImplicitSurface}\n#define HOPF_KIND {(int)Fractal3DKind.Hopf}\n#define PERIODIC_KIND {(int)Fractal3DKind.PeriodicSurfaces}\n" + source;
    }

    private static string Source => ShaderSource.Load("Fractal3D.hlsl");
}
