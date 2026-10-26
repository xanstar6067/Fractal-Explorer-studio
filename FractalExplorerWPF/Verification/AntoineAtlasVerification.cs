using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyAntoineAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("AntoineAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.AntoineNecklace;
        foreach (var source in Fractal3DCatalog.GetPresets(kind).Concat(
            from links in new[] { 24, 26, 48 } from generation in Enumerable.Range(0, 4) select State(links, generation)))
        {
            var atlas = new AntoineAtlasWindow(source);
            try
            {
                var boxes = FixedBoxes(atlas);
                Check(boxes.Length == 5 && boxes.All(NumericInput.IsRegistered), "Five validated Antoine parameters are required.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 2, "Default axes must compare scale and tilt.");
                for (int a = 0; a < 5; a++) for (int b = a + 1; b < 5; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b; NumericInput.CommitAll(atlas);
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == 3,
                        "Every axis pair must keep three fixed parameters.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded parameters or changed generation zero.");
                decimal[] original = [(decimal)source.Antoine.ScaleFactor, (decimal)source.Antoine.Thickness,
                    (decimal)source.Antoine.Tilt, source.Antoine.Links, source.Iterations];
                decimal[] minima = [1.35m, 0m, 60m, 24m, 0m], maxima = [1.5m, 1m, 90m, 48m, 3m];
                for (int i = 0; i < 5; i++)
                {
                    foreach (decimal value in new[] { minima[i], maxima[i] })
                    {
                        var point = (decimal[])original.Clone(); point[i] = value;
                        var target = source.Clone(); Apply(atlas, target, point); target.Antoine.Validate();
                        Check(target.Iterations is >= 0 and <= 3, "Generation bounds were lost.");
                    }
                    var invalid = (decimal[])original.Clone(); invalid[i] = maxima[i] + .1m;
                    Reject(atlas, source.Clone(), invalid);
                }
                var odd = (decimal[])original.Clone(); odd[3] = 25; Reject(atlas, source.Clone(), odd);
                var fractional = (decimal[])original.Clone(); fractional[4] = 1.5m; Reject(atlas, source.Clone(), fractional);
                var wrong = source.Clone(); wrong.Kind = Fractal3DKind.Kleinian; Reject(atlas, wrong, original);

                x.SelectedIndex = 0; y.SelectedIndex = 2;
                boxes[3].Text = "25"; NumericInput.CommitAll(atlas);
                Check(boxes[3].Text == "26" && NumericInput.GetHasNotice(boxes[3]), "Fixed links must correct odd input with feedback.");
                foreach (int size in new[] { 3, 5, 7 })
                foreach (var bounds in new[] { (24m, 48m), (24m, 26m), (25m, 27m), (47m, 48m) })
                {
                    decimal[] values = Samples(atlas, 3, bounds.Item1, bounds.Item2, size);
                    Check(values.Length == Math.Min(size, decimal.Floor(bounds.Item2 / 2) - decimal.Ceiling(bounds.Item1 / 2) + 1) &&
                        values.Distinct().Count() == values.Length && values.All(v => v >= bounds.Item1 && v <= bounds.Item2 && v % 2 == 0),
                        "Link sampling must contain only unique even counts within the requested interval.");
                    Check(Samples(atlas, 3, bounds.Item1, bounds.Item2, size, true).SequenceEqual(values.Reverse()), "Stepped Y order is reversed incorrectly.");
                }
                Check(Samples(atlas, 3, 25, 25, 7).Length == 0 && Samples(atlas, 3, 25, 27, 7).SequenceEqual(new[] { 26m }),
                    "Empty or single-value stepped intervals must be handled without division by zero.");
                Check(Samples(atlas, 4, 0, 3, 7).SequenceEqual(new[] { 0m, 1m, 2m, 3m }), "Generation zero was omitted or duplicated.");
                var narrowed = (ParameterAtlasRange)typeof(ParameterAtlasWindow).GetMethod("RangeAround", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(atlas, [1.4m, 75m, .03m, 6m])!;
                Check(narrowed.IsValid, "Continuous refinement escaped the valid interval.");
                x.SelectedIndex = 3; y.SelectedIndex = 4;
                narrowed = (ParameterAtlasRange)typeof(ParameterAtlasWindow).GetMethod("RangeAround", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(atlas, [26m, 0m, 1m, 1m])!;
                Check(Samples(atlas, 3, narrowed.MinX, narrowed.MaxX, 7).Contains(26m) &&
                    Samples(atlas, 4, narrowed.MinY, narrowed.MaxY, 7).Contains(0m), "Refinement must retain the selected even count and generation zero.");
            }
            finally { atlas.Close(); }
        }
        // Existing unit-step and continuous atlas axes retain their exact sampling behaviour; no GPU needed.
        var baseline = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
        var existing = new MandelbulbAtlasWindow(baseline);
        try
        {
            foreach (int size in new[] { 3, 5, 7 })
            foreach (var bounds in new[] { (1m, 64m), (63m, 64m), (7m, 13m) })
            {
                int count = (int)Math.Min(size, bounds.Item2 - bounds.Item1 + 1);
                decimal[] expected = Enumerable.Range(0, count).Select(i => decimal.Round(
                    bounds.Item1 + (bounds.Item2 - bounds.Item1) * i / (count - 1), 0, MidpointRounding.AwayFromZero)).ToArray();
                Check(Samples(existing, 1, bounds.Item1, bounds.Item2, size).SequenceEqual(expected), "Unit-step sampling changed for existing atlases.");
            }
            Check(Samples(existing, 0, 2.5m, 3.5m, 3).SequenceEqual(new[] { 2.5m, 3m, 3.5m }), "Continuous sampling changed for existing atlases.");
        }
        finally { existing.Close(); }

        foreach (bool discrete in new[] { false, true })
        {
            var source = State(discrete ? 24 : 32, discrete ? 3 : 1);
            if (discrete)
            {
                source.TargetX = 1; source.TargetY = .1827366691; source.CameraDistance = .15;
                source.CameraYaw = 18; source.CameraPitch = 35;
            }
            var main = new Fractal3DWindow(kind);
            AntoineAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var before = main.CaptureState("view");
                Check(before.Antoine == source.Antoine && before.Iterations == source.Iterations, "Loading rounded Antoine parameters.");
                var editor = Element<AntoineEditor>(main, "AntoineEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible,
                    "The active editor must expose its atlas.");
                atlas = CreateAtlas(); Check(atlas.Owner == main, "Atlas lost its owner.");
                if (discrete)
                {
                    // One small third-generation preview checks that the generation is never silently reduced.
                    var render = (Func<decimal[], CancellationToken, Task<byte[]>>)typeof(Fractal3DParameterAtlasWindow)
                        .GetMethod("CreateAsyncRenderSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, null)!;
                    byte[] actual = await render([(decimal)before.Antoine.ScaleFactor, (decimal)before.Antoine.Thickness,
                        (decimal)before.Antoine.Tilt, 24, 3], CancellationToken.None);
                    using var renderer = new Fractal3DRenderer();
                    var frame = await renderer.RenderPixelsAsync(Preview(before), 144, 108, null, null, CancellationToken.None);
                    Check(actual.SequenceEqual(frame.Buffer) && renderer.AntoineRingCount == 24 * 24 * 24,
                        "Third-generation atlas must render all 24³ rings with the source camera.");
                }
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                x.SelectedIndex = discrete ? 3 : 0; y.SelectedIndex = discrete ? 4 : 2;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Atlas bounds need NumericInput.");
                minX.Text = "-100"; maxX.Text = "100"; minY.Text = "-100"; maxY.Text = "100"; NumericInput.CommitAll(atlas);
                Check(minX.Text == (discrete ? "24" : "1.35") && maxX.Text == (discrete ? "48" : "1.5") &&
                    minY.Text == (discrete ? "0" : "60") && maxY.Text == (discrete ? "3" : "90"), "Per-parameter bounds are incorrect.");
                minX.Text = discrete ? "24" : "1.36"; maxX.Text = discrete ? "26" : "1.49";
                minY.Text = discrete ? "0" : "61"; maxY.Text = discrete ? "1" : "89";
                var thickness = FixedBoxes(atlas)[1]; thickness.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(thickness) && NumericInput.GetHasNotice(thickness), "Invalid fixed input must recover with feedback.");
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (discrete ? 4 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled),
                    "Preview grid is incomplete or repeats even counts / generations.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(expected.Antoine.Thickness == before.Antoine.Thickness, "A fixed thickness lost double precision.");
                using (var renderer = new Fractal3DRenderer())
                {
                    var frame = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(frame.Buffer) && renderer.AntoineRingCount == (int)Math.Pow(expected.Antoine.Links, expected.Iterations),
                        "Atlas differs from direct rendering of the selected necklace generation.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Parameter changes produced identical previews.");
                    if (discrete)
                    {
                        expected = before.Clone(); atlas.ApplySelectionTo(expected);
                        frame = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                        Check(expected.Iterations == 0 && expected.Antoine.Links == 26 && renderer.AntoineRingCount == 1 &&
                            Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source).SequenceEqual(frame.Buffer),
                            "Generation zero must preview the single original torus.");
                    }
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed unrelated settings or rounded parameters.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                var restored = main.CaptureState("restored");
                Check(restored.Antoine == after.Antoine && restored.Iterations == after.Iterations, "Save/load lost selected geometry or generation zero.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the view.");
                Check(source.Antoine.ScaleFactor == 1.4123456789012345 && source.Iterations == (discrete ? 3 : 1), "Preview rendering mutated the source.");
            }
            finally { atlas?.Close(); main.Close(); }
            AntoineAtlasWindow CreateAtlas() => (AntoineAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateAntoineAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (antoine-atlas): presets/generations, all axis pairs, bounds, even counts, refinement, shared sampling, precision, GPU previews 0/1/3, apply, saves and close.");

        static Fractal3DState State(int links, int generation)
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.AntoineNecklace);
            state.Iterations = generation;
            state.Antoine = new() { Links = links, ScaleFactor = 1.4123456789012345, Thickness = .6512345678901234, Tilt = 81.12345678901234 };
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var preview = state.Clone(); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
            preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false; return preview;
        }
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static decimal[] Samples(ParameterAtlasWindow atlas, int index, decimal min, decimal max, int size, bool reverse = false) =>
            (decimal[])typeof(ParameterAtlasWindow).GetMethod("SampleAxis", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(atlas, [index, min, max, size, reverse])!;
        static void Apply(AntoineAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(AntoineAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(AntoineAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Invalid parameters must be rejected atomically.");
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
