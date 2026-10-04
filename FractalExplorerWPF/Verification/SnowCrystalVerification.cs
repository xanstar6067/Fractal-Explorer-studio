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
    private static async Task VerifySnowCrystalAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("snow-crystal");
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        var tile = FractalCatalog.Create().Single(item => item.LaunchKey == "SnowCrystal");
        Check(MainWindow.GetWindowFactory(tile.LaunchKey) is not null && CatalogPreviewLoader.IsRendered(tile), "Snow crystals need a launchable tile and an actual simulated preview.");
        VerifyCatalogData();
        VerifySnowReference();

        SnowCrystalState? defaultGrown = null;
        var frames = new List<byte[]>();
        foreach (SnowCrystalPreset preset in SnowCrystalPresets.All)
        {
            SnowCrystalState state = preset.CreateState();
            var simulation = new SnowCrystalSimulation(state);
            int lastCount = simulation.FrozenCount;
            for (int steps = 0; steps < preset.PreviewSteps && !simulation.BoundaryReached; steps += 50)
            {
                simulation.Advance(Math.Min(50, preset.PreviewSteps - steps), state, CancellationToken.None);
                Check(simulation.FrozenCount >= lastCount, "Ice cannot melt in Reiter's growth model."); lastCount = simulation.FrozenCount;
            }
            Check(simulation.FrozenCount > 100 && simulation.CrystalRadius > 8, $"{preset.Name} must form a visible crystal, got {simulation.FrozenCount} ice cells, radius {simulation.CrystalRadius}.");
            state.Checkpoint = simulation.Snapshot();
            state.SaveName = preset.Name; state.Timestamp = DateTime.Now;
            state.Zoom = Math.Min(20, state.Radius / (double)Math.Max(12, simulation.CrystalRadius));
            var pixels = SnowCrystalRenderer.RenderFrame(state.Checkpoint, state, 512, 512, CancellationToken.None);
            Check(frames.All(frame => !frame.SequenceEqual(pixels)), "The five growth presets must be visually distinct.");
            frames.Add(pixels);
            Check(pixels.Where((_, i) => i % 4 == 3).All(alpha => alpha == 255), "Crystal images must be opaque.");
            VerifySnowConnected(simulation);
            var store = new SnowCrystalSaveStore(); var slot = store.Save(state);
            SnowCrystalState restored = store.Load().Single(s => s.SaveName == state.SaveName);
            Check(SnowCrystalRenderer.RenderFrame(restored.Checkpoint!, restored, 512, 512, CancellationToken.None).SequenceEqual(pixels), "Disk round-trip must preserve the exact view and crystal.");
            var continued = new SnowCrystalSimulation(restored);
            simulation.Advance(37, state, CancellationToken.None); continued.Advance(37, restored, CancellationToken.None);
            Check(simulation.Snapshot().Mass.SequenceEqual(continued.Snapshot().Mass) && simulation.Snapshot().FrozenAt.SequenceEqual(continued.Snapshot().FrozenAt), "Loading must continue diffusion and freezing exactly.");
            LocalCloudSave cloud = CloudSaveRepository.ReadLocal(slot.FilePath);
            Check(cloud.Category == "SnowCrystal" && cloud.JsonData.Contains("Checkpoint"), "The shared cloud repository must include the complete crystal state.");
            var clone = state.Clone(); clone.Checkpoint!.Mass[0] += .1;
            Check(clone.Checkpoint.Mass[0] != state.Checkpoint.Mass[0], "Render/save clones must own checkpoint arrays.");
            if (defaultGrown is null) defaultGrown = state;
            if (output is not null) SaveSnowPng(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 512 * 4), Path.Combine(output, $"snow-{preset.Id}.png"));
            Console.WriteLine($"Snow {preset.Id}: {state.Checkpoint.StepCount} steps, {lastCount:N0} frozen cells, radius {simulation.CrystalRadius}, exact saved continuation.");
        }

        var small = new SnowCrystalState { Radius = 32, Vapor = .95, Deposition = .01 };
        var bounded = new SnowCrystalSimulation(small); bounded.Advance(20_000, small, CancellationToken.None);
        Check(bounded.BoundaryReached, "Growth must stop before reaching the vapor reservoir.");
        int stopped = bounded.StepCount; bounded.Advance(10, small, CancellationToken.None);
        Check(bounded.StepCount == stopped, "A boundary-limited crystal must stay intact.");

        var slow = new SnowCrystalState { Radius = 64 };
        var cancelable = new SnowCrystalSimulation(slow);
        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(5);
            bool canceled = false;
            try { await Task.Run(() => cancelable.Advance(100_000, slow, cts.Token)); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Canceling growth must stop its worker.");
        }
        var repeat = new SnowCrystalSimulation(slow); repeat.Advance(cancelable.StepCount, slow, CancellationToken.None);
        Check(repeat.Snapshot().Mass.SequenceEqual(cancelable.Snapshot().Mass), "Cancellation must commit only complete time steps.");
        SnowCrystalState broken = slow.Clone(); broken.Checkpoint = cancelable.Snapshot(); broken.Checkpoint.Mass[0] = double.NaN;
        bool invalid = false; try { _ = new SnowCrystalSimulation(broken); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "A corrupted checkpoint must be rejected.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool canceled = false;
            try { await SnowCrystalRenderer.RenderStateAsync(defaultGrown!, 512, 512, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Snapshot export must support cancellation.");
        }
        BitmapSource preview = await CatalogPreviewLoader.RenderAsync(tile, CancellationToken.None);
        Check(preview.IsFrozen && preview.PixelWidth == 512, "Catalog preview must be a transferable rendered bitmap.");
        if (output is not null) SaveSnowPng(preview, Path.Combine(output, "snow-catalog.png"));
        await VerifySnowWindowAsync(defaultGrown!, output);
        Console.WriteLine("PASS (snow-crystal): independent local-rule reference, D6 symmetry, connected ice, five distinct presets, boundary stop, cancellation, exact saved continuation, cloud and WPF lifecycle.");
    }

    private static void VerifySnowReference()
    {
        // Independent full-plane implementation of the author's split/diffuse/recombine rule.
        const int radius = 32, side = radius * 2 + 1;
        var state = new SnowCrystalState { Radius = radius };
        var simulation = new SnowCrystalSimulation(state);
        var mass = new double[side * side]; var next = new double[mass.Length]; var air = new double[mass.Length];
        var receptive = new bool[mass.Length]; var frozen = new bool[mass.Length];
        int Id(int q, int r) => (r + radius) * side + q + radius;
        bool Valid(int q, int r) => Math.Abs(q) <= radius && Math.Abs(r) <= radius && Math.Abs(q + r) <= radius;
        (int, int)[] directions = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
        Array.Fill(mass, state.Vapor); mass[Id(0, 0)] = 1; frozen[Id(0, 0)] = true;
        for (int step = 0; step < 240; step++)
        {
            for (int r = -radius; r <= radius; r++)
            for (int q = -radius; q <= radius; q++)
            {
                if (!Valid(q, r)) continue;
                int i = Id(q, r); bool receiving = frozen[i];
                foreach (var (dq, dr) in directions) if (Valid(q + dq, r + dr)) receiving |= frozen[Id(q + dq, r + dr)];
                receptive[i] = receiving; air[i] = receiving ? 0 : mass[i];
            }
            for (int r = -radius; r <= radius; r++)
            for (int q = -radius; q <= radius; q++)
            {
                if (!Valid(q, r)) continue;
                int i = Id(q, r);
                if (Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r))) == radius) { next[i] = state.Vapor; continue; }
                double sum = 0; foreach (var (dq, dr) in directions) sum += air[Id(q + dq, r + dr)];
                next[i] = (receptive[i] ? mass[i] + state.Deposition : 0) + .5 * air[i] + sum / 12;
            }
            (mass, next) = (next, mass);
            for (int i = 0; i < mass.Length; i++) frozen[i] = mass[i] >= 1;
        }
        simulation.Advance(240, state, CancellationToken.None); var snapshot = simulation.Snapshot();
        double error = 0;
        for (int r = -radius; r <= radius; r++)
        for (int q = -radius; q <= radius; q++)
        {
            int index = simulation.Lattice.Index(q, r); if (index < 0) continue;
            error = Math.Max(error, Math.Abs(mass[Id(q, r)] - snapshot.Mass[index]));
            Check(index == simulation.Lattice.Index(-r, q + r) && index == simulation.Lattice.Index(r, q), "The sector must preserve 60-degree rotation and mirror symmetry.");
        }
        Check(error < 1e-10, $"Sector update must agree with an independent full-plane simulation: {error:G5}.");
    }

    private static void VerifySnowConnected(SnowCrystalSimulation simulation)
    {
        var snapshot = simulation.Snapshot(); var lattice = simulation.Lattice;
        var seen = new HashSet<(int Q, int R)>(); var queue = new Queue<(int Q, int R)>();
        seen.Add((0, 0)); queue.Enqueue((0, 0));
        (int, int)[] directions = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
        while (queue.TryDequeue(out var point))
            foreach (var (dq, dr) in directions)
            {
                var n = (Q: point.Q + dq, R: point.R + dr); int index = lattice.Index(n.Q, n.R);
                if (index >= 0 && snapshot.FrozenAt[index] >= 0 && seen.Add(n)) queue.Enqueue(n);
            }
        Check(seen.Count == simulation.FrozenCount, "Every ice cell must connect to the central seed through hexagonal neighbors.");
    }

    private static async Task VerifySnowWindowAsync(SnowCrystalState grown, string? output)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = (SnowCrystalWindow)MainWindow.GetWindowFactory("SnowCrystal")!();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] parameters) => typeof(SnowCrystalWindow).GetMethod(name, flags)!.Invoke(window, parameters);
        async Task Reset(SnowCrystalState state) => await (Task)Invoke("ResetAsync", state, false)!;
        try
        {
            await Reset(grown);
            SnowCrystalState captured = window.CaptureState("window");
            Check(captured.Checkpoint!.Mass.SequenceEqual(grown.Checkpoint!.Mass), "Window loading must restore the field without extra growth.");
            ((TextBox)window.FindName("DepositionBox")).Text = "0,003";
            Invoke("Apply_OnClick", window, new RoutedEventArgs());
            await (Task)typeof(SnowCrystalWindow).GetField("_workerIdle", flags)!.GetValue(window)!;
            captured = window.CaptureState("changed");
            Check(captured.Deposition == .003 && captured.Checkpoint!.StepCount == grown.Checkpoint.StepCount, "Changing conditions must retain the crystal and accept decimal commas.");
            await (Task)Invoke("ProduceFrameAsync", 1)!;
            captured = window.CaptureState("step");
            Check(captured.Checkpoint!.StepCount == grown.Checkpoint.StepCount + 1, "One step must advance exactly once while paused.");
            int beforeColor = captured.Checkpoint.StepCount;
            ((ComboBox)window.FindName("PaletteBox")).SelectedIndex = 1;
            await (Task)typeof(SnowCrystalWindow).GetField("_workerIdle", flags)!.GetValue(window)!;
            Check(window.CaptureState("palette").Checkpoint!.StepCount == beforeColor, "Color changes must not advance growth.");
            SnowCrystalState first = grown.Clone(); first.Vapor = .35;
            SnowCrystalState latest = grown.Clone(); latest.Vapor = .8;
            await Task.WhenAll(Reset(first), Reset(latest));
            Check(window.CaptureState("latest").Vapor == .8, "Rapid loading must publish only the latest requested field.");
            await Reset(grown);
            int timerStart = window.CaptureState("timer-start").Checkpoint!.StepCount;
            Invoke("SetRunning", true);
            var timerWatch = System.Diagnostics.Stopwatch.StartNew();
            while (window.CaptureState("timer").Checkpoint!.StepCount == timerStart && timerWatch.ElapsedMilliseconds < 3000)
                await Task.Delay(20);
            Invoke("SetRunning", false);
            await (Task)Invoke("RefreshPausedAsync")!;
            Check(window.CaptureState("timer-end").Checkpoint!.StepCount > timerStart, "The live dispatcher timer must advance and present the crystal.");
            await Reset(grown);
            if (output is not null)
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1180, 780)); content.Arrange(new Rect(0, 0, 1180, 780)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1180, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                SaveSnowPng(bitmap, Path.Combine(output, "snow-window.png"));
            }
            Task worker = (Task)Invoke("ProduceFrameAsync", 128)!;
            window.Close(); await worker;
            Check((bool)typeof(SnowCrystalWindow).GetField("_closed", flags)!.GetValue(window)!, "Closing must cancel workers and prevent stale presentation.");
        }
        finally { window.Close(); }
    }

    private static void SaveSnowPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
