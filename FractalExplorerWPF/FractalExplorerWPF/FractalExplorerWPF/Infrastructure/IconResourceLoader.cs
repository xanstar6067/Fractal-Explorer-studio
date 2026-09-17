using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FractalExplorerWPF.Infrastructure;

internal static class IconResourceLoader
{
    // Image со ссылкой на ICO использует первый (обычно 16×16) кадр.
    // Для масштабирования в интерфейсе выбираем самый крупный.
    public static BitmapSource? LoadLargestFrame(string relativePath)
    {
        try
        {
            string assembly = typeof(IconResourceLoader).Assembly.GetName().Name!;
            var uri = new Uri($"/{assembly};component/{relativePath.TrimStart('/')}", UriKind.Relative);
            var resource = Application.GetResourceStream(uri);
            if (resource is null) return null;
            using Stream stream = resource.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapFrame? frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }
}
