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
    private static async Task VerifyKobayashi3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Kobayashi3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Kobayashi3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,1,2,3 })
        {
            source.Kobayashi = source.Kobayashi with { InterfaceWidth = 5.0/6, Anisotropy = -.031234567890123456,
                TimeStep = .041234567890123456, SeedRadius = .07123456789012345, Undercooling = .5123456789012345, CutAxis = cut };
            var atlas = new Kobayashi3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Atlas rounded the equation or fixed seed/temperature settings.");
                Check(Boxes(atlas).Length == 8 + (cut > 0 ? 1 : 0) && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),
                    "Only evolution and active view parameters should appear, with validated duration.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    { var p = (decimal[])point.Clone(); p[i] = edge; target = source.Clone(); Apply(atlas,target,p); target.Kobayashi.Validate(); }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                foreach (var settings in new[] { source.Kobayashi with { Size = 32 }, source.Kobayashi with { Seed = source.Kobayashi.Seed + 1 },
                    source.Kobayashi with { Undercooling = .6 }, source.Kobayashi with { SeedRadius = .1 },
                    source.Kobayashi with { SeedSpread = .2 }, source.Kobayashi with { SeedCount = 7 }, source.Kobayashi with { CutAxis = (cut+1)%4 } })
                { var wrong = source.Clone(); wrong.Kobayashi = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "256","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Kobayashi atlas: five presets, exact doubles, active cuts, bounds, fixed seed/temperature and duration passed.");

        foreach (var seed in Enum.GetValues<Kobayashi3DSeed>())
        {
            var fresh = Small(seed); var atlas = new Kobayashi3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.Kobayashi.Field!.Step == 2,"Fresh trial must skip warmup.");
                Boxes(atlas)[1].Text = "2"; Boxes(atlas)[2].Text = "4"; Boxes(atlas)[3].Text = "4"; Boxes(atlas)[6].Text = ".1"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                Check(second.Kobayashi.EffectiveTimeStep < second.Kobayashi.TimeStep && second.Kobayashi.Field!.Step == 2,"CFL-limited trial accumulated steps or changed the model's time limit.");
                using var renderer = new Fractal3DRenderer();
                var common = new Kobayashi3DField(32,0,Kobayashi3DGpuSimulation.InitialField(fresh.Kobayashi));
                using var direct = new Kobayashi3DGpuSimulation(renderer.DeviceHost,second.Kobayashi with { Field = common });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.Kobayashi.Field!);
                Check(fresh.Kobayashi.Field is null && fresh.Kobayashi.InterfaceWidth == .6,"Trial mutated the original start.");
            }
            finally { atlas.Close(); }
        }

        var state = Small(Kobayashi3DSeed.RandomSpheres);
        state.Kobayashi = state.Kobayashi with { Field = new Kobayashi3DField(32,7,Kobayashi3DGpuSimulation.InitialField(state.Kobayashi)) };
        string original = JsonSerializer.Serialize(state.Kobayashi.Field);
        var main = new Fractal3DWindow(kind); Kobayashi3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<Kobayashi3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"KobColdBox").Text = "1.4";
            grid = (Kobayashi3DAtlasWindow)Invoke(main,"CreateKobayashiAtlas")!; var before = main.CaptureState("atlas");
            var pose = ((IFractal3DSimulationHost)main).Pose;
            Check(!live.Running && before.Kobayashi.Undercooling == state.Kobayashi.Undercooling,"Atlas must pause playback and ignore the unapplied draft.");
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<TextBox>(grid,"MinRealBox").Text = "-.06"; E<TextBox>(grid,"MaxRealBox").Text = ".06";
            E<TextBox>(grid,"MinImaginaryBox").Text = ".5"; E<TextBox>(grid,"MaxImaginaryBox").Text = "1.5";
            await Build(grid); var tiles = E<UniformGrid>(grid,"TilesPanel"); var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(ready.Length == 9,"Positive, zero and negative anisotropy trials must all finish.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.Kobayashi.Field!.Step == 10 && result.Kobayashi.Live is null,"Trial must add exactly N steps to the shown source.");
            using (var renderer = new Fractal3DRenderer())
            {
                using var direct = new Kobayashi3DGpuSimulation(renderer.DeviceHost,result.Kobayashi with { Field = before.Kobayashi.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.Kobayashi.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred φ/T must render exactly like the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.Kobayashi.Field!.Step == 10 && second.Kobayashi.Anisotropy == 0,"Independent isotropic trial used the wrong start or parameters.");
            Click(ready[0]);
            var history = (List<(Kobayashi3DSettings Settings, Fractal3DPose Pose)>)editor.GetType().GetField("_searchHistory",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            var oldPose = pose with { Distance = pose.Distance + 1 };
            history.Add((before.Kobayashi with { Anisotropy = .01234 },oldPose));
            Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running && history.Count == 2,"Modal application must queue a paused checkpoint and retain search history.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.Kobayashi = result.Kobayashi; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed coefficients, φ/T, step or view.");
            Check(E<TextBox>(editor,"KobColdBox").Text == state.Kobayashi.Undercooling.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded the fixed temperature coefficient.");
            Check(original == JsonSerializer.Serialize(state.Kobayashi.Field),"Trials mutated the original φ/T.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.Kobayashi.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Kobayashi.Field!);
            Click(E<Button>(editor,"KobSearchUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.Kobayashi.Field!,undone.Kobayashi.Field!);
            Check(undone.Kobayashi.Anisotropy == before.Kobayashi.Anisotropy && ((IFractal3DSimulationHost)main).Pose == pose,"Undo failed to recover the original equation and camera.");
            Click(E<Button>(editor,"KobSearchUndoButton"));
            Check(live.Settings.Anisotropy == .01234 && ((IFractal3DSimulationHost)main).Pose == oldPose && history.Count == 0,"Atlas discarded previous search history/camera.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new Kobayashi3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.Kobayashi.Field),"Cancel/close changed the starting φ/T.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (kobayashi3d-atlas): frozen φ/T for five seeds, CFL-limited short trials, both anisotropy signs and zero, exact transfer/render/save, undo/history/camera and cancellation.");

        static Fractal3DState Small(Kobayashi3DSeed seed)
        {
            var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Kobayashi3D);
            s.Kobayashi = s.Kobayashi with { Size = 32, SeedShape = seed, InterfaceWidth = .6, Undercooling = .5123456789012345, SeedRadius = .075, SeedSpread = .18, SeedCount = 4 };
            return s;
        }
        static void EqualField(Kobayashi3DField a,Kobayashi3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Concentrations.SequenceEqual(b.Concentrations),"Exact φ/T or step mismatch.");
        static async Task Cancelled(Task task) { bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; } Check(cancelled,"Operation must cancel before applying a result."); }
        static T E<T>(FrameworkElement e,string name) where T : class => (T)e.FindName(name);
        static void Set(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o,value);
        static object? Invoke(object o,string method,params object[] args) => o.GetType().GetMethod(method,BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null)!;
        static void ReadPoint(ParameterAtlasWindow a) => typeof(ParameterAtlasWindow).GetMethod("ReadPointFields",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null);
        static Task Display(Fractal3DWindow w) { var m = w.GetType().GetMethod("RenderFrameAsync",BindingFlags.Instance | BindingFlags.NonPublic)!; return (Task)m.Invoke(w,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!; }
        static async Task Wait(Func<bool> condition) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < 90) await Task.Delay(15); Check(condition(),"Timed out awaiting atlas/simulation work."); }
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a,"FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Kobayashi3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(Kobayashi3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
