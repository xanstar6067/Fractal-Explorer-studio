using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Полный кадр двумерного окна для экспорта и превью сохранений: расчёт в размере, умноженном
/// на коэффициент SSAA, и уменьшение Lanczos-3 до заданного. Расчёт занимает первые 90%
/// прогресса, уменьшение — остаток; без SSAA расчёт идёт на все 100%.
/// </summary>
public static class SsaaBitmapRenderer
{
    /// <summary>Заполняет буфер Bgra32 кадра <paramref name="width"/>×<paramref name="height"/>.</summary>
    public delegate void RenderPixels(byte[] pixels, int width, int height, int stride, Action<int> progress);

    /// <param name="maxFactor">Наибольший коэффициент SSAA, который поддерживает окно.</param>
    /// <param name="render">
    /// Расчёт кадра; идёт в пуле потоков, поэтому всё, что нужно от элементов окна (число потоков
    /// и т. п.), читается заранее.
    /// </param>
    public static async Task<BitmapSource> RenderAsync(int width, int height, int ssaa, int maxFactor,
        RenderPixels render, CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaa, 1, maxFactor);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] pixels = new byte[checked(stride * renderHeight)];
        await Task.Run(() => render(pixels, renderWidth, renderHeight, stride,
            value => progress?.Report(factor == 1 ? value : value * 90 / 100)));

        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        if (factor == 1 || token.IsCancellationRequested) return source;
        return await Task.Run(() => BitmapResampler.ResizeLanczos3(source, width, height, token, value => progress?.Report(value)));
    }
}
