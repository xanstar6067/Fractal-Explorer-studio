namespace FractalExplorerWPF.Models;

public enum NovaVariant
{
    Mandelbrot,
    Julia
}

public sealed class NovaState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "NovaMandelbrot";
    public NovaVariant Variant { get; set; }
    public decimal CenterX { get; set; }
    public decimal CenterY { get; set; }

    /// <summary>
    /// Центр области в произвольной точности — инвариантная строка, заполняется только на
    /// глубоком зуме (см. <c>NovaRenderer.DeepZoom</c>). У обычных сохранений null, и тогда
    /// центр берётся из <see cref="CenterX"/>/<see cref="CenterY"/>. За пределами decimal
    /// (28 знаков) только эти строки несут положение области.
    /// </summary>
    public string? CenterXExact { get; set; }

    /// <inheritdoc cref="CenterXExact"/>
    public string? CenterYExact { get; set; }

    /// <summary>
    /// Масштаб. <see cref="double"/>, а не decimal: пертурбационный движок уводит зум далеко
    /// за 1e28, где decimal переполняется. Старые сохранения читаются без изменений — в JSON
    /// это просто число.
    /// </summary>
    public double Zoom { get; set; } = 1;
    public decimal Threshold { get; set; } = 10;
    public int Iterations { get; set; } = 100;
    public decimal PReal { get; set; } = 3;
    public decimal PImaginary { get; set; }
    public decimal Z0Real { get; set; } = 1;
    public decimal Z0Imaginary { get; set; }
    public decimal M { get; set; } = 1;
    public decimal CReal { get; set; }
    public decimal CImaginary { get; set; } = 1;
    public bool UseSmoothColoring { get; set; } = true;
    public MandelbrotPalette Palette { get; set; } = new();
}
