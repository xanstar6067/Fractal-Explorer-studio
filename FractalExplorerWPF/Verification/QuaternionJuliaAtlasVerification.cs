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
    private static async Task VerifyQuaternionJuliaAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("quaternion-julia-atlas");
        EnsureWindowThemeStyles();
        var kind = Fractal3DKind.QuaternionJulia;
        foreach (QuaternionJuliaSettings? settings in new QuaternionJuliaSettings?[] { null, new(),
            new() { XW = 37.123456789012345, YW = -24.234567890123456, ZW = 51.34567890123456,
                SweepMinimum = -.7123456789012345, SweepMaximum = .8234567890123456, SweepSpeed = .34567890123456789 } })
        {
            var source = Fractal3DCatalog.CreateDefaultState(kind);
            source.JuliaCX = -.21234567890123456; source.JuliaCY = .6123456789012345;
            source.JuliaCZ = .12345678901234567; source.JuliaCW = .023456789012345678;
            source.QuaternionSlice = .12345678901234567; source.QuaternionJulia = settings;
            var atlas = new QuaternionJuliaAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var boxes = FixedBoxes(atlas);
                Check(boxes.Length == 8 && boxes.All(NumericInput.IsRegistered), "Eight validated quaternion parameters are required.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1 && atlas.SelectedConstant.Length == 4 && atlas.SelectedParameters.Length == 8,
                    "The original C axes and four-component constant must remain available.");
                for (int a = 0; a < 8; a++) for (int b = a + 1; b < 8; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == 6, "Each axis pair must retain six fixed values.");
                    NumericInput.CommitAll(atlas);
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Unchanged quaternion atlas rounded a value or replaced a legacy null DTO.");
                var wrong = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb);
                string before = JsonSerializer.Serialize(wrong); bool rejected = false;
                try { atlas.ApplySelectionTo(wrong); } catch (ArgumentException) { rejected = true; }
                Check(rejected && JsonSerializer.Serialize(wrong) == before, "A quaternion atlas must reject another mode without mutation.");
            }
            finally { atlas.Close(); }
        }
        var view = Fractal3DCatalog.GetPresets(kind).First(s => s.QuaternionJulia?.XW == 37).Clone();
        view.JuliaCZ = .12345678901234567; view.JuliaCW = .023456789012345678;
        view.QuaternionSlice = .12345678901234567;
        view.QuaternionJulia = new() { XW = 37.123456789012345, YW = -24.234567890123456, ZW = 51.34567890123456,
            SweepMinimum = -.7123456789012345, SweepMaximum = .8234567890123456, SweepSpeed = .34567890123456789 };
        var main = new Fractal3DWindow(kind);
        QuaternionJuliaAtlasWindow? active = null;
        try
        {
            main.LoadState(view);
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var editor = Element<QuaternionSliceEditor>(main, "QuaternionEditor");
            Check(Element<StackPanel>(main, "QuaternionPanel").Visibility == Visibility.Visible &&
                Element<Button>(editor, "AtlasButton").Content.ToString()!.Contains("среза"), "The editor must expose the expanded atlas.");
            Check(main.CaptureState("view").QuaternionJulia == view.QuaternionJulia, "Loading rounded slice or sweep settings.");
            typeof(Fractal3DWindow).GetField("_quaternionDisplayedSlice", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, view.QuaternionSlice);
            Click(Element<Button>(editor, "PlayButton"));
            editor.OnFrameDisplayed(view.QuaternionSlice);
            Check(editor.IsPlaying && Element<TextBox>(main, "SliceBox").Text != view.QuaternionSlice.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                "The sweep must queue a different slice before opening the atlas.");
            active = CreateAtlas();
            Check(!editor.IsPlaying && main.CaptureState("paused").QuaternionSlice == view.QuaternionSlice &&
                active.SelectedParameters[4] == (decimal)view.QuaternionSlice, "Opening must pause at the displayed slice, not the queued one.");
            Check(active.Owner == main, "Atlas lost its owner.");
            for (int pass = 0; pass < 2; pass++)
            {
                if (active is null) throw new InvalidOperationException("The atlas must be open for each application pass.");
                var before = main.CaptureState("view");
                var x = Element<ComboBox>(active, "XAxisBox"); var y = Element<ComboBox>(active, "YAxisBox");
                var minX = Element<TextBox>(active, "MinRealBox"); var maxX = Element<TextBox>(active, "MaxRealBox");
                var minY = Element<TextBox>(active, "MinImaginaryBox"); var maxY = Element<TextBox>(active, "MaxImaginaryBox");
                x.SelectedIndex = pass == 0 ? 4 : 3; y.SelectedIndex = pass == 0 ? 5 : 4;
                minX.Text = "-200"; maxX.Text = "200"; minY.Text = "-200"; maxY.Text = "200";
                NumericInput.CommitAll(active);
                Check(minX.Text == (pass == 0 ? "-4" : "-8") && maxX.Text == (pass == 0 ? "4" : "8") &&
                    minY.Text == (pass == 0 ? "-180" : "-4") && maxY.Text == (pass == 0 ? "180" : "4"), "C, w and angles need independent bounds.");
                minX.Text = pass == 0 ? "-0.25" : "-0.1"; maxX.Text = pass == 0 ? "0.25" : "0.1";
                minY.Text = pass == 0 ? "-30" : "-0.2"; maxY.Text = pass == 0 ? "30" : "0.2";
                if (pass == 0)
                {
                    var fixedAngle = FixedBoxes(active)[7]; fixedAngle.Text = "NaN"; NumericInput.CommitAll(active);
                    Check(NumericInput.IsValid(fixedAngle) && NumericInput.GetHasNotice(fixedAngle), "Invalid fixed angle must recover with feedback.");
                }
                Task stale = Build(active); Task current = Build(active); await Task.WhenAll(stale, current);
                var tiles = Element<UniformGrid>(active, "TilesPanel");
                Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled) && !active.IsRendering, "Only the current atlas generation must finish.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); active.ApplySelectionTo(expected);
                var preview = expected.Clone();
                preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    var actual = Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Quaternion atlas differs from direct rendering of its slice and constant.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(active, "SelectedPreview").Source)), "Slice/tilt or Cw/w changes did not affect the preview.");
                }
                var fixedYw = FixedBoxes(active)[6]; string value = fixedYw.Text;
                fixedYw.Text = value + "0";
                Check(!Element<Button>(active, "ApplyButton").IsEnabled, "Fixed angle edits must invalidate the previous selection.");
                await Build(active);
                expected = before.Clone(); active.ApplySelectionTo(expected);
                Click(Element<Button>(active, "ApplyButton")); active = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application rounded a value or changed camera, quality, palette, iterations, bailout or sweep settings.");
                Check(!editor.IsPlaying && editor.Capture() == after.QuaternionJulia, "Applied angles must synchronize the paused editor.");
                Check(after.QuaternionJulia!.SweepMinimum == view.QuaternionJulia!.SweepMinimum &&
                    after.QuaternionJulia.SweepMaximum == view.QuaternionJulia.SweepMaximum && after.QuaternionJulia.SweepSpeed == view.QuaternionJulia.SweepSpeed,
                    "Atlas application changed sweep settings.");
                if (pass == 0) active = CreateAtlas();
            }
            var saved = main.CaptureState("saved");
            var store = new Fractal3DSaveStore(kind); store.Save(saved); main.LoadState(store.Load().Single());
            var restored = main.CaptureState("restored");
            Check(restored.QuaternionJulia == saved.QuaternionJulia && restored.QuaternionSlice == saved.QuaternionSlice &&
                restored.JuliaCX == saved.JuliaCX && restored.JuliaCY == saved.JuliaCY && restored.JuliaCZ == saved.JuliaCZ && restored.JuliaCW == saved.JuliaCW,
                "Save/load lost constant, slice, angles or sweep precision.");
            var priorClose = main.CaptureState("view");
            active = CreateAtlas(); Task closing = Build(active); active.Close(); await closing; active = null;
            var closed = main.CaptureState("view"); closed.Timestamp = priorClose.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(priorClose), "Closing without applying changed the original view.");
            Check(view.QuaternionJulia!.XW == 37.123456789012345 && view.QuaternionSlice == .12345678901234567, "Rendering mutated the source snapshot.");
        }
        finally { active?.Close(); main.Close(); }
        Console.WriteLine("PASS (quaternion-julia-atlas): eight parameters, 28 axis pairs, bounds, precision/null DTO, GPU previews, generations, displayed-slice pause, apply, sweep, saves and close.");

        QuaternionJuliaAtlasWindow CreateAtlas() => (QuaternionJuliaAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateQuaternionAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
