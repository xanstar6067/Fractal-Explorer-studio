using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyImplicitSurfaceAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("implicit-surface-atlas");
        EnsureWindowThemeStyles();
        var kind = Fractal3DKind.ImplicitSurface;
        // Detect parameter tokens in the actual parser: functions and scientific notation are not a/b/c.
        var plain = CompiledRealSpaceExpression.Parse("exp(x)+abs(y)+cos(z)+1e-3");
        Check("abc".All(p => !plain.UsesParameter(p)), "Function names must not introduce atlas parameters.");
        var equality = CompiledRealSpaceExpression.Parse("X^2+Y^2+Z^2=A^2+min(B,C)");
        Check("abcABC".All(equality.UsesParameter), "Parameters on either side of an equality and inside functions must be detected.");
        Check(equality.Evaluate(1, 2, 3, 2, .5, .7) == 9.5, "Parameter metadata changed formula evaluation.");

        for (int mask = 0; mask < 8; mask++) for (int cut = 0; cut < 4; cut++)
        {
            string[] used = "abc".Where((_, i) => (mask & (1 << i)) != 0).Select(p => p.ToString()).ToArray();
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.ImplicitSurface = new()
            {
                Formula = "x^2+y^2+z^2-1" + string.Concat(used.Select(p => "+" + p)),
                A = .12345678901234567, B = .23456789012345678, C = .34567890123456789,
                Level = .4567890123456789, Extent = 1.567890123456789, CutAxis = cut, CutPosition = .6789012345678901
            };
            var atlas = new ImplicitSurfaceAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                int count = used.Length + 2 + (cut > 0 ? 1 : 0);
                var names = x.Items.Cast<string>().ToArray();
                Check(names.Length == count && "abc".All(p => names.Contains("Параметр " + p) == used.Contains(p.ToString())), "Unused parameters must be absent from atlas axes.");
                Check(names.Contains("Положение среза") == (cut > 0), "Cut position must follow the active cut axis.");
                var boxes = FixedBoxes(atlas);
                Check(count == 2 ? boxes.Length == 0 : boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Incorrect fixed parameter inputs.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Initial axes must select the first two available parameters.");
                if (count > 2)
                    for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                    {
                        x.SelectedIndex = a; y.SelectedIndex = b;
                        Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Axis changes must leave the other parameters fixed.");
                        NumericInput.CommitAll(atlas);
                    }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Unchanged atlas rounded active or unused parameters.");
                var wrongFormula = source.Clone(); wrongFormula.ImplicitSurface = wrongFormula.ImplicitSurface with { Formula = "x" };
                var wrongCut = source.Clone(); wrongCut.ImplicitSurface = wrongCut.ImplicitSurface with { CutAxis = (cut + 1) % 4 };
                foreach (var target in new[] { wrongFormula, wrongCut })
                {
                    string before = JsonSerializer.Serialize(target); bool rejected = false;
                    try { atlas.ApplySelectionTo(target); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && JsonSerializer.Serialize(target) == before, "Mismatched formulas/cuts must be rejected without mutation.");
                }
            }
            finally { atlas.Close(); }
        }
        // Every built-in formula can open its atlas without compiling or rendering a shader.
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var atlas = new ImplicitSurfaceAtlasWindow(preset);
            try
            {
                var unchanged = preset.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(unchanged.ImplicitSurface == preset.ImplicitSurface, "A built-in preset lost its formula or parameters.");
            }
            finally { atlas.Close(); }
        }
        var sourceView = Fractal3DCatalog.CreateDefaultState(kind);
        sourceView.ImplicitSurface = sourceView.ImplicitSurface with
        { C = .34567890123456789, Extent = 1.6123456789012345, CutAxis = 3, CutPosition = .23456789012345678 };
        var main = new Fractal3DWindow(kind);
        ImplicitSurfaceAtlasWindow? active = null;
        try
        {
            main.LoadState(sourceView);
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var before = main.CaptureState("view");
            Check(before.ImplicitSurface == sourceView.ImplicitSurface, "Loading rounded implicit surface values.");
            Check(Element<StackPanel>(main, "ImplicitPanel").Visibility == Visibility.Visible &&
                Element<Button>(main, "ImplicitSurfaceAtlasButton").Visibility == Visibility.Visible, "Atlas entry is absent from the formula panel.");
            var draft = Element<TextBox>(main, "ImplicitFormulaBox"); draft.Text = "sqrt(";
            string draftStatus = Element<TextBlock>(main, "ImplicitFormulaStatus").Text;
            active = CreateAtlas();
            Check(active.Owner == main && Element<TextBlock>(active, "ApplicationHintText").Text.Contains(before.ImplicitSurface.Formula), "Atlas must identify the applied formula and its owner.");
            var x = Element<ComboBox>(active, "XAxisBox"); var y = Element<ComboBox>(active, "YAxisBox");
            var minX = Element<TextBox>(active, "MinRealBox"); var maxX = Element<TextBox>(active, "MaxRealBox");
            var minY = Element<TextBox>(active, "MinImaginaryBox"); var maxY = Element<TextBox>(active, "MaxImaginaryBox");
            x.SelectedIndex = 2; y.SelectedIndex = 3; // Level and extent, different bounds from a/b.
            minX.Text = "-200"; maxX.Text = "200"; minY.Text = "-20"; maxY.Text = "20";
            NumericInput.CommitAll(active);
            Check(minX.Text == "-100" && maxX.Text == "100" && minY.Text == "0.1" && maxY.Text == "10", "Level and extent need independent bounds.");
            x.SelectedIndex = 4; y.SelectedIndex = 0; // Cut position and a.
            minX.Text = "-20"; maxX.Text = "20"; minY.Text = "-20"; maxY.Text = "20";
            NumericInput.CommitAll(active);
            Check(minX.Text == "-10" && maxX.Text == "10" && minY.Text == "-10" && maxY.Text == "10", "Cut position and a bounds are incorrect.");
            x.SelectedIndex = 0; y.SelectedIndex = 1;
            minX.Text = "0.8"; maxX.Text = "1.2"; minY.Text = "0.2"; maxY.Text = "0.5";
            var fixedExtent = FixedBoxes(active)[3];
            fixedExtent.Text = "NaN"; NumericInput.CommitAll(active);
            Check(NumericInput.IsValid(fixedExtent) && NumericInput.GetHasNotice(fixedExtent), "Invalid fixed value must recover with a notice.");
            await Build(active);
            var tiles = Element<UniformGrid>(active, "TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Formula atlas previews are incomplete.");
            Click((Button)tiles.Children[0]);
            var expected = before.Clone(); active.ApplySelectionTo(expected);
            Check(expected.ImplicitSurface.C == before.ImplicitSurface.C && expected.ImplicitSurface.CutPosition == before.ImplicitSurface.CutPosition,
                "Unused or fixed values lost precision.");
            var preview = expected.Clone();
            preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
            preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
            using (var renderer = new Fractal3DRenderer())
            {
                var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                var actual = Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source);
                Check(actual.SequenceEqual(reference.Buffer), "Atlas did not render the applied formula with selected parameters.");
                Click((Button)tiles.Children[tiles.Children.Count - 1]);
                Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source)), "Different a/b values produced identical previews.");
            }
            // A fixed-value edit invalidates the old selection; rebuilding restores application.
            fixedExtent.Text = "1.7123456789012345";
            Check(!Element<Button>(active, "ApplyButton").IsEnabled, "An old preview must not apply changed fixed parameters.");
            await Build(active);
            expected = before.Clone(); active.ApplySelectionTo(expected);
            var originalShader = Fractal3DRenderer.ImplicitPixelShaderEntry(before.ImplicitSurface);
            var selectedShader = Fractal3DRenderer.ImplicitPixelShaderEntry(expected.ImplicitSurface);
            Check(originalShader.Source == selectedShader.Source && originalShader.Key == selectedShader.Key &&
                File.Exists(AppPaths.GetShaderCacheFile(originalShader.Key)), "Numerical atlas variants must share the cached formula shader.");
            Click(Element<Button>(active, "ApplyButton")); active = null;
            var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed formula, camera, appearance or unrelated settings.");
            Check(draft.Text == "sqrt(" && Element<TextBlock>(main, "ImplicitFormulaStatus").Text == draftStatus, "Atlas application overwrote the unapplied formula draft.");
            Check(Element<Slider>(main, "ImplicitASlider").Value == after.ImplicitSurface.A &&
                Element<Slider>(main, "ImplicitExtentSlider").Value == after.ImplicitSurface.Extent, "Atlas did not synchronize parameter sliders.");
            var store = new Fractal3DSaveStore(kind); store.Save(after);
            var saved = store.Load().Single(); main.LoadState(saved);
            Check(main.CaptureState("saved").ImplicitSurface == after.ImplicitSurface && draft.Text == after.ImplicitSurface.Formula,
                "Save/load lost precise parameters or stored an unapplied formula.");
            draft.Text = "x+y+z-a";
            string priorStatus = Element<TextBlock>(main, "ImplicitFormulaStatus").Text;
            var priorClose = main.CaptureState("view");
            active = CreateAtlas(); Task closing = Build(active); active.Close(); await closing; active = null;
            var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose) && draft.Text == "x+y+z-a" &&
                Element<TextBlock>(main, "ImplicitFormulaStatus").Text == priorStatus, "Closing changed the source view or valid formula draft.");
            Check(sourceView.ImplicitSurface == before.ImplicitSurface, "Rendering mutated the original source.");
        }
        finally { active?.Close(); main.Close(); }
        Console.WriteLine("PASS (implicit-surface-atlas): formula parameters, all axes/cuts, presets, bounds, precision, GPU previews, shared shader, drafts, apply, saves and close.");

        ImplicitSurfaceAtlasWindow CreateAtlas() => (ImplicitSurfaceAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateImplicitSurfaceAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
