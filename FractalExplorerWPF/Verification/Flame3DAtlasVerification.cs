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
    private static async Task VerifyFlame3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Flame3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Flame3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int index in new[] { 0, source.Flame.Transforms.Count - 1 }.Distinct())
        {
            var atlas = new Flame3DAtlasWindow(source, index);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Untouched Flame atlas changed exact source values.");
                Check(Boxes(atlas).Length == 7 && Boxes(atlas).All(NumericInput.IsRegistered), "Seven validated Flame parameters required.");
                foreach (int variation in Enumerable.Range(1, 8))
                {
                    var p = Selected(atlas); p[0] = variation; p[1] = .3123456789012345m;
                    target = source.Clone(); Apply(atlas, target, p);
                    var expected = source.Clone(); expected.Flame.Transforms[index].Variation = (Flame3DVariation)(variation - 1);
                    expected.Flame.Transforms[index].Amount = (double)p[1];
                    Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(expected), "Variation selection changed fixed maps, colors or render settings.");
                }
                foreach (decimal[] invalid in new decimal[][] { [0,1,1,.5m,0,0,0], [1.5m,1,1,.5m,0,0,0], [1,2,1,.5m,0,0,0], [1,1,-1,.5m,0,0,0] })
                    Reject(atlas, source.Clone(), invalid);
            }
            finally { atlas.Close(); }
        }
        var single = Fractal3DCatalog.CreateDefaultState(kind); single.Flame.Transforms = [single.Flame.Transforms[0]];
        var one = new Flame3DAtlasWindow(single);
        try { var p = Selected(one); p[2] = 0; Reject(one, single.Clone(), p); } finally { one.Close(); }
        Console.WriteLine("Flame atlas: presets, eight variations, exact fixed fields and weight validation passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind); state.Iterations = 50_000;
        state.Flame.Transforms[0].Map.M12 = .012345678901234567;
        var main = new Fractal3DWindow(kind); Flame3DTransformEditorWindow? editor = null; Flame3DAtlasWindow? grid = null;
        try
        {
            main.LoadState(state); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            editor = (Flame3DTransformEditorWindow)Invoke(main, "CreateFlameTransformEditor")!;
            new System.Windows.Interop.WindowInteropHelper(editor).EnsureHandle();
            Check(editor.CaptureAtlasView is not null && editor.SuspendAtlas is not null, "Flame editor must preserve the main view and suspend rendering.");
            E<TextBox>(editor, "TxBox").Text = ".23456789012345678";
            grid = (Flame3DAtlasWindow)Invoke(editor, "CreateAtlas")!;
            var before = (Fractal3DState)Invoke(editor, "CaptureAtlasState")!; before.SaveName = "view";
            E<TextBox>(grid, "MinRealBox").Text = "1"; E<TextBox>(grid, "MaxRealBox").Text = "2";
            E<TextBox>(grid, "MinImaginaryBox").Text = ".2"; E<TextBox>(grid, "MaxImaginaryBox").Text = ".8";
            Task building = (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, null)!;
            var tiles = E<UniformGrid>(grid, "TilesPanel"); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2, "Flame previews failed.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Iterations = Math.Clamp(p.Iterations, 50_000, 100_000); p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source).SequenceEqual(reference.Buffer), "Flame preview differs from direct rendering.");
            }
            Click(ready[1]); expected = before.Clone(); grid.ApplySelectionTo(expected);
            Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Flame application changed unrelated main settings or precision.");
            Invoke(editor, "Apply_OnClick", editor, new RoutedEventArgs());
            Click(E<Button>(editor, "UndoButton"));
            Check(JsonSerializer.Serialize(((Fractal3DState)Invoke(editor, "CaptureAtlasState")!).Flame.Transforms) == JsonSerializer.Serialize(before.Flame.Transforms), "Flame atlas must be one undo entry.");
            Click(E<Button>(editor, "UndoButton"));
            Check(JsonSerializer.Serialize(((Fractal3DState)Invoke(editor, "CaptureAtlasState")!).Flame.Transforms) == JsonSerializer.Serialize(state.Flame.Transforms), "Flame atlas discarded prior history.");
            editor.Close(); editor = null;
            Check(JsonSerializer.Serialize(main.CaptureState("closed").Flame.Transforms) == JsonSerializer.Serialize(after.Flame.Transforms), "Editor close lost the committed Flame variant.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
            Check(JsonSerializer.Serialize(main.CaptureState("saved").Flame) == JsonSerializer.Serialize(after.Flame), "Flame save/load lost parameters.");
        }
        finally { grid?.Close(); editor?.Close(); main.Close(); }
        Console.WriteLine("PASS (flame3d-atlas): exact variants, GPU previews, main view, undo history, commit/rollback and saves.");
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Flame3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(Flame3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid Flame parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
