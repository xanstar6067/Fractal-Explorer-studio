using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyNewton3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("newton3d-atlas");
        EnsureWindowThemeStyles();
        var kind = Fractal3DKind.Newton3D;
        foreach (var region in Enum.GetValues<Newton3DRegion>())
        for (int cut = 0; cut <= 3; cut++)
        foreach (int iterations in new[] { 16, 64, 128 })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Newton = new() { Size = 96, MaxIterations = iterations, Region = region, CutAxis = cut,
                Extent = 1.8123456789012345, Relaxation = 1.1234567890123457, SliceW = .23456789012345678,
                CutPosition = .34567890123456789, SliceOnly = cut == 1 };
            var atlas = new Newton3DAtlasWindow(source);
            try
            {
                int count = 3 + (region == Newton3DRegion.Boundary ? 1 : 0) + (cut > 0 ? 1 : 0);
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas);
                Check(names.Length == count && boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Newton atlas must expose only active, validated parameters.");
                Check(names.Contains("Порог границы · итерации") == (region == Newton3DRegion.Boundary) &&
                    names.Contains("Положение отсечения · доля R") == (cut > 0), "Boundary and cut parameters have incorrect activation.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Initial Newton axes must be w and relaxation.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Newton axes must preserve the other fixed fields.");
                    NumericInput.CommitAll(atlas);
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Unchanged Newton atlas rounded parameters or lowered the main grid size.");
                if (region == Newton3DRegion.Boundary)
                {
                    x.SelectedIndex = 3; y.SelectedIndex = 0;
                    var min = Element<TextBox>(atlas, "MinRealBox"); var max = Element<TextBox>(atlas, "MaxRealBox");
                    min.Text = "0"; max.Text = "999"; NumericInput.CommitAll(atlas);
                    Check(min.Text == "2" && max.Text == (iterations - 1).ToString(), "Boundary threshold must remain below the fixed iteration count.");
                }
                var wrong = source.Clone(); wrong.Newton = wrong.Newton with { Region = (Newton3DRegion)(((int)region + 1) % 3) };
                string before = JsonSerializer.Serialize(wrong); bool rejected = false;
                try { atlas.ApplySelectionTo(wrong); } catch (ArgumentException) { rejected = true; }
                Check(rejected && JsonSerializer.Serialize(wrong) == before, "A different Newton region must be rejected without mutation.");
            }
            finally { atlas.Close(); }
        }
        // Degrees are fixed, including nonzero root indices; no rendering or remapping of roots here.
        for (int degree = 3; degree <= 8; degree++)
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            state.Newton = new() { Degree = degree, Region = Newton3DRegion.RootPair, PlusRoot = degree - 1, MinusRoot = degree - 2 };
            var atlas = new Newton3DAtlasWindow(state);
            try
            {
                var same = state.Clone(); atlas.ApplySelectionTo(same);
                Check(same.Newton == state.Newton, "The atlas changed a classical degree or its root indices.");
            }
            finally { atlas.Close(); }
        }
        foreach (bool custom in new[] { false, true })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Newton = Newton3DFormula.Prepare(new() { Size = 96, Formula = custom ? "z^3-z+1" : null,
                Region = custom ? Newton3DRegion.PlusBasin : Newton3DRegion.Boundary, PlusRoot = 0, MinusRoot = 1,
                SliceW = .23456789012345678, Relaxation = 1.1234567890123457, Extent = 1.8123456789012345,
                CutPosition = .34567890123456789 });
            var main = new Fractal3DWindow(kind);
            Newton3DAtlasWindow? active = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<Newton3DEditor>(main, "NewtonEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").IsEnabled, "Newton editor must expose its atlas.");
                var before = main.CaptureState("view");
                Check(before.Newton == source.Newton, "Loading rounded Newton parameters or reordered formula roots.");
                var formulaDraft = Element<TextBox>(editor, "FormulaBox"); formulaDraft.Text = "z^";
                var radiusDraft = Element<TextBox>(editor, "RootRadiusBox"); radiusDraft.Text = "9.123456789012345";
                string draftStatus = Element<TextBlock>(editor, "FormulaStatus").Text;
                active = CreateAtlas();
                Check(active.Owner == main && Element<TextBlock>(active, "ApplicationHintText").Text.Contains(source.Newton.Expression), "The atlas must identify its applied formula and owner.");
                var x = Element<ComboBox>(active, "XAxisBox"); var y = Element<ComboBox>(active, "YAxisBox");
                x.SelectedIndex = 0; y.SelectedIndex = custom ? 1 : 3;
                var minX = Element<TextBox>(active, "MinRealBox"); var maxX = Element<TextBox>(active, "MaxRealBox");
                var minY = Element<TextBox>(active, "MinImaginaryBox"); var maxY = Element<TextBox>(active, "MaxImaginaryBox");
                minX.Text = "-20"; maxX.Text = "20"; minY.Text = "0"; maxY.Text = "200";
                NumericInput.CommitAll(active);
                Check(minX.Text == "-2" && maxX.Text == "2" && minY.Text == (custom ? "0.2" : "2") && maxY.Text == (custom ? "1.8" : "63"), "Newton axis bounds are incorrect.");
                minX.Text = "-0.2"; maxX.Text = "0.4";
                minY.Text = custom ? "0.85" : "6"; maxY.Text = custom ? "1.15" : "7";
                var fixedExtent = FixedBoxes(active)[2]; fixedExtent.Text = "NaN"; NumericInput.CommitAll(active);
                Check(NumericInput.IsValid(fixedExtent) && NumericInput.GetHasNotice(fixedExtent), "Invalid fixed extent must recover with feedback.");
                var fixedCut = FixedBoxes(active)[custom ? 3 : 4]; fixedCut.Text = "-2"; NumericInput.CommitAll(active);
                Check(fixedCut.Text == "-1", "Cut position must remain a normalized fraction of the extent.");
                fixedCut.Text = "-0.12345678901234567";
                await Build(active);
                var tiles = Element<UniformGrid>(active, "TilesPanel");
                Check(tiles.Children.Count == (custom ? 9 : 6) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Newton previews are incomplete or integer thresholds repeat.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); active.ApplySelectionTo(expected);
                Check(expected.Newton.Size == 96 && expected.Newton.Roots == before.Newton.Roots && expected.Newton.Extent == before.Newton.Extent,
                    "Applying must retain the main grid, root catalog and precise fixed extent.");
                var preview = expected.Clone(); preview.Newton = preview.Newton with { Size = 32 };
                preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    var actual = Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Newton atlas must match direct rendering on its 32³ preview grid.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source)), "Newton parameter changes did not affect previews.");
                }
                expected = before.Clone(); active.ApplySelectionTo(expected);
                var firstShader = Newton3DComputeShader.Entry(before.Newton);
                var selectedShader = Newton3DComputeShader.Entry(expected.Newton);
                Check(firstShader.Source == selectedShader.Source && firstShader.Key == selectedShader.Key, "Numerical atlas variants must reuse the formula shader.");
                Click(Element<Button>(active, "ApplyButton")); active = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed formula, roots, iterations, grid, camera, appearance or precise parameters.");
                Check(formulaDraft.Text == "z^" && radiusDraft.Text == "9.123456789012345" && Element<TextBlock>(editor, "FormulaStatus").Text == draftStatus,
                    "Atlas application overwrote formula/radius drafts or their status.");
                Check(Element<Slider>(editor, "CutSlider").Value == after.Newton.CutPosition, "Applied cut position did not reach the editor.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                Check(main.CaptureState("saved").Newton == after.Newton && formulaDraft.Text == after.Newton.Expression,
                    "Save/load lost Newton parameters or stored the unapplied draft.");
                formulaDraft.Text = "z^5-z+1"; radiusDraft.Text = "12.5";
                string priorStatus = Element<TextBlock>(editor, "FormulaStatus").Text;
                var priorClose = main.CaptureState("view");
                active = CreateAtlas(); Task closing = Build(active); active.Close(); await closing; active = null;
                var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose) && formulaDraft.Text == "z^5-z+1" &&
                    radiusDraft.Text == "12.5" && Element<TextBlock>(editor, "FormulaStatus").Text == priorStatus, "Closing changed the Newton view or its drafts.");
                Check(source.Newton.Size == 96 && source.Newton.SliceW == .23456789012345678, "Preview rendering mutated the source settings.");
            }
            finally { active?.Close(); main.Close(); }
            Newton3DAtlasWindow CreateAtlas() => (Newton3DAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateNewton3DAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (newton3d-atlas): regions/cuts, degrees, axis pairs, integer thresholds, precision, 32³ GPU previews, formula/roots, drafts, apply, saves and close.");
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
