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
/// только для пункта, открытого в панели деталей. Лаборатории, трёхмерные фракталы, Gray–Scott,
/// орбитальные орнаменты, снежные кристаллы и новые варианты Мандельброта/Жюлиа
/// своих картинок не имеют: их превью рендерится в фоне двумя независимыми очередями ЦП и ГП,
/// по одному на каждой очереди, и хранится в памяти. Если рендер не удался (например, нет
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
    private Task? _pendingRendering;
    private readonly Func<FractalCatalogItem, CancellationToken, Task<BitmapSource>> _render;

    public CatalogPreviewLoader(Func<FractalCatalogItem, CancellationToken, Task<BitmapSource>>? render = null) =>
        _render = render ?? RenderAsync;

    /// <summary>Очередь по штатному состоянию превью; остальные рендерящиеся пункты относятся к ЦП.</summary>
    internal static bool UsesGpu(FractalCatalogItem item) =>
        Fractal3DCatalog.TryParseLaunchKey(item.LaunchKey, out _) ||
        item.LaunchKey == GrayScottLaunchKey && GrayScottPresets.All[0].State.Backend == GrayScottBackend.Gpu ||
        item.LaunchKey == "TuringPatterns" && TuringPresets.All[0].CreateState().Backend == TuringBackend.Gpu;

    public static bool IsRendered(FractalCatalogItem item) =>
        MathematicalLaboratoryCatalog.TryParseLaunchKey(item.LaunchKey, out _) ||
        Fractal3DCatalog.TryParseLaunchKey(item.LaunchKey, out _) ||
        item.LaunchKey is "JuliaGeneralized" or "JuliaTricorn" or "JuliaBuffalo" or "JuliaCeltic" or "JuliaSimonobrot"
            or "PerpendicularMandelbrot" or "PerpendicularBurningShip" or "PerpendicularCeltic" or "PerpendicularBuffalo" or "JuliaPerpendicularMandelbrot" or "JuliaPerpendicularBurningShip" or "JuliaPerpendicularCeltic" or "JuliaPerpendicularBuffalo"
            or "CelticMandelbar" or "CubicQuasiBurningShip" or "CubicFlyingSquirrel"
            or "JuliaCelticMandelbar" or "JuliaCubicQuasiBurningShip" or "JuliaCubicFlyingSquirrel"
            or GrayScottLaunchKey or "TuringPatterns" or "SprottQuadratic" or "SymmetricIcon" or "Popcorn" or "SnowCrystal" or "Hopalong";

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
        if (Enum.TryParse(item.LaunchKey, out MandelbrotVariant variant) && Enum.IsDefined(variant))
        {
            MandelbrotState state = PresetManager.GetMandelbrotPresets(variant)[0];
            return Task.Run(() =>
            {
                byte[] pixels = new byte[RenderedPixelSize * RenderedPixelSize * 4];
                MandelbrotFamilyRenderer.Render(state, pixels, RenderedPixelSize, RenderedPixelSize,
                    RenderedPixelSize * 4, token);
                token.ThrowIfCancellationRequested();
                BitmapSource image = BitmapSource.Create(RenderedPixelSize, RenderedPixelSize, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, pixels, RenderedPixelSize * 4);
                image.Freeze();
                return image;
            }, token);
        }
        if (item.LaunchKey == GrayScottLaunchKey)
        {
            return GrayScottRenderer.RenderPreviewAsync(
                GrayScottPresets.All[0].State.Clone(), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (item.LaunchKey == "TuringPatterns")
        {
            return TuringRenderer.RenderStateAsync(TuringPresets.All[0].CreateState(), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (item.LaunchKey == "SnowCrystal")
        {
            return SnowCrystalRenderer.RenderStateAsync(
                SnowCrystalPresets.All[0].CreateState(), RenderedPixelSize, RenderedPixelSize, token);
        }
        if (item.LaunchKey == "SymmetricIcon")
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
            state.ApplyAttractor2DPreset(Attractor2DKind.SymmetricIcon);
            state.Iterations = 400_000;
            DynamicPalette? palette = DynamicPaletteStore.IconBuiltIns().FirstOrDefault(p => p.Name == state.PaletteName);
            return DynamicSystemRenderer.RenderAsync(state, RenderedPixelSize, RenderedPixelSize, palette, token);
        }
        if (item.LaunchKey == "Popcorn")
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Popcorn);
            DynamicPalette palette = DynamicPaletteStore.PopcornBuiltIns().First(p => p.Name == state.PaletteName);
            return DynamicSystemRenderer.RenderAsync(state, RenderedPixelSize, RenderedPixelSize, palette, token);
        }
        if (item.LaunchKey == "Hopalong")
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong);
            DynamicPalette palette = DynamicPaletteStore.HopalongBuiltIns().First(p => p.Name == state.PaletteName);
            return DynamicSystemRenderer.RenderAsync(state, RenderedPixelSize, RenderedPixelSize, palette, token);
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
            {
                tile.Thumbnail = LoadThumbnail(tile.Item.PreviewResourcePath);
                tile.IsPreviewFailed = tile.Thumbnail is null;
            }
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
    /// Одновременно выполняет очереди ЦП и ГП, по одному превью внутри каждой.
    /// Выбранный пункт получает приоритет в своей очереди. Вызывается на UI-потоке:
    /// выбор следующего пункта и публикация изображений сохраняют контекст WPF.
    /// Повторный вызов присоединяется к уже запущенной подготовке.
    /// </summary>
    public Task RenderPendingAsync(IReadOnlyList<CatalogTile> tiles, CancellationToken token) =>
        _pendingRendering is { IsCompleted: false }
            ? _pendingRendering
            : _pendingRendering = RenderQueuesAsync(tiles, token);

    private Task RenderQueuesAsync(IReadOnlyList<CatalogTile> tiles, CancellationToken token)
    {
        CatalogTile[] gpu = tiles.Where(tile => UsesGpu(tile.Item)).ToArray();
        CatalogTile[] cpu = tiles.Where(tile => !UsesGpu(tile.Item)).ToArray();
        return Task.WhenAll(RenderQueueAsync(cpu, token), RenderQueueAsync(gpu, token));
    }

    private async Task RenderQueueAsync(IReadOnlyList<CatalogTile> tiles, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            CatalogTile? next = _priority is { IsPreviewPending: true } priority && tiles.Contains(priority)
                ? priority : tiles.FirstOrDefault(tile => tile.IsPreviewPending);
            if (next is null) return;
            try
            {
                BitmapSource bitmap = await _render(next.Item, token);
                // Даже рендерер, закончивший кадр одновременно с закрытием окна, не публикует поздний результат.
                token.ThrowIfCancellationRequested();
                next.IsPreviewFailed = false;
                next.Thumbnail = bitmap;
                next.Preview = bitmap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) return;
                next.IsPreviewFailed = true;
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
