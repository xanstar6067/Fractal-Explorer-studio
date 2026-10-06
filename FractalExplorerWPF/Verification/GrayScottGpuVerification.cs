using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task MeasureGrayScottFramesAsync()
    {
        using var sandbox = DataSandbox.Create("gray-scott-perf");
        await Task.Run(() =>
        {
            var state = GrayScottPresets.All[0].State.Clone();
            const int width = 1024, height = 768, frames = 6;
            foreach (GrayScottBackend backend in new[] { GrayScottBackend.Cpu, GrayScottBackend.Gpu })
            {
                state.Backend = backend;
                using IGrayScottEngine engine = GrayScottEngineFactory.Create(state, out string? fallback);
                Check(engine.Backend == backend, $"Benchmark requires the requested engine: {fallback}.");
                Console.WriteLine($"Gray–Scott {backend}: {engine.DeviceName}, {state.GridSize}², {state.StepsPerFrame} steps/frame, {width}×{height} pixels.");
                engine.Advance(1, CancellationToken.None); engine.Snapshot();
                engine.RenderFrame(state, width, height, CancellationToken.None);
                double advance = 0, snapshot = 0, render = 0, slowest = 0;
                var watch = new Stopwatch();
                for (int i = 0; i < frames; i++)
                {
                    var frameWatch = Stopwatch.StartNew();
                    watch.Restart(); engine.Advance(state.StepsPerFrame, CancellationToken.None); advance += watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); engine.Snapshot(); snapshot += watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); engine.RenderFrame(state, width, height, CancellationToken.None); render += watch.Elapsed.TotalMilliseconds;
                    slowest = Math.Max(slowest, frameWatch.Elapsed.TotalMilliseconds);
                }
                double total = (advance + snapshot + render) / frames;
                Console.WriteLine($"Gray–Scott {backend}: evolve {advance / frames:F2} ms, snapshot {snapshot / frames:F2} ms, render/readback {render / frames:F2} ms; total {total:F2} ms, slowest {slowest:F2} ms ({1000 / total:F1} frames/s before WPF presentation).");
            }
        });
        await MeasureGrayScottWindowFramesAsync();
    }

    private static async Task MeasureGrayScottWindowFramesAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        foreach (GrayScottBackend backend in new[] { GrayScottBackend.Cpu, GrayScottBackend.Gpu })
        {
            var window = new GrayScottWindow();
            object? Invoke(string name, params object[] p) => typeof(GrayScottWindow).GetMethod(name, flags)!.Invoke(window, p);
            T Field<T>(string name) => (T)typeof(GrayScottWindow).GetField(name, flags)!.GetValue(window)!;
            try
            {
                var root = (FrameworkElement)window.Content;
                root.Measure(new System.Windows.Size(1280, 800)); root.Arrange(new Rect(0, 0, 1280, 800)); root.UpdateLayout();
                var state = GrayScottPresets.All[0].State.Clone(); state.Backend = backend;
                await (Task)Invoke("InstallEngineAsync", state, false, true)!;
                Check(Field<IGrayScottEngine>("_simulation").Backend == backend, "Window benchmark requires the requested backend.");
                await Task.Delay(250); await Field<Task>("_frameIdleTask");
                var before = window.CaptureState("before");
                Field<Stopwatch>("_fpsWatch").Restart();
                typeof(GrayScottWindow).GetField("_presentedFrames", flags)!.SetValue(window, 0);
                Invoke("SetRunning", true); await Task.Delay(3000); Invoke("SetRunning", false);
                await Field<Task>("_frameIdleTask");
                var after = window.CaptureState("after"); var bitmap = Field<WriteableBitmap>("_bitmap");
                Console.WriteLine($"Gray–Scott WPF {backend}: {Field<double>("_measuredFps"):F1} FPS at target {state.TargetFps}, {state.GridSize}², {state.StepsPerFrame} steps/frame, {bitmap.PixelWidth}×{bitmap.PixelHeight} pixels; advanced {after.Checkpoint!.StepCount - before.Checkpoint!.StepCount} steps (hidden window, including timer and WritePixels).");
            }
            finally { window.Close(); }
        }
    }

    private static async Task VerifyGrayScottGpuAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("gray-scott-gpu");
        string? output = args.Length > 1 ? Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName : null;
        var state = GrayScottPresets.All[0].State.Clone(); state.GridSize = 64;
        foreach (GrayScottSeedMode seed in Enum.GetValues<GrayScottSeedMode>())
        {
            state.SeedMode = seed;
            using var gpu = new GrayScottGpuEngine(state); using var cpu = new GrayScottCpuEngine(state);
            Check(gpu.Snapshot().U.SequenceEqual(cpu.Snapshot().U) && gpu.Snapshot().V.SequenceEqual(cpu.Snapshot().V), "Both engines must start from exactly the same seed.");
            cpu.Inject(0, .01, 8); gpu.Inject(0, .01, 8);
            Check(gpu.Snapshot().U.SequenceEqual(cpu.Snapshot().U) && gpu.Snapshot().V.SequenceEqual(cpu.Snapshot().V), "GPU brush must wrap at the boundaries identically to CPU.");
            var initial = cpu.Snapshot(); cpu.Advance(1, CancellationToken.None);
            var one = cpu.Snapshot();
            int n = state.GridSize;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int index = y * n + x; double u = initial.U[index], v = initial.V[index], lu = 0, lv = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    double weight = dx == 0 && dy == 0 ? -1 : dx == 0 || dy == 0 ? .2 : .05;
                    int neighbor = ((y + dy + n) % n) * n + (x + dx + n) % n;
                    lu += weight * initial.U[neighbor]; lv += weight * initial.V[neighbor];
                }
                double reaction = u * v * v;
                Check(Math.Abs(one.U[index] - Math.Clamp(u + (state.DiffusionU * lu - reaction + state.Feed * (1 - u)) * state.DeltaTime, 0, 1)) < 2e-7 &&
                    Math.Abs(one.V[index] - Math.Clamp(v + (state.DiffusionV * lv + reaction - (state.Feed + state.Kill) * v) * state.DeltaTime, 0, 1)) < 2e-7,
                    "Gray–Scott must agree with an independent toroidal stencil.");
            }
            cpu.Advance(9, CancellationToken.None); gpu.Advance(10, CancellationToken.None);
            var actual = gpu.Snapshot(); var expected = cpu.Snapshot();
            double error = Math.Max(actual.U.Zip(expected.U, (a,b) => Math.Abs(a-b)).Max(), actual.V.Zip(expected.V, (a,b) => Math.Abs(a-b)).Max());
            Check(error < 3e-6, $"GPU evolution must match CPU for {seed}: {error:G4}.");
            foreach (GrayScottFieldMode mode in Enum.GetValues<GrayScottFieldMode>())
            {
                state.FieldMode = mode; state.ReversePalette = true; state.Palette.Gamma = 1.4;
                byte[] pixels = gpu.RenderFrame(state, 336, 192, CancellationToken.None, 1.3);
                byte[] reference = GrayScottRenderer.RenderFrame(actual, state, 336, 192, CancellationToken.None, 1.3);
                Check(pixels.Zip(reference, (a,b) => Math.Abs(a-b)).Average() < 1, "GPU bilinear colouring must agree with CPU, including mismatched manual aspect.");
            }
            Console.WriteLine($"Gray–Scott {gpu.DeviceName}, {seed}: 10-step error {error:G4}.");
        }
        state.FieldMode = GrayScottFieldMode.V; state.ReversePalette = false; state.Palette.Gamma = 1;
        state.SeedMode = GrayScottSeedMode.Noise;
        using (var gpu = new GrayScottGpuEngine(state))
        {
            gpu.Advance(100, CancellationToken.None); state.Checkpoint = gpu.Snapshot(); state.SaveName = "Exact GPU";
            var store = new GrayScottSaveStore(); store.Save(state); var loaded = store.Load().Single();
            Check(loaded.Checkpoint!.U.SequenceEqual(state.Checkpoint.U) && loaded.Checkpoint.V.SequenceEqual(state.Checkpoint.V), "Disk checkpoint compression must preserve both fields exactly.");
            using var restored = new GrayScottGpuEngine(loaded); gpu.Advance(15, CancellationToken.None); restored.Advance(15, CancellationToken.None);
            Check(gpu.Snapshot().U.SequenceEqual(restored.Snapshot().U) && gpu.Snapshot().V.SequenceEqual(restored.Snapshot().V), "Restored GPU field must continue exactly on this device.");
            var before = gpu.Snapshot(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { gpu.Advance(10, cancel.Token); throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
            Check(before.U.SequenceEqual(gpu.Snapshot().U), "Pre-canceled GPU work must leave the field unchanged.");
            using var during = new CancellationTokenSource(); during.CancelAfter(5);
            try { gpu.Advance(2000, during.Token); } catch (OperationCanceledException) { }
            var after = gpu.Snapshot(); var replayState = state.Clone(); replayState.Checkpoint = before;
            using var replay = new GrayScottGpuEngine(replayState); replay.Advance((int)(after.StepCount - before.StepCount), CancellationToken.None);
            Check(after.U.SequenceEqual(replay.Snapshot().U) && after.V.SequenceEqual(replay.Snapshot().V), "Mid-work cancellation must retain complete GPU steps.");
            BitmapSource preview = await GrayScottRenderer.RenderPreviewAsync(loaded, 240, 160, CancellationToken.None);
            byte[] previewBytes = new byte[240 * 160 * 4]; preview.CopyPixels(previewBytes, 960, 0);
            using var view = new GrayScottGpuEngine(loaded);
            Check(previewBytes.SequenceEqual(view.RenderFrame(loaded, 240, 160, CancellationToken.None)), "Checkpoint preview must not add simulation steps.");
        }
        foreach (int size in new[] { 33, 127, 511 })
        {
            var odd = new GrayScottState { GridSize = size, SeedMode = GrayScottSeedMode.Noise };
            using var gpu = new GrayScottGpuEngine(odd); using var cpu = new GrayScottCpuEngine(odd);
            gpu.Advance(13, CancellationToken.None); cpu.Advance(13, CancellationToken.None);
            var actual = gpu.Snapshot(); var expected = cpu.Snapshot();
            double error = Math.Max(actual.U.Zip(expected.U, (a, b) => Math.Abs(a - b)).Max(),
                actual.V.Zip(expected.V, (a, b) => Math.Abs(a - b)).Max());
            Check(error < 3e-6, $"Periodic GPU neighbours must match CPU on a non-power-of-two, partial-group grid {size}: {error:G4}.");
            byte[] pixels = gpu.RenderFrame(odd, size, size, CancellationToken.None);
            byte[] reference = GrayScottRenderer.RenderFrame(actual, odd, size, size, CancellationToken.None);
            Check(pixels.Zip(reference, (a, b) => Math.Abs(a - b)).Average() < 1, "Rendering must preserve periodic bilinear samples at all four edges on odd grids.");
        }
        foreach (int size in new[] { 192, 1024, 2048 })
        {
            var large = new GrayScottState { GridSize = size }; using var gpu = new GrayScottGpuEngine(large);
            gpu.Advance(2, CancellationToken.None); var cp = gpu.Snapshot();
            Check(cp.StepCount == 2 && cp.U.Concat(cp.V).All(v => float.IsFinite(v) && v is >= 0 and <= 1), "Large fields must remain finite and bounded.");
        }
        var benchmark = new GrayScottState { GridSize = 512 };
        using (var gpu = new GrayScottGpuEngine(benchmark)) using (var cpu = new GrayScottCpuEngine(benchmark))
        {
            gpu.Advance(10, CancellationToken.None); cpu.Advance(5, CancellationToken.None);
            var watch = Stopwatch.StartNew(); cpu.Advance(50, CancellationToken.None); double cpuMs = watch.Elapsed.TotalMilliseconds / 50;
            watch.Restart(); gpu.Advance(500, CancellationToken.None); double gpuMs = watch.Elapsed.TotalMilliseconds / 500;
            Console.WriteLine($"Gray–Scott 512 benchmark: CPU {cpuMs:F3} ms/step, GPU {gpuMs:F3} ms/step ({cpuMs/gpuMs:F1}×), {gpu.DeviceName}.");
        }
        var legacy = System.Text.Json.Nodes.JsonNode.Parse("{\"SaveFormatVersion\":2,\"GridSize\":256}")!.AsObject();
        SaveFormat.UpgradeInPlace("GrayScott", legacy); Check(legacy["Backend"]!.GetValue<int>() == 0, "Legacy Gray–Scott saves must keep their CPU backend.");
        Check(GrayScottComputeShader.CacheEntries.All(e => File.Exists(AppPaths.GetShaderCacheFile(e.Key))), "All Gray–Scott shaders must enter the common disk cache.");
        try
        {
            GrayScottEngineFactory.GpuFactoryOverrideForTests = _ => throw new IOException("Simulated missing GPU");
            using var fallback = GrayScottEngineFactory.Create(state, out string? reason);
            Check(fallback.Backend == GrayScottBackend.Cpu && reason!.Contains("Simulated missing GPU") && fallback.Snapshot().U.SequenceEqual(state.Checkpoint!.U), "Missing GPU must preserve the checkpoint on CPU.");
        }
        finally { GrayScottEngineFactory.GpuFactoryOverrideForTests = null; }
        await VerifyGrayScottPresetsAsync(args, gpu: true);
        await VerifyGrayScottWindowAsync(output);
        Console.WriteLine("PASS (gray-scott-gpu): independent stencil, hardware compute, periodic brush, bilinear display, exact disk continuation, large grids, cache, switching, fallback and DPI/manual buffers.");
    }

    private static async Task VerifyGrayScottWindowAsync(string? output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new GrayScottWindow();
        object? Invoke(string name, params object[] p) => typeof(GrayScottWindow).GetMethod(name, flags)!.Invoke(window, p);
        T Field<T>(string name) => (T)typeof(GrayScottWindow).GetField(name, flags)!.GetValue(window)!;
        async Task Idle()
        {
            await Task.Delay(220); var watch = Stopwatch.StartNew();
            while ((Field<bool>("_resetting") || Field<bool>("_executionPending") || Field<bool>("_frameBusy")) && watch.ElapsedMilliseconds < 10000) await Task.Delay(10);
            Check(watch.ElapsedMilliseconds < 10000, "Window operation must finish.");
        }
        var root = (FrameworkElement)window.Content;
        void Layout(int w, int h) { root.Measure(new System.Windows.Size(w,h)); root.Arrange(new Rect(0,0,w,h)); root.UpdateLayout(); }
        try
        {
            await (Task)Invoke("InstallEngineAsync", new GrayScottState { GridSize = 128 }, false, true)!;
            Layout(1280,800); await Idle(); await (Task)Invoke("ProduceFrameAsync", (int?)20)!;
            var before = window.CaptureState("before"); Check(before.Backend == GrayScottBackend.Gpu, "GPU must be the default engine.");
            var canvas = (FrameworkElement)window.FindName("CanvasHost"); var dpi = VisualTreeHelper.GetDpi(canvas); var bitmap = Field<WriteableBitmap>("_bitmap");
            Check(bitmap.PixelWidth == (int)Math.Ceiling(canvas.ActualWidth * dpi.DpiScaleX) && bitmap.PixelHeight == (int)Math.Ceiling(canvas.ActualHeight * dpi.DpiScaleY), "Auto buffer must follow physical canvas pixels.");
            ((TextBox)window.FindName("FeedBox")).Text = "0.07";
            ((ComboBox)window.FindName("BackendBox")).SelectedIndex = 1; await Idle();
            var cpu = window.CaptureState("CPU");
            Check(cpu.Backend == GrayScottBackend.Cpu && cpu.Checkpoint!.U.SequenceEqual(before.Checkpoint!.U) && cpu.Checkpoint.V.SequenceEqual(before.Checkpoint.V) && cpu.Checkpoint.StepCount == before.Checkpoint.StepCount, "Switching to CPU must preserve fields and time.");
            Check(((TextBox)window.FindName("FeedBox")).Text == "0.07" && cpu.Feed == before.Feed, "Engine switching must preserve pending equation edits without applying them.");
            ((ComboBox)window.FindName("BackendBox")).SelectedIndex = 0; await Idle();
            Check(window.CaptureState("GPU").Checkpoint!.U.SequenceEqual(cpu.Checkpoint!.U), "Switching back to GPU must preserve all concentration bits.");
            ((TextBox)window.FindName("GridSizeBox")).Text = "192"; Invoke("GridSize_OnApply",window,new RoutedEventArgs()); await Idle();
            Check(window.CaptureState("resized").Checkpoint!.StepCount == cpu.Checkpoint.StepCount && window.CaptureState("resized").GridSize == 192, "Resizing must preserve time.");
            ((TextBox)window.FindName("GridSizeBox")).Text = "99999"; Invoke("GridSize_OnApply",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid").GridSize == 192 && !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("GridSizeError")).Text), "Invalid grids must not change the field.");
            ((TextBox)window.FindName("GridSizeBox")).Text = "192";
            ((CheckBox)window.FindName("AutoFrameBox")).IsChecked = false;
            ((TextBox)window.FindName("FrameWidthBox")).Text = "336"; ((TextBox)window.FindName("FrameHeightBox")).Text = "192";
            Invoke("FrameSize_OnApply",window,new RoutedEventArgs()); await Idle(); bitmap = Field<WriteableBitmap>("_bitmap");
            Check(bitmap.PixelWidth == 336 && bitmap.PixelHeight == 192, "Manual frame size must reach the renderer.");
            ((TextBox)window.FindName("FrameWidthBox")).Text = "99999"; Invoke("FrameSize_OnApply",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid frame").FrameWidth == 336 && !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("FrameSizeError")).Text), "Invalid buffers must preserve the previous size.");
            ((CheckBox)window.FindName("AutoFrameBox")).IsChecked = true; Invoke("FrameSize_OnApply",window,new RoutedEventArgs()); Layout(1400,900); await Idle();
            Check(Field<WriteableBitmap>("_bitmap").PixelWidth == (int)Math.Ceiling(canvas.ActualWidth * dpi.DpiScaleX), "Auto mode must resume following resize.");
            var count = window.CaptureState("colour before").Checkpoint!.StepCount;
            ((ComboBox)window.FindName("FieldModeBox")).SelectedIndex = 1; await Idle();
            Check(window.CaptureState("colour after").Checkpoint!.StepCount == count, "Appearance changes must repaint without advancing time.");
            Invoke("SetRunning",true); await Task.Delay(150); Invoke("SetRunning",false); await Idle();
            Check(window.CaptureState("timer").Checkpoint!.StepCount > count && !Field<System.Windows.Threading.DispatcherTimer>("_frameTimer").IsEnabled,
                "The timer must advance the selected engine, then stop on pause.");
            Invoke("SetRunning",true); ((ComboBox)window.FindName("BackendBox")).SelectedIndex = 1;
            var switchingWatch = Stopwatch.StartNew();
            while ((Field<bool>("_resetting") || Field<bool>("_executionPending")) && switchingWatch.ElapsedMilliseconds < 10000) await Task.Delay(10);
            Check(Field<bool>("_running") && window.CaptureState("resumed CPU").Backend == GrayScottBackend.Cpu, "Engine switching must resume a running simulation.");
            Invoke("SetRunning",false); await Idle();
            ((ComboBox)window.FindName("BackendBox")).SelectedIndex = 0; await Idle();
            var mitosis = GrayScottPresets.All.Single(p => p.Id == "mitosis");
            ((ComboBox)window.FindName("PresetBox")).SelectedItem = mitosis; await Idle();
            Invoke("SetRunning",false); await Idle();
            var applied = window.CaptureState("preset");
            Check(applied.DiffusionU == mitosis.State.DiffusionU && applied.DiffusionV == mitosis.State.DiffusionV &&
                applied.Feed == mitosis.State.Feed && applied.Kill == mitosis.State.Kill &&
                applied.StepsPerFrame == mitosis.State.StepsPerFrame && applied.SeedRadius == mitosis.State.SeedRadius,
                "Selecting a preset must install its corrected equation, speed and viable seeds.");
            Check(((TextBlock)window.FindName("PresetHint")).Text == mitosis.Description,
                "The selected preset must explain its expected evolution.");
            if (output is not null)
            {
                var pretty = GrayScottPresets.All[0].State.Clone(); using var engine = new GrayScottGpuEngine(pretty);
                engine.Advance(900, CancellationToken.None); pretty.Checkpoint = engine.Snapshot();
                await (Task)Invoke("InstallEngineAsync",pretty,false,true)!; await Idle();
                ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToTop(); root.UpdateLayout();
                var image = new RenderTargetBitmap(1400,900,96,96,PixelFormats.Pbgra32); image.Render(root);
                SaveTuringPng(image,Path.Combine(output,"gray-scott-gpu-window.png"));
                Layout(920,620); await Idle();
                image = new RenderTargetBitmap(920,620,96,96,PixelFormats.Pbgra32); image.Render(root);
                SaveTuringPng(image,Path.Combine(output,"gray-scott-minimum.png"));
            }
            var saved = window.CaptureState("load"); await (Task)Invoke("ProduceFrameAsync", (int?)1)!;
            window.LoadState(saved); await Idle();
            Check(!Field<bool>("_running") && window.CaptureState("loaded").Checkpoint!.U.SequenceEqual(saved.Checkpoint!.U), "Loading a checkpoint must restore the exact field on pause.");
            try
            {
                GrayScottEngineFactory.GpuFactoryOverrideForTests = s => new FailingGrayScottEngine(s);
                await (Task)Invoke("InstallEngineAsync",saved,false,true)!;
                await (Task)Invoke("ProduceFrameAsync", (int?)1)!;
                Check(window.CaptureState("recovery").Backend == GrayScottBackend.Cpu && window.CaptureState("recovery").Checkpoint!.U.SequenceEqual(saved.Checkpoint!.U), "Runtime GPU failure must recover the displayed field on CPU.");
            }
            finally { GrayScottEngineFactory.GpuFactoryOverrideForTests = null; }
            var runningState = saved.Clone(); runningState.Backend = GrayScottBackend.Gpu;
            await (Task)Invoke("InstallEngineAsync",runningState,false,true)!;
            Task worker = (Task)Invoke("ProduceFrameAsync", (int?)64)!; window.Close(); await worker;
        }
        finally { window.Close(); }
    }

    private sealed class FailingGrayScottEngine(GrayScottState state) : IGrayScottEngine
    {
        private readonly GrayScottCpuEngine _cpu = new(state);
        public int Size => _cpu.Size;
        public GrayScottBackend Backend => GrayScottBackend.Gpu;
        public string DeviceName => "Simulated GPU failure";
        public void Advance(int steps, CancellationToken token) => throw new IOException("Simulated device removal");
        public void Inject(double x, double y, int radius) => _cpu.Inject(x,y,radius);
        public GrayScottSnapshot Snapshot() => _cpu.Snapshot();
        public byte[] RenderFrame(GrayScottState state,int width,int height,CancellationToken token,double displayAspect=0) => _cpu.RenderFrame(state,width,height,token,displayAspect);
        public void Dispose() => _cpu.Dispose();
    }
}
