using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Пиксельный шейдер объёмных режимов (<c>Shaders/Volume.hlsl</c>): камера, зонд, девять стилей
/// освещения и окраска по трёхмерной текстуре плотности. Отличия режимов — ветви
/// <c>#if VOLUME_KIND == KIND_…</c> в самом исходнике; DLA и фигуры Лихтенберга делят вариант
/// <see cref="Fractal3DKind.Dla3D"/>, аттракторы — <see cref="Fractal3DKind.Ifs3D"/>.
/// </summary>
internal static class VolumeShader
{
    public static string Build(Fractal3DKind kind) =>
        ShaderSource.KindDefines + $"#define VOLUME_KIND KIND_{kind}\n#define FLAME_SIDE {Flame3DVolume.Side}.0\n" +
        ShaderSource.Load("Volume.hlsl");
}
