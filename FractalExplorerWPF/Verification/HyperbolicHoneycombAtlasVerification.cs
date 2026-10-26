using System.Globalization;
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
    private static async Task VerifyHyperbolicHoneycombAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("HyperbolicHoneycombAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.HyperbolicHoneycomb;
        var configurations = from tessellation in Enum.GetValues<HyperbolicHoneycombKind>()
            from edges in new[] { false, true } from faces in new[] { false, true } from cut in Enumerable.Range(0, 4)
            select State(tessellation, edges, faces, cut);
        foreach (var source in configurations.Concat(Fractal3DCatalog.GetPresets(kind)))
        {
            var atlas = new HyperbolicHoneycombAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas);
                var s = source.HyperbolicHoneycomb;
                int count = 4 + (s.ShowEdges ? 1 : 0) + (s.ShowFaces ? 2 : 0) + (s.CutAxis > 0 ? 1 : 0);
                Check(names.Length == count && boxes.Length == count && boxes.All(NumericInput.IsRegistered), "The honeycomb atlas needs only active, validated geometry fields.");
                Check(names.Contains("Радиус рёбер в H³") == s.ShowEdges && names.Contains("Толщина граней в H³") == s.ShowFaces &&
                    names.Contains("Непрозрачность граней") == s.ShowFaces && names.Contains("Положение среза") == (s.CutAxis > 0), "Hidden geometry/cut parameters must be excluded.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == (s.ShowEdges ? 4 : s.ShowFaces ? 5 : 1), "Default axes must compare depth with the visible geometry parameter.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b; NumericInput.CommitAll(atlas);
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Axis pairs lost fixed fields.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded Cartesian observer coordinates or inactive fields.");
                var point = Selected(atlas);
                for (int i = 0; i < count; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))typeof(HyperbolicHoneycombAtlasWindow)
                        .GetMethod("ComponentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [i])!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var values = (decimal[])point.Clone(); values[i] = edge;
                        var target = source.Clone(); Apply(atlas, target, values); target.HyperbolicHoneycomb.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + .1m; Reject(atlas, source.Clone(), invalid);
                }
                var fraction = (decimal[])point.Clone(); fraction[0] = 2.5m; Reject(atlas, source.Clone(), fraction);
                var wrong = source.Clone(); wrong.HyperbolicHoneycomb = s with { Tessellation = (HyperbolicHoneycombKind)(((int)s.Tessellation + 1) % 4) }; Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.HyperbolicHoneycomb = s with { ShowEdges = !s.ShowEdges }; Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.HyperbolicHoneycomb = s with { ShowFaces = !s.ShowFaces }; Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.HyperbolicHoneycomb = s with { CutAxis = (s.CutAxis + 1) % 4 }; Reject(atlas, wrong, point);
            }
            finally { atlas.Close(); }
        }
        // Origin, poles, the boundary, and the model's floating-point tolerance retain exact XYZ.
        foreach (var position in new[] { (0d, 0d, 0d), (.85, 0d, 0d), (-.85, 0d, 0d), (0d, .85, 0d),
            (0d, -.85, 0d), (0d, 0d, .85), (.51, .68, 0d), (.8500000000003, 0d, 0d) })
        {
            var source = State(HyperbolicHoneycombKind.Dodecahedral534, true, false, 0);
            source.HyperbolicHoneycomb = source.HyperbolicHoneycomb with { ObserverX = position.Item1, ObserverY = position.Item2, ObserverZ = position.Item3 };
            var atlas = new HyperbolicHoneycombAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(target.HyperbolicHoneycomb == source.HyperbolicHoneycomb, "A spherical roundtrip changed the exact source XYZ.");
                foreach (decimal radius in new[] { 0m, .85m }) foreach (decimal azimuth in new[] { -180m, -90m, 0m, 90m, 180m })
                foreach (decimal latitude in new[] { -90m, 0m, 90m })
                {
                    var point = Selected(atlas); point[1] = radius; point[2] = azimuth; point[3] = latitude;
                    target = source.Clone(); Apply(atlas, target, point); var s = target.HyperbolicHoneycomb; s.Validate();
                    Check(Math.Abs(Math.Sqrt(s.ObserverX * s.ObserverX + s.ObserverY * s.ObserverY + s.ObserverZ * s.ObserverZ) - (double)radius) < 1e-12,
                        "Observer directions must preserve radius and remain within the allowed ball.");
                }
                foreach (var probe in new[] { (0m, 0m, .5, 0d, 0d), (90m, 0m, 0d, 0d, .5), (0m, 90m, 0d, .5, 0d) })
                {
                    var point = Selected(atlas); point[1] = .5m; point[2] = probe.Item1; point[3] = probe.Item2;
                    target = source.Clone(); Apply(atlas, target, point); var s = target.HyperbolicHoneycomb;
                    Check(Math.Abs(s.ObserverX - probe.Item3) < 1e-14 && Math.Abs(s.ObserverY - probe.Item4) < 1e-14 && Math.Abs(s.ObserverZ - probe.Item5) < 1e-14,
                        "Azimuth must run from +X to +Z and latitude toward +Y.");
                }
            }
            finally { atlas.Close(); }
        }

        // Three small GPU grids: edge/depth, transparent faces, and observer movement.
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var source = State(scenario == 0 ? HyperbolicHoneycombKind.Dodecahedral534 : scenario == 1 ? HyperbolicHoneycombKind.Cubic435 : HyperbolicHoneycombKind.Icosahedral353,
                edges: scenario != 1, faces: scenario != 2, cut: scenario == 1 ? 0 : 3);
            string original = JsonSerializer.Serialize(source);
            var main = new Fractal3DWindow(kind); HyperbolicHoneycombAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<HyperbolicHoneycombEditor>(main, "HyperbolicEditor");
                Check(Element<StackPanel>(main, "HyperbolicPanel").Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible,
                    "The active editor must expose the honeycomb atlas.");
                var before = main.CaptureState("view"); Check(before.HyperbolicHoneycomb == source.HyperbolicHoneycomb, "Loading rounded honeycomb parameters.");
                // A valid number in each box can still be an unapplied, invalid vector.
                foreach (var box in ObserverBoxes(editor)) box.Text = ".8";
                typeof(HyperbolicHoneycombEditor).GetMethod("ApplyPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [editor, new RoutedEventArgs()]);
                string[] drafts = ObserverBoxes(editor).Select(b => b.Text).ToArray();
                string warning = Element<TextBlock>(editor, "PositionStatus").Text;
                Check(warning.Length > 0 && editor.Capture() == before.HyperbolicHoneycomb, "Invalid observer drafts must preserve the applied position.");
                atlas = CreateAtlas(); Check(atlas.Owner == main, "Atlas lost its owner.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray();
                x.SelectedIndex = scenario == 0 ? 0 : scenario == 1 ? Array.IndexOf(names, "Толщина граней в H³") : 1;
                y.SelectedIndex = scenario == 0 ? Array.IndexOf(names, "Радиус рёбер в H³") : scenario == 1 ? Array.IndexOf(names, "Непрозрачность граней") : 2;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Range fields need NumericInput.");
                minX.Text = "-200"; maxX.Text = "200"; minY.Text = "-200"; maxY.Text = "200"; NumericInput.CommitAll(atlas);
                Check(minX.Text == (scenario == 0 ? "1" : scenario == 1 ? "0.006" : "0") &&
                    maxX.Text == (scenario == 0 ? "6" : scenario == 1 ? "0.08" : "0.85") &&
                    minY.Text == (scenario == 0 ? "0.012" : scenario == 1 ? "0" : "-180") &&
                    maxY.Text == (scenario == 0 ? "0.2" : scenario == 1 ? "1" : "180"), "Depth, metric size, opacity and observer require independent bounds.");
                minX.Text = scenario == 0 ? "5" : scenario == 1 ? ".01" : ".15";
                maxX.Text = scenario == 0 ? "6" : scenario == 1 ? ".03" : ".45";
                minY.Text = scenario == 0 ? ".04" : scenario == 1 ? "0" : "-40";
                maxY.Text = scenario == 0 ? ".08" : scenario == 1 ? ".4" : "40";
                var fixedLatitude = FixedBoxes(atlas)[3]; fixedLatitude.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(fixedLatitude) && NumericInput.GetHasNotice(fixedLatitude), "Invalid fixed latitude must recover with feedback.");
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (scenario == 0 ? 6 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Honeycomb previews are incomplete or repeat depth rows.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(scenario != 0 || expected.HyperbolicHoneycomb.Depth == 5, "Depth must follow the selected X coordinate.");
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Honeycomb atlas differs from direct rendering of the selected geometry, cut or observer.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Honeycomb parameter changes produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed fixed settings, quality or exact geometry/observer values.");
                if (scenario < 2)
                    Check(ObserverBoxes(editor).Select(b => b.Text).SequenceEqual(drafts) && Element<TextBlock>(editor, "PositionStatus").Text == warning,
                        "Geometry-only application must preserve unapplied observer drafts and their status.");
                else
                    Check(ObserverBoxes(editor).Select(b => double.Parse(b.Text, CultureInfo.InvariantCulture)).SequenceEqual(
                        new[] { after.HyperbolicHoneycomb.ObserverX, after.HyperbolicHoneycomb.ObserverY, after.HyperbolicHoneycomb.ObserverZ }) &&
                        Element<TextBlock>(editor, "PositionStatus").Text.Length == 0, "A selected observer move must replace drafts with its exact valid XYZ.");
                Check(Element<TextBlock>(editor, "DepthText").Text.StartsWith(after.HyperbolicHoneycomb.Depth.ToString(CultureInfo.InvariantCulture) + " ·"), "The depth/radius label must follow the applied geometry.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                Check(main.CaptureState("restored").HyperbolicHoneycomb == after.HyperbolicHoneycomb, "Save/load lost exact honeycomb settings.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without application changed the view.");
                Check(JsonSerializer.Serialize(source) == original, "Rendering mutated the source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
            HyperbolicHoneycombAtlasWindow CreateAtlas() => (HyperbolicHoneycombAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateHyperbolicAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (hyperbolic-honeycomb-atlas): tessellations/visibility/cuts/presets, active axes, sphere-safe observer, precision, GPU previews, observer drafts, apply, saves and close.");

        static Fractal3DState State(HyperbolicHoneycombKind tessellation, bool edges, bool faces, int cut)
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.HyperbolicHoneycomb);
            state.HyperbolicHoneycomb = new() { Tessellation = tessellation, ShowEdges = edges, ShowFaces = faces, CutAxis = cut,
                EdgeRadius = .055123456789012345, FaceThickness = .018123456789012345, FaceOpacity = .16123456789012345,
                CutPosition = .12345678901234567, ObserverX = .23456789012345678, ObserverY = -.12345678901234567, ObserverZ = .19876543210987654 };
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var preview = state.Clone(); preview.Iterations = Math.Clamp(preview.Iterations, 1, 32);
            preview.MaxSteps = Math.Min(preview.MaxSteps, 160); preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false; return preview;
        }
        static TextBox[] ObserverBoxes(FrameworkElement editor) => new[] { "ObserverXBox", "ObserverYBox", "ObserverZBox" }.Select(name => Element<TextBox>(editor, name)).ToArray();
        static decimal[] Selected(ParameterAtlasWindow atlas) => (decimal[])typeof(ParameterAtlasWindow)
            .GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(HyperbolicHoneycombAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(HyperbolicHoneycombAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(HyperbolicHoneycombAtlasWindow atlas, Fractal3DState target, decimal[] point)
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
