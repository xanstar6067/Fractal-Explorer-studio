using System.Diagnostics;
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
    private static async Task VerifyIfs3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Ifs3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Ifs3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind).Append(State()))
        foreach (int index in new[] { 0, source.IfsTransforms.Count - 1 }.Distinct())
        {
            var atlas = new Ifs3DAtlasWindow(source, index);
            try
            {
                string original = JsonSerializer.Serialize(source); var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == original, "Untouched IFS atlas changed exact coefficients or unrelated fields.");
                var boxes = FixedBoxes(atlas); Check(boxes.Length == 13 && boxes.All(NumericInput.IsRegistered), "IFS requires validated matrix, translation and weight fields.");
                Check(Element<ComboBox>(atlas, "XAxisBox").SelectedIndex == 3 && Element<ComboBox>(atlas, "YAxisBox").SelectedIndex == 7, "Default axes must compare Tx and Ty.");
                var point = Selected(atlas);
                for (int p = 0; p < point.Length; p++)
                {
                    var values = (decimal[])point.Clone(); values[p] += .001m;
                    target = source.Clone(); Apply(atlas, target, values);
                    Check(target.Iterations == source.Iterations && target.IfsTransforms.Where((_, i) => i != index).Select(t => JsonSerializer.Serialize(t))
                        .SequenceEqual(source.IfsTransforms.Where((_, i) => i != index).Select(t => JsonSerializer.Serialize(t))), "Changing one coefficient altered other maps or point count.");
                    values[p] = p == 12 ? 11 : 1001; Reject(atlas, source.Clone(), values);
                }
                target = source.Clone(); target.IfsTransforms.Add(target.IfsTransforms[0].Clone()); Reject(atlas, target, point);
                if (source.IfsTransforms.Count > 1)
                { target = source.Clone(); target.IfsTransforms[(index + 1) % target.IfsTransforms.Count].Tx += .01; Reject(atlas, target, point); }
                Check(JsonSerializer.Serialize(source) == original, "IFS atlas mutated source maps.");
            }
            finally { atlas.Close(); }
        }
        foreach (double weight in new[] { 0d, 20d, 1e29 })
        {
            var source = State(); source.IfsTransforms[0].Probability = weight;
            var atlas = new Ifs3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Zero/large weights must retain their exact source values.");
                Check(FixedBoxes(atlas).Length == (weight >= 1e28 ? 12 : 13), "Unrepresentable double weights must remain fixed.");
            }
            finally { atlas.Close(); }
        }
        var single = State(); single.IfsTransforms = [single.IfsTransforms[0]];
        var singleAtlas = new Ifs3DAtlasWindow(single);
        try { var p = Selected(singleAtlas); p[12] = 0; Reject(singleAtlas, single.Clone(), p); }
        finally { singleAtlas.Close(); }
        foreach (var invalid in new[] { State(), State(), State() }.Select((s, i) =>
        {
            if (i == 0) s.IfsTransforms.Clear();
            else if (i == 1) s.IfsTransforms.ForEach(t => t.Probability = 0);
            else s.IfsTransforms[0].M11 = double.NaN;
            return s;
        }))
        {
            bool rejected = false; try { new Ifs3DAtlasWindow(invalid).Close(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid IFS sources must be rejected.");
        }
        Console.WriteLine("IFS atlas: presets, selected maps, exact parameters, weights and atomic validation passed.");

        // One grid, stopped after two previews. Keep the existing 512³ volume, avoid long orbit sweeps.
        var state = State(); state.Iterations = 10_000;
        var main = new Fractal3DWindow(kind); Ifs3DTransformEditorWindow? editor = null; Ifs3DAtlasWindow? grid = null;
        try
        {
            main.LoadState(state); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            editor = (Ifs3DTransformEditorWindow)typeof(Fractal3DWindow).GetMethod("CreateIfsTransformEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            new System.Windows.Interop.WindowInteropHelper(editor).EnsureHandle();
            Check(editor.CaptureAtlasView is not null && editor.SuspendAtlas is not null && Element<Button>(editor, "AtlasButton").IsEnabled, "Editor must use the current view and suspend parent rendering for the atlas.");
            Check(Element<TextBox>(editor, "M12Box").Text == state.IfsTransforms[0].M12.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Editor rounded source coefficients.");
            Element<TextBox>(editor, "TxBox").Text = ".23456789012345678";
            grid = CreateAtlas(editor); Check(grid.Owner == editor, "Atlas must belong to the transform editor.");
            var before = CaptureEditor(editor); before.SaveName = "view";
            FixedBoxes(grid)[1].Text = "NaN"; NumericInput.CommitAll(grid);
            Check(NumericInput.IsValid(FixedBoxes(grid)[1]) && NumericInput.GetHasNotice(FixedBoxes(grid)[1]), "Invalid fixed coefficients must recover with feedback.");
            Element<TextBox>(grid, "MinRealBox").Text = "-.8"; Element<TextBox>(grid, "MaxRealBox").Text = "-.2";
            Element<TextBox>(grid, "MinImaginaryBox").Text = "-.8"; Element<TextBox>(grid, "MaxImaginaryBox").Text = "-.2";
            Task building = Build(grid); var watch = Stopwatch.StartNew();
            var tiles = Element<UniformGrid>(grid, "TilesPanel");
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(Element<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(ready.Length >= 2 && !grid.IsRendering, "IFS previews did not complete or cancellation failed.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            using (var renderer = new Fractal3DRenderer())
            {
                var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                byte[] pixels = Pixels((BitmapSource)Element<Image>(grid, "SelectedPreview").Source);
                Check(pixels.SequenceEqual(reference.Buffer), "IFS atlas differs from direct rendering with its preview point count.");
                Click(ready[1]); Check(!pixels.SequenceEqual(Pixels((BitmapSource)Element<Image>(grid, "SelectedPreview").Source)), "Changing one affine map must alter the preview.");
            }
            expected = before.Clone(); grid.ApplySelectionTo(expected);
            Click(Element<Button>(grid, "ApplyButton")); grid = null;
            var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Applying an IFS variant changed camera, palette, point count or other maps.");
            Check(Element<TextBox>(editor, "TxBox").Text == after.IfsTransforms[0].Tx.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Selected coefficients must reach the editor exactly.");
            // Commit the atlas result, then undo local edits. Closing restores the committed result.
            Invoke(editor, "Apply_OnClick", editor, new RoutedEventArgs());
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            Click(Element<Button>(editor, "UndoButton")); Invoke(editor, "Preview");
            Check(JsonSerializer.Serialize(CaptureEditor(editor).IfsTransforms) == JsonSerializer.Serialize(before.IfsTransforms), "Atlas result must be one undo entry.");
            Click(Element<Button>(editor, "UndoButton")); Invoke(editor, "Preview");
            Check(JsonSerializer.Serialize(CaptureEditor(editor).IfsTransforms) == JsonSerializer.Serialize(state.IfsTransforms), "Atlas discarded earlier editor history.");
            var beforeClose = CaptureEditor(editor); grid = CreateAtlas(editor); grid.Close(); grid = null;
            Check(JsonSerializer.Serialize(CaptureEditor(editor).IfsTransforms) == JsonSerializer.Serialize(beforeClose.IfsTransforms), "Closing the atlas applied a selection.");
            editor.Close(); editor = null;
            Check(JsonSerializer.Serialize(main.CaptureState("closed").IfsTransforms) == JsonSerializer.Serialize(after.IfsTransforms), "Closing the editor must restore the last committed atlas result.");
            main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
            Check(JsonSerializer.Serialize(main.CaptureState("saved").IfsTransforms) == JsonSerializer.Serialize(after.IfsTransforms), "Save/load rounded affine maps.");
        }
        finally { grid?.Close(); editor?.Close(); main.Close(); }
        Console.WriteLine("PASS (ifs3d-atlas): exact selected affine map, weights, bounded previews, main view, undo history, commit/rollback and saves.");

        static Fractal3DState State()
        {
            var s = Fractal3DCatalog.CreateDefaultState(kind); s.Iterations = 1_234_567;
            s.IfsTransforms[0].M12 = .012345678901234567; s.IfsTransforms[0].Probability = .12345678901234567;
            return s;
        }
        static Fractal3DState Preview(Fractal3DState source)
        {
            var s = source.Clone(); s.Iterations = Math.Clamp(s.Iterations, 10_000, 40_000); s.MaxSteps = Math.Min(s.MaxSteps, 160);
            s.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(s.Detail); s.Ssaa = 1; s.SoftShadows = s.AmbientOcclusion = false; return s;
        }
        static Ifs3DAtlasWindow CreateAtlas(Ifs3DTransformEditorWindow e) => (Ifs3DAtlasWindow)Invoke(e, "CreateAtlas")!;
        static Fractal3DState CaptureEditor(Ifs3DTransformEditorWindow e) => (Fractal3DState)Invoke(e, "CaptureAtlasState")!;
        static object? Invoke(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
        static decimal[] Selected(ParameterAtlasWindow atlas) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow atlas) => Element<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Ifs3DAtlasWindow atlas, Fractal3DState state, decimal[] point) => Invoke(atlas, "ApplyComponents", state, point);
        static void Reject(Ifs3DAtlasWindow atlas, Fractal3DState state, decimal[] point)
        {
            string before = JsonSerializer.Serialize(state); bool rejected = false;
            try { Apply(atlas, state, point); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(state) == before, "Invalid or incompatible IFS parameters must be rejected atomically.");
        }
        static Task Build(ParameterAtlasWindow atlas) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
