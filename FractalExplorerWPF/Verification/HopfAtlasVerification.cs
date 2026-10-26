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
    private static async Task VerifyHopfAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("HopfAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Hopf;
        var configurations = new List<Fractal3DState>(Fractal3DCatalog.GetPresets(kind));
        foreach (var family in Enum.GetValues<HopfFamily>())
        foreach (int selected in new[] { -1, 0, family == HopfFamily.Custom ? 2 : 7 })
        foreach (bool isolated in selected < 0 ? new[] { false } : new[] { false, true })
        {
            var source = State(family);
            // Single-family modes have four points, layered modes have eight.
            int last = source.Hopf.BasePoints().Count - 1;
            source.Hopf = source.Hopf with { SelectedFiber = Math.Min(selected, last), OnlySelected = isolated };
            configurations.Add(source);
        }
        var empty = State(HopfFamily.Custom); empty.Hopf = empty.Hopf with { Points = [] }; configurations.Add(empty);
        var large = State(HopfFamily.Latitudes);
        large.Hopf = large.Hopf with { Fibers = 64, Layers = 4, SelectedFiber = 255, OnlySelected = true }; configurations.Add(large);
        var single = State(HopfFamily.Latitudes);
        single.Hopf = single.Hopf with { Layers = 1, SelectedFiber = 0 }; configurations.Add(single);
        foreach (var source in configurations)
        {
            var atlas = new HopfAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas);
                bool custom = source.Hopf.Family == HopfFamily.Custom, selected = source.Hopf.SelectedFiber >= 0;
                bool layered = source.Hopf.Family is HopfFamily.Latitudes or HopfFamily.LinkedTori;
                Check(names.Length == boxes.Length && boxes.All(NumericInput.IsRegistered), "Every Hopf atlas field must use NumericInput.");
                Check(names.Take(6).All(n => n.StartsWith("Поворот ")) && names.Contains("Толщина трубок") && names.Contains("Радиус видимой области"), "All six rotations, thickness and clipping must be available.");
                Check(names.Contains("Колец на семейство") == (!custom && !selected) &&
                    names.Contains("Число семейств") == (!custom && !selected && layered), "Counts must stay fixed with selected/isolated fibers.");
                Check(names.Contains("Широта семейства, °") == (!custom && source.Hopf.Family != HopfFamily.Sphere) &&
                    names.Contains("Разброс широт, °") == (source.Hopf.Family == HopfFamily.Latitudes && (!selected || source.Hopf.Layers > 1)), "Unused family parameters must be excluded.");
                Check(names.Contains("Сдвиг по долготе, °") == !custom &&
                    names.Contains("Долгота выбранной точки, °") == (custom && selected) &&
                    names.Contains("Широта выбранной точки, °") == (custom && selected), "Only a selected custom point can expose its coordinates.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Default axes must compare XW and YW.");
                for (int a = 0; a < names.Length; a++) for (int b = a + 1; b < names.Length; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b; NumericInput.CommitAll(atlas);
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == names.Length - 2, "Axis pairs lost fixed parameters.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched Hopf parameters lost precision, selection or hidden fields.");
                var point = Selected(atlas);
                for (int i = 0; i < names.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))typeof(HopfAtlasWindow)
                        .GetMethod("ComponentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [i])!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var values = (decimal[])point.Clone(); values[i] = edge;
                        var target = source.Clone(); Apply(atlas, target, values); target.Hopf.Validate();
                        Check(target.Hopf.SelectedFiber == source.Hopf.SelectedFiber && target.Hopf.OnlySelected == source.Hopf.OnlySelected,
                            "A numerical parameter must preserve selection and isolation.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + .1m;
                    Reject(atlas, source.Clone(), invalid);
                    if (names[i] is "Колец на семейство" or "Число семейств")
                    { invalid[i] = 1.5m; Reject(atlas, source.Clone(), invalid); }
                }
                var wrong = source.Clone(); wrong.Hopf = wrong.Hopf with { Family = (HopfFamily)(((int)wrong.Hopf.Family + 1) % 5) };
                Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.Hopf.Points.Add(new(17, 23)); Reject(atlas, wrong, point);
                if (selected)
                {
                    wrong = source.Clone(); wrong.Hopf = wrong.Hopf with { OnlySelected = !wrong.Hopf.OnlySelected };
                    Reject(atlas, wrong, point);
                }
            }
            finally { atlas.Close(); }
        }

        // Only three small grids: rotations, integer counts, and an isolated custom point.
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var source = State(scenario == 2 ? HopfFamily.Custom : HopfFamily.Latitudes);
            if (scenario == 2) source.Hopf = source.Hopf with { SelectedFiber = 0, OnlySelected = true };
            string original = JsonSerializer.Serialize(source);
            var main = new Fractal3DWindow(kind);
            HopfAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<HopfEditor>(main, "HopfEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible, "Hopf must expose its atlas.");
                Check(JsonSerializer.Serialize(editor.Capture()) == JsonSerializer.Serialize(source.Hopf), "Loading rounded Hopf slider values.");
                if (scenario == 0)
                {
                    var displayed = main.CaptureState("displayed"); editor.Start();
                    typeof(Fractal3DWindow).GetMethod("OnHopfFrameDisplayed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [displayed]);
                    Check(editor.IsPlaying && editor.Capture().XW != displayed.Hopf.XW, "Animation must queue a different angle after a displayed frame.");
                    atlas = CreateAtlas();
                    Check(!editor.IsPlaying && JsonSerializer.Serialize(editor.Capture()) == JsonSerializer.Serialize(displayed.Hopf), "Atlas must pause at the displayed angles, preserving all other settings.");
                }
                else atlas = CreateAtlas();
                Check(atlas.Owner == main, "Atlas lost its owner.");
                var before = main.CaptureState("view");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray();
                x.SelectedIndex = scenario == 0 ? 0 : Array.IndexOf(names, scenario == 1 ? "Колец на семейство" : "Долгота выбранной точки, °");
                y.SelectedIndex = scenario == 0 ? 1 : Array.IndexOf(names, scenario == 1 ? "Число семейств" : "Широта выбранной точки, °");
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Axis bounds need NumericInput.");
                minX.Text = "-200"; maxX.Text = "200"; minY.Text = "-200"; maxY.Text = "200"; NumericInput.CommitAll(atlas);
                Check(minX.Text == (scenario == 1 ? "1" : "-180") && maxX.Text == (scenario == 1 ? "64" : "180") &&
                    minY.Text == (scenario == 0 ? "-180" : scenario == 1 ? "1" : "-90") &&
                    maxY.Text == (scenario == 0 ? "180" : scenario == 1 ? "4" : "90"), "Rotations, counts and point coordinates need independent bounds.");
                minX.Text = scenario == 1 ? "1" : "-30"; maxX.Text = scenario == 1 ? "2" : "30";
                minY.Text = scenario == 1 ? "1" : "-20"; maxY.Text = scenario == 1 ? "2" : "20";
                var thickness = FixedBoxes(atlas)[6]; thickness.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(thickness) && NumericInput.GetHasNotice(thickness), "Invalid fixed thickness must recover with feedback.");
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (scenario == 1 ? 4 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Hopf grid is incomplete or duplicates integer counts.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                var preview = Preview(expected);
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Hopf atlas differs from direct rendering of its selected geometry/isolation.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Hopf parameter changes produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(expected.Hopf.Thickness == before.Hopf.Thickness && expected.Hopf.AnimationSpeed == before.Hopf.AnimationSpeed &&
                    expected.Hopf.AnimationPlane == before.Hopf.AnimationPlane, "Fixed thickness or animation controls lost precision.");
                if (scenario == 2)
                    Check(expected.Hopf.Points.Skip(1).SequenceEqual(before.Hopf.Points.Skip(1)) && expected.Hopf.SelectedFiber == 0 && expected.Hopf.OnlySelected,
                        "Editing the selected manual point must preserve other points and isolation.");
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected) && !editor.IsPlaying, "Application rounded values or changed unrelated view settings / animation state.");
                Check(Element<HopfBaseSphere>(editor, "BaseSphere").Points.SequenceEqual(after.Hopf.BasePoints()), "The base sphere must reflect applied family/point changes.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                Check(JsonSerializer.Serialize(main.CaptureState("restored").Hopf) == JsonSerializer.Serialize(after.Hopf), "Save/load lost exact Hopf settings or point data.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the Hopf view.");
                Check(JsonSerializer.Serialize(source) == original, "Preview generation mutated source angles or custom points.");
            }
            finally { atlas?.Close(); main.Close(); }
            HopfAtlasWindow CreateAtlas() => (HopfAtlasWindow)typeof(Fractal3DWindow).GetMethod("CreateHopfAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        var emptyAtlas = new HopfAtlasWindow(empty);
        try
        {
            var render = (Func<decimal[], CancellationToken, Task<byte[]>>)typeof(Fractal3DParameterAtlasWindow)
                .GetMethod("CreateAsyncRenderSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(emptyAtlas, null)!;
            byte[] actual = await render(Selected(emptyAtlas), CancellationToken.None);
            using var renderer = new Fractal3DRenderer();
            var frame = await renderer.RenderPixelsAsync(Preview(empty), 144, 108, null, null, CancellationToken.None);
            Check(actual.SequenceEqual(frame.Buffer), "An empty custom set must preview a valid background.");
        }
        finally { emptyAtlas.Close(); }
        Console.WriteLine("PASS (hopf-atlas): families/presets/selection, active axis pairs, numeric bounds, precision, displayed-frame pause, GPU previews, point isolation, base sphere, apply, saves and close.");

        static Fractal3DState State(HopfFamily family)
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Hopf);
            state.Hopf = new() { Family = family, Fibers = 4, Layers = 2, Latitude = 13.123456789012345,
                Spread = 63.123456789012345, Phase = 17.123456789012345,
                Thickness = .021234567890123456, ClipRadius = 3.1234567890123457,
                XW = 6.123456789012345, YW = -7.123456789012345, ZW = 8.123456789012345,
                XY = 9.123456789012345, XZ = -10.123456789012345, YZ = 11.123456789012345,
                AnimationPlane = HopfRotationPlane.XW, AnimationSpeed = 12.123456789012345,
                Points = [new(17.123456789012345, 23.123456789012345), new(0, -90), new(90, 90)] };
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var preview = state.Clone(); preview.Iterations = Math.Clamp(preview.Iterations, 1, 32);
            preview.MaxSteps = Math.Min(preview.MaxSteps, 160); preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false; return preview;
        }
        static decimal[] Selected(ParameterAtlasWindow atlas) => (decimal[])typeof(ParameterAtlasWindow)
            .GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(HopfAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(HopfAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(HopfAtlasWindow atlas, Fractal3DState target, decimal[] point)
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
