using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

internal static partial class Program
{
    private static async Task VerifyCatalogPreviewQueuesAsync()
    {
        var catalog = FractalCatalog.Create();
        var cpu = catalog.Where(item => CatalogPreviewLoader.IsRendered(item) && !CatalogPreviewLoader.UsesGpu(item))
            .Take(2).Select(item => new CatalogTile(item, new CatalogGroup(item.CategoryPath))).ToArray();
        var gpu = catalog.Where(CatalogPreviewLoader.UsesGpu).Take(3)
            .Select(item => new CatalogTile(item, new CatalogGroup(item.CategoryPath))).ToArray();
        var tiles = cpu.Concat(gpu).ToArray();
        var gates = tiles.ToDictionary(tile => tile.Item, _ =>
            new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously));
        var starts = tiles.ToDictionary(tile => tile.Item, _ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var calls = new List<FractalCatalogItem>();
        int activeCpu = 0, activeGpu = 0, peakCpu = 0, peakGpu = 0;
        BitmapSource bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        bitmap.Freeze();

        async Task<BitmapSource> Render(FractalCatalogItem item, CancellationToken token)
        {
            Check(Application.Current.Dispatcher.CheckAccess(), "Queue selection must stay on the WPF thread.");
            calls.Add(item);
            bool isGpu = CatalogPreviewLoader.UsesGpu(item);
            if (isGpu) peakGpu = Math.Max(peakGpu, ++activeGpu);
            else peakCpu = Math.Max(peakCpu, ++activeCpu);
            starts[item].TrySetResult();
            try { return await gates[item].Task; }
            finally { if (isGpu) activeGpu--; else activeCpu--; }
        }

        var loader = new CatalogPreviewLoader(Render);
        loader.LoadThumbnails(tiles);
        loader.ShowInDetails(gpu[2]);
        Task work = loader.RenderPendingAsync(tiles, CancellationToken.None);
        Check(ReferenceEquals(work, loader.RenderPendingAsync(tiles, CancellationToken.None)),
            "A repeated start must join the existing queues instead of rendering tiles twice.");
        await Task.WhenAll(starts[cpu[0].Item].Task, starts[gpu[2].Item].Task).WaitAsync(TimeSpan.FromSeconds(5));
        Check(activeCpu == 1 && activeGpu == 1 && calls.Count == 2,
            "CPU and GPU must overlap while each queue starts exactly one preview.");

        gates[cpu[0].Item].SetException(new InvalidOperationException("Expected preview failure."));
        await starts[cpu[1].Item].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cpu[0].IsPreviewFailed && !cpu[0].IsPreviewPending && gpu[2].IsPreviewPending,
            "A failed CPU preview must release its queue while GPU work remains active.");
        gates[cpu[1].Item].SetResult(bitmap);
        loader.ShowInDetails(gpu[1]);
        gates[gpu[2].Item].SetResult(bitmap);
        await starts[gpu[1].Item].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!calls.Contains(gpu[0].Item), "Changing selection must reprioritize the next GPU preview.");
        gates[gpu[1].Item].SetResult(bitmap);
        await starts[gpu[0].Item].Task.WaitAsync(TimeSpan.FromSeconds(5));
        gates[gpu[0].Item].SetResult(bitmap);
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Check(peakCpu == 1 && peakGpu == 1 && activeCpu == 0 && activeGpu == 0 &&
            calls.Count == tiles.Length && calls.Distinct().Count() == tiles.Length,
            "Each preview must render once, with at most one active render per queue.");
        Check(tiles.Count(tile => tile.IsPreviewFailed) == 1 &&
            tiles.All(tile => !tile.IsPreviewPending) &&
            tiles.Where(tile => !tile.IsPreviewFailed).All(tile => ReferenceEquals(tile.Thumbnail, bitmap)),
            "Successful results must be published and one failed preview must not stop either queue.");

        // Deliberately ignore the token in the fake renderers to reproduce a late completion after closing.
        var cancelledTiles = tiles.Select(tile => new CatalogTile(tile.Item, tile.Group)).ToArray();
        var cancellationGate = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        int cancellationStarts = 0;
        var cancelLoader = new CatalogPreviewLoader((_, _) =>
        {
            cancellationStarts++;
            return cancellationGate.Task;
        });
        cancelLoader.LoadThumbnails(cancelledTiles);
        using var cancellation = new CancellationTokenSource();
        Task cancelledWork = cancelLoader.RenderPendingAsync(cancelledTiles, cancellation.Token);
        Check(cancellationStarts == 2, "Both queues must begin before cancellation.");
        cancellation.Cancel();
        cancellationGate.SetResult(bitmap);
        await cancelledWork.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancellationStarts == 2 && cancelledTiles.All(tile => tile.IsPreviewPending &&
            tile.Thumbnail is null && tile.Preview is null && !tile.IsPreviewFailed),
            "Cancellation must prevent late publication and prevent either queue starting another preview.");
        Console.WriteLine("PASS (catalog queues): CPU/GPU overlap, single render per queue, priority, repeated start, failure recovery and late cancellation.");
    }
}