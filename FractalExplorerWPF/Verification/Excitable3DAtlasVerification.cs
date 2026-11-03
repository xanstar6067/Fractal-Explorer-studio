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
    private static async Task VerifyExcitable3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Excitable3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Excitable3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,2 })
        foreach (var view in Enum.GetValues<Excitable3DView>())
        {
            source.Excitable = source.Excitable with { A = source.Excitable.A + .000123456789012345,
                B = source.Excitable.B + .000012345678901234, Epsilon = .22123456789012345, Mu1 = .21234567890123456,
                Mu2 = .31234567890123456, Spacing = 5.0/6, TimeStep = .001234567890123456, CutAxis = cut, View = view };
            var atlas = new Excitable3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Atlas rounded coefficients, including inactive μ values.");
                Check(Boxes(atlas).Length == 10 + (source.Excitable.Model == Excitable3DModel.AlievPanfilov ? 2 : 0) + (cut > 0 ? 1 : 0) &&
                    Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),"Only active μ/cut should appear, with validated duration.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) { Reject(atlas,source.Clone(),p); continue; }
                        target = source.Clone(); Apply(atlas,target,p); target.Excitable.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                foreach (int i in new[] { 8,9 })
                { var p = (decimal[])point.Clone(); p[i] = .5m; Reject(atlas,source.Clone(),p); Check((bool)Invoke(atlas,"IsIntegerComponent",i)!,"Boundary/view samples must be discrete."); }
                Check(Invoke(atlas,"ComponentValueLabel",8,1m,"G")!.ToString() == "Периодические" && Invoke(atlas,"ComponentValueLabel",9,2m,"G")!.ToString() == "Волны и нити","Discrete axes need readable labels.");
                var cfl = (decimal[])point.Clone(); cfl[2] = .5m; cfl[3] = 2; cfl[5] = .25m; cfl[6] = .02m;
                Check(Invoke(atlas,"UnavailableSample",cfl) is string,"Invalid diffusion CFL must disable the sample."); Reject(atlas,source.Clone(),cfl);
                var stiff = (decimal[])point.Clone(); stiff[2] = .005m; stiff[6] = .02m;
                Check((Invoke(atlas,"UnavailableSample",stiff) is not null) == (source.Excitable.Model != Excitable3DModel.AlievPanfilov),"Reaction time bound must follow the selected model.");
                foreach (var settings in new[] { source.Excitable with { Size = 32 }, source.Excitable with { Model = (Excitable3DModel)(((int)source.Excitable.Model+1)%4) },
                    source.Excitable with { SeedShape = Excitable3DSeed.Rest }, source.Excitable with { CutAxis = (cut+1)%4 } })
                { var wrong = source.Clone(); wrong.Excitable = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "240","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Excitable atlas: seven presets, four models, active μ/cuts, exact coefficients, bounds, reaction/CFL rejection and named boundary/view passed.");

        foreach (var model in Enum.GetValues<Excitable3DModel>())
        foreach (var seed in Enum.GetValues<Excitable3DSeed>())
        {
            var fresh = Small(model,seed); var atlas = new Excitable3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.Excitable.Field!.Step == 2,"Fresh trial must skip warmup.");
                var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",0)!;
                Boxes(atlas)[0].Text = ((bounds.Minimum+bounds.Maximum)/2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Boxes(atlas)[6].Text = ".001"; Boxes(atlas)[8].Text = "1"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                using var renderer = new Fractal3DRenderer();
                var common = new Excitable3DField(32,0,Excitable3DGpuSimulation.InitialField(fresh.Excitable));
                using var direct = new Excitable3DGpuSimulation(renderer.DeviceHost,second.Excitable with { Field = common });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.Excitable.Field!);
                Check(second.Excitable.Field!.Step == 2 && fresh.Excitable.Field is null,"Trial accumulated steps or mutated the original start.");
                var restored = JsonSerializer.Deserialize<Excitable3DSettings>(JsonSerializer.Serialize(second.Excitable))!;
                EqualField(second.Excitable.Field!,restored.Field!);
            }
            finally { atlas.Close(); }
        }

        var invalidAtlas = new Excitable3DAtlasWindow(Small(Excitable3DModel.Barkley,Excitable3DSeed.Scroll));
        try
        {
            E<TextBox>(invalidAtlas,"TrialStepsBox").Text = "1";
            E<ComboBox>(invalidAtlas,"XAxisBox").SelectedIndex = 2; E<ComboBox>(invalidAtlas,"YAxisBox").SelectedIndex = 6;
            Range(invalidAtlas,".005",".01",".01",".02"); await Build(invalidAtlas);
            Check(!invalidAtlas.IsRendering && !E<Button>(invalidAtlas,"ApplyButton").IsEnabled && Tiles(invalidAtlas).All(b => !b.IsEnabled),"All-invalid range must finish without an applicable selection.");
            Range(invalidAtlas,".005",".045",".001",".01"); await Build(invalidAtlas);
            Check(Tiles(invalidAtlas).Count(b => b.IsEnabled) == 6 && Tiles(invalidAtlas).Count(b => !b.IsEnabled) == 3,"Mixed reaction bounds must disable only invalid cells.");
        }
        finally { invalidAtlas.Close(); }

        var state = Small(Excitable3DModel.FitzHughNagumo,Excitable3DSeed.CrossedScrolls);
        state.Excitable = state.Excitable with { A = .5812345678901234, CutAxis = 2, CutPosition = .1234567890123456,
            Field = new Excitable3DField(32,7,Excitable3DGpuSimulation.InitialField(state.Excitable)) };
        string original = JsonSerializer.Serialize(state.Excitable.Field);
        var main = new Fractal3DWindow(kind); Excitable3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<Excitable3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<ComboBox>(editor,"ExModelBox").SelectedIndex = 3; E<TextBox>(editor,"ExABox").Text = ".25";
            grid = (Excitable3DAtlasWindow)Invoke(main,"CreateExcitableAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.Excitable.Model == Excitable3DModel.FitzHughNagumo && before.Excitable.A == state.Excitable.A,"Atlas must pause and ignore the unapplied model/equation draft.");
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 8; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 9;
            Boxes(grid)[6].Text = ".005"; Range(grid,"0","1","0","2");
            await Build(grid); var ready = Tiles(grid).Where(b => b.IsEnabled).ToArray();
            Check(E<UniformGrid>(grid,"TilesPanel").Columns == 2 && ready.Length == 6,"Boundary/view axes must create two unique columns and three rows.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.Excitable.Field!.Step == 10 && result.Excitable.Live is null && result.Excitable.TimeStep == .005,"Trial must add N steps and retain the selected Δt.");
            using (var renderer = new Fractal3DRenderer())
            {
                using var direct = new Excitable3DGpuSimulation(renderer.DeviceHost,result.Excitable with { Field = before.Excitable.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.Excitable.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred U/V must render exactly like the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.Excitable.Field!.Step == 10 && second.Excitable.Boundary == Excitable3DBoundary.Periodic,"Independent periodic trial used the wrong start or boundary.");
            Click(ready[0]); Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running,"Modal application must queue a paused checkpoint.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.Excitable = result.Excitable; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact parameters, signed U/V, step or view.");
            Check(E<TextBox>(editor,"ExABox").Text == state.Excitable.A.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded a coefficient.");
            Check(original == JsonSerializer.Serialize(state.Excitable.Field),"Trials mutated the original U/V.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.Excitable.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Excitable.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.Excitable.Field!,undone.Excitable.Field!);
            Check(undone.Excitable.TimeStep == before.Excitable.TimeStep && undone.Excitable.View == before.Excitable.View && !E<Button>(editor,"AtlasUndoButton").IsEnabled,"Undo failed to recover the original rule/view and consume history.");
            E<TextBox>(editor,"ExMu1Box").Text = "invalid hidden μ"; E<TextBox>(editor,"ExWarmupBox").Text = "0";
            Invoke(editor,"ExRestart_OnClick",editor,new RoutedEventArgs());
            Check(live.Settings.Field is null && live.Settings.WarmupSteps == 0 && live.Settings.Mu1 == before.Excitable.Mu1,"Inactive μ must not block explicit application or lose its applied value.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new Excitable3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.Excitable.Field),"Cancel/close changed the starting U/V.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (excitable3d-atlas): four models/five frozen seeds, valid/invalid reaction-CFL ranges, boundary/view grid, exact signed U/V transfer/render/save, paused apply/undo, inactive μ and cancellation.");

        static Fractal3DState Small(Excitable3DModel model,Excitable3DSeed seed)
        { var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Excitable3D); s.Excitable = Excitable3DSettings.Default(model) with { Size = 32, SeedShape = seed }; return s; }
        static void EqualField(Excitable3DField a,Excitable3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Concentrations.SequenceEqual(b.Concentrations),"Exact signed U/V or step mismatch.");
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
        static Button[] Tiles(ParameterAtlasWindow a) => E<UniformGrid>(a,"TilesPanel").Children.OfType<Button>().ToArray();
        static void Range(ParameterAtlasWindow a,string x0,string x1,string y0,string y1) { E<TextBox>(a,"MinRealBox").Text = x0; E<TextBox>(a,"MaxRealBox").Text = x1; E<TextBox>(a,"MinImaginaryBox").Text = y0; E<TextBox>(a,"MaxImaginaryBox").Text = y1; }
        static void Apply(Excitable3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(Excitable3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
