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
    private static async Task VerifyTuring3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Turing3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Turing3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind).Concat(Enum.GetValues<TuringReactionModel>().Select(model =>
        {
            var s = Fractal3DCatalog.CreateDefaultState(kind);
            s.Turing = s.Turing with { Reaction = TuringReactionSettings.Default(model), Region = Turing3DRegion.Shell,
                Symmetry = Turing3DSymmetry.Axial, SheetThickness = .08123456789012345, CutAxis = 2,
                Layers = [new(true, 2.5123456789012345, .012345678901234567), new(false, 5, .02)] };
            return s;
        })))
        {
            source.Turing = source.Turing with { DetailSize = 1.1234567890123456, Level = .5123456789012345 };
            var atlas = new Turing3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Turing mapping rounded exact coefficients or replaced layers.");
                int count = 3 + (source.Turing.Reaction.IsClassical ? 4 : 1 + source.Turing.Layers.Count(l => l.Enabled) * 2)
                    + (source.Turing.Region == Turing3DRegion.Shell ? 1 : 0) + (source.Turing.Symmetry == Turing3DSymmetry.Axial ? 1 : 0)
                    + (source.Turing.SheetThickness > 0 ? 1 : 0) + (source.Turing.CutAxis > 0 ? 1 : 0);
                Check(Boxes(atlas).Length == count && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),
                    "Only active model/shape parameters and a validated trial duration should be exposed.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) continue;
                        target = source.Clone(); Apply(atlas,target,p); target.Turing.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                    if ((bool)Invoke(atlas,"IsIntegerComponent",i)!) { invalid[i] = 1.5m; Reject(atlas,source.Clone(),invalid); }
                }
                var wrong = source.Clone(); wrong.Turing = wrong.Turing with { Seed = wrong.Turing.Seed + 1 }; Reject(atlas,wrong,point);
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == (source.Turing.Reaction.IsClassical ? "40" : "120"), "Trial duration fallback failed.");
                if (source.Turing.Reaction.Model == TuringReactionModel.Brusselator)
                {
                    var invalid = (decimal[])point.Clone(); invalid[0] = .001m; invalid[1] = 8;
                    Check(Invoke(atlas,"UnavailableSample",invalid) is string, "Excess equilibrium must disable the sample before GPU work."); Reject(atlas,source.Clone(),invalid);
                }
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Turing atlas: presets, four models, active scales, exact values, bounds and equilibrium constraints passed.");

        // Two short trials per reaction verify that the uncheckpointed start is frozen before changing A/B.
        foreach (var model in Enum.GetValues<TuringReactionModel>())
        {
            var state = Small(model); var atlas = new Turing3DAtlasWindow(state);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var p = Selected(atlas); p[0] += .01m; SetPoint(atlas,p);
                var result = await atlas.PrepareSelectionAsync(state);
                Check(result.Turing.Field?.Step == 2 && result.Turing.Field.Model == model && result.Turing.Live is null, "Trial lost its reaction, resolution or time.");
                using var host = new Fractal3DRenderer(); using var direct = new Turing3DGpuSimulation(host.DeviceHost,state.Turing);
                var start = direct.ReadCheckpoint(direct.Publish());
                direct.Reset(result.Turing with { Field = start }); direct.Advance(2,CancellationToken.None);
                var reference = direct.ReadCheckpoint(direct.Publish()); EqualField(reference,result.Turing.Field!);
                var repeated = await atlas.PrepareSelectionAsync(state); EqualField(result.Turing.Field!,repeated.Turing.Field!);
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(result);
                var restored = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Turing;
                EqualField(result.Turing.Field!,restored.Field!); Check(restored.Reaction == result.Turing.Reaction,"Save/load changed reaction coefficients.");
            }
            finally { atlas.Close(); }
        }

        var initial = Small(TuringReactionModel.McCabe);
        using (var host = new Fractal3DRenderer())
        using (var model = new Turing3DGpuSimulation(host.DeviceHost,initial.Turing))
        {
            model.Advance(1,CancellationToken.None); initial.Turing = initial.Turing with { Field = model.ReadCheckpoint(model.Publish()) };
        }
        var original = JsonSerializer.Serialize(initial.Turing.Field);
        var main = new Fractal3DWindow(kind); Turing3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(initial);
            var editor = SimulationEditor<Turing3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            // An unapplied layer draft must not change the rule used by the atlas.
            var layerRows = (System.Collections.IEnumerable)editor.GetType().GetField("_layerRows",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            var first = layerRows.Cast<object>().First(); ((TextBox)first.GetType().GetField("Item2")!.GetValue(first)!).Text = "63";
            grid = (Turing3DAtlasWindow)Invoke(main,"CreateTuringAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.Turing.Layers[0].Radius == initial.Turing.Layers[0].Radius, "Atlas must use the applied rule and pause the source.");
            E<TextBox>(grid,"TrialStepsBox").Text = "2";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid,"TilesPanel");
            await Wait(() => !grid.IsRendering || tiles.Children.OfType<Button>().Count(b => b.IsEnabled) >= 2);
            Click(E<Button>(grid,"CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2,"GPU tile trials failed.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.Turing.Field!.Step == 3,"Selected checkpoint must add exactly the requested steps to the shown source.");
            using (var renderer = new Fractal3DRenderer())
            {
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred field does not match the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before); Check(second.Turing.Field!.Step == 3,"Trials must not accumulate time."); Click(ready[0]);
            Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 3); grid = null;
            Check(live.ResetPending && !live.Running,"Application from a modal atlas must queue the checkpoint on pause.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.Turing = result.Turing; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact parameters, time or field.");
            Check(original == JsonSerializer.Serialize(initial.Turing.Field),"Trials mutated the original field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.Turing.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Turing.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.Turing.Field!,undone.Turing.Field!);
            Check(undone.Turing.Layers.SequenceEqual(before.Turing.Layers) && undone.Turing.DetailSize == before.Turing.DetailSize && !E<Button>(editor,"AtlasUndoButton").IsEnabled,
                "Undo failed to restore the applied rule or empty history state.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new Turing3DAtlasWindow(initial);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(initial);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(initial); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(initial.Turing.Field),"Cancel/close changed the source.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (turing3d-atlas): common initial fields for all reactions, independent short trials, exact transfer/render/save, modal resume, undo and cancellation.");

        static Fractal3DState Small(TuringReactionModel model)
        {
            var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Turing3D); s.ShadingStyle = Fractal3DShadingStyle.Density;
            s.Turing = s.Turing with { Size = 32, WarmupSteps = 0, Symmetry = Turing3DSymmetry.None, Region = Turing3DRegion.Cube,
                Reaction = TuringReactionSettings.Default(model), Layers = [new(true,2.5123456789012345,.012345678901234567)], Level = .5123456789012345 };
            return s;
        }
        static void EqualField(Turing3DField a,Turing3DField b) => Check(a.Model == b.Model && a.Size == b.Size && a.Step == b.Step && a.Field.SequenceEqual(b.Field) &&
            a.ScaleMap.SequenceEqual(b.ScaleMap) && a.ConcentrationU.SequenceEqual(b.ConcentrationU) && a.ConcentrationV.SequenceEqual(b.ConcentrationV),"Exact field/scale/U/V checkpoint mismatch.");
        static async Task Cancelled(Task task) { bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; } Check(cancelled,"Operation must cancel before applying a result."); }
        static T E<T>(FrameworkElement e,string name) where T : class => (T)e.FindName(name);
        static void Set(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o,value);
        static object? Invoke(object o,string method,params object[] args) => o.GetType().GetMethod(method,BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null)!;
        static Task Display(Fractal3DWindow w) { var m = w.GetType().GetMethod("RenderFrameAsync",BindingFlags.Instance | BindingFlags.NonPublic)!; return (Task)m.Invoke(w,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!; }
        static async Task Wait(Func<bool> condition) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < 90) await Task.Delay(15); Check(condition(),"Timed out awaiting atlas/simulation work."); }
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a,"FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void SetPoint(ParameterAtlasWindow a,decimal[] p)
        {
            var boxes = Boxes(a); for (int i = 0; i < p.Length; i++) boxes[i].Text = p[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            typeof(ParameterAtlasWindow).GetMethod("ReadPointFields",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null);
        }
        static void Apply(Turing3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(Turing3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
