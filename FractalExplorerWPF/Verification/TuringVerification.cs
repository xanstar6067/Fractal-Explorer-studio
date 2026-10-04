using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private static async Task VerifyTuringAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("turing");
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        var tile = FractalCatalog.Create().Single(i => i.LaunchKey == "TuringPatterns");
        Check(MainWindow.GetWindowFactory(tile.LaunchKey) is not null && CatalogPreviewLoader.IsRendered(tile), "Turing needs a working launch and simulated catalog preview.");
        VerifyCatalogData(); VerifyTuringReference();
        TuringState? grown = null;
        var frames = new List<byte[]>();
        foreach (TuringPreset preset in TuringPresets.All)
        {
            TuringState state = preset.CreateState(); var simulation = new TuringSimulation(state);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            simulation.Advance(state.WarmupSteps, state, CancellationToken.None);
            state.Checkpoint = simulation.Snapshot();
            Check(state.Checkpoint.Field.All(v => float.IsFinite(v) && v is >= -1 and <= 1), "Evolved fields must remain finite and normalized.");
            Check(state.Checkpoint.Field.Max() - state.Checkpoint.Field.Min() > 1.9, "Patterns need a visible range.");
            byte[] pixels = TuringRenderer.RenderFrame(state.Checkpoint, state, 512, 512, CancellationToken.None);
            Check(frames.All(f => !f.SequenceEqual(pixels)), "All six presets must be distinct."); frames.Add(pixels);
            Check(Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct().Count() > 150, "The preset must have structure and shaded colours.");
            state.SaveName = preset.Name; state.Timestamp = DateTime.Now;
            var store = new TuringSaveStore(); var slot = store.Save(state); TuringState restored = store.Load().Single(s => s.SaveName == state.SaveName);
            Check(restored.Checkpoint!.Field.SequenceEqual(state.Checkpoint.Field) && restored.Checkpoint.Scales.SequenceEqual(state.Checkpoint.Scales), "Checkpoint JSON must preserve every float bit and scale.");
            var continued = new TuringSimulation(restored);
            simulation.Advance(13, state.Clone(includeCheckpoint: false), CancellationToken.None);
            continued.Advance(13, restored.Clone(includeCheckpoint: false), CancellationToken.None);
            Check(simulation.Snapshot().Field.SequenceEqual(continued.Snapshot().Field), "Saved states must continue identically, without replaying initial development.");
            LocalCloudSave cloud = CloudSaveRepository.ReadLocal(slot.FilePath);
            Check(cloud.Category == "TuringPatterns", "Cloud records must use the dedicated category.");
            FractalCloudClient.ValidateSave(cloud.Name, cloud.JsonData);
            Console.WriteLine($"Turing {preset.Id}: {watch.ElapsedMilliseconds} ms, {state.Checkpoint.Scales.Distinct().Count()} active scales, {Encoding.UTF8.GetByteCount(cloud.JsonData):N0} cloud bytes.");
            if (output is not null) SaveTuringPng(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 512 * 4), Path.Combine(output, $"turing-{preset.Id}.png"));
            grown ??= state;
        }
        var state4 = new TuringState { GridSize = 64, Symmetry = 4, Mirror = true };
        var symmetric = new TuringSimulation(state4); symmetric.Advance(20, state4, CancellationToken.None);
        var cp4 = symmetric.Snapshot(); double symmetryError = 0;
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            symmetryError = Math.Max(symmetryError, Math.Abs(cp4.Field[y * 64 + x] - cp4.Field[x * 64 + 63 - y]));
        Check(symmetryError < 1e-6, "Fourfold rotations must agree on the grid.");
        var painted = new TuringSimulation(grown!); TuringCheckpoint before = painted.Snapshot();
        painted.Paint(.5, .5, .08, .5, TuringBrush.Dark, grown!);
        Check(!painted.Snapshot().Field.SequenceEqual(before.Field) && painted.StepCount == before.StepCount, "Brushes must edit the field without advancing time.");
        var canceled = new TuringSimulation(grown!); using var cts = new CancellationTokenSource(); cts.Cancel();
        try { canceled.Advance(100, grown!.Clone(includeCheckpoint: false), cts.Token); throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
        Check(canceled.Snapshot().Field.SequenceEqual(grown!.Checkpoint!.Field) && canceled.StepCount == grown.Checkpoint.StepCount, "A canceled step must not change the field.");
        var resized = TuringSimulation.Resize(grown.Checkpoint, 384);
        Check(resized.Size == 384 && resized.StepCount == grown.Checkpoint.StepCount && resized.Field.Length == 384 * 384, "Quality resizing must retain model time and field shape.");
        var large = grown.Clone(); large.GridSize = 384; large.Checkpoint = resized;
        var interrupted = new TuringSimulation(large);
        using (var midCancel = new CancellationTokenSource())
        {
            midCancel.CancelAfter(25);
            try { await Task.Run(() => interrupted.Advance(80, large.Clone(includeCheckpoint: false), midCancel.Token)); }
            catch (OperationCanceledException) { }
        }
        var interruptedCp = interrupted.Snapshot(); var independent = new TuringSimulation(large);
        independent.Advance((int)(interruptedCp.StepCount - resized.StepCount), large.Clone(includeCheckpoint: false), CancellationToken.None);
        Check(interruptedCp.Field.SequenceEqual(independent.Snapshot().Field), "Mid-filter cancellation may commit only complete iterations.");
        interrupted.Advance(2, large.Clone(includeCheckpoint: false), CancellationToken.None); independent.Advance(2, large.Clone(includeCheckpoint: false), CancellationToken.None);
        Check(interrupted.Snapshot().Field.SequenceEqual(independent.Snapshot().Field), "Continuing after canceled scratch calculations must be exact.");
        var clone = grown.Clone(); clone.Checkpoint!.Field[0] += .2f;
        Check(clone.Checkpoint.Field[0] != grown.Checkpoint.Field[0], "Save clones must own field arrays.");
        var paletteStore = new DynamicPaletteStore("turing_palettes.json", TuringPalettes.All());
        var palette = TuringPalettes.All()[0].Clone("User Turing"); paletteStore.Save([palette]);
        Check(paletteStore.Load().Any(p => p.Name == "User Turing" && p.Colors.SequenceEqual(palette.Colors)), "Custom palettes must persist separately.");
        TuringState color = grown.Clone(); color.Coloring = TuringColoring.Field;
        byte[] plain = TuringRenderer.RenderFrame(color.Checkpoint!, color, 400, 240, CancellationToken.None);
        color.Coloring = TuringColoring.Scales;
        Check(!plain.SequenceEqual(TuringRenderer.RenderFrame(color.Checkpoint!, color, 400, 240, CancellationToken.None)), "Scale diagnostics must differ from field colouring.");
        Check(Enumerable.Range(0, plain.Length / 4).All(i => plain[i * 4 + 3] == 255), "Export must be opaque and preserve square aspect in rectangular outputs.");
        await VerifyTuringWindowAsync(grown, output);
        Console.WriteLine("PASS (turing): independent multiscale rule, boundaries, six presets, symmetry, exact checkpoints, palettes, live window, cancellation and exports.");
    }

    private static void VerifyTuringReference()
    {
        int n = 32; var random = new Random(53); float[] field = Enumerable.Range(0, n * n).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        foreach (TuringBoundary boundary in Enum.GetValues<TuringBoundary>())
        {
            int Edge(int p) { if (boundary == TuringBoundary.Wrap) return (p % n + n) % n; int q = (p % (n * 2) + n * 2) % (n * 2); return q < n ? q : n * 2 - 1 - q; }
            float[] Mean(float[] input, int radius)
            {
                var result = new float[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    double sum = 0;
                    for (int j = -radius; j <= radius; j++) for (int i = -radius; i <= radius; i++) sum += input[Edge(y + j) * n + Edge(x + i)];
                    result[y * n + x] = (float)(sum / ((radius * 2 + 1) * (radius * 2 + 1)));
                }
                return result;
            }
            float[] Smooth(float[] input, int radius)
            {
                foreach (int width in TuringSimulation.GaussianBoxWidths(radius * .75)) input = Mean(input, width / 2);
                return input;
            }
            var actual = new float[field.Length]; TuringSimulation.BoxMean(field, actual, new float[field.Length], n, 5, boundary, CancellationToken.None);
            Check(actual.Zip(Mean(field, 5), (a, b) => Math.Abs(a - b)).Max() < 1e-6, "Sliding means must match independent direct neighbourhood averages.");
            TuringState state = new() { GridSize = n, Boundary = boundary, Layers = [new() { Radius = 8, Amount = .017 }, new() { Radius = 24, Amount = .05 }, new() { Radius = 48, Amount = .08 }],
                Checkpoint = new() { Size = n, Field = [.. field], Scales = new byte[field.Length] } };
            var simulation = new TuringSimulation(state);
            float[] best = Enumerable.Repeat(float.PositiveInfinity, field.Length).ToArray(), delta = new float[field.Length];
            for (int layer = 0; layer < state.Layers.Count; layer++)
            {
                TuringScale scale = state.Layers[layer]; int radius = (int)Math.Round(scale.Radius * n / 256);
                float[] a = Smooth(field, radius), b = Smooth(field, radius * 2);
                float[] variation = Smooth(a.Zip(b, (v, w) => Math.Abs(v - w)).ToArray(), radius);
                for (int i = 0; i < field.Length; i++) if (variation[i] < best[i]) { best[i] = variation[i]; delta[i] = a[i] > b[i] ? (float)scale.Amount : -(float)scale.Amount; }
            }
            float[] next = field.Zip(delta, (a, b) => a + b).ToArray(); float min = next.Min(), max = next.Max();
            for (int i = 0; i < next.Length; i++) next[i] = (float)((next[i] - min) / (double)(max - min) * 2 - 1);
            simulation.Advance(1, state.Clone(includeCheckpoint: false), CancellationToken.None);
            Check(simulation.Snapshot().Field.Zip(next, (a, b) => Math.Abs(a - b)).Max() < 1e-5, "A multiscale step must match an independent competition and normalization.");
        }
    }

    private static async Task VerifyTuringWindowAsync(TuringState grown, string? output)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new TuringWindow(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] parameters) => typeof(TuringWindow).GetMethod(name, flags)!.Invoke(window, parameters);
        async Task Reset(TuringState state) => await (Task)Invoke("ResetAsync", state, false)!;
        async Task Idle() { await (Task)Invoke("WaitForFrameIdleAsync")!; }
        T Field<T>(string name) => (T)typeof(TuringWindow).GetField(name, flags)!.GetValue(window)!;
        async Task WaitFor(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 6000) await Task.Delay(10);
            Check(condition(), "A window operation did not finish within the expected time.");
        }
        try
        {
            await Reset(grown); TuringState captured = window.CaptureState("window");
            Check(captured.Checkpoint!.Field.SequenceEqual(grown.Checkpoint!.Field), "Loading in the window must restore the exact displayed field.");
            ((Slider)window.FindName("DetailSlider")).Value = .75; Invoke("FlushShape");
            Check(window.CaptureState("shape").DetailSize == .75 && window.CaptureState("shape").Checkpoint!.StepCount == grown.Checkpoint.StepCount, "Shape controls must apply without resetting time.");
            ((ComboBox)window.FindName("PaletteBox")).SelectedIndex = 1; await Idle();
            Check(window.CaptureState("palette").Checkpoint!.StepCount == grown.Checkpoint.StepCount, "Palette changes must not grow a paused field.");
            await (Task)Invoke("ProduceFrameAsync", 1)!;
            Check(window.CaptureState("step").Checkpoint!.StepCount == grown.Checkpoint.StepCount + 1, "One step means one model iteration.");
            TuringState first = grown.Clone(); first.RandomSeed = 19;
            TuringState latest = grown.Clone(); latest.RandomSeed = 73;
            await Task.WhenAll(Reset(first), Reset(latest));
            Check(window.CaptureState("latest").RandomSeed == 73, "Rapid resets may publish only the latest result.");
            await Reset(grown); Invoke("Remember");
            ((Slider)window.FindName("ContrastSlider")).Value = 1.8; await Idle();
            Invoke("Undo_OnClick", window, new RoutedEventArgs());
            await (Task)typeof(TuringWindow).GetField("_resetIdle", flags)!.GetValue(window)!;
            Check(window.CaptureState("undo").Contrast == grown.Contrast && window.CaptureState("undo").Checkpoint!.Field.SequenceEqual(grown.Checkpoint.Field), "Undo must restore both field and settings.");
            Invoke("SetRunning", true); long start = window.CaptureState("timer").Checkpoint!.StepCount;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (window.CaptureState("timer").Checkpoint!.StepCount == start && watch.ElapsedMilliseconds < 4000) await Task.Delay(20);
            Invoke("SetRunning", false); await Idle();
            Check(window.CaptureState("timer").Checkpoint!.StepCount > start, "The live timer must advance and present the field.");
            await Reset(grown);
            Task inFlight = (Task)Invoke("ProduceFrameAsync", 8)!;
            ((ComboBox)window.FindName("QualityBox")).SelectedIndex = 2;
            ((ComboBox)window.FindName("QualityBox")).SelectedIndex = 3;
            await inFlight;
            await WaitFor(() => !Field<bool>("_resetting") && Field<TuringState>("_state").GridSize == 512);
            Check(window.CaptureState("quality").Checkpoint!.StepCount == grown.Checkpoint.StepCount + 8, "Rapid quality edits must preserve all completed time and choose the latest size.");
            await Reset(grown);
            Check(((TextBlock)window.FindName("StatusText")).Text.StartsWith("256 × 256"), "Status must immediately show the restored field size.");
            inFlight = (Task)Invoke("ProduceFrameAsync", 8)!;
            ((ComboBox)window.FindName("QualityBox")).SelectedIndex = 2;
            ((ComboBox)window.FindName("QualityBox")).SelectedIndex = 1;
            await inFlight;
            await WaitFor(() => !Field<bool>("_qualityPending") && !Field<bool>("_resetting"));
            Check(window.CaptureState("quality-return").GridSize == 256 && window.CaptureState("quality-return").Checkpoint!.StepCount == grown.Checkpoint.StepCount + 8,
                "Returning the quality selector to the original size must supersede the pending resize.");
            await Reset(grown);
            var content = (FrameworkElement)window.Content;
            void Layout(int width, int height)
            {
                content.Measure(new System.Windows.Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            }
            Layout(1280, 800);
            var layersExpander = (Expander)window.FindName("LayersExpander"); layersExpander.IsExpanded = true; content.UpdateLayout();
            var layers = (ItemsControl)window.FindName("LayersItems");
            var numericFields = FindTuringVisuals<TextBox>(layers).ToList();
            TextBox amountBox = numericFields.First(b => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path.Path == "Amount");
            amountBox.Text = "0,027"; Invoke("ApplyLayers_OnClick", window, new RoutedEventArgs());
            Check(window.CaptureState("comma").Layers[0].Amount == .027, "Scale editors must accept decimal commas.");
            amountBox.Text = "invalid"; Invoke("ApplyLayers_OnClick", window, new RoutedEventArgs());
            Check(window.CaptureState("invalid").Layers[0].Amount == .027 && !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("LayersError")).Text), "Invalid edits must not silently reuse the old value as an applied setting.");
            amountBox.Text = "0.012"; Invoke("ApplyLayers_OnClick", window, new RoutedEventArgs());
            await Reset(grown); layersExpander.IsExpanded = false; Layout(1280, 800);
            if (output is not null)
            {
                void RenderLayout(string name, int width = 1280, int height = 800)
                {
                    Layout(width, height); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                    SaveTuringPng(bitmap, Path.Combine(output, name));
                }
                RenderLayout("turing-window.png");
                var tabs = (TabControl)window.FindName("SettingsTabs"); tabs.SelectedIndex = 1; content.UpdateLayout();
                RenderLayout("turing-color.png");
                ((ComboBox)window.FindName("ColoringBox")).SelectedIndex = 2; await Idle(); RenderLayout("turing-scales.png");
                tabs.SelectedIndex = 2; content.UpdateLayout(); RenderLayout("turing-brush.png", 1040, 650);
                tabs.SelectedIndex = 0; layersExpander.IsExpanded = true; content.UpdateLayout();
                ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToBottom(); content.UpdateLayout(); RenderLayout("turing-layers.png", 1040, 650);
                layersExpander.IsExpanded = false; ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToTop();
                await Reset(grown); FractalExplorerWPF.Theming.ThemeManager.SetTheme("light"); RenderLayout("turing-light.png", 1040, 650);
                FractalExplorerWPF.Theming.ThemeManager.SetTheme(FractalExplorerWPF.Theming.ThemeManager.DefaultThemeId);
            }
            Task worker = (Task)Invoke("ProduceFrameAsync", 8)!; window.Close(); await worker;
            Check((bool)typeof(TuringWindow).GetField("_closed", flags)!.GetValue(window)!, "Closing must stop all simulation work.");
        }
        finally { window.Close(); }
        var preparing = new TuringWindow();
        try
        {
            Task reset = (Task)typeof(TuringWindow).GetMethod("ResetAsync", flags)!.Invoke(preparing, [new TuringState { GridSize = 128, WarmupSteps = 2000 }, true])!;
            await Task.Delay(20);
            typeof(TuringWindow).GetMethod("CancelPreparation_OnClick", flags)!.Invoke(preparing, [preparing, new RoutedEventArgs()]);
            await reset;
            Check(preparing.CaptureState("partial").Checkpoint!.StepCount < 2000 && !(bool)typeof(TuringWindow).GetField("_running", flags)!.GetValue(preparing)!, "Stopping preparation must retain a usable partially evolved field on pause.");
            reset = (Task)typeof(TuringWindow).GetMethod("ResetAsync", flags)!.Invoke(preparing, [new TuringState { GridSize = 384, WarmupSteps = 2000 }, true])!;
            await Task.Delay(20); preparing.Close(); await reset;
            Check((bool)typeof(TuringWindow).GetField("_closed", flags)!.GetValue(preparing)!, "Closing during preparation must cancel and drop the prepared result.");
        }
        finally { preparing.Close(); }
    }
    private static IEnumerable<T> FindTuringVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (T item in FindTuringVisuals<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static void SaveTuringPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
