using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Прогрессивный тайловый рендер одного кадра двумерного окна. Рабочие потоки считают тайлы
/// (свои — <see cref="RenderTilesAsync"/>, или потоки внешнего движка через
/// <see cref="ReportTileStarted"/> и <see cref="ReportTileCompleted"/>) и складывают события в
/// очередь; UI-поток по собственному таймеру переносит готовые тайлы в
/// <see cref="Bitmap"/>, подсвечивает их на <see cref="RenderProgressOverlay"/> и двигает
/// полосу прогресса.
///
/// Сеанс живёт от <see cref="Begin"/> до <see cref="End"/>: окно держит ссылку на активный
/// сеанс, чтобы при зуме или перетаскивании дослить готовые тайлы (<see cref="Flush"/>) и
/// «запечь» видимый кадр (<see cref="TryCaptureLayer"/>).
/// </summary>
public sealed class TiledRenderSession
{
    /// <summary>
    /// Сколько событий переносится за один тик таймера: при мелких тайлах и многих потоках
    /// очередь иначе подвешивала бы UI-поток на десятки миллисекунд.
    /// </summary>
    private const int MaxEventsPerTick = 512;

    private readonly ConcurrentQueue<TileEvent> _events = new();
    private readonly RenderProgressOverlay _overlay;
    private readonly RangeBase? _progress;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _ended;

    private TiledRenderSession(WriteableBitmap bitmap, int tileCount, RenderProgressOverlay overlay,
        RangeBase? progress, CancellationTokenSource? cancellation)
    {
        Bitmap = bitmap;
        RenderWidth = bitmap.PixelWidth;
        RenderHeight = bitmap.PixelHeight;
        TileCount = tileCount;
        Cancellation = cancellation;
        _overlay = overlay;
        _progress = progress;
        _timer.Tick += Timer_OnTick;
    }

    /// <summary>Холст кадра в разрешении рендера (с учётом SSAA).</summary>
    public WriteableBitmap Bitmap { get; }

    public int TileCount { get; }

    public int CompletedTiles { get; private set; }

    /// <summary>
    /// Размер холста, запомненный при открытии сеанса. Читается и из рабочих потоков:
    /// сам <see cref="Bitmap"/> принадлежит UI-потоку, и его PixelWidth там недоступен.
    /// </summary>
    public int RenderWidth { get; }

    public int RenderHeight { get; }

    /// <summary>Источник отмены кадра, если окно передало его в сеанс.</summary>
    public CancellationTokenSource? Cancellation { get; }

    /// <summary>
    /// Ошибка переноса тайлов на тике таймера. Таймер к этому моменту уже остановлен, а
    /// исключение записано в журнал; окно может показать сообщение в строке состояния.
    /// </summary>
    public event Action<Exception>? VisualizationFailed;

    /// <summary>
    /// Открывает сеанс: сбрасывает подсветку тайлов и запускает таймер переноса.
    /// </summary>
    public static TiledRenderSession Begin(WriteableBitmap bitmap, int tileCount, RenderProgressOverlay overlay,
        RangeBase? progress = null, CancellationTokenSource? cancellation = null)
    {
        var session = new TiledRenderSession(bitmap, tileCount, overlay, progress, cancellation);
        overlay.BeginSession(session.RenderWidth, session.RenderHeight);
        session._timer.Start();
        return session;
    }

