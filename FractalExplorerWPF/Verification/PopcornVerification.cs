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
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPopcornAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("popcorn");
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        var items = FractalCatalog.Create().Where(item => item.LaunchKey == "Popcorn").ToArray();
        Check(items.Length == 1 && MainWindow.GetWindowFactory("Popcorn") is not null && CatalogPreviewLoader.IsRendered(items[0]),
            "Popcorn must have one launchable catalog tile with a rendered preview.");
        VerifyCatalogData();

        double x = .1, y = .2;
        PopcornRenderer.Iterate(.05, 3, ref x, ref y);
        Check(Math.Abs(x - .061331597566948025) < 1e-15 && Math.Abs(y - .18009997495408095) < 1e-15,
            "Both Popcorn coordinates must use the old x/y, matching the published one-step reference.");
        double sx = .2, sy = .1, nx = -.1, ny = -.2;
        PopcornRenderer.Iterate(.05, 3, ref sx, ref sy);
        PopcornRenderer.Iterate(.05, 3, ref nx, ref ny);
        Check(sx == y && sy == x && nx == -x && ny == -y, "The map must commute with axis swap and central reflection.");
        foreach (int side in new[] { 4, 8, 16, 32 })
        {
            var visited = new HashSet<(int, int)>();
            (int X, int Y) previous = default;
            for (int index = 0; index < side * side; index++)
            {
                var point = PopcornRenderer.HilbertPoint(side, index);
                Check(point.X >= 0 && point.X < side && point.Y >= 0 && point.Y < side && visited.Add(point),
                    "Hilbert must visit every cell exactly once.");
                if (index > 0) Check(Math.Abs(point.X - previous.X) + Math.Abs(point.Y - previous.Y) == 1,
                    "Consecutive Hilbert vertices must share an edge.");
                previous = point;
            }
        }

        var palettes = DynamicPaletteStore.PopcornBuiltIns();
        var frames = new List<byte[]>();
        for (int index = 0; index < PopcornPresets.All.Count; index++)
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Popcorn);
            PopcornPresets.Apply(state, index);
            state.Threads = Math.Min(4, Environment.ProcessorCount);
            DynamicPalette palette = palettes.First(p => p.Name == state.PaletteName);
            int lastProgress = -1;
            byte[] frame = PopcornRenderer.RenderBuffer(state, 512, 512, palette, CancellationToken.None,
                new PopcornProgress(value => { Check(value >= lastProgress, "Progress must be monotone."); lastProgress = value; }));
            int lit = Enumerable.Range(0, frame.Length / 4).Count(i => frame[i * 4] != 0 || frame[i * 4 + 1] != 0 || frame[i * 4 + 2] != 0);
            Check(lit > 8_000 && frame.Where((_, i) => i % 4 == 3).All(a => a == 255) && lastProgress == 100,
                $"Preset {index} must yield a visible opaque finished image, got {lit} lit pixels.");
            Check(frames.All(prior => !prior.SequenceEqual(frame)), "Each preset must produce a distinct image.");
            frames.Add(frame);
            state.Threads = 1;
            Check(PopcornRenderer.RenderBuffer(state, 512, 512, palette, CancellationToken.None).SequenceEqual(frame),
                "Grid seeds, curve ordering and hit sums must not change with CPU thread count.");
            state.SaveName = "Popcorn test " + index;
            var store = new DynamicSystemSaveStore(DynamicSystemKind.Popcorn);
            store.Save(state);
            var restored = store.Load().Single(s => s.SaveName == state.SaveName);
            Check(JsonSerializer.Serialize(restored.Popcorn) == JsonSerializer.Serialize(state.Popcorn) && restored.PaletteName == state.PaletteName,
                "Disk saves must retain the map, sampling, plot mode and palette.");
            Check(PopcornRenderer.RenderBuffer(restored, 512, 512, palette, CancellationToken.None).SequenceEqual(frame),
                "Reloading must reproduce the exact frame.");
            var clone = state.Clone(); clone.Popcorn.H += .001;
            Check(clone.Popcorn.H != state.Popcorn.H, "Render snapshots must own their settings.");
            if (output is not null) SavePopcornPng(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, frame, 512 * 4),
                Path.Combine(output, $"popcorn-{index:D2}.png"));
            Console.WriteLine($"Popcorn {index}: {state.Popcorn.PlotMode}, {lit:N0} lit pixels, exact repeat and disk round-trip.");
        }

        foreach (PopcornPlotMode mode in Enum.GetValues<PopcornPlotMode>())
        {
            using var cts = new CancellationTokenSource();
            var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Popcorn);
            state.Popcorn.PlotMode = mode;
            bool canceled = false;
            try
            {
                PopcornRenderer.RenderBuffer(state, 96, 96, null, cts.Token,
                    new PopcornProgress(value => { if (value > (mode == PopcornPlotMode.HilbertCurve ? 75 : 0)) cts.Cancel(); }));
            }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Cancellation during grid accumulation and curve drawing must stop the renderer.");
        }

        var changed = DynamicSystemState.CreateDefault(DynamicSystemKind.Popcorn);
        changed.Popcorn.GridSize = 64;
        byte[] before = PopcornRenderer.RenderBuffer(changed, 160, 120, null, CancellationToken.None);
        changed.Popcorn.K = 2.7;
        Check(!PopcornRenderer.RenderBuffer(changed, 160, 120, null, CancellationToken.None).SequenceEqual(before), "Changing k must change the picture.");
        changed.Popcorn.K = 3; changed.CenterX = .3; changed.Zoom = 2;
        Check(!PopcornRenderer.RenderBuffer(changed, 160, 120, null, CancellationToken.None).SequenceEqual(before), "Navigation must change the viewport.");
        var spans = PopcornRenderer.ViewSpans(changed, 600, 300);
        Check(spans.X == spans.Y * 2 && spans.Y == changed.Popcorn.Span / changed.Zoom, "Rectangular frames must retain square world pixels.");
        var invalid = changed.Popcorn.Clone(); invalid.H = double.NaN;
        bool rejected = false;
        try { invalid.Validate(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Non-finite map parameters must be rejected.");
        var legacy = JsonSerializer.Deserialize<DynamicSystemState>("{\"Kind\":7,\"Attractor2DMode\":\"Clifford\"}")!;
        Check(legacy.Kind == DynamicSystemKind.Attractors2D && legacy.Clone().Popcorn is not null,
            "Appending the new mode must preserve legacy numeric enum values and default missing settings.");

        var paletteStore = new DynamicPaletteStore(DynamicSystemKind.Popcorn);
        var custom = palettes[0].Clone("Popcorn custom");
        paletteStore.Save([custom]);
        Check(paletteStore.Load().Single(p => p.Name == custom.Name).Colors.SequenceEqual(custom.Colors), "Custom palettes must round-trip.");
        BitmapSource preview = await CatalogPreviewLoader.RenderAsync(items[0], CancellationToken.None);
        byte[] previewPixels = new byte[512 * 512 * 4]; preview.CopyPixels(previewPixels, 512 * 4, 0);
        Check(previewPixels.SequenceEqual(frames[0]), "The catalog must show the actual default Popcorn image.");
        await VerifyPopcornWindowAsync(output, preview);
        Console.WriteLine("PASS (popcorn): formula, Hilbert topology, five distinct presets, thread independence, cancellation, saves, palettes, catalog and WPF controls.");
    }

    private static async Task VerifyPopcornWindowAsync(string? output, BitmapSource preview)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = (DynamicSystemWindow)MainWindow.GetWindowFactory("Popcorn")!();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] arguments) => typeof(DynamicSystemWindow).GetMethod(name, flags)!.Invoke(window, arguments);
        T Field<T>(string name) => (T)typeof(DynamicSystemWindow).GetField(name, flags)!.GetValue(window)!;
        try
        {
            Check(window.Title.Contains("Popcorn"), "The tile must open the Popcorn window directly.");
            var fields = Field<Dictionary<string, TextBox>>("_popcornBoxes");
            fields["H"].Text = "0.035";
            fields["K"].Text = "2.8";
            var captured = (DynamicSystemState)Invoke("CaptureState", "UI")!;
            Check(captured.Popcorn.H == .035 && captured.Popcorn.K == 2.8, "UI map edits must reach render/export snapshots.");
            Field<ComboBox>("_popcornPresetsBox").SelectedIndex = 3;
            captured = (DynamicSystemState)Invoke("CaptureState", "Hilbert")!;
            Check(captured.Popcorn.PlotMode == PopcornPlotMode.HilbertCurve && captured.Popcorn.OrbitIterations == 12,
                "A curve preset must restore the mode and its iteration count.");
            var fieldPanels = Field<Dictionary<string, StackPanel>>("_popcornFieldPanels");
            Check(fieldPanels["HilbertOrder"].Visibility == Visibility.Visible && fieldPanels["GridSize"].Visibility == Visibility.Collapsed,
                "Only the sampling controls for the current mode must be visible.");
            var paletteBox = Field<ComboBox>("_popcornPaletteBox"); paletteBox.SelectedIndex = 0;
            Check(((DynamicSystemState)Invoke("CaptureState", "Single color")!).PaletteName == "", "One-color selection must persist.");
            var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Popcorn); state.SsaaFactor = 2;
            Invoke("LoadState", state);
            BitmapSource export = await window.RenderStatePreviewAsync(state, 320, 240, CancellationToken.None);
            Check(export.PixelWidth == 320 && export.PixelHeight == 240, "SSAA export must keep the requested size.");
            var loaded = (DynamicSystemState)Invoke("CaptureState", "Loaded")!;
            Check(JsonSerializer.Serialize(loaded.Popcorn) == JsonSerializer.Serialize(state.Popcorn), "Loading must restore all settings without rounding.");
            if (output is not null)
            {
                var image = (Image)window.FindName("StableImage"); image.Source = preview;
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1120, 700)); content.Arrange(new Rect(0, 0, 1120, 700)); content.UpdateLayout();
                var canvas = (FrameworkElement)window.FindName("CanvasSurface");
                image.Source = await window.RenderStatePreviewAsync(loaded, (int)canvas.ActualWidth, (int)canvas.ActualHeight, CancellationToken.None);
                content.UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                var snapshot = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32);
                snapshot.Render(content);
                SavePopcornPng(snapshot, Path.Combine(output, "popcorn-window.png"));
                SavePopcornPng(export, Path.Combine(output, "popcorn-ssaa.png"));
            }
        }
        finally { window.Close(); }
    }

    private sealed class PopcornProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private static void SavePopcornPng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
