using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifySandpileAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("sandpile");
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        VerifyCatalogData();
        var tile = FractalCatalog.Create().Single(i => i.LaunchKey == "Sandpile");
        Check(CatalogPreviewLoader.IsRendered(tile), "Sandpile needs a simulated catalog preview.");
        foreach (var lattice in Enum.GetValues<SandpileLattice>()) VerifySandpileReference(lattice);
        SandpileState? settled = null;
        var frames = new List<byte[]>();
        foreach (var preset in SandpilePresets.All)
        {
            var state = preset.CreateState(); var simulation = new SandpileSimulation(state); var watch = Stopwatch.StartNew();
            while (!simulation.IsStable)
            {
                simulation.Advance(500_000, CancellationToken.None);
                Check(watch.Elapsed < TimeSpan.FromSeconds(60), $"{preset.Name}: settling must be bounded.");
            }
            state.Checkpoint = simulation.Snapshot();
            Check(state.Checkpoint.Grains.All(g => g >= 0 && g < state.Degree), "Settled heights must be below the degree.");
            Check(state.Checkpoint.Grains.Sum() + simulation.Lost == simulation.Added, "Boundary dissipation must conserve the accounted mass.");
            byte[] frame = SandpileRenderer.RenderFrame(state.Checkpoint, state, 512, 512, CancellationToken.None);
            Check(frames.All(f => !f.SequenceEqual(frame)), "Preset images must differ."); frames.Add(frame);
            if (preset.Seed != SandpileSeed.Empty)
                Check(frame.Chunk(4).Select(p => Convert.ToHexString(p)).Distinct().Count() >= 3, "Nonempty presets need visible mosaic levels.");
            if (output is not null) SaveSandpilePng(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, frame, 2048), Path.Combine(output, $"sandpile-{frames.Count}.png"));
            Console.WriteLine($"Sandpile {preset.Name}: {simulation.Operations:N0} operations, {simulation.Lost:N0} lost, {watch.ElapsedMilliseconds} ms.");
            settled ??= state;
        }
        Check(settled!.Checkpoint!.Lost == 0, "Default field must be large enough to retain the mandala.");
        int n = settled.Size;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            Check(settled.Checkpoint.Grains[y * n + x] == settled.Checkpoint.Grains[x * n + n - 1 - y], "The square central pile must have exact fourfold symmetry.");
        var brush = new SandpileSimulation(new() { Seed = SandpileSeed.Empty, Size = 33 });
        brush.AddBrush(16, 16, 2, 7);
        Check(brush.Added == 13 * 7, "A round square-lattice brush of radius two must cover thirteen cells.");
        brush.AddBrush(0, 0, 1, 3); Check(brush.Added == 13 * 7 + 9, "Edge brushes must clip to actual cells.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool caught = false;
            var before = brush.Snapshot();
            try { brush.Advance(int.MaxValue, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
            Check(caught && brush.Snapshot().Grains.SequenceEqual(before.Grains), "Cancelled operations must not start mutating the field.");
            caught = false;
            try { await SandpileRenderer.RenderStateAsync(settled, 128, 128, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
            Check(caught, "Export must honor cancellation.");
        }
        var activeState = new SandpileState { Size = 65, InitialGrains = 20_000, PanX = 4, PanY = -3, Zoom = 1.7 };
        var live = new SandpileSimulation(activeState); live.Advance(12_345, CancellationToken.None);
        activeState.Checkpoint = live.Snapshot(); activeState.SaveName = "Unfinished avalanche"; activeState.Timestamp = DateTime.Now;
        Check(activeState.Checkpoint.Queue.Length > 0, "Continuation test needs an unfinished avalanche.");
        var store = new SandpileSaveStore(); var slot = store.Save(activeState); var restored = store.Load().Single();
        var copy = new SandpileSimulation(restored);
        live.Advance(17_777, CancellationToken.None); copy.Advance(17_777, CancellationToken.None);
        var a = live.Snapshot(); var b = copy.Snapshot();
        Check(a.Grains.SequenceEqual(b.Grains) && a.Topplings.SequenceEqual(b.Topplings) && a.Queue.SequenceEqual(b.Queue) && a.Lost == b.Lost && a.Operations == b.Operations,
            "Disk round-trip must preserve exact transient continuation and FIFO order.");
        Check(SandpileRenderer.RenderFrame(activeState.Checkpoint, activeState, 192, 128, CancellationToken.None)
            .SequenceEqual(SandpileRenderer.RenderFrame(restored.Checkpoint!, restored, 192, 128, CancellationToken.None)), "Saved view, palette and field must reproduce the exact frame.");
        var cloud = CloudSaveRepository.ReadLocal(slot.FilePath);
        Check(cloud.Category == "Sandpile" && cloud.JsonData.Contains("Checkpoint"), "Cloud payload must carry the checkpoint.");
        var broken = activeState.Clone(); broken.Checkpoint!.Queue = [];
        bool rejected = false; try { _ = new SandpileSimulation(broken); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Missing active cells must be rejected.");
        broken = activeState.Clone(); broken.Checkpoint!.Grains[0] = -1;
        rejected = false; try { _ = new SandpileSimulation(broken); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Negative checkpoint heights must be rejected.");
        var clone = activeState.Clone(); clone.Checkpoint!.Grains[0]++; clone.Colors[0] = Colors.Red;
        Check(clone.Checkpoint.Grains[0] != activeState.Checkpoint.Grains[0] && clone.Colors[0] != activeState.Colors[0], "Clones must own their arrays.");
        var catalogWatch = Stopwatch.StartNew();
        BitmapSource preview = await CatalogPreviewLoader.RenderAsync(tile, CancellationToken.None);
        Check(preview.IsFrozen && preview.PixelWidth == 512, "Catalog tile must publish a frozen 512px image.");
        Console.WriteLine($"Sandpile catalog: {catalogWatch.ElapsedMilliseconds} ms.");
        await VerifySandpileWindowAsync(activeState, output);
        Console.WriteLine("PASS (sandpile): independent reverse-order reference, abelian identity, mass/sinks, symmetry, presets, brush, cancellation, exact saved continuation, cloud and WPF lifecycle.");
    }

    private static void VerifySandpileReference(SandpileLattice lattice)
    {
        const int n = 33;
        var state = new SandpileState { Size = n, Lattice = lattice, Seed = SandpileSeed.Empty };
        var simulation = new SandpileSimulation(state); var heights = new long[n * n]; var odometer = new long[n * n];
        var random = new Random(234); long added = 0, lost = 0;
        for (int k = 0; k < 80; k++)
        {
            int x = random.Next(n), y = random.Next(n), amount = random.Next(1, 90);
            simulation.Add(x, y, amount); heights[y * n + x] += amount; added += amount;
        }
        // Independent reverse scan, firing just once per site; production batches all legal firings in a FIFO.
        bool changed;
        do
        {
            changed = false;
            for (int y = n - 1; y >= 0; y--) for (int x = n - 1; x >= 0; x--)
            {
                int i = y * n + x; if (heights[i] < state.Degree) continue;
                changed = true; heights[i] -= state.Degree; odometer[i]++;
                Send(x + 1, y); Send(x - 1, y); Send(x, y + 1); Send(x, y - 1);
                if (lattice == SandpileLattice.Triangular) { Send(x - 1, y + 1); Send(x + 1, y - 1); }
            }
        } while (changed);
        void Send(int x, int y) { if (x < 0 || y < 0 || x >= n || y >= n) lost++; else heights[y * n + x]++; }
        while (!simulation.IsStable) simulation.Advance(721, CancellationToken.None);
        var actual = simulation.Snapshot();
        Check(actual.Grains.SequenceEqual(heights) && actual.Topplings.SequenceEqual(odometer) && actual.Lost == lost,
            $"{lattice}: different legal toppling orders must give identical stable heights, odometer and boundary loss.");
        Check(actual.Grains.Sum() + actual.Lost == added && lost > 0, "Reference must exercise absorbing boundaries.");
        // stabilize(stabilize(A)+B) == stabilize(A+B).
        var incremental = new SandpileSimulation(state); incremental.Add(16, 16, 1200);
        while (!incremental.IsStable) incremental.Advance(50_000, CancellationToken.None);
        incremental.Add(16, 16, 800);
        while (!incremental.IsStable) incremental.Advance(50_000, CancellationToken.None);
        var combined = new SandpileSimulation(state); combined.Add(16, 16, 2000);
        while (!combined.IsStable) combined.Advance(50_000, CancellationToken.None);
        Check(incremental.Snapshot().Grains.SequenceEqual(combined.Snapshot().Grains), "Sequential grain addition must obey the abelian identity.");
    }

    private static async Task VerifySandpileWindowAsync(SandpileState state, string? output)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = (SandpileWindow)MainWindow.GetWindowFactory("Sandpile")!();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] parameters) => typeof(SandpileWindow).GetMethod(name, flags)!.Invoke(window, parameters);
        Task Reset(SandpileState s) => (Task)Invoke("ResetAsync", s, false)!;
        try
        {
            await Reset(state);
            Check(window.CaptureState("loaded").Checkpoint!.Grains.SequenceEqual(state.Checkpoint!.Grains), "Window must load the captured avalanche on pause without advancing.");
            long operations = window.CaptureState("before").Checkpoint!.Operations;
            ((ComboBox)window.FindName("PaletteBox")).SelectedIndex = 1;
            await (Task)Invoke("FreezeAsync")!;
            Check(window.CaptureState("palette").Checkpoint!.Operations == operations, "Changing color must not settle any sand.");
            Invoke("QueueBrush", 32, 32); await (Task)Invoke("FreezeAsync")!;
            Check(window.CaptureState("brush").Checkpoint!.Added == state.Checkpoint.Added + state.BrushGrains, "Paused brush must appear in the captured state.");
            await (Task)Invoke("ProduceFrameAsync", 1000, false)!;
            Check(window.CaptureState("step").Checkpoint!.Operations == operations + 1000, "A paused step must process its requested budget.");
            var first = state.Clone(); first.BrushGrains = 7;
            var latest = state.Clone(); latest.BrushGrains = 19;
            await Task.WhenAll(Reset(first), Reset(latest));
            Check(window.CaptureState("latest").BrushGrains == 19, "Rapid resets must only publish the latest state.");
            var empty = new SandpileState { Seed = SandpileSeed.Empty, AutoDrop = true, Size = 33, DropGrains = 4 };
            await Reset(empty); Invoke("SetRunning", true);
            var watch = Stopwatch.StartNew();
            while (window.CaptureState("timer").Checkpoint!.Added == 0 && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);
            await (Task)Invoke("FreezeAsync")!;
            Check(window.CaptureState("auto").Checkpoint!.Added > 0, "Dispatcher timer must drive automatic additions.");
            empty.AutoDrop = false;
            await Reset(empty); Invoke("SetRunning", true);
            await Task.Delay(80);
            Check(window.CaptureState("idle").Checkpoint!.Operations == 0, "Stable running fields must stay idle without automatic additions.");
            Invoke("QueueBrush", 16, 16);
            watch.Restart();
            while (window.CaptureState("paint-live").Checkpoint!.Operations == 0 && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);
            await (Task)Invoke("FreezeAsync")!;
            Check(window.CaptureState("painted").Checkpoint!.Operations > 0, "Painting into a stable running field must resume the avalanche automatically.");
            await Reset(state);
            if (output is not null)
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1200, 850)); content.Arrange(new Rect(0, 0, 1200, 850)); content.UpdateLayout();
                await (Task)Invoke("FreezeAsync")!;
                var bitmap = new RenderTargetBitmap(1200, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                SaveSandpilePng(bitmap, Path.Combine(output, "sandpile-window.png"));
            }
            Task worker = (Task)Invoke("ProduceFrameAsync", 500_000, false)!;
            window.Close(); await worker;
        }
        finally { window.Close(); }
    }

    private static void SaveSandpilePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
