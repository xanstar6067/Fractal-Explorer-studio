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
    private static async Task VerifyLyapunov3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("lyapunov3d-atlas");
        EnsureWindowThemeStyles();
        var kind = Fractal3DKind.Lyapunov3D;
        foreach (string pattern in new[] { "ABCCAAB", "AB", "C", "A" })
        foreach (var region in Enum.GetValues<Lyapunov3DRegion>())
        foreach (int cut in new[] { 0, 1, 2, 3 })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Lyapunov = new() { Pattern = pattern, Region = region, CutAxis = cut, SliceOnly = cut == 1,
                InitialX = .41234567890123456, Level = -.012345678901234567, BandWidth = .04345678901234567 };
            var atlas = new Lyapunov3DAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas);
                int count = pattern.Distinct().Count() + 1 + (cut == 1 ? 0 : region == Lyapunov3DRegion.Boundary ? 2 : 1) + (cut > 0 ? 1 : 0);
                Check(names.Length == count && boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Lyapunov atlas needs only active, validated fields.");
                Check("ABC".All(c => names.Contains("Центр диапазона " + c) == pattern.Contains(c)), "Unused sequence components must not occupy atlas axes.");
                Check(names.Contains("Уровень λ") == (cut != 1) && names.Contains("Полуширина полосы λ") == (cut != 1 && region == Lyapunov3DRegion.Boundary),
                    "A colored plane must not expose unused level or band settings.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Lyapunov axis pairs must retain their fixed fields.");
                    NumericInput.CommitAll(atlas);
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Unchanged atlas rounded values or lowered the main measurement quality.");
                var wrong = source.Clone(); wrong.Lyapunov = wrong.Lyapunov with { Pattern = pattern + "A" };
                string before = JsonSerializer.Serialize(wrong); bool rejected = false;
                try { atlas.ApplySelectionTo(wrong); } catch (ArgumentException) { rejected = true; }
                Check(rejected && JsonSerializer.Serialize(wrong) == before, "A different sequence must be rejected without mutation.");
            }
            finally { atlas.Close(); }
        }
        foreach ((double min, double max) in new[] { (0d, 4d), (0d, .00001), (4 - .00001, 4d), (3.1234567890123457, 3.8234567890123457) })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Lyapunov = new() { Pattern = "A", MinA = min, MaxA = max, MinB = 0, MaxB = 4, MinC = 0, MaxC = 4 };
            var atlas = new Lyapunov3DAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox");
                Check(x.Items.Contains("Центр диапазона A") == (max - min < 4), "A full 0…4 interval has no movable center.");
                if (max - min < 4)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))typeof(Lyapunov3DAtlasWindow)
                        .GetMethod("ComponentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [0])!;
                    foreach (decimal center in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var point = (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
                        point[0] = center; var moved = source.Clone();
                        typeof(Lyapunov3DAtlasWindow).GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [moved, point]);
                        moved.Lyapunov.Validate();
                        Check(moved.Lyapunov.MinA >= 0 && moved.Lyapunov.MaxA <= 4 &&
                            Math.Abs((moved.Lyapunov.MaxA - moved.Lyapunov.MinA) - (max - min)) < 1e-14,
                            "Shifting to an edge must retain a valid interval and its width to double precision.");
                    }
                }
            }
            finally { atlas.Close(); }
        }
        foreach (bool plane in new[] { false, true })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.Lyapunov = new() { Size = 128, Transient = plane ? 17 : 257, Samples = plane ? 32 : 513,
                Region = Lyapunov3DRegion.Boundary, CutAxis = 3, SliceOnly = plane,
                MinA = 3.1234567890123457, MaxA = 3.8234567890123457,
                InitialX = .41234567890123456, Level = -.012345678901234567,
                BandWidth = .04345678901234567, ColorRange = .4567890123456789, CutPosition = .12345678901234567 };
            if (plane) { source.CameraYaw = 0; source.CameraPitch = 0; }
            var main = new Fractal3DWindow(kind);
            Lyapunov3DAtlasWindow? active = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                Check(main.CaptureState("view").Lyapunov == source.Lyapunov, "Loading rounded Lyapunov settings.");
                Element<TextBox>(main, "LyapunovPatternBox").Text = " ab cc aab ";
                var before = main.CaptureState("view");
                Check(Element<StackPanel>(main, "LyapunovPanel").Visibility == Visibility.Visible && Element<Button>(main, "Lyapunov3DAtlasButton").Visibility == Visibility.Visible,
                    "The Lyapunov panel must expose its atlas.");
                active = CreateAtlas(); Check(active.Owner == main, "Atlas lost its owner.");
                var x = Element<ComboBox>(active, "XAxisBox"); var y = Element<ComboBox>(active, "YAxisBox");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Default axes must compare the first movable range centers.");
                x.SelectedIndex = plane ? 2 : 0; y.SelectedIndex = plane ? 4 : 1;
                var minX = Element<TextBox>(active, "MinRealBox"); var maxX = Element<TextBox>(active, "MaxRealBox");
                var minY = Element<TextBox>(active, "MinImaginaryBox"); var maxY = Element<TextBox>(active, "MaxImaginaryBox");
                minX.Text = "-10"; maxX.Text = "10"; minY.Text = "-10"; maxY.Text = "10";
                NumericInput.CommitAll(active);
                Check(decimal.Parse(minX.Text, System.Globalization.CultureInfo.InvariantCulture) > 0 &&
                    decimal.Parse(maxX.Text, System.Globalization.CultureInfo.InvariantCulture) < 4 &&
                    (!plane || minY.Text == "-1" && maxY.Text == "1"), "Center and cut bounds are incorrect.");
                minX.Text = "3.35"; maxX.Text = "3.65"; minY.Text = plane ? "-0.3" : "3.35"; maxY.Text = plane ? "0.3" : "3.65";
                var initial = FixedBoxes(active)[3]; initial.Text = "NaN"; NumericInput.CommitAll(active);
                Check(NumericInput.IsValid(initial) && NumericInput.GetHasNotice(initial), "Invalid fixed initial x must recover with feedback.");
                await Build(active);
                var tiles = Element<UniformGrid>(active, "TilesPanel");
                Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Lyapunov previews did not complete.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); active.ApplySelectionTo(expected);
                var preview = expected.Clone(); preview.Lyapunov = preview.Lyapunov with { Size = 32,
                    Transient = Math.Min(preview.Lyapunov.Transient, 256), Samples = Math.Min(preview.Lyapunov.Samples, 512) };
                preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    var actual = Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Lyapunov atlas differs from the direct preview calculation.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source)), "Center/cut changes did not affect Lyapunov previews.");
                }
                expected = before.Clone(); active.ApplySelectionTo(expected);
                Click(Element<Button>(active, "ApplyButton")); active = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application rounded parameters or changed measurement quality, sequence, camera or appearance.");
                Check(after.Lyapunov.Size == 128 && after.Lyapunov.Transient == source.Lyapunov.Transient && after.Lyapunov.Samples == source.Lyapunov.Samples,
                    "Preview limits leaked into the main field.");
                Check(Element<TextBox>(main, "LyapunovPatternBox").Text == " ab cc aab ", "Atlas application rewrote the sequence text.");
                Check(Element<TextBlock>(main, "LyapunovCutText").Text.StartsWith("C = "), "The cut label must track the applied range.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                var restored = main.CaptureState("restored").Lyapunov;
                Check((restored with { Pattern = after.Lyapunov.Pattern }) == after.Lyapunov, "Save/load lost exact Lyapunov settings.");
                var priorClose = main.CaptureState("view");
                active = CreateAtlas(); Task closing = Build(active); active.Close(); await closing; active = null;
                var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose), "Closing without application changed the view.");
                Check(source.Lyapunov.MinA == 3.1234567890123457 && source.Lyapunov.Size == 128, "Rendering mutated the source snapshot.");
            }
            finally { active?.Close(); main.Close(); }
            Lyapunov3DAtlasWindow CreateAtlas() => (Lyapunov3DAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateLyapunov3DAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (lyapunov3d-atlas): sequences/regions/cuts, active axes, center bounds/widths, precision, limited GPU previews, main quality, apply, saves and close.");
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
