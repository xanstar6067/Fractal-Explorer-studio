namespace FractalExplorerWPF.Models;

/// <summary>Диапазон двух осей атласа. Верхняя строка соответствует максимуму Y.</summary>
public readonly record struct ParameterAtlasRange(decimal MinX, decimal MaxX, decimal MinY, decimal MaxY)
{
    public static ParameterAtlasRange JuliaOverview(MandelbrotVariant variant)
    {
        if (!MandelbrotVariantDefinition.IsJulia(variant))
            throw new ArgumentException("Требуется вариант семейства Жюлиа.", nameof(variant));
        return MandelbrotVariantDefinition.ParameterVariant(variant) switch
        {
            MandelbrotVariant.Mandelbrot => new(-2m, 1m, -1.2m, 1.2m),
            MandelbrotVariant.BurningShip => new(-2m, 1.5m, -1m, 1.5m),
            _ => new(-2m, 2m, -1.5m, 1.5m)
        };
    }

    public bool IsValid => MinX >= -10m && MaxX <= 10m && MinY >= -10m && MaxY <= 10m
        && MinX < MaxX && MinY < MaxY;

    public (decimal X, decimal Y) Sample(int column, int row, int size)
    {
        if (!IsValid) throw new InvalidOperationException("Недопустимый диапазон атласа.");
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        if (column < 0 || column >= size || row < 0 || row >= size)
            throw new ArgumentOutOfRangeException(nameof(column));
        return (MinX + (MaxX - MinX) * column / (size - 1),
            MaxY - (MaxY - MinY) * row / (size - 1));
    }

    public static ParameterAtlasRange Around(decimal x, decimal y, decimal width, decimal height)
    {
        width = Math.Clamp(width, 0m, 20m);
        height = Math.Clamp(height, 0m, 20m);
        decimal left = Math.Clamp(x - width / 2, -10m, 10m - width);
        decimal bottom = Math.Clamp(y - height / 2, -10m, 10m - height);
        return new(left, left + width, bottom, bottom + height);
    }
}
