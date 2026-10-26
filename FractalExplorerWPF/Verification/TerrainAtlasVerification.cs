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
    private static async Task VerifyTerrainAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("TerrainAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Terrain;
        // Every type, shape, source grid and preset is checked without generating height fields.
        var configurations = from type in Enum.GetValues<TerrainKind>() from shape in Enum.GetValues<TerrainShape>()
            from resolution in new[] { 257, 513, 1025, 2049 } select State(type, shape, resolution);
        foreach (var source in configurations.Concat(Fractal3DCatalog.GetPresets(kind)))
        {
            var atlas = new TerrainAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas);
                int count = source.Terrain.Shape == TerrainShape.Patch ? 9 : 11;
                Check(names.Length == count && boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Only relevant, validated terrain parameters belong in the atlas.");
                Check(names.Contains("Сила формы") == (count == 11) && names.Contains("Радиус формы") == (count == 11), "A plain patch must not expose unused shape controls.");
                Check(names.Contains("Угол осыпания, °"), "Talus must remain available when erosion can be enabled within the atlas.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Default axes must compare roughness and scale.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b; NumericInput.CommitAll(atlas);
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(box => box.IsEnabled) == count - 2, "Axis pairs lost fixed terrain fields.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded terrain values or changed the main grid.");
                var point = (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
                for (int i = 0; i < count; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))typeof(TerrainAtlasWindow)
                        .GetMethod("ComponentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [i])!;
                    foreach (decimal value in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var edge = (decimal[])point.Clone(); edge[i] = value;
                        var target = source.Clone(); Apply(atlas, target, edge); target.Terrain.Validate();
                        Check(target.Terrain.Resolution == source.Terrain.Resolution, "A numerical axis changed the main resolution.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + .1m; Reject(atlas, source.Clone(), invalid);
                }
                foreach (int index in new[] { 4, 6, 7 })
                {
                    var invalid = (decimal[])point.Clone(); invalid[index] = index == 6 ? 42.5m : 1.5m;
                    Reject(atlas, source.Clone(), invalid);
                }
                var wrong = source.Clone(); wrong.Terrain = wrong.Terrain with { Type = (TerrainKind)(((int)wrong.Terrain.Type + 1) % 3) };
                Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.Terrain = wrong.Terrain with { Shape = (TerrainShape)(((int)wrong.Terrain.Shape + 1) % 3) };
                Reject(atlas, wrong, point);
            }
            finally { atlas.Close(); }
        }

        // Small 257² previews only; the 2049² source maps are never built by this group.
        foreach (var shape in Enum.GetValues<TerrainShape>())
        {
            var source = State(shape switch { TerrainShape.Patch => TerrainKind.Ridged, TerrainShape.Island => TerrainKind.Hybrid, _ => TerrainKind.Fbm }, shape, 2049);
            bool erosion = shape == TerrainShape.Crater;
            if (erosion) source.Terrain = source.Terrain with { ThermalIterations = 8 };
            var main = new Fractal3DWindow(kind);
            TerrainAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var before = main.CaptureState("view");
                Check(before.Terrain == source.Terrain, "Loading rounded terrain settings.");
                var editor = Element<TerrainEditor>(main, "TerrainPanel");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible, "The active terrain panel must expose its atlas.");
                atlas = CreateAtlas(); Check(atlas.Owner == main, "Atlas lost its owner.");
                if (erosion)
                {
                    var point = (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
                    point[7] = 64;
                    var render = (Func<decimal[], CancellationToken, Task<byte[]>>)typeof(Fractal3DParameterAtlasWindow)
                        .GetMethod("CreateAsyncRenderSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, null)!;
                    byte[] actual = await render(point, CancellationToken.None);
                    var expected = before.Clone(); Apply(atlas, expected, point);
                    using var renderer = new Fractal3DRenderer();
                    var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    Check(actual.SequenceEqual(reference.Buffer) && expected.Terrain.ThermalIterations == 64 && expected.Terrain.Resolution == 2049,
                        "The preview must retain 64 erosion passes while application retains the main resolution.");
                }
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                x.SelectedIndex = shape switch { TerrainShape.Island => 9, TerrainShape.Crater => 7, _ => 0 };
                y.SelectedIndex = shape switch { TerrainShape.Island => 10, TerrainShape.Crater => 8, _ => 1 };
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Axis bounds need NumericInput.");
                minX.Text = "-100"; maxX.Text = "100"; minY.Text = "-100"; maxY.Text = "100"; NumericInput.CommitAll(atlas);
                Check(minX.Text == (shape == TerrainShape.Patch ? "0.05" : "0") &&
                    maxX.Text == (shape == TerrainShape.Patch ? "0.95" : erosion ? "64" : "1") &&
                    minY.Text == (shape == TerrainShape.Patch ? "0.25" : erosion ? "5" : "0.2") &&
                    maxY.Text == (shape == TerrainShape.Patch ? "16" : erosion ? "60" : "1"), "Per-parameter terrain bounds are incorrect.");
                minX.Text = shape == TerrainShape.Patch ? ".25" : erosion ? "0" : ".4";
                maxX.Text = shape == TerrainShape.Patch ? ".75" : "1";
                minY.Text = shape == TerrainShape.Patch ? "2" : erosion ? "15" : ".5";
                maxY.Text = shape == TerrainShape.Patch ? "4" : erosion ? "35" : ".9";
                var fields = FixedBoxes(atlas);
                fields[5].Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(fields[5]) && NumericInput.GetHasNotice(fields[5]), "Invalid fixed input must recover with feedback.");
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (erosion ? 6 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Terrain previews are incomplete or repeat integer passes.");
                Click((Button)tiles.Children[0]);
                var selected = before.Clone(); atlas.ApplySelectionTo(selected);
                Check(selected.Terrain.Lacunarity == before.Terrain.Lacunarity && selected.Terrain.Resolution == 2049 &&
                    (shape != TerrainShape.Patch || selected.Terrain.ShapeRadius == before.Terrain.ShapeRadius), "Fixed/hidden values lost precision or the main resolution changed.");
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(Preview(selected), 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Terrain atlas differs from a direct frame with the selected preview grid and erosion.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Terrain parameter samples produced identical previews.");
                }
                if (erosion) Click((Button)tiles.Children[tiles.Children.Count - tiles.Columns]);
                selected = before.Clone(); atlas.ApplySelectionTo(selected);
                Check(!erosion || selected.Terrain.ThermalIterations == 0, "The first column must permit disabling erosion.");
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = selected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(selected), "Application changed unrelated view settings, the main grid or exact terrain parameters.");
                Check(after.Terrain.Resolution == 2049 && after.Terrain.Type == source.Terrain.Type && after.Terrain.Shape == source.Terrain.Shape,
                    "Preview quality leaked into the main map or categorical settings changed.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                Check(main.CaptureState("restored").Terrain == after.Terrain, "Save/load lost terrain parameters, hidden fields or the main resolution.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the terrain view.");
                Check(source.Terrain.Resolution == 2049 && source.Terrain.Roughness == .5512345678901234 &&
                    source.Terrain.ThermalIterations == (erosion ? 8 : 0), "Preview rendering mutated its source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
            TerrainAtlasWindow CreateAtlas() => (TerrainAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateTerrainAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (terrain-atlas): types/shapes/grids/presets, active axes, bounds and integer fields, precision, 257² previews, 64 erosion passes, main resolution, apply, saves and close.");

        static Fractal3DState State(TerrainKind type, TerrainShape shape, int resolution)
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Terrain);
            state.Terrain = new() { Type = type, Shape = shape, Resolution = resolution, Seed = int.MaxValue,
                Roughness = .5512345678901234, Lacunarity = 2.1234567890123457, Scale = 3.1234567890123457,
                Height = 1.6123456789012345, Size = 6.123456789012345,
                ShapeStrength = .8123456789012345, ShapeRadius = .7123456789012345, TalusAngle = 25.123456789012345 };
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var preview = state.Clone(); preview.Terrain = preview.Terrain with { Resolution = 257 };
            preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
            preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false; return preview;
        }
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(TerrainAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(TerrainAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(TerrainAtlasWindow atlas, Fractal3DState target, decimal[] point)
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
