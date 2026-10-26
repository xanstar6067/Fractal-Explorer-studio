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
    private static async Task VerifyKleinianAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("KleinianAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Kleinian;
        // All configurations and presets: parameter contracts only, without rendering.
        foreach (var source in Fractal3DCatalog.GetPresets(kind).Concat(Enum.GetValues<KleinianConfiguration>().Select(configuration =>
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            state.Kleinian = new() { Configuration = configuration, RadiusRatio = .9123456789012345,
                MinRadius = .006123456789012345, Thickness = .003123456789012345 };
            return state;
        })))
        {
            var atlas = new KleinianAtlasWindow(source);
            try
            {
                var boxes = FixedBoxes(atlas);
                Check(boxes.Length == 4 && boxes.All(NumericInput.IsRegistered), "Four validated Kleinian parameters are required.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 2, "Default axes must compare radius and thickening.");
                for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b; NumericInput.CommitAll(atlas);
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == 2,
                        "Every axis pair must keep two fixed parameters.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded precise source parameters.");
                decimal[] original = [(decimal)source.Kleinian.RadiusRatio, (decimal)source.Kleinian.MinRadius,
                    (decimal)source.Kleinian.Thickness, source.Iterations];
                decimal[] minima = [.65m, .001m, 0m, 1m], maxima = [1m, .05m, .04m, 10m];
                for (int i = 0; i < 4; i++)
                {
                    foreach (decimal value in new[] { minima[i], maxima[i] })
                    {
                        var point = (decimal[])original.Clone(); point[i] = value;
                        var target = source.Clone(); Apply(atlas, target, point); target.Kleinian.Validate();
                        Check(target.Kleinian.Configuration == source.Kleinian.Configuration, "A numerical axis changed generators.");
                    }
                    var invalid = (decimal[])original.Clone(); invalid[i] = maxima[i] + .1m;
                    Reject(atlas, source.Clone(), invalid);
                }
                var fractional = (decimal[])original.Clone(); fractional[3] = 3.5m;
                Reject(atlas, source.Clone(), fractional);
                var wrong = source.Clone(); wrong.Kleinian = wrong.Kleinian with
                { Configuration = (KleinianConfiguration)(((int)wrong.Kleinian.Configuration + 1) % 3) };
                Reject(atlas, wrong, original);
            }
            finally { atlas.Close(); }
        }

        // Only two small grids exercise the GPU and the actual application/save path.
        foreach (bool depthAxis in new[] { false, true })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Kleinian = new() { Configuration = depthAxis ? KleinianConfiguration.Tetrahedron : KleinianConfiguration.Octahedron,
                RadiusRatio = .9512345678901234, MinRadius = .006123456789012345, Thickness = .003123456789012345 };
            var main = new Fractal3DWindow(kind);
            KleinianAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var before = main.CaptureState("view");
                Check(before.Kleinian == source.Kleinian, "Loading rounded sphere parameters.");
                var editor = Element<KleinianEditor>(main, "KleinianEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible,
                    "The active editor must expose its atlas.");
                atlas = CreateAtlas(); Check(atlas.Owner == main, "Atlas lost its owner.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                x.SelectedIndex = depthAxis ? 1 : 0; y.SelectedIndex = depthAxis ? 3 : 2;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Atlas bounds need NumericInput.");
                minX.Text = "-100"; maxX.Text = "100"; minY.Text = "-100"; maxY.Text = "100";
                NumericInput.CommitAll(atlas);
                Check(minX.Text == (depthAxis ? "0.001" : "0.65") && maxX.Text == (depthAxis ? "0.05" : "1") &&
                    minY.Text == (depthAxis ? "1" : "0") && maxY.Text == (depthAxis ? "10" : "0.04"), "Per-parameter bounds are incorrect.");
                minX.Text = depthAxis ? ".025" : ".87"; maxX.Text = depthAxis ? ".04" : "1";
                minY.Text = depthAxis ? "9" : "0"; maxY.Text = depthAxis ? "10" : ".01";
                var fixedBox = FixedBoxes(atlas)[depthAxis ? 0 : 1];
                fixedBox.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(fixedBox) && NumericInput.GetHasNotice(fixedBox), "Invalid fixed input must recover with feedback.");
                if (depthAxis) FixedBoxes(atlas)[2].Text = ".0123456789012345";
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (depthAxis ? 6 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled),
                    "Preview grid is incomplete or duplicates integer depths.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(!depthAxis || expected.Iterations == 10, "The top integer row must retain depth 10.");
                Check(depthAxis || expected.Kleinian.MinRadius == before.Kleinian.MinRadius, "A precise fixed detail radius was rounded.");
                var preview = expected.Clone();
                preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Kleinian atlas differs from direct rendering of the selected finite approximation.");
                    Check(renderer.KleinianBallCount <= KleinianGeometry.MaxBalls, "Atlas exceeded the shared sphere budget.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)),
                        "Different radius/thickness or depth/detail samples produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed unrelated view settings or rounded parameters.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                var restored = main.CaptureState("restored");
                Check(restored.Kleinian == after.Kleinian && restored.Iterations == after.Iterations, "Save/load lost sphere parameters or depth.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the view.");
                Check(source.Kleinian.MinRadius == .006123456789012345 && source.Iterations == 7, "Preview rendering mutated the source.");
            }
            finally { atlas?.Close(); main.Close(); }
            KleinianAtlasWindow CreateAtlas() => (KleinianAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateKleinianAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (kleinian-atlas): configurations/presets, all axis pairs, numeric bounds, integer depth, precision, GPU previews, apply, saves and close.");

        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(KleinianAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(KleinianAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(KleinianAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Incompatible or invalid parameters must be rejected atomically.");
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
