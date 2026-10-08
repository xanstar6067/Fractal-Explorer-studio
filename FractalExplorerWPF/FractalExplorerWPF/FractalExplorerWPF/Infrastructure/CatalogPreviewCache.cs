using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Постоянный кэш каталога. Версия меняется при изменении формата или качества превью.</summary>
internal static class CatalogPreviewCache
{
    internal static string GetPath(FractalCatalogItem item) => Path.Combine(
        AppPaths.CatalogPreviewCacheDirectory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v1|{item.LaunchKey}|{item.PreviewResourcePath}|{CatalogPreviewLoader.IsRendered(item)}"))) + ".png");

    public static BitmapSource? Load(FractalCatalogItem item)
    {
        try
        {
            using var stream = File.OpenRead(GetPath(item));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            int size = CatalogPreviewLoader.IsRendered(item)
                ? CatalogPreviewLoader.RenderedPixelSize : CatalogPreviewLoader.ThumbnailPixelWidth;
            return image.PixelWidth == size && image.PixelHeight == size ? image : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static void Save(FractalCatalogItem item, BitmapSource image)
    {
        string path = GetPath(item);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            AppPaths.EnsureDirectoryFor(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            RecycleBin.ReplaceWith(temporary, path);
        }
        catch (Exception exception)
        {
            // Ошибка диска не должна превращать успешно построенную картинку в заглушку.
            CrashLogger.Log("CatalogPreviewCache.Save", exception);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
