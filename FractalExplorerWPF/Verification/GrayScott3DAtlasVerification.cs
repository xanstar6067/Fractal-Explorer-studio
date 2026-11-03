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
    private static async Task VerifyGrayScott3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("GrayScott3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.GrayScott3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0, 1 })
        {
            source.GrayScott = source.GrayScott with { Feed = .031234567890123456, Kill = .061234567890123456, CutAxis = cut };
            var atlas = new GrayScott3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Parameter mapping rounded source values or replaced the starting field.");
                Check(Boxes(atlas).Length == (cut == 0 ? 5 : 6) && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas, "TrialStepsBox")),
                    "Equation/view fields and the trial duration must use NumericInput; inactive cuts must be absent.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas, "ComponentBounds", i)!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    { var p = (decimal[])point.Clone(); p[i] = edge; target = source.Clone(); Apply(atlas, target, p); target.GrayScott.Validate(); }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                var wrong = source.Clone(); wrong.GrayScott = wrong.GrayScott with { Size = 32 }; Reject(atlas, wrong, point);
                E<TextBox>(atlas, "TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas, "TrialStepsBox").Text == "400", "Invalid trial duration must recover without starting work.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Gray–Scott atlas: presets, active cuts, exact coefficients, bounds and validated duration passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind); state.ShadingStyle = Fractal3DShadingStyle.Density;
        state.GrayScott = state.GrayScott with { Size = 32, Feed = .031234567890123456, Kill = .061234567890123456 };
        state.GrayScott = state.GrayScott with { Field = new GrayScott3DField(32, 7, GrayScott3DGpuSimulation.InitialField(state.GrayScott)) };
        float[] original = state.GrayScott.Field.Concentrations.ToArray();
        var main = new Fractal3DWindow(kind); GrayScott3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main, "_suspended", true); main.LoadState(state);
            var editor = SimulationEditor<GrayScott3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy);
            Check(live.ResetPending, "Loading while suspended must remain queued.");
            Set(main, "_suspended", false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            Check(live.Ready && live.Shown!.Step == 7, "Resume must publish a loaded checkpoint without adding preparation steps.");
            E<TextBox>(editor, "GrayFeedBox").Text = ".09";
            grid = (GrayScott3DAtlasWindow)Invoke(main, "CreateGrayScottAtlas")!;
            var before = main.CaptureState("view");
            Check(before.GrayScott.Feed == state.GrayScott.Feed && !live.Running, "The atlas must capture applied coefficients and pause playback, preserving the equation draft.");
            E<TextBox>(grid, "TrialStepsBox").Text = "12";
            E<TextBox>(grid, "MinRealBox").Text = ".025"; E<TextBox>(grid, "MaxRealBox").Text = ".04";
            E<TextBox>(grid, "MinImaginaryBox").Text = ".05"; E<TextBox>(grid, "MaxImaginaryBox").Text = ".07";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel");
            await Wait(() => !grid.IsRendering || tiles.Children.OfType<Button>().Count(b => b.IsEnabled) >= 2);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2, "Independent GPU trials failed.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.GrayScott.Field?.Step == 19 && result.GrayScott.Live is null && result.GrayScott.Size == 32, "Selected results must contain exactly start step + trial steps at the original resolution.");
            using (var renderer = new Fractal3DRenderer())
            {
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations, 1, 32); p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source).SequenceEqual(reference.Buffer), "The transferred checkpoint must render exactly like the selected independent trial.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.GrayScott.Field!.Step == 19, "A later tile must restart from the common field instead of accumulating prior trials.");
            Click(ready[0]);
            var history = (List<GrayScott3DSettings>)editor.GetType().GetField("_searchHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            history.Add(before.GrayScott with { Feed = .0123 });
            Set(main, "_suspended", true); Click(E<Button>(grid, "ApplyButton"));
            await Wait(() => live.Settings.Field?.Step == 19); grid = null;
            Check(live.ResetPending && !live.Running && history.Count == 2, "Modal application must queue the exact result on pause and retain previous undo history.");
            Set(main, "_suspended", false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("view"); var expected = before.Clone(); expected.GrayScott = result.GrayScott; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Applying the trial changed its field, view settings or exact coefficients.");
            Check(state.GrayScott.Field!.Concentrations.SequenceEqual(original), "Comparisons mutated the initial field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            var restored = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.GrayScott.Field!;
            Check(restored.Step == 19 && restored.Concentrations.SequenceEqual(after.GrayScott.Field!.Concentrations), "Save/load lost exact trial concentrations or time.");
            Click(E<Button>(editor, "GraySearchUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo").GrayScott;
            Check(undone.Feed == before.GrayScott.Feed && undone.Field!.Step == 7 && undone.Field.Concentrations.SequenceEqual(before.GrayScott.Field!.Concentrations),
                "Undo must recover the original displayed field and coefficients exactly.");
            Click(E<Button>(editor, "GraySearchUndoButton")); Check(live.Settings.Feed == .0123, "Atlas application discarded earlier history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new GrayScott3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel, "TrialStepsBox").Text = "10000";
            var task = cancel.PrepareSelectionAsync(state); Check(cancel.IsApplying, "Preparation must expose a cancellable busy state.");
            Click(E<Button>(cancel, "CancelAtlasButton")); bool stopped = false;
            try { await task; } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !cancel.IsApplying && state.GrayScott.Field!.Concentrations.SequenceEqual(original), "Cancel must leave the source field intact and release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close();
            stopped = false; try { await task; } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Closing while preparing must cancel before any result is applied.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (gray-scott3d-atlas): common start/time, independent GPU trials, exact result transfer, modal resume, undo history, cancellation and saves.");
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static Task Display(Fractal3DWindow w) { var m = w.GetType().GetMethod("RenderFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!; return (Task)m.Invoke(w, [Enum.Parse(m.GetParameters()[0].ParameterType, "Full")])!; }
        static async Task Wait(Func<bool> condition) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < 90) await Task.Delay(15); Check(condition(), "Timed out awaiting atlas/simulation work."); }
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(GrayScott3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(GrayScott3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
