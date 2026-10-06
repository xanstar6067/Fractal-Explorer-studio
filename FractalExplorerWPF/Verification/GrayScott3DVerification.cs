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
        string? output = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        // Simulation and display share the renderer's device, exactly as in the window.
        using var renderer = new Fractal3DRenderer();
        var host = renderer.DeviceHost;
        if (args.Contains("--probe"))
        {
            int index = 0;
            foreach (var pair in new[] { (.035, .060), (.03, .062), (.04, .062), (.022, .051), (.03, .058), (.026, .051) })
            {
                var view = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.GrayScott3D);
                view.GrayScott = view.GrayScott with { Feed = pair.Item1, Kill = pair.Item2 };
                using var engine = new GrayScott3DGpuSimulation(host, view.GrayScott);
                for (int stage = 0; stage < 2; stage++)
                {
                    await Task.Run(() => engine.Advance(1200, CancellationToken.None));
                    view.GrayScott = view.GrayScott with { Live = engine.Publish(view.GrayScott.Live) };
                    var field = engine.ReadCurrent();
                    Console.WriteLine($"Probe {index}: {pair}, step {field.Step}, Vmax {VMax(field):F3}");
                    if (output is not null)
                    {
                        Save(await renderer.RenderAsync(view, 320, 320, null, CancellationToken.None), Path.Combine(output, $"probe-{index}-{stage}.png"));
                        var slice = view.Clone(); slice.GrayScott = slice.GrayScott with { CutAxis = 3 };
                        Save(await renderer.RenderAsync(slice, 320, 320, null, CancellationToken.None), Path.Combine(output, $"probe-{index}-{stage}-cut.png"));
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
        var settings = new GrayScott3DSettings { Size = n, Field = new(n, 17, initial) };
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
        using var gpu = new GrayScott3DGpuSimulation(host, settings);
        Check(gpu.DeviceName.StartsWith("ГП", StringComparison.Ordinal), "The 3D simulation runs only on the GPU device.");
        gpu.Advance(1, CancellationToken.None);
        var one = gpu.ReadCurrent();
        Check(one.Step == 18 && one.Concentrations.ToArray().Zip(expected).Max(p => Math.Abs(p.First - p.Second)) < 5e-7,
            "The GPU 3D rule must match independent face fluxes plus chemical reaction, including wrapped edges.");
        float[] reference = one.Concentrations.ToArray();
        for (int step = 0; step < 60; step++) reference = GrayReferenceStep(reference, n, settings);
        Check(gpu.Advance(60, CancellationToken.None) == 60 && MaxArrayDifference(gpu.ReadCurrent().Concentrations.ToArray(), reference) < 3e-5,
            "The GPU evolution must agree with a float reference over multiple steps.");

        // Publication slots: the shown frame survives later publications and stays readable for saves.
        var shown = gpu.Publish();
        var shownField = gpu.ReadCheckpoint(shown);
        Check(shownField.Step == 78 && MaxDifference(shownField, gpu.ReadCurrent()) == 0, "A published frame must hold the exact U/V.");
        gpu.Advance(5, CancellationToken.None); var next = gpu.Publish(shown);
        gpu.Advance(5, CancellationToken.None); var third = gpu.Publish(shown);
        Check(next.Slot == third.Slot && third.Slot != shown.Slot && MaxDifference(gpu.ReadCheckpoint(shown), shownField) == 0 &&
            gpu.ReadCheckpoint(shown).Step == 78, "Publishing past a kept frame must never overwrite it.");
        try { gpu.ReadCheckpoint(next); throw new Exception("A replaced frame was read."); }
        catch (InvalidOperationException) { }

        var empty = settings with { Field = null, SeedShape = GrayScott3DSeed.Empty };
        using var brushGpu = new GrayScott3DGpuSimulation(host, empty);
        brushGpu.Inject(0, .5, .5, .12);
        float[] brushExpected = GrayReferenceBrush(new float[n * n * n * 2].Select((_, i) => i % 2 == 0 ? 1f : 0f).ToArray(), n, 0, .5, .5, .12);
        var injected = brushGpu.ReadCurrent().Concentrations.ToArray();
        Check(MaxArrayDifference(injected, brushExpected) == 0, "Periodic spherical brushes must match the reference exactly.");
        Check(injected[((16 * n + 16) * n) * 2 + 1] > 0 && injected[((16 * n + 16) * n + 31) * 2 + 1] > 0,
            "A brush on the boundary must reach both sides of the periodic volume.");
        using (var cancellation = new CancellationTokenSource())
        {
            var before = brushGpu.ReadCurrent(); cancellation.Cancel();
            Check(brushGpu.Advance(100, cancellation.Token) == 0 && MaxDifference(before, brushGpu.ReadCurrent()) == 0 &&
                before.Step == brushGpu.Step, "Cancellation before a step must preserve the field and time.");
        }
        foreach (int side in new[] { 33, 128 })
        {
            using var sized = new GrayScott3DGpuSimulation(host, empty with { Size = side });
            sized.Inject(.5, .5, .5, .08); sized.Advance(2, CancellationToken.None);
            var field = sized.ReadCurrent();
            Check(field.Size == side && field.Step == 2 && VMax(field) > .2,
                "Both the maximum grid and incomplete compute thread groups must evolve correctly.");
        }

        var options = JsonOptionsFactory.Create(); var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.GrayScott = settings with { Field = gpu.ReadCurrent() }; state.SaveName = "3D exact continuation";
        string json = JsonSerializer.Serialize(state, options);
        Check(!json.Contains("\"Live\"", StringComparison.Ordinal) && !json.Contains("\"Backend\"", StringComparison.Ordinal),
            "Saves contain neither GPU handles nor a computation backend.");
        var restored = JsonSerializer.Deserialize<Fractal3DState>(json, options)!;
        Check(MaxDifference(state.GrayScott.Field!, restored.GrayScott.Field!) == 0 && restored.GrayScott.Field!.Step == 88,
            "Brotli checkpoints must retain exact float32 bits and simulation time.");
        var legacy = JsonNode.Parse(json)!.AsObject(); legacy["GrayScott"]!["Backend"] = 0;
        Check(JsonSerializer.Deserialize<Fractal3DState>(legacy.ToJsonString(), options)!.GrayScott.Field!.Step == 88,
            "Saves from the former CPU backend must still open (on the GPU).");
        using (var continued = new GrayScott3DGpuSimulation(host, restored.GrayScott))
        {
            continued.Advance(20, CancellationToken.None); gpu.Advance(20, CancellationToken.None);
            Check(MaxDifference(gpu.ReadCurrent(), continued.ReadCurrent()) == 0, "Disk continuation on the same GPU must be exact.");
        }
        var corrupt = JsonNode.Parse(json)!.AsObject(); corrupt["GrayScott"]!["Field"]!["Data"] = "AAAA";
        try { JsonSerializer.Deserialize<Fractal3DState>(corrupt.ToJsonString(), options); throw new Exception("Corrupt field accepted."); }
        catch (JsonException) { }
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(MaxDifference(store.Load().Single().GrayScott.Field!, state.GrayScott.Field!) == 0, "The shared save manager store must retain U/V.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DGrayScott" && unreadable == 0,
            "The new category must be visible to the existing cloud repository.");

        var presets = Fractal3DCatalog.GetPresets(kind);
        foreach (var preset in presets.Where(p => p.GrayScott.SeedShape != GrayScott3DSeed.Empty))
        {
            using var simulation = new GrayScott3DGpuSimulation(host, preset.GrayScott);
            await Task.Run(() => simulation.Advance(preset.GrayScott.InitialSteps, CancellationToken.None));
            var early = simulation.ReadCurrent();
            await Task.Run(() => simulation.Advance(600, CancellationToken.None));
            var late = simulation.ReadCurrent();
            double vMax = VMax(preset.GrayScott.SeedShape == GrayScott3DSeed.Ring ? early : late);
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
        // A live frame on the shared device renders without CPU copies and matches its own checkpoint.
        using (var live = new GrayScott3DGpuSimulation(host, state.GrayScott))
        {
            var liveState = state.Clone(); liveState.GrayScott = state.GrayScott with { Field = null, Live = live.Publish() };
            byte[] livePixels = Pixels(await renderer.RenderAsync(liveState, 200, 200, null, CancellationToken.None));
            Check(basePixels.SequenceEqual(livePixels),
                "A live GPU frame must render exactly like the same field loaded from a save.");
            using var foreign = new Fractal3DRenderer();
            try { await foreign.RenderAsync(liveState, 40, 40, null, CancellationToken.None); throw new Exception("Foreign device accepted a live frame."); }
            catch (InvalidOperationException) { }
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
            "Changing the simulation size must recreate the volume and restore the same frame.");
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
        GrayScott3DVolume? Shown() => (GrayScott3DVolume?)typeof(Fractal3DWindow).GetField("_grayShown", flags)!.GetValue(window);
        GrayScott3DVolume? Pending() => (GrayScott3DVolume?)typeof(Fractal3DWindow).GetField("_grayPending", flags)!.GetValue(window);
        async Task Idle()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Field<bool>("_grayBusy") && watch.ElapsedMilliseconds < 30000) await Task.Delay(10);
            Check(!Field<bool>("_grayBusy"), "Background simulation must finish or cancel.");
        }
        async Task Display()
        {
            var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)method.Invoke(window, [Enum.Parse(method.GetParameters()[0].ParameterType, "Full")])!;
        }
        try
        {
            Check(window.FindName("GrayBackendBox") is null, "The 3D window must not offer a CPU computation backend.");
            await Idle(); await Display();
            Check(Shown() is { Step: > 0 } && Pending() is null, "Opening the window must prepare and show the first preset on the GPU.");
            window.LoadState(state);
            Check(Shown() is null, "Loading must drop the previous live frame.");
            await Idle(); await Display();
            var loaded = window.CaptureState("ui").GrayScott;
            Check(Shown() is not null && loaded.Live is null && MaxDifference(loaded.Field!, state.GrayScott.Field!) == 0 &&
                loaded.Field!.Step == state.GrayScott.Field!.Step, "WPF loading must preserve the exact field on pause.");
            Check(((FrameworkElement)window.FindName("GrayPanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("IterationsBox")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "The window must show chemical controls rather than unrelated fractal fields.");
            ((Slider)window.FindName("GrayThresholdSlider")).Value = .22;
            Check(window.CaptureState("ui").GrayScott.Threshold == .22 && window.CaptureState("ui").GrayScott.Field!.Step == state.GrayScott.Field!.Step,
                "View changes must preserve the simulation time and concentrations.");
            ((TextBox)window.FindName("GrayFeedBox")).Text = "invalid";
            Check(window.CaptureState("draft").GrayScott.Feed == state.GrayScott.Feed, "Unapplied equation drafts must not corrupt saves or camera frames.");
            var rootForTest = (FrameworkElement)window.Content;
            rootForTest.Measure(new Size(960, 600)); rootForTest.Arrange(new Rect(0, 0, 960, 600)); rootForTest.UpdateLayout();
            var beforeStep = window.CaptureState("before").GrayScott.Field!;
            ((Button)window.FindName("GrayStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(Pending() is not null && window.CaptureState("during").GrayScott.Field!.Step == beforeStep.Step,
                "Saves must retain the displayed field until the computed frame is shown.");
            await Display();
            Check(window.CaptureState("after").GrayScott.Field!.Step == beforeStep.Step + 16 && !Field<bool>("_grayRunning"),
                "One frame must advance exactly the selected number of steps and stay paused.");
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseGrayScott"); await Idle();
            Check(!Field<bool>("_grayRunning"), "Pausing during computation must terminate the live simulation.");
            if (Pending() is not null) await Display();
            Check(Pending() is null, "Steps submitted before a pause must be published and shown.");
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseGrayScott");
            ((Button)window.FindName("GrayPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(Field<bool>("_grayRunning") && Pending() is not null,
                "Rapid pause/resume during a canceled batch must resume computation.");
            Invoke("PauseGrayScott"); await Display(); await Idle();
            if (Pending() is not null) await Display();

            // The brush edits the GPU field in place without advancing time.
            var beforeBrush = window.CaptureState("brush-before").GrayScott.Field!;
            ((Button)window.FindName("GrayBrushButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle(); await Display();
            var afterBrush = window.CaptureState("brush-after").GrayScott.Field!;
            Check(afterBrush.Step == beforeBrush.Step && MaxDifference(afterBrush, beforeBrush) > .01,
                "The central brush must change the displayed field without advancing time.");

            var saved = window.CaptureState("Saved live frame");
            store.Save(saved);
            var reloaded = store.Load().Single(s => s.SaveName == "Saved live frame");
            Check(MaxDifference(reloaded.GrayScott.Field!, afterBrush) == 0 && reloaded.GrayScott.Field!.Step == afterBrush.Step,
                "Saving from the window must store the exact displayed GPU field.");

            window.LoadState(state); await Idle(); await Display();
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
        Console.WriteLine("PASS (gray-scott3d): independent 3D flux rule on the GPU, publication slots, periodic brush, exact continuation, presets, live frames on the shared device, nine styles, cuts, probe, volume resizing, cache, saves, cloud and WPF.");

        static double VMax(GrayScott3DField field) => field.Concentrations.ToArray().Where((_, i) => i % 2 == 1).Max();
        static double MaxDifference(GrayScott3DField a, GrayScott3DField b) =>
            MaxArrayDifference(a.Concentrations.ToArray(), b.Concentrations.ToArray());
        static byte[] Pixels(BitmapSource bitmap) { var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes; }
        static void Save(BitmapSource bitmap, string path)
        { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file); }
    }

    private static double MaxArrayDifference(float[] a, float[] b) => a.Zip(b).Max(p => Math.Abs((double)p.First - p.Second));

    /// <summary>Test oracle only: the same simultaneous float32 update, computed on the CPU.</summary>
    private static float[] GrayReferenceStep(float[] field, int n, GrayScott3DSettings s)
    {
        float du = (float)s.DiffusionU, dv = (float)s.DiffusionV, feed = (float)s.Feed, kill = (float)s.Kill;
        var next = new float[field.Length];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int I(int ix, int iy, int iz) => ((((iz + n) % n) * n + (iy + n) % n) * n + (ix + n) % n) * 2;
            int i = I(x, y, z);
            int[] around = [I(x - 1, y, z), I(x + 1, y, z), I(x, y - 1, z), I(x, y + 1, z), I(x, y, z - 1), I(x, y, z + 1)];
            float u = field[i], v = field[i + 1], reaction = u * v * v;
            float lu = around.Sum(j => field[j]) - 6 * u, lv = around.Sum(j => field[j + 1]) - 6 * v;
            next[i] = Math.Clamp(u + du * lu - reaction + feed * (1 - u), 0, 1);
            next[i + 1] = Math.Clamp(v + dv * lv + reaction - (feed + kill) * v, 0, 1);
        }
        return next;
    }

    private static float[] GrayReferenceBrush(float[] field, int n, double x, double y, double z, double radius)
    {
        for (int iz = 0; iz < n; iz++) for (int iy = 0; iy < n; iy++) for (int ix = 0; ix < n; ix++)
        {
            float dx = MathF.Abs((ix + .5f) / n - (float)x), dy = MathF.Abs((iy + .5f) / n - (float)y), dz = MathF.Abs((iz + .5f) / n - (float)z);
            dx = MathF.Min(dx, 1 - dx); dy = MathF.Min(dy, 1 - dy); dz = MathF.Min(dz, 1 - dz);
            if (dx * dx + dy * dy + dz * dz > (float)radius * (float)radius) continue;
            int i = ((iz * n + iy) * n + ix) * 2; field[i] = .5f; field[i + 1] = .3f;
        }
        return field;
    }
}
