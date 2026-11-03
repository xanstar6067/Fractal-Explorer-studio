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
    private static async Task VerifyLSystem3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("LSystem3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.LSystem3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        {
            source.LSystem = source.LSystem with { Radius = .12345678901234567, BranchTaper = .7123456789012345,
                StepDecay = .8234567890123456, Growth = .23456789012345678 };
            var main = new Fractal3DWindow(kind); LSystem3DAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                Check(main.CaptureState("view").LSystem == source.LSystem, "Loading scaled L-system fields lost precision.");
                atlas = Create(main); var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Untouched atlas changed exact fields or grammar.");
                Check(Boxes(atlas).Length == 8 && Boxes(atlas).All(NumericInput.IsRegistered), "Eight validated shape parameters required.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas, "ComponentBounds", i)!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var values = (decimal[])point.Clone(); values[i] = edge; target = source.Clone(); Apply(atlas, target, values); target.LSystem.Validate();
                        Check(target.LSystem.Axiom == source.LSystem.Axiom && target.LSystem.RulesText == source.LSystem.RulesText &&
                            target.LSystem.DrawSymbols == source.LSystem.DrawSymbols && target.LSystem.ColorSource == source.LSystem.ColorSource, "Atlas changed fixed grammar/color fields.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                var fractional = (decimal[])point.Clone(); fractional[0] += .5m; Reject(atlas, source.Clone(), fractional);
                var wrong = source.Clone(); wrong.LSystem = wrong.LSystem with { RulesText = "F = FF" }; Reject(atlas, wrong, point);
                wrong = source.Clone(); wrong.LSystem = wrong.LSystem with { ColorSource = (LSystem3DColorSource)(((int)wrong.LSystem.ColorSource + 1) % 3) }; Reject(atlas, wrong, point);
            }
            finally { atlas?.Close(); main.Close(); }
        }
        foreach (int generation in new[] { 6, 7 })
        {
            bool rejected = false;
            try { LSystem3DGeometry.Build(new() { Axiom = "F", RulesText = "F = FFFFFFFF", Generations = generation }, CancellationToken.None); }
            catch (LSystem3DGeometryException ex) { rejected = ex.Message.Contains(generation == 6 ? "ветвей" : "символов"); }
            Check(rejected, "Geometry limits must report a sample failure without truncating a generation.");
        }
        Console.WriteLine("L-system atlas: six presets, exact fields, bounds, fixed grammar and geometry limits passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.LSystem = state.LSystem with { Generations = 2, Growth = .23456789012345678 };
        var window = new Fractal3DWindow(kind); LSystem3DAtlasWindow? grid = null;
        try
        {
            window.LoadState(state); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var editor = E<LSystem3DEditor>(window, "LSystemEditor");
            E<TextBox>(editor, "RulesBox").Text = "A = F[&F]A"; Click(E<Button>(editor, "ApplyButton"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!E<Button>(editor, "ApplyButton").IsEnabled && watch.Elapsed.TotalSeconds < 10) await Task.Delay(10);
            Check(E<Button>(editor, "UndoButton").IsEnabled && editor.Capture().RulesText == "A = F[&F]A", "Setup must create real applied-rule history.");
            E<TextBox>(editor, "RulesBox").Text = "A = ["; const string draft = "A = [";
            Click(E<Button>(editor, "PlayButton")); var shown = window.CaptureState("view");
            await Task.Delay(10); editor.OnFrameDisplayed(6, 20, shown.LSystem);
            Check(editor.Capture().Growth != shown.LSystem.Growth, "Playback must have a pending growth phase.");
            grid = Create(window); var before = window.CaptureState("view");
            Check(!editor.IsPlaying && before.LSystem.Growth == shown.LSystem.Growth, "Opening the atlas must freeze the exact displayed phase.");
            E<ComboBox>(grid, "XAxisBox").SelectedIndex = 2; E<ComboBox>(grid, "YAxisBox").SelectedIndex = 0;
            E<TextBox>(grid, "MinRealBox").Text = "10"; E<TextBox>(grid, "MaxRealBox").Text = "50";
            E<TextBox>(grid, "MinImaginaryBox").Text = "0"; E<TextBox>(grid, "MaxImaginaryBox").Text = "1";
            // A full growth phase makes the branching-angle variants visible.
            Boxes(grid)[7].Text = "1";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel"); watch.Restart();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 60) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(ready.Length >= 2 && tiles.Children.Count == 6 && !((Button)tiles.Children[4]).IsEnabled &&
                ((Button)tiles.Children[4]).ToolTip.ToString()!.Contains("Нет ветвей"), "Empty generations must not prevent valid neighbours from rendering.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            byte[] first = Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source);
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Iterations = Math.Clamp(p.Iterations, 1, 32); p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(first.SequenceEqual(reference.Buffer) && renderer.LSystemSegmentCount == 2, "Preview must use the complete selected generation and applied grammar.");
            }
            Click(ready[1]); Check(!first.SequenceEqual(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source)), "Different angles need different previews.");
            expected = before.Clone(); grid.ApplySelectionTo(expected); Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = window.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed unrelated fields or precision.");
            Check(E<TextBox>(editor, "RulesBox").Text == draft && E<Button>(editor, "UndoButton").IsEnabled && !editor.IsPlaying,
                "Application must preserve the rule draft/history and keep playback stopped.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            Check(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.LSystem == after.LSystem, "Saving lost exact shape/grammar fields.");
            Click(E<Button>(editor, "UndoButton"));
            Check(editor.Capture() == after.LSystem with { Axiom = state.LSystem.Axiom, RulesText = state.LSystem.RulesText, DrawSymbols = state.LSystem.DrawSymbols },
                "The existing rule undo must still work without reverting atlas shape parameters.");
            window.LoadState(after); grid = Create(window); var unchanged = window.CaptureState("view");
            Task closing = Build(grid); grid.Close(); await closing; grid = null;
            var closed = window.CaptureState("view"); closed.Timestamp = unchanged.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(unchanged), "Closing during calculation changed the main state.");
            editor.Load(after.LSystem with { Growth = .2 }); Click(E<Button>(editor, "PlayButton")); editor.StopAtDisplayedGrowth();
            Check(editor.Capture().Growth == .2, "Loading must clear stale displayed phases.");
            editor.Load(after.LSystem with { Growth = .99 }); E<Slider>(editor, "SpeedSlider").Value = 5;
            Click(E<Button>(editor, "PlayButton")); await Task.Delay(80);
            editor.OnFrameDisplayed(2, 6, after.LSystem with { Growth = .99 });
            Check(!editor.IsPlaying && editor.Capture().Growth == 1, "Setup must reach the automatic playback stop with a pending final frame.");
            editor.StopAtDisplayedGrowth();
            Check(editor.Capture().Growth == .99, "Automatic playback stop must not expose a final phase that was never displayed.");
        }
        finally { grid?.Close(); window.Close(); }
        Console.WriteLine("PASS (lsystem3d-atlas): exact fields, full geometry, unavailable generations, displayed phase, drafts/history, GPU previews, apply, close and saves.");
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static LSystem3DAtlasWindow Create(Fractal3DWindow w) => (LSystem3DAtlasWindow)Invoke(w, "CreateLSystemAtlas")!;
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(LSystem3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(LSystem3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
