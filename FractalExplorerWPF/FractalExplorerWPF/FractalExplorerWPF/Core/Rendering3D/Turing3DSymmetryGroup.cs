using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Конечные группы ортогональных преобразований для объёмных узоров. Матрицы — построчно, 3×3,
/// в координатах с началом в центре куба; тождество всегда первое. Многогранники стоят в
/// стандартном положении: оси куба по X/Y/Z, вершины тетраэдра в (±1, ±1, ±1) с чётным числом
/// минусов, вершины икосаэдра в циклических перестановках (0, ±1, ±φ).
/// </summary>
public static class Turing3DSymmetryGroup
{
    /// <summary>
    /// Направление, задающее фундаментальную область: каждая точка берёт значение у того образа
    /// своей орбиты, который дальше всех продвинут вдоль него. Вектор не лежит ни на одной
    /// плоскости симметрии перечисленных групп, поэтому образ выбирается однозначно.
    /// </summary>
    public static readonly (double X, double Y, double Z) DomainDirection = Normalize(.83, .47, .31);

    public static IReadOnlyList<double[]> Build(Turing3DSymmetry symmetry, int arms, bool mirror)
    {
        double phi = (1 + Math.Sqrt(5)) / 2;
        double[] cyclic = [0, 1, 0, 0, 0, 1, 1, 0, 0];
        double[] halfTurnZ = [-1, 0, 0, 0, -1, 0, 0, 0, 1];
        double[] flipX = [-1, 0, 0, 0, 1, 0, 0, 0, 1];
        double[][] generators;
        double[]? reflection;
        switch (symmetry)
        {
            case Turing3DSymmetry.Axial:
                // The mirror plane z = 0 contains the axis, as the 2D mirror line contains the centre.
                generators = arms > 1 ? [RotationY(Math.Tau / arms)] : [];
                reflection = [1, 0, 0, 0, 1, 0, 0, 0, -1];
                break;
            case Turing3DSymmetry.Tetrahedral:
                // x ↔ y keeps the tetrahedron: the full group Td, not the pyritohedral Th.
                generators = [cyclic, halfTurnZ];
                reflection = [0, 1, 0, 1, 0, 0, 0, 0, 1];
                break;
            case Turing3DSymmetry.Octahedral:
                generators = [cyclic, [0, -1, 0, 1, 0, 0, 0, 0, 1]];
                reflection = flipX;
                break;
            case Turing3DSymmetry.Icosahedral:
                generators = [cyclic, halfTurnZ, Rotation(Normalize(0, 1, phi), Math.Tau / 5)];
                reflection = flipX;
                break;
            default:
                generators = [];
                reflection = null;
                break;
        }
        var group = Closure(generators);
        if (mirror && reflection is not null)
            group.AddRange(group.ToArray().Select(g => Multiply(reflection, g)));
        return group;
    }

    /// <summary>Группа для ГП: на элемент — три строки матрицы и вектор gᵀ·d для выбора образа.</summary>
    public static float[] Pack(IReadOnlyList<double[]> group)
    {
        var (dx, dy, dz) = DomainDirection;
        var packed = new float[group.Count * 16];
        for (int g = 0; g < group.Count; g++)
        {
            double[] m = group[g];
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    packed[g * 16 + row * 4 + column] = (float)m[row * 3 + column];
            packed[g * 16 + 12] = (float)(m[0] * dx + m[3] * dy + m[6] * dz);
            packed[g * 16 + 13] = (float)(m[1] * dx + m[4] * dy + m[7] * dz);
            packed[g * 16 + 14] = (float)(m[2] * dx + m[5] * dy + m[8] * dz);
        }
        return packed;
    }

    private static List<double[]> Closure(double[][] generators)
    {
        List<double[]> group = [[1, 0, 0, 0, 1, 0, 0, 0, 1]];
        for (int index = 0; index < group.Count; index++)
            foreach (double[] generator in generators)
            {
                double[] product = Multiply(generator, group[index]);
                if (!group.Any(existing => Same(existing, product))) group.Add(product);
                if (group.Count > 240) throw new InvalidOperationException("Группа симметрии не замкнулась.");
            }
        return group;
    }

    private static double[] RotationY(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return [c, 0, s, 0, 1, 0, -s, 0, c];
    }

    private static double[] Rotation((double X, double Y, double Z) axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
        var (x, y, z) = axis;
        return
        [
            t * x * x + c, t * x * y - s * z, t * x * z + s * y,
            t * x * y + s * z, t * y * y + c, t * y * z - s * x,
            t * x * z - s * y, t * y * z + s * x, t * z * z + c
        ];
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        var result = new double[9];
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
                result[row * 3 + column] = a[row * 3] * b[column] + a[row * 3 + 1] * b[3 + column] + a[row * 3 + 2] * b[6 + column];
        return result;
    }

    private static bool Same(double[] a, double[] b)
    {
        for (int i = 0; i < 9; i++) if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
        return true;
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        return (x / length, y / length, z / length);
    }
}
