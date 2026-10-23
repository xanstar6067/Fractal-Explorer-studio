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
    private static async Task VerifySurfaceShapeAtlasAsync(Fractal3DKind kind)
    {
        bool tetrahedron = kind == Fractal3DKind.SierpinskiTetrahedron;
        string prefix = tetrahedron ? "Sierpinski" : "PeriodicSurface";
        using var sandbox = DataSandbox.Create(prefix + "Atlas");
        EnsureWindowThemeStyles();
        if (!tetrahedron)
        {
            // All categorical combinations use the same numerical adapter; no GPU work here.
            foreach (var family in Enum.GetValues<PeriodicSurfaceFamily>())
            foreach (var region in Enum.GetValues<PeriodicSurfaceRegion>())
            foreach (bool extras in new[] { false, true })
            {
                var state = Fractal3DCatalog.CreateDefaultState(kind);
                state.PeriodicSurface = new()
                {
                    Family = family, BlendFamily = extras ? (PeriodicSurfaceFamily)(((int)family + 1) % 3) : family,
                    Region = region, ThicknessAxis = extras ? 2 : 0, CutAxis = extras ? 3 : 0,
                    StretchZ = 1.1234567890123457, ThicknessGradient = .23456789012345678, CutPosition = .34567890123456789
                };
                var window = new PeriodicSurfaceAtlasWindow(state);
                try
                {
                    bool width = region is PeriodicSurfaceRegion.Sheet or PeriodicSurfaceRegion.Both;
                    int count = 5 + (extras ? 2 : 0) + (width ? (extras ? 2 : 1) : 0);
                    var boxes = FixedBoxes(window);
                    Check(boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Inactive surface parameters must be excluded from atlas axes.");
                    var x = Element<ComboBox>(window, "XAxisBox"); var y = Element<ComboBox>(window, "YAxisBox");
                    Check(x.SelectedIndex == 0 && y.SelectedIndex == (width ? (extras ? 6 : 5) : 1), "Wrong initial surface axes.");
                    for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                    {
                        x.SelectedIndex = a; y.SelectedIndex = b;
                        Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Surface axis pairs lost fixed fields.");
                        NumericInput.CommitAll(window);
                    }
                    var target = state.Clone(); window.ApplySelectionTo(target);
                    Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(state), "Untouched surface values lost precision or inactive settings.");
                    target.PeriodicSurface = target.PeriodicSurface with { CutAxis = (target.PeriodicSurface.CutAxis + 1) % 4 };
                    bool rejected = false;
                    try { window.ApplySelectionTo(target); } catch (ArgumentException) { rejected = true; }
                    Check(rejected, "An atlas must reject a different categorical configuration.");
                }
                finally { window.Close(); }
            }
        }
        var source = Fractal3DCatalog.CreateDefaultState(kind);
        source.SierpinskiScale = 1.9876543210987654;
        source.PeriodicSurface = new()
        {
            Region = PeriodicSurfaceRegion.Both, ThicknessAxis = 2, CutAxis = 3,
            StretchZ = 1.1234567890123457, ThicknessGradient = .23456789012345678,
            CutPosition = .34567890123456789, Blend = .12345678901234567
        };
        var main = new Fractal3DWindow(kind);
        Fractal3DParameterAtlasWindow? atlas = null;
        try
        {
            main.LoadState(source);
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var before = main.CaptureState("view");
            Check(!tetrahedron || before.SierpinskiScale == source.SierpinskiScale, "Loading rounded folding scale.");
            Check(tetrahedron || before.PeriodicSurface == source.PeriodicSurface, "Loading rounded surface settings.");
            FrameworkElement panel = tetrahedron ? Element<StackPanel>(main, "SierpinskiPanel") : Element<PeriodicSurfaceEditor>(main, "PeriodicEditor");
            Check(panel.Visibility == Visibility.Visible && Element<Button>(tetrahedron ? main : panel, prefix + "AtlasButton").Visibility == Visibility.Visible,
                "Atlas entry is missing from the active form panel.");
            atlas = CreateAtlas();
            Check(atlas.Owner == main, "Atlas lost its owner.");
            var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
            Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(before), "Unchanged atlas modified source settings.");
            var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
            var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
            minX.Text = "0"; maxX.Text = "100"; minY.Text = "-100"; maxY.Text = "100";
            NumericInput.CommitAll(atlas);
            Check(minX.Text == (tetrahedron ? "1.05" : "0.5") && maxX.Text == (tetrahedron ? "8" : "4") &&
                minY.Text == (tetrahedron ? "1" : "0.02") && maxY.Text == (tetrahedron ? "64" : "1.2"), "Axis bounds are incorrect.");
            minX.Text = tetrahedron ? "1.8" : "1.2"; maxX.Text = tetrahedron ? "2.2" : "1.8";
            minY.Text = tetrahedron ? "63" : "0.2"; maxY.Text = tetrahedron ? "64" : "0.6";
            if (!tetrahedron)
            {
                var boxes = FixedBoxes(atlas);
                boxes[4].Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(boxes[4]) && NumericInput.GetHasNotice(boxes[4]), "Invalid fixed input must recover with feedback.");
            }
            await Build(atlas);
            var tiles = Element<UniformGrid>(atlas, "TilesPanel");
            Check(tiles.Children.Count == (tetrahedron ? 6 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Incomplete previews or duplicated integer rows.");
            Click((Button)tiles.Children[0]);
            var expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Check(!tetrahedron || expected.Iterations == 64, "Atlas must retain iterations above the common preview budget.");
            var preview = expected.Clone();
            if (!tetrahedron) preview.Iterations = Math.Clamp(preview.Iterations, 1, 32);
            preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
            preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
            using (var renderer = new Fractal3DRenderer())
            {
                var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                var actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                Check(actual.SequenceEqual(reference.Buffer), "Atlas differs from direct GPU rendering.");
                Click((Button)tiles.Children[tiles.Children.Count - 1]);
                Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Parameter changes did not affect previews.");
            }
            expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
            var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed unrelated view settings or rounded parameters.");
            var store = new Fractal3DSaveStore(kind); store.Save(after);
            var loaded = store.Load().Single(); main.LoadState(loaded);
            var restored = main.CaptureState("view");
            Check(tetrahedron ? restored.SierpinskiScale == after.SierpinskiScale && restored.Iterations == after.Iterations
                : restored.PeriodicSurface == after.PeriodicSurface, "Save/load lost selected or fixed settings.");
            var priorClose = main.CaptureState("view");
            atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
            var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose), "Closing without applying changed the main view.");
            Check(source.SierpinskiScale == 1.9876543210987654 && source.PeriodicSurface.StretchZ == 1.1234567890123457, "Source snapshot was mutated.");
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine($"PASS ({(tetrahedron ? "sierpinski-atlas" : "periodic-surface-atlas")}): active axes, bounds, precision, GPU previews, apply, saves and cancellation.");

        Fractal3DParameterAtlasWindow CreateAtlas() => (Fractal3DParameterAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("Create" + prefix + "Atlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
