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
    private static async Task VerifyApollonianPackingAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("ApollonianPackingAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.ApollonianPacking;
        var mode = Fractal3DCatalog.Mode(kind);
        var sources = Fractal3DCatalog.GetPresets(kind).Concat(Enum.GetValues<Fractal3DColoringMode>().Select(coloring =>
        {
            var s = Fractal3DCatalog.CreateDefaultState(kind); s.ColoringMode = coloring;
            s.ColorScale = 1.1234567890123456; s.ColorOffset = .12345678901234567; return s;
        }));
        foreach (var source in sources)
        {
            string original = JsonSerializer.Serialize(source);
            var atlas = new ApollonianPackingAtlasWindow(source);
            try
            {
                Check(atlas.Colorings.SequenceEqual(Enum.GetValues<Fractal3DColoringMode>().Where(mode.HasColoringMode)), "Atlas must expose the mode's supported coloring sources.");
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == original, "Untouched atlas changed generation, coloring or exact unrelated values.");
                Check(Element<ComboBox>(atlas, "XAxisBox").SelectedIndex == 0 && Element<ComboBox>(atlas, "YAxisBox").SelectedIndex == 1,
                    "Atlas axes must compare generation and coloring.");
                Check(new[] { "MinRealBox", "MaxRealBox", "MinImaginaryBox", "MaxImaginaryBox" }.All(n => NumericInput.IsRegistered(Element<TextBox>(atlas, n))),
                    "Integer range fields require NumericInput.");
                foreach (int generation in Enumerable.Range(1, 5)) for (int color = 0; color < atlas.Colorings.Count; color++)
                {
                    var target = source.Clone(); Apply(atlas, target, [generation, color + 1]);
                    var expected = source.Clone(); expected.Iterations = generation; expected.ColoringMode = atlas.Colorings[color];
                    Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(expected), "Atlas application must change only generation and coloring.");
                }
                foreach (decimal[] invalid in new decimal[][] { [0, 1], [6, 1], [1.5m, 1], [1, 0], [1, atlas.Colorings.Count + 1], [1, 1.5m] })
                    Reject(atlas, source.Clone(), invalid);
                var wrong = source.Clone(); wrong.Kind = Fractal3DKind.Mandelbulb; Reject(atlas, wrong, [1, 1]);
                Check(JsonSerializer.Serialize(source) == original, "Atlas mutated its source.");
            }
            finally { atlas.Close(); }
        }
        foreach (var invalid in new[] { (-1, Fractal3DColoringMode.Material), (6, Fractal3DColoringMode.Material), (1, (Fractal3DColoringMode)999) })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind); source.Iterations = invalid.Item1; source.ColoringMode = invalid.Item2;
            bool rejected = false;
            try { new ApollonianPackingAtlasWindow(source).Close(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid source generations and colors must be rejected.");
        }
        Console.WriteLine("Apollonian atlas: presets, supported color sources, integer limits, exact state and atomic application passed.");

        // Two small grids, with both material colors and the mode-specific sphere data.
        for (int scenario = 0; scenario < 2; scenario++)
        {
            var source = Fractal3DCatalog.GetPresets(kind)[scenario == 0 ? 0 : 3].Clone();
            var main = new Fractal3DWindow(kind); ApollonianPackingAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<ApollonianPackingEditor>(main, "ApollonianEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible,
                    "The packing panel must expose its atlas.");
                var before = main.CaptureState("view"); string original = JsonSerializer.Serialize(before);
                atlas = CreateAtlas(main); Check(atlas.Owner == main, "Atlas lost its owner.");
                SetRange(atlas, -100, 999, -100, 999); NumericInput.CommitAll(atlas);
                Check(Element<TextBox>(atlas, "MinRealBox").Text == "1" && Element<TextBox>(atlas, "MaxRealBox").Text == "5" &&
                    Element<TextBox>(atlas, "MinImaginaryBox").Text == "1" && Element<TextBox>(atlas, "MaxImaginaryBox").Text == atlas.Colorings.Count.ToString(),
                    "Generation and coloring indices require independent integer bounds.");
                SetRange(atlas, scenario == 0 ? 1 : 4, scenario == 0 ? 2 : 5, scenario == 0 ? 1 : 3, scenario == 0 ? 2 : 5);
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (scenario == 0 ? 4 : 6) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Atlas repeated or omitted integer rows/columns.");
                Click((Button)tiles.Children[0]); var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(Element<TextBlock>(atlas, "SelectedText").Text.Contains(mode.ColoringModeName(expected.ColoringMode)), "Selected coloring requires its actual mode-specific name.");
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    byte[] pixels = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(pixels.SequenceEqual(reference.Buffer), "Atlas differs from direct rendering of the selected generation and coloring.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!pixels.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Generation/color changes must alter the preview.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(expected.Iterations == (scenario == 0 ? 2 : 5) && expected.ColoringMode == (scenario == 0 ? Fractal3DColoringMode.Material : Fractal3DColoringMode.OrbitTrap),
                    "Generation and named color must follow their selected coordinates.");
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed camera, palette, quality or unrelated parameters.");
                Check(Element<ComboBox>(main, "ColoringModeBox").SelectedIndex == (int)after.ColoringMode &&
                    Element<FrameworkElement>(main, "MaterialColorPanel").Visibility == (scenario == 0 ? Visibility.Visible : Visibility.Collapsed),
                    "Main coloring selection and its controls did not follow the atlas.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                var saved = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State;
                main.LoadState(saved); var restored = main.CaptureState("restored");
                Check(restored.Iterations == after.Iterations && restored.ColoringMode == after.ColoringMode &&
                    JsonSerializer.Serialize(restored.ResolvePalette()) == JsonSerializer.Serialize(after.ResolvePalette()), "Save/load lost generation, coloring or palette.");
                var beforeClose = main.CaptureState("view"); atlas = CreateAtlas(main);
                SetRange(atlas, 1, 2, 1, 2); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing during rendering applied an unwanted selection.");
                Check(JsonSerializer.Serialize(before) == original, "Rendering mutated the source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
            Console.WriteLine($"Apollonian atlas: small GPU grid {scenario + 1}/2, apply, saves and cancellation passed.");
        }
        // The new panel is confined to this mode.
        var other = new Fractal3DWindow(Fractal3DKind.Mandelbulb);
        try { Check(Element<ApollonianPackingEditor>(other, "ApollonianEditor").Visibility == Visibility.Collapsed, "Packing panel leaked into another mode."); }
        finally { other.Close(); }
        Console.WriteLine("PASS (apollonian-packing-atlas): supported colors, generations, exact state, GPU previews, main UI, save/load and close.");

        static Fractal3DState Preview(Fractal3DState state)
        {
            var p = state.Clone(); p.MaxSteps = Math.Min(p.MaxSteps, 160); p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail);
            p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false; return p;
        }
        static ApollonianPackingAtlasWindow CreateAtlas(Fractal3DWindow main) => (ApollonianPackingAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateApollonianPackingAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static void Apply(ApollonianPackingAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(ApollonianPackingAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(ApollonianPackingAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Invalid values must be rejected without partial application.");
        }
        static void SetRange(ParameterAtlasWindow atlas, int minX, int maxX, int minY, int maxY)
        {
            Element<TextBox>(atlas, "MinRealBox").Text = minX.ToString(); Element<TextBox>(atlas, "MaxRealBox").Text = maxX.ToString();
            Element<TextBox>(atlas, "MinImaginaryBox").Text = minY.ToString(); Element<TextBox>(atlas, "MaxImaginaryBox").Text = maxY.ToString();
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
