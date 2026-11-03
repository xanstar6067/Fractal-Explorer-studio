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
    private static async Task VerifyPhysarum3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Physarum3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Physarum3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,2 })
        {
            source.Physarum = source.Physarum with { SensorDistance = 2.1234567890123456, SensorAngle = 40.123456789012345,
                TurnAngle = 22.123456789012345, Speed = .8123456789012345, Deposit = 2.1234567890123456,
                Diffusion = .12345678901234567, Decay = .025123456789012345, Exposure = .12345678901234567,
                Threshold = .35123456789012345, CutAxis = cut };
            var atlas = new Physarum3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Mapping rounded parameters or replaced the starting network.");
                Check(Boxes(atlas).Length == (cut == 0 ? 9 : 10) && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),
                    "All equation/view fields and duration must use NumericInput; inactive cuts must be absent.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    { var p = (decimal[])point.Clone(); p[i] = edge; target = source.Clone(); Apply(atlas,target,p); target.Physarum.Validate(); }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                foreach (var settings in new[] { source.Physarum with { Size = 48 }, source.Physarum with { AgentCount = 1024 },
                    source.Physarum with { Seed = source.Physarum.Seed + 1 }, source.Physarum with { SeedShape = (Physarum3DSeed)(((int)source.Physarum.SeedShape + 1) % 4) } })
                { var wrong = source.Clone(); wrong.Physarum = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "80","Invalid duration must recover without starting work.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Physarum atlas: presets, active cut, exact parameters, bounds, fixed population and validated duration passed.");

        foreach (var shape in Enum.GetValues<Physarum3DSeed>())
        {
            var fresh = Small(shape); var atlas = new Physarum3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.Physarum.Field!.Step == 2 && first.Physarum.Field.AgentCount == 1024 && first.Physarum.Size == 48,"Fresh trials must skip warmup and keep full population/resolution.");
                Boxes(atlas)[3].Text = "1.25";
                typeof(ParameterAtlasWindow).GetMethod("ReadPointFields",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas,null);
                var second = await atlas.PrepareSelectionAsync(fresh);
                using var renderer = new Fractal3DRenderer(); using var direct = new Physarum3DGpuSimulation(renderer.DeviceHost,second.Physarum with { Field = null });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.Physarum.Field!);
                Check(fresh.Physarum.Field is null && fresh.Physarum.Speed == .8123456789012345,"Trials mutated the initial settings.");
            }
            finally { atlas.Close(); }
        }

        var state = Small(Physarum3DSeed.TwinStars);
        using (var renderer = new Fractal3DRenderer())
        using (var simulation = new Physarum3DGpuSimulation(renderer.DeviceHost,state.Physarum))
        { simulation.Advance(3,CancellationToken.None); state.Physarum = state.Physarum with { Field = simulation.ReadCheckpoint(simulation.Publish()) }; }
        string original = JsonSerializer.Serialize(state.Physarum.Field);
        var main = new Fractal3DWindow(kind); Physarum3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<Physarum3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"PhysarumSensorBox").Text = "15";
            grid = (Physarum3DAtlasWindow)Invoke(main,"CreatePhysarumAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.Physarum.SensorDistance == state.Physarum.SensorDistance,"Atlas must pause playback and use the applied rule, preserving the equation draft.");
            E<TextBox>(grid,"TrialStepsBox").Text = "4";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid,"TilesPanel");
            await Wait(() => !grid.IsRendering || tiles.Children.OfType<Button>().Count(b => b.IsEnabled) >= 2);
            Click(E<Button>(grid,"CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2,"Independent GPU trials failed.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.Physarum.Field?.Step == 7 && result.Physarum.Live is null,"Selected checkpoint must contain exactly the source step plus N trial steps.");
            using (var renderer = new Fractal3DRenderer())
            {
                using var direct = new Physarum3DGpuSimulation(renderer.DeviceHost,result.Physarum with { Field = before.Physarum.Field });
                direct.Advance(4,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.Physarum.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred field must render exactly like the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.Physarum.Field!.Step == 7,"Later trials accumulated earlier steps."); Click(ready[0]);
            var history = (List<Physarum3DSettings>)editor.GetType().GetField("_searchHistory",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            history.Add(before.Physarum with { SensorDistance = 1.25 });
            Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 7); grid = null;
            Check(live.ResetPending && !live.Running && history.Count == 2,"Modal application must queue a paused checkpoint and retain earlier search history.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.Physarum = result.Physarum; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed parameters, step, trail or exact agents.");
            Check(E<TextBox>(editor,"PhysarumMoveBox").Text == after.Physarum.Speed.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded a loaded atlas coefficient.");
            Check(original == JsonSerializer.Serialize(state.Physarum.Field),"Comparisons mutated the starting trail or agents.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.Physarum.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Physarum.Field!);
            Click(E<Button>(editor,"PhysarumSearchUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.Physarum.Field!,undone.Physarum.Field!);
            Check(undone.Physarum.SensorDistance == before.Physarum.SensorDistance,"Undo failed to recover the original rule.");
            Click(E<Button>(editor,"PhysarumSearchUndoButton")); Check(live.Settings.SensorDistance == 1.25 && history.Count == 0,"Atlas discarded earlier search history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new Physarum3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.Physarum.Field),"Cancel/close changed the starting network.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (physarum3d-atlas): four starts, common trail/agents, independent short trials, exact transfer/render/save, modal resume, search history, undo and cancellation.");

        static Fractal3DState Small(Physarum3DSeed shape)
        {
            var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Physarum3D);
            s.Physarum = s.Physarum with { Size = 48, AgentCount = 1024, SeedShape = shape, Speed = .8123456789012345, Decay = .025123456789012345, CutAxis = 2 };
            return s;
        }
        static void EqualField(Physarum3DField a,Physarum3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.AgentCount == b.AgentCount && a.Trail.SequenceEqual(b.Trail) && a.AgentState.SequenceEqual(b.AgentState),"Exact trail, positions, headings or step mismatch.");
        static async Task Cancelled(Task task) { bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; } Check(cancelled,"Operation must cancel before applying a result."); }
        static T E<T>(FrameworkElement e,string name) where T : class => (T)e.FindName(name);
        static void Set(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o,value);
        static object? Invoke(object o,string method,params object[] args) => o.GetType().GetMethod(method,BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null)!;
        static Task Display(Fractal3DWindow w) { var m = w.GetType().GetMethod("RenderFrameAsync",BindingFlags.Instance | BindingFlags.NonPublic)!; return (Task)m.Invoke(w,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!; }
        static async Task Wait(Func<bool> condition) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < 90) await Task.Delay(15); Check(condition(),"Timed out awaiting atlas/simulation work."); }
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a,"FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Physarum3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(Physarum3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
