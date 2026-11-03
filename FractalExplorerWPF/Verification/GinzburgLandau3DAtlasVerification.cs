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
    private static async Task VerifyGinzburgLandau3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("GinzburgLandau3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.GinzburgLandau3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,3 })
        foreach (var view in Enum.GetValues<GinzburgLandau3DView>())
        foreach (var boundary in Enum.GetValues<GinzburgLandau3DBoundary>())
        {
            source.GinzburgLandau = source.GinzburgLandau with { C1 = 5.0/6, C3 = -1.2123456789012345,
                Spacing = 5.0/6, TimeStep = .001234567890123456, Threshold = .15123456789012345,
                CutAxis = cut, CutPosition = .1234567890123456, View = view, Boundary = boundary };
            var atlas = new GinzburgLandau3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Atlas rounded exact coefficients or view settings.");
                Check(Boxes(atlas).Length == 7 + (cut > 0 ? 1 : 0) && Boxes(atlas).All(NumericInput.IsRegistered) &&
                    NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),"Only active cut should appear, with validated duration.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) { Reject(atlas,source.Clone(),p); continue; }
                        target = source.Clone(); Apply(atlas,target,p); target.GinzburgLandau.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                foreach (int i in new[] { 5,6 })
                { var p = (decimal[])point.Clone(); p[i] = .5m; Reject(atlas,source.Clone(),p); Check((bool)Invoke(atlas,"IsIntegerComponent",i)!,"Boundary/view samples must be discrete."); }
                Check(Invoke(atlas,"ComponentValueLabel",5,1m,"G")!.ToString() == "Периодические" &&
                    Invoke(atlas,"ComponentValueLabel",6,2m,"G")!.ToString() == "Амплитуда |A|","Discrete axes need readable names.");
                var cfl = (decimal[])point.Clone(); cfl[0] = 3; cfl[2] = .5m; cfl[3] = .05m;
                Check(Invoke(atlas,"UnavailableSample",cfl) is string,"Invalid complex diffusion CFL must disable the sample."); Reject(atlas,source.Clone(),cfl);
                cfl[0] = 2; cfl[2] = 1; cfl[3] = .03m;
                Check(Invoke(atlas,"UnavailableSample",cfl) is null,"Exact CFL boundary must remain valid."); Apply(atlas,source.Clone(),cfl);
                cfl[3] += .000000000001m; Reject(atlas,source.Clone(),cfl);
                foreach (var settings in new[] { source.GinzburgLandau with { Size = 32 }, source.GinzburgLandau with { Seed = source.GinzburgLandau.Seed + 1 },
                    source.GinzburgLandau with { SeedShape = GinzburgLandau3DSeed.Uniform }, source.GinzburgLandau with { CutAxis = (cut+1)%4 } })
                { var wrong = source.Clone(); wrong.GinzburgLandau = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "160","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Ginzburg–Landau atlas: six presets, three views, both boundaries, active cuts, exact coefficients, ranges/CFL and duration passed.");

        foreach (var seed in Enum.GetValues<GinzburgLandau3DSeed>())
        {
            var fresh = Small(seed); var atlas = new GinzburgLandau3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.GinzburgLandau.Field!.Step == 2,"Fresh trial must skip warmup.");
                Boxes(atlas)[0].Text = "-.75"; Boxes(atlas)[1].Text = "-1.25"; Boxes(atlas)[2].Text = ".5";
                Boxes(atlas)[3].Text = ".001"; Boxes(atlas)[5].Text = "1"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                using var renderer = new Fractal3DRenderer();
                var common = new GinzburgLandau3DField(32,0,GinzburgLandau3DGpuSimulation.InitialField(fresh.GinzburgLandau));
                using var direct = new GinzburgLandau3DGpuSimulation(renderer.DeviceHost,second.GinzburgLandau with { Field = common });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.GinzburgLandau.Field!);
                Check(second.GinzburgLandau.Field!.Step == 2 && fresh.GinzburgLandau.Field is null && fresh.GinzburgLandau.Spacing == 1,"Trial accumulated steps or rebuilt the original start at a different Δx.");
                var restored = JsonSerializer.Deserialize<GinzburgLandau3DSettings>(JsonSerializer.Serialize(second.GinzburgLandau))!;
                EqualField(second.GinzburgLandau.Field!,restored.Field!);
            }
            finally { atlas.Close(); }
        }

        var invalidAtlas = new GinzburgLandau3DAtlasWindow(Small(GinzburgLandau3DSeed.Ring));
        try
        {
            E<TextBox>(invalidAtlas,"TrialStepsBox").Text = "1";
            Boxes(invalidAtlas)[2].Text = ".5"; Boxes(invalidAtlas)[3].Text = ".05";
            Range(invalidAtlas,"1","3","-1","1"); await Build(invalidAtlas);
            Check(!invalidAtlas.IsRendering && !E<Button>(invalidAtlas,"ApplyButton").IsEnabled && Tiles(invalidAtlas).All(b => !b.IsEnabled),"All-invalid range must finish without an applicable selection.");
            Boxes(invalidAtlas)[2].Text = "1"; E<ComboBox>(invalidAtlas,"YAxisBox").SelectedIndex = 3;
            Range(invalidAtlas,"-3","3",".001",".03"); await Build(invalidAtlas);
            Check(Tiles(invalidAtlas).Count(b => b.IsEnabled) == 5 && Tiles(invalidAtlas).Count(b => !b.IsEnabled) == 4,"Mixed CFL range must disable only unstable cells.");
        }
        finally { invalidAtlas.Close(); }

        var state = Small(GinzburgLandau3DSeed.Random);
        state.GinzburgLandau = state.GinzburgLandau with { C1 = 5.0/6, C3 = -.7123456789012345, Spacing = 1.1234567890123456,
            CutAxis = 2, CutPosition = .1234567890123456, Threshold = .2345678901234567,
            Field = new GinzburgLandau3DField(32,7,GinzburgLandau3DGpuSimulation.InitialField(state.GinzburgLandau)) };
        string original = JsonSerializer.Serialize(state.GinzburgLandau.Field);
        Check(state.GinzburgLandau.Field.Values.Any(v => v < 0),"Test checkpoint must exercise signed complex components.");
        var main = new Fractal3DWindow(kind); GinzburgLandau3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<GinzburgLandau3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"GlC1Box").Text = "2.9"; E<ComboBox>(editor,"GlSeedBox").SelectedIndex = 5;
            grid = (GinzburgLandau3DAtlasWindow)Invoke(main,"CreateGinzburgLandauAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.GinzburgLandau.C1 == state.GinzburgLandau.C1 && before.GinzburgLandau.SeedShape == GinzburgLandau3DSeed.Random,"Atlas must pause and ignore unapplied equation/seed drafts.");
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 5; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 6;
            Boxes(grid)[3].Text = ".01"; Range(grid,"0","1","0","2");
            await Build(grid); var ready = Tiles(grid).Where(b => b.IsEnabled).ToArray();
            Check(E<UniformGrid>(grid,"TilesPanel").Columns == 2 && ready.Length == 6,"Boundary/view axes must create two unique columns and three rows.");
            Fractal3DState? result = null;
            foreach (int index in new[] { 0,2,4 })
            {
                Click(ready[index]); result = await grid.PrepareSelectionAsync(before);
                Check(result.GinzburgLandau.Field!.Step == 10 && result.GinzburgLandau.Live is null && result.GinzburgLandau.TimeStep == .01,"Trial must add N steps and retain the selected Δt.");
                using var renderer = new Fractal3DRenderer();
                using var direct = new GinzburgLandau3DGpuSimulation(renderer.DeviceHost,result.GinzburgLandau with { Field = before.GinzburgLandau.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.GinzburgLandau.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred Re/Im must render exactly like the selected tile in all three views.");
            }
            Click(ready[5]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.GinzburgLandau.Field!.Step == 10 && second.GinzburgLandau.Boundary == GinzburgLandau3DBoundary.Periodic,"Independent periodic trial used the wrong start or boundary.");
            Click(ready[4]); Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running,"Modal application must queue a paused checkpoint.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.GinzburgLandau = result!.GinzburgLandau; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact parameters, Re/Im, step or view.");
            Check(E<TextBox>(editor,"GlC1Box").Text == (5.0/6).ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded a fractional coefficient.");
            Check(original == JsonSerializer.Serialize(state.GinzburgLandau.Field),"Trials mutated the original complex field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.GinzburgLandau.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.GinzburgLandau.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.GinzburgLandau.Field!,undone.GinzburgLandau.Field!);
            Check(undone.GinzburgLandau.TimeStep == before.GinzburgLandau.TimeStep && undone.GinzburgLandau.View == before.GinzburgLandau.View && !E<Button>(editor,"AtlasUndoButton").IsEnabled,"Undo failed to recover the original equation/view and consume history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new GinzburgLandau3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.GinzburgLandau.Field),"Cancel/close changed the starting Re/Im.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (ginzburg-landau3d-atlas): six frozen seeds, CFL ranges, boundary/view axes, exact complex transfer/render/save in three views, paused apply/undo and cancellation.");

        static Fractal3DState Small(GinzburgLandau3DSeed seed)
        { var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.GinzburgLandau3D); s.GinzburgLandau = s.GinzburgLandau with { Size = 32, SeedShape = seed }; return s; }
        static void EqualField(GinzburgLandau3DField a,GinzburgLandau3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Components.SequenceEqual(b.Components),"Exact signed Re/Im or step mismatch.");
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
        static void Apply(GinzburgLandau3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(GinzburgLandau3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
