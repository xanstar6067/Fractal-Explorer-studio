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
    private static async Task VerifyKifsAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("kifs-atlas");
        EnsureWindowThemeStyles();
        foreach (var symmetry in Enum.GetValues<KifsSymmetry>())
        {
            var source = Fractal3DCatalog.GetPresets(Fractal3DKind.Kifs).First(s => s.Kifs.Symmetry == symmetry).Clone();
            source.Kifs.OffsetZ = .34567890123456789;
            source.Kifs.RotationX = 7.123456789012345;
            var main = new Fractal3DWindow(Fractal3DKind.Kifs);
            KifsAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source);
                new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var before = main.CaptureState("view");
                Check(before.Kifs.OffsetZ == source.Kifs.OffsetZ && before.Kifs.RotationX == source.Kifs.RotationX, "Loading rounded KIFS parameters.");
                Check(Element<Button>(main, "KifsAtlasButton").Visibility == Visibility.Visible && Element<StackPanel>(main, "KifsPanel").Visibility == Visibility.Visible,
                    "The offscreen KIFS window must expose its atlas in the form panel.");
                atlas = CreateAtlas();
                Check(atlas.Owner == main, "Atlas lost its owner.");
                var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(before), "Untouched atlas rounded source values.");
                var boxes = Element<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>()
                    .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
                bool star = symmetry == KifsSymmetry.Dihedral;
                int count = star ? 9 : 8;
                Check(boxes.Length == count && boxes.All(NumericInput.IsRegistered), "The atlas needs eight validated fields, plus ray count only for stars.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 4, "Default axes must be scale and rotation Z.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Each axis pair must retain the other fixed values.");
                    NumericInput.CommitAll(atlas);
                }
                x.SelectedIndex = star ? 8 : 0; y.SelectedIndex = 4;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                minX.Text = "0"; maxX.Text = "30"; minY.Text = "-200"; maxY.Text = "200";
                NumericInput.CommitAll(atlas);
                Check(minX.Text == (star ? "3" : "1.2") && maxX.Text == (star ? "16" : "4") &&
                    minY.Text == "-180" && maxY.Text == "180", "Scale, rays and angles need different bounds.");
                minX.Text = star ? "6" : "1.8"; maxX.Text = star ? "7" : "2.2";
                minY.Text = "-10"; maxY.Text = "10";
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (star ? 6 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Incomplete previews or repeated integer ray counts.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(expected.Kifs.OffsetZ == source.Kifs.OffsetZ && expected.Kifs.RotationX == source.Kifs.RotationX, "Fixed values lost precision.");
                Check(expected.Kifs.Symmetry == source.Kifs.Symmetry && expected.Kifs.Seed == source.Kifs.Seed, "The atlas changed categorical settings.");
                var preview = expected.Clone();
                preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    var actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "KIFS atlas differs from direct GPU rendering.");
                    if (symmetry == KifsSymmetry.Tetrahedral)
                    {
                        Click((Button)tiles.Children[tiles.Children.Count - 1]);
                        Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Parameter changes did not affect KIFS previews.");
                    }
                }
                boxes[7].Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(boxes[7]) && NumericInput.GetHasNotice(boxes[7]) && !Element<Button>(atlas, "ApplyButton").IsEnabled,
                    "Invalid fixed input must recover and invalidate the old selection.");
                await Build(atlas);
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Applying changed unrelated view settings or rounded selected values.");
                Click(Element<Button>(main, "KifsUndoButton"));
                Check(JsonSerializer.Serialize(main.CaptureState("undo").Kifs) == JsonSerializer.Serialize(before.Kifs), "Atlas application did not undo in one step.");
                // Save the selected result and restore it through the actual main-window fields.
                var store = new Fractal3DSaveStore(Fractal3DKind.Kifs); after.SaveName = symmetry.ToString(); store.Save(after);
                var saved = store.Load().Single(s => s.SaveName == after.SaveName); main.LoadState(saved);
                Check(JsonSerializer.Serialize(main.CaptureState("loaded").Kifs) == JsonSerializer.Serialize(after.Kifs), "Saving/loading lost KIFS parameters.");
                var priorClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose), "Closing without application changed the main view.");
                Check(source.Kifs.OffsetZ == .34567890123456789, "Rendering mutated the source settings.");
            }
            finally { atlas?.Close(); main.Close(); }

            KifsAtlasWindow CreateAtlas() => (KifsAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateKifsAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        // Each seed is held fixed; its snapshot must not share the mutable KifsSettings instance.
        foreach (var seed in Enum.GetValues<KifsSeed>())
        {
            var source = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Kifs); source.Kifs.Seed = seed;
            var atlas = new KifsAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target); target.Kifs.OffsetX = -1;
                Check(source.Kifs.OffsetX == 1 && target.Kifs.Seed == seed, "Seed or source snapshot was modified.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("PASS (kifs-atlas): four symmetries, three seeds, all axis pairs, numeric bounds, integer rays, precise values, GPU previews, apply/undo, saves and close.");
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
