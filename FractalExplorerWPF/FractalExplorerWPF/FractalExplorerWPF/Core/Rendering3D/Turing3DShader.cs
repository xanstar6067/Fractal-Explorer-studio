namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Общие девять объёмных стилей, свет и зонд для опубликованного поля Тьюринга (R32G32_Float:
/// поле в [0, 1] и значение победившего масштаба; пишет <see cref="Turing3DGpuSimulation"/>).
/// Поле переводится в заполненность с уровнем поверхности 0,5 на шкале заполненности: так у
/// пустой фазы плотность нулевая и тени, затенение складок и облако ведут себя как у остальных
/// объёмов. Область показа — куб, шар или сферическая оболочка — с мягким краем в полторы клетки.
/// </summary>
internal static class Turing3DShader
{
    /// <summary>
    /// Радиус шара и внешний радиус оболочки: шар не упирается в грани куба. В HLSL записан
    /// литералом 0.97 — интерполяция строки зависела бы от культуры (запятая вместо точки).
    /// </summary>
    public const float RegionRadius = .97f;

    public static string Source { get; } = Patch(Ifs3DShader.Source,
        ("Texture3D<float> Density : register(t0);", "Texture3D<float2> Density : register(t0);"),
        ("float SampleDensity(float3 samplePosition)", """
            float RegionMask(float3 p)
            {
                if (any(abs(p) > 1.0001)) return 0.0;
                int axis = (int)ShapeB.x;
                if (axis > 0 && p[axis - 1] > ShapeB.y + .0001) return 0.0;
                int region = (int)ShapeB.z;
                if (region == 0) return 1.0;
                float edge = 3.0 / ShapeA.x, radius = length(p);
                float mask = saturate((0.97 - radius) / edge + 0.5);
                if (region == 2) mask *= saturate((radius - (0.97 - ShapeB.w)) / edge + 0.5);
                return mask;
            }

            // Filtered, so borders between scales blend instead of showing cubic cells.
            float ScaleAt(float3 p)
            {
                return Density.SampleLevel(DensitySampler, saturate(p * 0.5 + 0.5), 0).y;
            }

            float SampleDensity(float3 samplePosition)
            """),
        ("return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);", """
            float region = RegionMask(samplePosition);
            if (region <= 0.0) return 0.0;
            float field = Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).x;
            // ShapeA.z > 0: a two-sided sheet of that half-thickness around the level.
            float shape = ShapeA.z > 0.0 ? saturate((ShapeA.z - abs(field - ShapeA.y)) * 5.0 + 0.5)
                : saturate((field - ShapeA.y) * 2.5 + 0.5);
            return shape * region;
            """),
        ("2.0 / 512.0", "2.0 / ShapeA.x"),
        ("const float threshold = 0.075;", "const float threshold = 0.5;"),
        ("float3 farPlane = (1.0 - origin) / safe;", """
            int cutAxis = (int)ShapeB.x;
            float3 boxMax = float3(cutAxis == 1 ? ShapeB.y : 1.0,
                cutAxis == 2 ? ShapeB.y : 1.0, cutAxis == 3 ? ShapeB.y : 1.0);
            float3 farPlane = (boxMax - origin) / safe;
            """),
        ("float3 gradient = float3(", """
            int axis = (int)ShapeB.x;
            if (axis > 0 && abs(p[axis - 1] - ShapeB.y) < .0002)
                return axis == 1 ? float3(1,0,0) : axis == 2 ? float3(0,1,0) : float3(0,0,1);
            if (abs(abs(p.x) - 1.0) < .0002) return float3(sign(p.x),0,0);
            if (abs(abs(p.y) - 1.0) < .0002) return float3(0,sign(p.y),0);
            if (abs(abs(p.z) - 1.0) < .0002) return float3(0,0,sign(p.z));
            float3 gradient = float3(
            """),
        ("if (mode == 3)", "if (mode == 2) value = ScaleAt(surfacePoint);\n            else if (mode == 4) value = length(surfacePoint);\n            else if (mode == 3)"));

    /// <summary>Изменившийся исходник IFS должен падать явно, а не терять заплатку молча.</summary>
    private static string Patch(string source, params (string Find, string Replace)[] patches)
    {
        foreach (var (find, replace) in patches)
        {
            if (source.IndexOf(find, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException($"Шейдер узоров Тьюринга 3D: не найден фрагмент «{find}».");
            source = source.Replace(find, replace, StringComparison.Ordinal);
        }
        return source;
    }
}
