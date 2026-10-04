using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyHopalongAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("hopalong");
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        var tile = FractalCatalog.Create().Single(item => item.LaunchKey == "Hopalong");
        Check(MainWindow.GetWindowFactory("Hopalong") is not null && CatalogPreviewLoader.IsRendered(tile),
            "Hopalong must have one launchable tile with a computed preview.");
        VerifyCatalogData();

        var map = new HopalongSettings { A = 1.1, B = -.5, C = 1 };
        double x = .1, y = .2;
        HopalongMap.Iterate(map, ref x, ref y);
        Check(Math.Abs(x - -.8246950765959599) < 1e-14 && y == 1,
            "Published simultaneous formula: both next coordinates must use old x and sign(x).");
        x = 0; y = 0; HopalongMap.Iterate(map, ref x, ref y);
        Check(x == 0 && y == 1.1, "At x=0 the sign must be zero, including when c is nonzero.");
        var random = new Random(8241);
        for (int i = 0; i < 256; i++)
        {
            double oldX = random.NextDouble() * 40 - 20, oldY = random.NextDouble() * 40 - 20;
            x = oldX; y = oldY; HopalongMap.Iterate(map, ref x, ref y);
            double inverseX = map.A - y;
            double inverseY = x + Math.Sign(inverseX) * Math.Sqrt(Math.Abs(map.B * inverseX - map.C));
            Check(Math.Abs(inverseX - oldX) < 1e-13 && Math.Abs(inverseY - oldY) < 1e-13,
                "The map must retain its inverse; do not substitute a dissipative attractor formula.");
        }

        VerifyHopalongOrbitReference();
        var palettes = DynamicPaletteStore.HopalongBuiltIns();
        var frames = new List<byte[]>();
        for (int index = 0; index < HopalongPresets.All.Count; index++)
        {
            var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong);
            HopalongPresets.Apply(state, index);
            DynamicPalette palette = palettes.Single(p => p.Name == state.PaletteName);
            int lastProgress = -1, snapshots = 0;
            byte[] frame = HopalongRenderer.RenderBuffer(state, 512, 512, palette, CancellationToken.None,
                new HopalongProgress(value => { Check(value >= lastProgress, "Progress must be monotone."); lastProgress = value; }),
                data => { Check(data.Length == 512 * 512 * 4, "Intermediate frames must keep the canvas size."); snapshots++; });
            int lit = Enumerable.Range(0, frame.Length / 4).Count(i => frame[i * 4] != 0 || frame[i * 4 + 1] != 0 || frame[i * 4 + 2] != 0);
            Check(lit > 4_000 && lastProgress == 100 && snapshots >= 3, $"Preset {index} must be visible and progressive; got {lit} pixels, {snapshots} snapshots.");
            Check(frame.Where((_, i) => i % 4 == 3).All(a => a == 255), "Default frames must be opaque.");
            Check(frames.All(prior => !prior.SequenceEqual(frame)), "The six presets must show different images.");
            state.Threads = Environment.ProcessorCount;
            Check(HopalongRenderer.RenderBuffer(state, 512, 512, palette, CancellationToken.None).SequenceEqual(frame),
                "Changing CPU threads or enabling progressive preview must preserve the one continuous orbit.");
            frames.Add(frame);
            state.SaveName = "Hopalong " + index;
            var store = new DynamicSystemSaveStore(DynamicSystemKind.Hopalong); store.Save(state);
            var restored = store.Load().Single(s => s.SaveName == state.SaveName);
            Check(JsonSerializer.Serialize(state.Hopalong) == JsonSerializer.Serialize(restored.Hopalong) && state.PaletteName == restored.PaletteName,
                "Disk saves must retain the formula, initial point, rotation, span and palette.");
            Check(HopalongRenderer.RenderBuffer(restored, 512, 512, palette, CancellationToken.None).SequenceEqual(frame),
                "Reloading a save must reproduce the exact orbit image.");
            var clone = state.Clone(); clone.Hopalong.A += .01;
            Check(clone.Hopalong.A != state.Hopalong.A, "Snapshots must own their parameters.");
            if (output is not null) SaveHopalongPng(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, frame, 512 * 4),
                Path.Combine(output, $"hopalong-{index:D2}.png"));
            Console.WriteLine($"Hopalong {index}: {lit:N0} lit pixels, {snapshots} progressive frames, exact repeat and disk round-trip.");
        }

        using (var cts = new CancellationTokenSource())
        {
            bool canceled = false;
            try
            {
                HopalongRenderer.RenderBuffer(DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong), 128, 128, null, cts.Token,
                    new HopalongProgress(value => { if (value >= 5) cts.Cancel(); }));
            }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Cancellation must interrupt a running orbit.");
        }
        var baseline = DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong);
        baseline.Iterations = 100_000;
        byte[] original = HopalongRenderer.RenderBuffer(baseline, 160, 120, null, CancellationToken.None);
        foreach (Action<DynamicSystemState> change in new Action<DynamicSystemState>[]
        {
            s => s.Hopalong.StartX = .1, s => s.Hopalong.B = .93,
            s => s.Hopalong.Rotation = 37, s => { s.CenterX += .3; s.Zoom = 2; }
        })
        {
            var changed = baseline.Clone(); change(changed);
            Check(!HopalongRenderer.RenderBuffer(changed, 160, 120, null, CancellationToken.None).SequenceEqual(original),
                "Initial point, formula, rotation and navigation must affect the image.");
        }
        var spans = HopalongRenderer.ViewSpans(baseline, 600, 300);
        Check(spans.X == spans.Y * 2 && spans.Y == baseline.Hopalong.Span / baseline.Zoom, "World pixels must remain square.");
        HopalongMap.Analysis view = HopalongMap.Analyze(baseline.Hopalong, CancellationToken.None);
        Check(view.Span > 1 && view.OccupiedCells > 1000, "Fit must find the nontrivial central orbit region.");
        var search1 = HopalongMap.Search(482, baseline.Hopalong, CancellationToken.None);
        var search2 = HopalongMap.Search(482, baseline.Hopalong, CancellationToken.None);
        Check(JsonSerializer.Serialize(search1) == JsonSerializer.Serialize(search2) && search1.View.OccupiedCells >= 1200,
            "Seeded variation must be reproducible and spatially rich.");
        var legacy = JsonSerializer.Deserialize<DynamicSystemState>("{\"Kind\":8}")!;
        Check(legacy.Kind == DynamicSystemKind.Popcorn && legacy.Clone().Hopalong is not null,
            "Appending the enum and missing settings must preserve existing saves.");
        foreach (Action<HopalongSettings> change in new Action<HopalongSettings>[]
            { s => s.A = double.NaN, s => s.StartX = double.PositiveInfinity, s => s.Span = 0 })
        {
            var invalid = new HopalongSettings(); change(invalid);
            bool rejected = false; try { invalid.Validate(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Invalid settings must be rejected before calculating.");
        }
        var paletteStore = new DynamicPaletteStore(DynamicSystemKind.Hopalong);
        var custom = palettes[0].Clone("Hopalong custom"); paletteStore.Save([custom]);
        Check(paletteStore.Load().Single(p => p.Name == custom.Name).Colors.SequenceEqual(custom.Colors), "Custom palettes must round-trip.");
        BitmapSource preview = await CatalogPreviewLoader.RenderAsync(tile, CancellationToken.None);
        byte[] previewPixels = new byte[512 * 512 * 4]; preview.CopyPixels(previewPixels, 512 * 4, 0);
        Check(previewPixels.SequenceEqual(frames[0]), "Catalog preview must be the actual default mode.");
        await VerifyHopalongWindowAsync(output);
        Console.WriteLine("PASS (hopalong): formula, continuous orbit, six presets, progressive frames, cancellation, fit, variation, saves, palettes, catalog and WPF controls.");
    }

    private static void VerifyHopalongOrbitReference()
    {
        var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong);
        state.Iterations = 8193; state.Hopalong.Rotation = 23; state.Hopalong.StartX = -.31;
        const int width = 121, height = 79;
        int[] hits = new int[width * height];
        double x = state.Hopalong.StartX, y = state.Hopalong.StartY;
        double radians = state.Hopalong.Rotation * Math.PI / 180;
        double sx = state.Hopalong.Span * width / height, sy = state.Hopalong.Span;
        for (int i = 0; i < state.Iterations; i++)
        {
            double oldX = x, oldY = y;
            x = oldY - (oldX > 0 ? 1 : oldX < 0 ? -1 : 0) * Math.Sqrt(Math.Abs(state.Hopalong.B * oldX - state.Hopalong.C));
            y = state.Hopalong.A - oldX;
            double fx = (x * Math.Cos(radians) - y * Math.Sin(radians) - (state.CenterX - sx * .5)) / sx;
            double fy = (state.CenterY + sy * .5 - x * Math.Sin(radians) - y * Math.Cos(radians)) / sy;
            if (fx is >= 0 and <= 1 && fy is >= 0 and <= 1)
                hits[(int)Math.Round(fy * (height - 1)) * width + (int)Math.Round(fx * (width - 1))]++;
        }
        byte[] expected = AttractorDensityColorizer.Colorize(hits, state.BackgroundColor, state.FractalColor, null, state.DensityGamma, CancellationToken.None);
        Check(HopalongRenderer.RenderBuffer(state, width, height, null, CancellationToken.None).SequenceEqual(expected),
            "Density must match an independent one-orbit reference, including its first point, rotation and rectangular viewport.");
    }

    private static async Task VerifyHopalongWindowAsync(string? output)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = (DynamicSystemWindow)MainWindow.GetWindowFactory("Hopalong")!();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] arguments) => typeof(DynamicSystemWindow).GetMethod(name, flags)!.Invoke(window, arguments);
        T Field<T>(string name) => (T)typeof(DynamicSystemWindow).GetField(name, flags)!.GetValue(window)!;
        try
        {
            Check(window.Title.Contains("Hopalong"), "The tile must open Hopalong directly.");
            var fields = Field<Dictionary<string, TextBox>>("_hopalongBoxes");
            fields["A"].Text = "1.2345678901234567"; fields["StartX"].Text = "0.125";
            var captured = (DynamicSystemState)Invoke("CaptureState", "UI")!;
            Check(captured.Hopalong.A == 1.2345678901234567 && captured.Hopalong.StartX == .125, "UI edits must reach render/export snapshots precisely.");
            Field<ComboBox>("_hopalongPresetsBox").SelectedIndex = 4;
            captured = (DynamicSystemState)Invoke("CaptureState", "Preset")!;
            Check(captured.Hopalong.A == -11 && captured.Hopalong.B == .05, "Presets must restore formula and framing.");
            Invoke("RestorePreviousHopalong"); captured = (DynamicSystemState)Invoke("CaptureState", "Back")!;
            Check(captured.Hopalong.A == 1.2345678901234567 && captured.Hopalong.StartX == .125, "Undo must restore the previous edited form.");
            Field<ComboBox>("_hopalongPaletteBox").SelectedIndex = 0;
            Check(((DynamicSystemState)Invoke("CaptureState", "Single")!).PaletteName == "", "Single-color mode must survive saving.");
            var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Hopalong); state.SsaaFactor = 2;
            Invoke("LoadState", state);
            var loaded = (DynamicSystemState)Invoke("CaptureState", "Loaded")!;
            Check(JsonSerializer.Serialize(loaded.Hopalong) == JsonSerializer.Serialize(state.Hopalong), "Load must restore all settings.");
            BitmapSource export = await window.RenderStatePreviewAsync(loaded, 320, 240, CancellationToken.None);
            Check(export.PixelWidth == 320 && export.PixelHeight == 240, "SSAA output must retain requested dimensions.");
            var work = (Task)Invoke("FindHopalongAsync", true)!;
            Field<ComboBox>("_hopalongPresetsBox").SelectedIndex = 2;
            await work;
            Check(((DynamicSystemState)Invoke("CaptureState", "Canceled")!).Hopalong.A == 7.3,
                "A stale search result must never replace a newly selected preset.");
            work = (Task)Invoke("FindHopalongAsync", true)!; await work;
            var varied = (DynamicSystemState)Invoke("CaptureState", "Variation")!;
            Check(varied.PointOfInterestId is null && varied.Hopalong.A != 7.3, "Variation must apply a new form.");
            Invoke("RestorePreviousHopalong");
            Check(((DynamicSystemState)Invoke("CaptureState", "Back again")!).Hopalong.A == 7.3, "Generated forms must support undo.");
            if (output is not null)
            {
                Invoke("LoadState", state);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1120, 700)); content.Arrange(new Rect(0, 0, 1120, 700)); content.UpdateLayout();
                var canvas = (FrameworkElement)window.FindName("CanvasSurface");
                ((Image)window.FindName("StableImage")).Source = await window.RenderStatePreviewAsync(state, (int)canvas.ActualWidth, (int)canvas.ActualHeight, CancellationToken.None);
                content.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render);
                var snapshot = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); snapshot.Render(content);
                SaveHopalongPng(snapshot, Path.Combine(output, "hopalong-window.png"));
                SaveHopalongPng(export, Path.Combine(output, "hopalong-ssaa.png"));
            }
        }
        finally { window.Close(); }
    }

    private sealed class HopalongProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private static void SaveHopalongPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