    /// <summary>
    /// Считает тайлы на <paramref name="threadCount"/> рабочих потоках. Делегат вызывается
    /// вне UI-потока и возвращает BGRA-пиксели тайла либо <see langword="null"/> при отмене.
    /// Отмена не бросает исключение: метод просто завершается, проверку токена делает окно.
    /// </summary>
    public async Task RenderTilesAsync(IReadOnlyList<MandelbrotRenderTile> tiles, int threadCount,
        Func<MandelbrotRenderTile, CancellationToken, byte[]?> renderTile, CancellationToken token)
    {
        var queue = new ConcurrentQueue<MandelbrotRenderTile>(tiles);
        Task[] workers = Enumerable.Range(0, Math.Clamp(threadCount, 1, Environment.ProcessorCount))
            .Select(_ => Task.Run(() =>
            {
                while (queue.TryDequeue(out MandelbrotRenderTile tile))
                {
                    if (token.IsCancellationRequested) return;
                    ReportTileStarted(tile);
                    byte[]? pixels = renderTile(tile, token);
                    if (pixels is null || token.IsCancellationRequested) return;
                    ReportTileCompleted(tile, pixels);
                }
            })).ToArray();
        await Task.WhenAll(workers);
    }

    /// <summary>Тайл взят в работу. Вызывается из любого потока.</summary>
    public void ReportTileStarted(MandelbrotRenderTile tile) => _events.Enqueue(new TileEvent(true, tile, null));

    /// <summary>Тайл готов: BGRA-пиксели его области. Вызывается из любого потока.</summary>
    public void ReportTileCompleted(MandelbrotRenderTile tile, byte[] pixels) =>
        _events.Enqueue(new TileEvent(false, tile, pixels));

    /// <summary>
    /// Переносит накопленные события на холст и подсветку. Только из UI-потока. После
    /// <see cref="End"/> ничего не делает: опоздавшие тайлы отменённого кадра не должны снова
    /// зажечь подсветку.
    /// </summary>
    /// <param name="drainAll">Перенести всю очередь, а не порцию одного тика.</param>
    public void Flush(bool drainAll = true)
    {
        if (_ended) return;
        int processed = 0;
        bool changed = false;
        while ((drainAll || processed < MaxEventsPerTick) && _events.TryDequeue(out TileEvent entry))
        {
            if (entry.IsStart)
            {
                _overlay.StartTile(entry.Tile);
            }
            else if (entry.Pixels is not null && ProgressiveRenderBitmap.WriteTile(Bitmap, entry.Tile, entry.Pixels))
            {
                _overlay.CompleteTile(entry.Tile);
                CompletedTiles++;
            }
            processed++;
            changed = true;
        }
        if (!changed) return;
        _overlay.Refresh();
        if (_progress is not null)
            _progress.Value = TileCount == 0 ? 0 : Math.Min(100, CompletedTiles * 100.0 / TileCount);
    }

    /// <summary>
    /// Готовый кадр как замороженная копия холста. Вызывать после <see cref="Flush"/>.
    /// </summary>
    public BitmapSource Snapshot()
    {
        BitmapSource completed = Bitmap.Clone();
        completed.Freeze();
        return completed;
    }

    /// <summary>
    /// Закрывает сеанс: останавливает таймер и гасит подсветку тайлов. Повторный вызов ничего
    /// не делает, поэтому его можно звать и из «запекания», и из finally рендера.
    /// </summary>
    public void End()
    {
        if (_ended) return;
        _ended = true;
        _timer.Stop();
        _overlay.EndSession();
    }

    /// <summary>
    /// Снимок того, что сейчас видно в слое предпросмотра (сдвинутый прежний кадр плюс
    /// успевшие лечь тайлы), в физических пикселях экрана. <see langword="null"/>, если разметка
    /// недоступна — так бывает на свёртывании окна и в момент изменения размера.
    /// </summary>
    public static BitmapSource? TryCaptureLayer(FrameworkElement layer)
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(layer);
        try
        {
            var baked = new RenderTargetBitmap(surface.PixelWidth, surface.PixelHeight,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            baked.Render(layer);
            baked.Freeze();
            return baked;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            Flush(drainAll: false);
        }
        catch (Exception ex)
        {
            _timer.Stop();
            CrashLogger.Log($"{nameof(TiledRenderSession)}.{nameof(Flush)}", ex);
            VisualizationFailed?.Invoke(ex);
        }
    }

    private readonly record struct TileEvent(bool IsStart, MandelbrotRenderTile Tile, byte[]? Pixels);
}
