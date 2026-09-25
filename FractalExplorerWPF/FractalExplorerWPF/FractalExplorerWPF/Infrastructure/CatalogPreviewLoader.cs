using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Превью плиток каталога. Встроенные PNG для сетки декодируются уменьшенными, в полном размере —
/// только для пункта, открытого в панели деталей. Лаборатории, трёхмерные фракталы и Gray–Scott
/// своих картинок не имеют: их превью рендерится по состоянию по умолчанию в фоне, по одному,
/// и хранится в памяти (все вместе — несколько секунд). Если рендер не удался (например, нет
/// Direct3D 11 для трёхмерных видов), плитка показывает встроенную картинку-заглушку.
/// </summary>
internal sealed class CatalogPreviewLoader
{
    /// <summary>Ширина декодирования для сетки: плитка ~150–200 px, с запасом на масштаб экрана 125–150 %.</summary>
    public const int ThumbnailPixelWidth = 256;

    /// <summary>Сторона превью, которое строится на лету.</summary>
    public const int RenderedPixelSize = 512;

    private const string GrayScottLaunchKey = "GrayScott";

    private static readonly string AssemblyName = typeof(CatalogPreviewLoader).Assembly.GetName().Name!;

    private readonly Dictionary<string, BitmapSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private CatalogTile? _priority;

    public static bool IsRendered(FractalCatalogItem item) =>
        MathematicalLaboratoryCatalog.TryParseLaunchKey(item.LaunchKey, out _) ||
        Fractal3DCatalog.TryParseLaunchKey(item.LaunchKey, out _) ||
        item.LaunchKey is GrayScottLaunchKey or "SprottQuadratic";

    /// <summary>Встроенный ресурс по пути из каталога; работает и вне самого приложения (проверки, генератор скриншотов).</summary>
    public static BitmapSource? DecodeResource(string resourcePath, int decodePixelWidth)
    {
        try
        {
            var uri = new Uri($"/{AssemblyName};component/{resourcePath.TrimStart('/')}", UriKind.Relative);
            StreamResourceInfo? resource = Application.GetResourceStream(uri);
            if (resource is null) return null;
            using Stream stream = resource.Stream;
            // Поток, а не UriSource: кэш изображений WPF по адресу игнорирует DecodePixelWidth.
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static Task<BitmapSource> RenderAsync(FractalCatalogItem item, CancellationToken token)
    {
        if (MathematicalLaboratoryCatalog.TryParseLaunchKey(item.LaunchKey, out MathematicalLaboratoryKind kind))
        {
            return MathematicalLaboratoryRenderer.RenderBitmapAsync(
                MathematicalLaboratoryCatalog.CreateDefaultState(kind), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (Fractal3DCatalog.TryParseLaunchKey(item.LaunchKey, out Fractal3DKind fractal3DKind))
        {
            return Fractal3DRenderer.RenderOnceAsync(
                Fractal3DCatalog.CreateDefaultState(fractal3DKind), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (item.LaunchKey == GrayScottLaunchKey)
        {
            return GrayScottRenderer.RenderPreviewAsync(
                GrayScottPresets.All[0].State.Clone(), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (item.LaunchKey == "SprottQuadratic")
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
            SprottQuadraticMap.ApplyCode(state, SprottQuadraticMap.Presets[0].Code, token);
            state.Iterations = 600_000;
            return DynamicSystemRenderer.RenderAsync(state, RenderedPixelSize, RenderedPixelSize, null, token);
        }
        throw new ArgumentException($"Превью «{item.DisplayName}» не рендерится на лету.", nameof(item));
    }

    /// <summary>Заполняет плитки превью для сетки; рендерящиеся на лету помечаются как ожидающие.</summary>
    public void LoadThumbnails(IEnumerable<CatalogTile> tiles)
    {
        foreach (CatalogTile tile in tiles)
        {
            if (IsRendered(tile.Item))
                tile.IsPreviewPending = tile.Preview is null;
            else
                tile.Thumbnail = LoadThumbnail(tile.Item.PreviewResourcePath);
        }
    }

    /// <summary>Пункт открыт в панели деталей: полноразмерное превью или рендер вне очереди.</summary>
    public void ShowInDetails(CatalogTile tile)
    {
        if (IsRendered(tile.Item))
        {
            if (tile.IsPreviewPending) _priority = tile;
            return;
        }
        tile.Preview ??= DecodeResource(tile.Item.PreviewResourcePath, 0);
    }

    /// <summary>Пункт ушёл из панели деталей: полноразмерная копия встроенного PNG больше не нужна.</summary>
    public void HideFromDetails(CatalogTile tile)
    {
        if (!IsRendered(tile.Item)) tile.Preview = null;
    }

    /// <summary>
    /// Рендерит ожидающие превью по одному, начиная с открытого в панели деталей. Ошибка рендера
    /// оставляет встроенную картинку-заглушку из каталога. Вызывается на UI-потоке.
    /// </summary>
    public async Task RenderPendingAsync(IReadOnlyList<CatalogTile> tiles, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            CatalogTile? next = _priority is { IsPreviewPending: true } ? _priority : tiles.FirstOrDefault(tile => tile.IsPreviewPending);
            if (next is null) return;
            try
            {
                BitmapSource bitmap = await RenderAsync(next.Item, token);
                next.Thumbnail = bitmap;
                next.Preview = bitmap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                next.Thumbnail = LoadThumbnail(next.Item.PreviewResourcePath);
            }
            next.IsPreviewPending = false;
        }
    }

    private BitmapSource? LoadThumbnail(string resourcePath)
    {
        if (!_thumbnails.TryGetValue(resourcePath, out BitmapSource? thumbnail))
            _thumbnails[resourcePath] = thumbnail = DecodeResource(resourcePath, ThumbnailPixelWidth);
        return thumbnail;
    }
}
