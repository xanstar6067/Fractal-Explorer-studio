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
    private static async Task VerifyBurningShipAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("BurningShipAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.BurningShip;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var window = new BurningShipAtlasWindow(preset);
            try
            {
                var target = preset.Clone(); window.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(preset), "An untouched atlas changed a preset.");
            }
            finally { window.Close(); }
        }
        // Four 2×2 grids cover every formula without large renders.
        foreach (var formula in Enum.GetValues<BurningShip3DFormula>())
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.BurningShipFormula = formula; source.Power = 3; source.Iterations = 20; source.Bailout = 4.125;
            string original = JsonSerializer.Serialize(source);
            var main = new Fractal3DWindow(kind);
            BurningShipAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var before = main.CaptureState("view");
                Check(Element<Button>(main, "BurningShipAtlasButton").Visibility == Visibility.Visible &&
                    Element<Button>(main, "BurningShipJuliaAtlasButton").Visibility == Visibility.Collapsed &&
                    Element<Button>(main, "MandelbulbAtlasButton").Visibility == Visibility.Collapsed, "The active source ship must expose its own atlas.");
                atlas = CreateAtlas(); Check(atlas.Owner == main, "Atlas lost its owner.");
                Check(Element<TextBlock>(atlas, "ApplicationHintText").Text.Contains(Fractal3DCatalog.BurningShipFormulaName(formula)), "The fixed formula must be identified.");
                var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(before), "Untouched atlas changed the view or fixed parameters.");
                foreach (var point in new[] { new[] { 2m, 1m }, new[] { 16m, 64m } })
                {
                    var target = before.Clone(); Apply(atlas, target, point);
                    Check(target.Power == (double)point[0] && target.Iterations == (int)point[1] && target.Bailout == before.Bailout,
                        "The atlas must support the full degree/iteration range and preserve bailout.");
                }
                foreach (var point in new[] { new[] { 1m, 20m }, new[] { 17m, 20m }, new[] { 2.5m, 20m },
                    new[] { 3m, 0m }, new[] { 3m, 65m }, new[] { 3m, 20.5m } }) Reject(atlas, before.Clone(), point);
                var wrong = before.Clone(); wrong.BurningShipFormula = (BurningShip3DFormula)(((int)formula + 1) % 4);
                Reject(atlas, wrong, [3m, 20m]);
                wrong = before.Clone(); wrong.Kind = Fractal3DKind.BurningShipJulia; Reject(atlas, wrong, [3m, 20m]);
                foreach (int size in new[] { 3, 5, 7 })
                foreach (int index in new[] { 0, 1 })
                {
                    decimal minimum = index == 0 ? 2 : 1, maximum = index == 0 ? 16 : 64;
                    var values = Samples(atlas, index, minimum, maximum, size);
                    Check(values.Length == size && values.Distinct().Count() == size && values.First() == minimum && values.Last() == maximum &&
                        values.All(v => v == decimal.Truncate(v)), "Both axes must sample distinct integers throughout the supported range.");
                    Check(Samples(atlas, index, minimum, maximum, size, true).SequenceEqual(values.Reverse()), "Y must run from maximum to minimum.");
                }
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Range inputs need NumericInput.");
                minX.Text = "1"; maxX.Text = "100"; minY.Text = "0"; maxY.Text = "100"; NumericInput.CommitAll(atlas);
                Check(minX.Text == "2" && maxX.Text == "16" && minY.Text == "1" && maxY.Text == "64", "Degree and iteration bounds differ.");
                minX.Text = "2.5"; NumericInput.CommitAll(atlas);
                Check(minX.Text == "2" && NumericInput.GetHasNotice(minX), "A fractional degree bound must recover with feedback.");
                minY.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(minY.Text == "1" && NumericInput.GetHasNotice(minY), "Invalid iteration input must recover with feedback.");
                minX.Text = "15"; maxX.Text = "16"; minY.Text = "63"; maxY.Text = "64";
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == 4 && tiles.Columns == 2 && tiles.Children.OfType<Button>().All(b => b.IsEnabled),
                    "Narrow integer ranges must produce four distinct previews.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(expected.Power == 15 && expected.Iterations == 64, "Selected 64 iterations must survive the common preview cap of 32.");
                var preview = expected.Clone(); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), $"{formula}: atlas differs from the direct selected degree/iteration frame.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), $"{formula}: different degrees produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed formula, bailout, camera, appearance or quality.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                var restored = main.CaptureState("restored");
                Check(restored.Power == after.Power && restored.Iterations == after.Iterations && restored.BurningShipFormula == formula && restored.Bailout == after.Bailout,
                    "Save/load lost selected parameters or fixed formula/bailout.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the view.");
                Check(JsonSerializer.Serialize(source) == original, "Atlas rendering mutated the source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
            BurningShipAtlasWindow CreateAtlas() => (BurningShipAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateBurningShipAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (burning-ship-atlas): four formulas, presets, integer axes/bounds, 64-iteration GPU previews, fixed formula/bailout, apply, saves and close.");

        static decimal[] Samples(ParameterAtlasWindow atlas, int index, decimal min, decimal max, int size, bool reverse = false) =>
            (decimal[])typeof(ParameterAtlasWindow).GetMethod("SampleAxis", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(atlas, [index, min, max, size, reverse])!;
        static void Apply(BurningShipAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(BurningShipAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(BurningShipAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Invalid or incompatible parameters must be rejected atomically.");
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
