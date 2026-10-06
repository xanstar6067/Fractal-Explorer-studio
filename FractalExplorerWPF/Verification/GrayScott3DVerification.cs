using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyGrayScott3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("gray-scott3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        if (args.Contains("--probe"))
        {
            using var probeRenderer = new Fractal3DRenderer(); int index = 0;
            foreach (var pair in new[] { (.035, .060), (.03, .062), (.04, .062), (.022, .051), (.03, .058), (.026, .051) })
            {
                var view = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.GrayScott3D);
                view.GrayScott = view.GrayScott with { Feed = pair.Item1, Kill = pair.Item2 };
                using var engine = new GrayScott3DGpuEngine(view.GrayScott);
                for (int stage = 0; stage < 2; stage++)
                {
                    await Task.Run(() => engine.Advance(1200, CancellationToken.None));
                    view.GrayScott = view.GrayScott with { Field = engine.Snapshot() };
                    Console.WriteLine($"Probe {index}: {pair}, step {view.GrayScott.Field.Step}, Vmax {view.GrayScott.Field.Concentrations.ToArray().Where((_,i) => i % 2 == 1).Max():F3}");
                    if (output is not null)
                    {
                        Save(await probeRenderer.RenderAsync(view, 320, 320, null, CancellationToken.None), Path.Combine(output, $"probe-{index}-{stage}.png"));
                        var slice = view.Clone(); slice.GrayScott = slice.GrayScott with { CutAxis = 3 };
                        Save(await probeRenderer.RenderAsync(slice, 320, 320, null, CancellationToken.None), Path.Combine(output, $"probe-{index}-{stage}-cut.png"));
                    }
                }
                index++;
            }
            return;
        }
        var kind = Fractal3DKind.GrayScott3D;
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "Gray–Scott 3D must appear in the cross-category 3D collection.");

        // Independent flux accumulation: every face contributes opposite flux to its two cells.
        const int n = 32; var random = new Random(12); var initial = new float[n * n * n * 2];
        for (int i = 0; i < initial.Length; i += 2) { initial[i] = (float)(.5 + random.NextDouble() * .2); initial[i + 1] = (float)(.05 + random.NextDouble() * .1); }
        var settings = new GrayScott3DSettings { Size = n, Field = new(n, 17, initial), Backend = GrayScottBackend.Cpu };
        double[] expected = initial.Select(v => (double)v).ToArray();
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int a = ((z * n + y) * n + x) * 2;
            int[] neighbors = [((z * n + y) * n + (x + 1) % n) * 2,
                ((z * n + (y + 1) % n) * n + x) * 2, ((((z + 1) % n) * n + y) * n + x) * 2];
            foreach (int b in neighbors) for (int channel = 0; channel < 2; channel++)
            {
                double flux = (initial[b + channel] - initial[a + channel]) * (channel == 0 ? settings.DiffusionU : settings.DiffusionV);
                expected[a + channel] += flux; expected[b + channel] -= flux;
            }
            double u = initial[a], v = initial[a + 1], reaction = u * v * v;
            expected[a] += -reaction + settings.Feed * (1 - u);
            expected[a + 1] += reaction - (settings.Feed + settings.Kill) * v;
        }
        using var cpu = new GrayScott3DSimulation(settings); cpu.Advance(1, CancellationToken.None);
        var one = cpu.Snapshot();
        Check(one.Step == 18 && one.Concentrations.ToArray().Zip(expected).Max(p => Math.Abs(p.First - p.Second)) < 2e-7,
            "The local 3D rule must match independent face fluxes plus chemical reaction, including wrapped edges.");
        using var gpu = new GrayScott3DGpuEngine(settings); gpu.Advance(1, CancellationToken.None);
        Check(MaxDifference(one, gpu.Snapshot()) < 3e-7, "The GPU must implement the same simultaneous 3D update.");
        cpu.Advance(60, CancellationToken.None); gpu.Advance(60, CancellationToken.None);
        Check(MaxDifference(cpu.Snapshot(), gpu.Snapshot()) < 3e-5, "CPU/GPU evolution must agree over multiple steps.");

        var empty = settings with { Field = null, SeedShape = GrayScott3DSeed.Empty };
        using var brushCpu = new GrayScott3DSimulation(empty); using var brushGpu = new GrayScott3DGpuEngine(empty);
        brushCpu.Inject(0, .5, .5, .12); brushGpu.Inject(0, .5, .5, .12);
        Check(MaxDifference(brushCpu.Snapshot(), brushGpu.Snapshot()) == 0, "Periodic spherical brushes must match exactly.");
        var injected = brushCpu.Snapshot().Concentrations.ToArray();
        Check(injected[((16 * n + 16) * n) * 2 + 1] > 0 && injected[((16 * n + 16) * n + 31) * 2 + 1] > 0,
            "A brush on the boundary must reach both sides of the periodic volume.");
        using (var cancellation = new CancellationTokenSource())
        {
            var before = brushCpu.Snapshot(); cancellation.Cancel();
            try { brushCpu.Advance(100, cancellation.Token); throw new Exception("Canceled simulation advanced."); }
            catch (OperationCanceledException) { }
            Check(MaxDifference(before, brushCpu.Snapshot()) == 0 && before.Step == brushCpu.Snapshot().Step,
                "Cancellation before a step must preserve the checkpoint.");
        }
        foreach (int side in new[] { 33, 128 })
        {
            using var sized = new GrayScott3DGpuEngine(empty with { Size = side });
            sized.Inject(.5, .5, .5, .08); sized.Advance(2, CancellationToken.None);
            var field = sized.Snapshot();
            Check(field.Size == side && field.Step == 2 && field.Concentrations.ToArray().Where((_,i) => i % 2 == 1).Max() > .2,
                "Both the maximum grid and incomplete compute thread groups must evolve correctly.");
        }

        var options = JsonOptionsFactory.Create(); var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.GrayScott = settings with { Field = cpu.Snapshot() }; state.SaveName = "3D exact continuation";
        string json = JsonSerializer.Serialize(state, options);
        var restored = JsonSerializer.Deserialize<Fractal3DState>(json, options)!;
        Check(MaxDifference(state.GrayScott.Field!, restored.GrayScott.Field!) == 0 && restored.GrayScott.Field!.Step == 78,
            "Brotli checkpoints must retain exact float32 bits and simulation time.");
        using var continued = new GrayScott3DSimulation(restored.GrayScott); continued.Advance(20, CancellationToken.None);
        cpu.Advance(20, CancellationToken.None);
        Check(MaxDifference(cpu.Snapshot(), continued.Snapshot()) == 0, "Disk continuation on the same backend must be exact.");
        var corrupt = JsonNode.Parse(json)!.AsObject(); corrupt["GrayScott"]!["Field"]!["Data"] = "AAAA";
        try { JsonSerializer.Deserialize<Fractal3DState>(corrupt.ToJsonString(), options); throw new Exception("Corrupt field accepted."); }
        catch (JsonException) { }
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(MaxDifference(store.Load().Single().GrayScott.Field!, state.GrayScott.Field!) == 0, "The shared save manager store must retain U/V.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DGrayScott" && unreadable == 0,
            "The new category must be visible to the existing cloud repository.");

        using var renderer = new Fractal3DRenderer(); var presets = Fractal3DCatalog.GetPresets(kind);
        foreach (var preset in presets.Where(p => p.GrayScott.SeedShape != GrayScott3DSeed.Empty))
        {
            using var simulation = new GrayScott3DGpuEngine(preset.GrayScott);
            await Task.Run(() =>
            {
                for (int remaining = preset.GrayScott.InitialSteps; remaining > 0; remaining -= Math.Min(remaining, 256))
                    simulation.Advance(Math.Min(remaining, 256), CancellationToken.None);
            });
            var early = simulation.Snapshot();
            await Task.Run(() => simulation.Advance(600, CancellationToken.None));
            var late = simulation.Snapshot();
            double vMax = (preset.GrayScott.SeedShape == GrayScott3DSeed.Ring ? early : late).Concentrations.ToArray().Where((_, i) => i % 2 == 1).Max();
            double change = MaxDifference(early, late);
            Console.WriteLine($"{preset.SaveName}: Vmax {vMax:F3}, change {change:F3}");
            Check(vMax > .15 && change > .01, "Nonempty presets must survive and evolve in a genuine 3D field.");
            var view = preset.Clone(); view.GrayScott = preset.GrayScott with { Field = early };
            var bitmap = await renderer.RenderAsync(view, 240, 240, null, CancellationToken.None);
            if (output is not null) Save(bitmap, Path.Combine(output, $"preset-{presets.ToList().IndexOf(preset)}.png"));
            if (ReferenceEquals(preset, presets[0])) state = view;
        }
        byte[] basePixels = Pixels(await renderer.RenderAsync(state, 200, 200, null, CancellationToken.None));
        Check(basePixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 40, "The volume renderer must produce a nonuniform colored surface.");
        using (var previewRenderer = new Fractal3DRenderer())
        {
            var preparedPixels = Pixels(await previewRenderer.RenderAsync(Fractal3DCatalog.CreateDefaultState(kind), 200, 200, null, CancellationToken.None));
            Check(basePixels.SequenceEqual(preparedPixels), "Parameter-only previews must match the exact initial stage shown by the window.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view = state.Clone(); view.ShadingStyle = style;
            var bitmap = await renderer.RenderAsync(view, 200, 200, null, CancellationToken.None);
            Check(ReferenceEquals(view.GrayScott.Field, state.GrayScott.Field), "Styles must reuse the immutable field.");
            if (output is not null) Save(bitmap, Path.Combine(output, $"style-{(int)style}.png"));
        }
        var cut = state.Clone(); cut.GrayScott = cut.GrayScott with { CutAxis = 3, CutPosition = 0 };
        var cutPixels = Pixels(await renderer.RenderAsync(cut, 200, 200, null, CancellationToken.None));
        Check(!basePixels.SequenceEqual(cutPixels),
            "The cutting plane must reveal the interior without changing the field.");
        bool hit = false;
        for (int y = 50; y < 160 && !hit; y += 20) for (int x = 50; x < 160 && !hit; x += 20)
            hit = await renderer.ProbeDistanceAsync(state, x, y, 200, 200, CancellationToken.None) > 0;
        Check(hit, "The common 3D probe must hit the chemical surface.");
        var small = state.Clone(); small.GrayScott = settings with { Field = one };
        await renderer.RenderAsync(small, 80, 80, null, CancellationToken.None);
        var restoredPixels = Pixels(await renderer.RenderAsync(state, 200, 200, null, CancellationToken.None));
        Check(basePixels.SequenceEqual(restoredPixels),
            "Changing the simulation size must recreate the texture and restore the same frame.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("gray-scott3d-pixel")) &&
            GrayScott3DComputeShader.CacheEntries.All(e => File.Exists(AppPaths.GetShaderCacheFile(e.Key))),
            "Both computation and display must participate in the shared shader cache.");

        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] parameters) => typeof(Fractal3DWindow).GetMethod(name, flags)!.Invoke(window, parameters);
        T Field<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name, flags)!.GetValue(window)!;
        async Task Idle()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Field<bool>("_grayBusy") && watch.ElapsedMilliseconds < 20000) await Task.Delay(10);
            Check(!Field<bool>("_grayBusy"), "Background simulation must finish or cancel.");
        }
        async Task Display()
        {
            var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)method.Invoke(window, [Enum.Parse(method.GetParameters()[0].ParameterType, "Full")])!;
        }
        try
        {
            window.LoadState(state);
            await Idle();
            Check(ReferenceEquals(window.CaptureState("ui").GrayScott.Field, state.GrayScott.Field), "WPF loading must preserve the exact field on pause.");
            Check(((FrameworkElement)window.FindName("GrayPanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("IterationsBox")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "The window must show chemical controls rather than unrelated fractal fields.");
            ((Slider)window.FindName("GrayThresholdSlider")).Value = .22;
            Check(window.CaptureState("ui").GrayScott.Threshold == .22 && ReferenceEquals(window.CaptureState("ui").GrayScott.Field, state.GrayScott.Field),
                "View changes must preserve the simulation time and concentrations.");
            ((TextBox)window.FindName("GrayFeedBox")).Text = "invalid";
            Check(window.CaptureState("draft").GrayScott.Feed == state.GrayScott.Feed, "Unapplied equation drafts must not corrupt saves or camera frames.");
            ((ComboBox)window.FindName("GrayBackendBox")).SelectedIndex = (int)GrayScottBackend.Cpu;
            Check(ReferenceEquals(window.CaptureState("transfer").GrayScott.Field, state.GrayScott.Field), "Backend switching must preserve the checkpoint.");
            var rootForTest = (FrameworkElement)window.Content;
            rootForTest.Measure(new Size(960, 600)); rootForTest.Arrange(new Rect(0, 0, 960, 600)); rootForTest.UpdateLayout();
            var beforeStep = window.CaptureState("before").GrayScott.Field!;
            ((Button)window.FindName("GrayStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(ReferenceEquals(window.CaptureState("during").GrayScott.Field, beforeStep), "Saves must retain the displayed field until the computed frame is published.");
            await Display();
            Check(window.CaptureState("after").GrayScott.Field!.Step == beforeStep.Step + 16 && !Field<bool>("_grayRunning"),
                "One frame must advance exactly the selected number of steps and stay paused.");
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseGrayScott"); await Idle();
            Check(!Field<bool>("_grayRunning"), "Pausing during computation must terminate the live simulation.");
            if (Field<GrayScott3DField?>("_grayPendingField") is not null) await Display();
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseGrayScott");
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(Field<bool>("_grayRunning") && Field<GrayScott3DField?>("_grayPendingField") is not null,
                "Rapid pause/resume during a canceled batch must resume computation.");
            Invoke("PauseGrayScott"); await Display();
            var savedForFailure = window.CaptureState("recovery");
            try
            {
                GrayScott3DEngineFactory.GpuFactoryOverrideForTests = s => new BrokenGrayScott3DEngine(s);
                ((ComboBox)window.FindName("GrayBackendBox")).SelectedIndex = (int)GrayScottBackend.Gpu;
                ((Button)window.FindName("GrayStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
                Check(window.CaptureState("failure").GrayScott.Backend == GrayScottBackend.Cpu &&
                    ReferenceEquals(window.CaptureState("failure").GrayScott.Field, savedForFailure.GrayScott.Field),
                    "A runtime GPU failure must recover the displayed field on CPU, on pause.");
                GrayScott3DEngineFactory.GpuFactoryOverrideForTests = _ => throw new IOException("Test GPU unavailable");
                ((ComboBox)window.FindName("GrayBackendBox")).SelectedIndex = (int)GrayScottBackend.Gpu;
                ((Button)window.FindName("GrayStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(); await Display();
                Check(window.CaptureState("fallback").GrayScott.Backend == GrayScottBackend.Cpu &&
                    window.CaptureState("fallback").GrayScott.Field!.Step == savedForFailure.GrayScott.Field!.Step + 16,
                    "An unavailable GPU must continue the exact checkpoint on CPU.");
            }
            finally { GrayScott3DEngineFactory.GpuFactoryOverrideForTests = null; }
            window.LoadState(state); await Idle();
            if (output is not null)
            {
                window.CanvasImage.Source = await renderer.RenderAsync(state, 720, 640, null, CancellationToken.None);
                var root = (FrameworkElement)window.Content;
                foreach (var size in new[] { new Size(1180, 800), new Size(960, 600) })
                {
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    Save(bitmap, Path.Combine(output, $"window-{size.Width}.png"));
                }
                Save(await renderer.RenderAsync(cut, 600, 600, null, CancellationToken.None), Path.Combine(output, "cut.png"));
            }
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (gray-scott3d): independent 3D flux rule, GPU/CPU, periodic brush, exact continuation, presets, nine styles, cuts, probe, texture resizing, cache, saves, cloud and WPF.");

        static double MaxDifference(GrayScott3DField a, GrayScott3DField b) =>
            a.Concentrations.ToArray().Zip(b.Concentrations.ToArray()).Max(p => Math.Abs((double)p.First - p.Second));
        static byte[] Pixels(BitmapSource bitmap) { var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes; }
        static void Save(BitmapSource bitmap, string path)
        { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file); }
    }

    private sealed class BrokenGrayScott3DEngine(GrayScott3DSettings settings) : IGrayScott3DEngine
    {
        private readonly GrayScott3DSimulation _cpu = new(settings);
        public string DeviceName => "Test failing GPU";
        public void Advance(int steps, CancellationToken token) => throw new IOException("Test device removed");
        public void Inject(double x, double y, double z, double radius) => _cpu.Inject(x, y, z, radius);
        public GrayScott3DField Snapshot() => _cpu.Snapshot();
        public void Dispose() => _cpu.Dispose();
    }
}
