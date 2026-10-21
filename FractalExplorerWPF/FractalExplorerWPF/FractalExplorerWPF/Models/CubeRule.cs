using System.Numerics;

namespace FractalExplorerWPF.Models;

/// <summary>Разряд рекурсивной решётки: x + 3*y + 9*z, координаты от 0 до 2.</summary>
public static class CubeRule
{
    public const uint All = (1u << 27) - 1;
    public static int Index(int x, int y, int z) => x + 3 * y + 9 * z;
    public static bool Contains(uint mask, int x, int y, int z) => (mask & (1u << Index(x, y, z))) != 0;
    public static int Count(uint mask) => BitOperations.PopCount(mask & All);

    public static uint Menger { get; } = Create((x, y, z) => (x == 1 ? 1 : 0) + (y == 1 ? 1 : 0) + (z == 1 ? 1 : 0) <= 1);
    public static uint Vicsek { get; } = Create((x, y, z) => (x == 1 ? 1 : 0) + (y == 1 ? 1 : 0) + (z == 1 ? 1 : 0) >= 2);
    public static uint Cantor { get; } = Create((x, y, z) => x != 1 && y != 1 && z != 1);

    public static uint Classic(Fractal3DKind kind) => kind switch
    {
        Fractal3DKind.MengerSponge => Menger,
        Fractal3DKind.Vicsek => Vicsek,
        Fractal3DKind.CantorDust => Cantor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static uint Create(Func<int, int, int, bool> include)
    {
        uint mask = 0;
        for (int z = 0; z < 3; z++)
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            if (include(x, y, z)) mask |= 1u << Index(x, y, z);
        return mask;
    }

    public static uint Set(uint mask, int x, int y, int z, bool included, bool mirrorX, bool mirrorY, bool mirrorZ)
    {
        for (int iz = 0; iz <= (mirrorZ ? 1 : 0); iz++)
        for (int iy = 0; iy <= (mirrorY ? 1 : 0); iy++)
        for (int ix = 0; ix <= (mirrorX ? 1 : 0); ix++)
        {
            uint bit = 1u << Index(ix == 0 ? x : 2 - x, iy == 0 ? y : 2 - y, iz == 0 ? z : 2 - z);
            mask = included ? mask | bit : mask & ~bit;
        }
        return mask & All;
    }
}
