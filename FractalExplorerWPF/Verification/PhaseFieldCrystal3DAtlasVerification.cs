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
    private static async Task VerifyPhaseFieldCrystal3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("PhaseFieldCrystal3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.PhaseFieldCrystal3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,3 })
        foreach (var view in Enum.GetValues<PhaseFieldCrystal3DView>())
        {
            source.PhaseFieldCrystal = source.PhaseFieldCrystal with { Undercooling = .35123456789012345, Mean = -.22123456789012345,
                WaveNumber = 5.0/6, Mobility = 5.0/6, Spacing = 1.1234567890123456, TimeStep = .021234567890123456,
                Noise = .01234567890123456, DisplayRange = 1.5123456789012345, Threshold = .35123456789012345,
                CutAxis = cut, CutPosition = .1234567890123456, View = view };
            var atlas = new PhaseFieldCrystal3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Atlas rounded coefficients, fixed mean or view settings.");
                Check(Boxes(atlas).Length == 8 + (cut > 0 ? 1 : 0) && Boxes(atlas).All(NumericInput.IsRegistered) &&
                    NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),"Mean must remain fixed; only active cut should appear, with validated duration.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) { Reject(atlas,source.Clone(),p); continue; }
                        target = source.Clone(); Apply(atlas,target,p); target.PhaseFieldCrystal.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                var fractional = (decimal[])point.Clone(); fractional[7] = .5m; Reject(atlas,source.Clone(),fractional);
                Check((bool)Invoke(atlas,"IsIntegerComponent",7)! && Invoke(atlas,"ComponentValueLabel",7,2m,"G")!.ToString() == "Местная модуляция","Views need distinct integer samples and readable names.");
                var coupled = (decimal[])point.Clone(); coupled[1] = 1.4m; coupled[3] = 1.5m;
                Check(Invoke(atlas,"UnavailableSample",coupled) is string,"Band violation must disable the sample."); Reject(atlas,source.Clone(),coupled);
                coupled[1] = 1.2m; coupled[3] = 1; coupled[2] = 1; coupled[4] = .03m;
                Check(Invoke(atlas,"UnavailableSample",coupled) is null,"Both exact constraint boundaries must remain valid."); Apply(atlas,source.Clone(),coupled);
                coupled[1] += .000000000002m; Reject(atlas,source.Clone(),coupled);
                coupled[1] = 1; coupled[2] = 2; coupled[4] = .03m;
                Check(Invoke(atlas,"UnavailableSample",coupled) is string,"M·Δt violation must disable the sample."); Reject(atlas,source.Clone(),coupled);
                foreach (var settings in new[] { source.PhaseFieldCrystal with { Size = 32 }, source.PhaseFieldCrystal with { Mean = -.2 },
                    source.PhaseFieldCrystal with { Noise = .1 }, source.PhaseFieldCrystal with { Seed = source.PhaseFieldCrystal.Seed + 1 },
                    source.PhaseFieldCrystal with { SeedShape = PhaseFieldCrystal3DSeed.Liquid }, source.PhaseFieldCrystal with { CutAxis = (cut+1)%4 } })
                { var wrong = source.Clone(); wrong.PhaseFieldCrystal = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "48","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("PFC atlas: eight presets, fixed exact mean, three views, active cuts, band/M·Δt limits and duration passed.");

        foreach (var seed in Enum.GetValues<PhaseFieldCrystal3DSeed>())
        {
            var fresh = Small(seed); var atlas = new PhaseFieldCrystal3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.PhaseFieldCrystal.Field!.Step == 2,"Fresh trial must skip warmup.");
                Boxes(atlas)[0].Text = "-.1"; Boxes(atlas)[1].Text = ".6"; Boxes(atlas)[2].Text = "2";
                Boxes(atlas)[3].Text = ".5"; Boxes(atlas)[4].Text = ".01"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                using var renderer = new Fractal3DRenderer();
                using var direct = new PhaseFieldCrystal3DGpuSimulation(renderer.DeviceHost,fresh.PhaseFieldCrystal);
                var common = direct.ReadCheckpoint(direct.Publish());
                direct.Reset(first.PhaseFieldCrystal with { Field = common }); direct.Advance(2,CancellationToken.None);
                EqualField(direct.ReadCheckpoint(direct.Publish()),first.PhaseFieldCrystal.Field!);
                direct.Reset(second.PhaseFieldCrystal with { Field = common }); direct.Advance(2,CancellationToken.None);
                EqualField(direct.ReadCheckpoint(direct.Publish()),second.PhaseFieldCrystal.Field!);
                Check(second.PhaseFieldCrystal.Field!.Step == 2 && second.PhaseFieldCrystal.Field.Mean == common.Mean && fresh.PhaseFieldCrystal.Field is null,
                    "Trial changed the common mean, accumulated steps or mutated the original start.");
                CheckMass(second.PhaseFieldCrystal.Field);
                var restored = JsonSerializer.Deserialize<PhaseFieldCrystal3DSettings>(JsonSerializer.Serialize(second.PhaseFieldCrystal))!;
                EqualField(second.PhaseFieldCrystal.Field!,restored.Field!);
            }
            finally { atlas.Close(); }
        }

        var invalidAtlas = new PhaseFieldCrystal3DAtlasWindow(Small(PhaseFieldCrystal3DSeed.Bcc));
        try
        {
            E<TextBox>(invalidAtlas,"TrialStepsBox").Text = "1";
            E<ComboBox>(invalidAtlas,"XAxisBox").SelectedIndex = 1; E<ComboBox>(invalidAtlas,"YAxisBox").SelectedIndex = 3;
            Range(invalidAtlas,"1.2","1.4","1.1","1.5"); await Build(invalidAtlas); AllInvalid(invalidAtlas);
            Range(invalidAtlas,".6","1.4",".5","1.5"); await Build(invalidAtlas); Mixed(invalidAtlas);
            E<ComboBox>(invalidAtlas,"XAxisBox").SelectedIndex = 2; E<ComboBox>(invalidAtlas,"YAxisBox").SelectedIndex = 4;
            Boxes(invalidAtlas)[1].Text = "1"; Boxes(invalidAtlas)[3].Text = "1";
            Range(invalidAtlas,"1.5","2",".025",".03"); await Build(invalidAtlas); AllInvalid(invalidAtlas);
            Range(invalidAtlas,".5","2",".01",".03"); await Build(invalidAtlas); Mixed(invalidAtlas);
        }
        finally { invalidAtlas.Close(); }

        var state = Small(PhaseFieldCrystal3DSeed.TwoGrains);
        state.PhaseFieldCrystal = state.PhaseFieldCrystal with { WaveNumber = 5.0/6, Mobility = 5.0/6,
            Spacing = 1.1234567890123456, CutAxis = 2, CutPosition = .1234567890123456,
            DisplayRange = 1.5123456789012345, Threshold = .2345678901234567 };
        using (var renderer = new Fractal3DRenderer())
        using (var sourceGpu = new PhaseFieldCrystal3DGpuSimulation(renderer.DeviceHost,state.PhaseFieldCrystal with { Mean = state.PhaseFieldCrystal.Mean + 1e-7 }))
        {
            sourceGpu.Advance(7,CancellationToken.None);
            // Existing files may have a tiny permitted mismatch between the parameter and the exact zero mode.
            state.PhaseFieldCrystal = state.PhaseFieldCrystal with { Field = sourceGpu.ReadCheckpoint(sourceGpu.Publish()) };
        }
        state.PhaseFieldCrystal.Validate(); string original = JsonSerializer.Serialize(state.PhaseFieldCrystal.Field);
        var main = new Fractal3DWindow(kind); PhaseFieldCrystal3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<PhaseFieldCrystal3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"PfcMeanBox").Text = ".4"; E<ComboBox>(editor,"PfcSeedBox").SelectedIndex = 7;
            grid = (PhaseFieldCrystal3DAtlasWindow)Invoke(main,"CreatePhaseFieldCrystalAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.PhaseFieldCrystal.Mean == state.PhaseFieldCrystal.Mean && before.PhaseFieldCrystal.SeedShape == PhaseFieldCrystal3DSeed.TwoGrains,"Atlas must pause and ignore unapplied mean/seed drafts.");
            EqualField(state.PhaseFieldCrystal.Field!,before.PhaseFieldCrystal.Field!);
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 7; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 5;
            Boxes(grid)[4].Text = ".01"; Range(grid,"0","2","1","2");
            await Build(grid); var ready = Tiles(grid).Where(b => b.IsEnabled).ToArray();
            Check(E<UniformGrid>(grid,"TilesPanel").Columns == 3 && ready.Length == 9,"View axis must create three unique columns.");
            Fractal3DState? result = null;
            foreach (int index in new[] { 0,1,2 })
            {
                Click(ready[index]); result = await grid.PrepareSelectionAsync(before);
                Check(result.PhaseFieldCrystal.Field!.Step == 10 && result.PhaseFieldCrystal.Live is null && result.PhaseFieldCrystal.TimeStep == .01 &&
                    result.PhaseFieldCrystal.Field.Mean == before.PhaseFieldCrystal.Field!.Mean,"Trial must add N steps and preserve the exact zero mode/selected Δt.");
                CheckMass(result.PhaseFieldCrystal.Field);
                using var renderer = new Fractal3DRenderer();
                using var direct = new PhaseFieldCrystal3DGpuSimulation(renderer.DeviceHost,result.PhaseFieldCrystal with { Field = before.PhaseFieldCrystal.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.PhaseFieldCrystal.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred ψ must reproduce the selected tile, including the recomputed RMS modulation.");
            }
            Click(ready[3]); var second = await grid.PrepareSelectionAsync(before);
            EqualField(result!.PhaseFieldCrystal.Field!,second.PhaseFieldCrystal.Field!);
            Check(second.PhaseFieldCrystal.DisplayRange != result.PhaseFieldCrystal.DisplayRange,"Display scale must change only the view, not ψ.");
            Click(ready[2]); Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running,"Modal application must queue a paused checkpoint.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.PhaseFieldCrystal = result.PhaseFieldCrystal; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed parameters, exact ψ/mean, step or view.");
            Check(E<TextBox>(editor,"PfcMeanBox").Text == state.PhaseFieldCrystal.Mean.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded the conserved mean parameter.");
            Check(original == JsonSerializer.Serialize(state.PhaseFieldCrystal.Field),"Trials mutated the original density field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.PhaseFieldCrystal.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.PhaseFieldCrystal.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.PhaseFieldCrystal.Field!,undone.PhaseFieldCrystal.Field!);
            Check(undone.PhaseFieldCrystal.TimeStep == before.PhaseFieldCrystal.TimeStep && undone.PhaseFieldCrystal.View == before.PhaseFieldCrystal.View &&
                undone.PhaseFieldCrystal.DisplayRange == before.PhaseFieldCrystal.DisplayRange && !E<Button>(editor,"AtlasUndoButton").IsEnabled,"Undo failed to recover the original equation/view and consume history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new PhaseFieldCrystal3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.PhaseFieldCrystal.Field),"Cancel/close changed the starting density field.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (phase-field-crystal3d-atlas): eight projected frozen seeds, fixed zero mode/mass, band and M·Δt ranges, exact three-view transfer/render/save, paused apply/undo and cancellation.");

        static Fractal3DState Small(PhaseFieldCrystal3DSeed seed)
        { var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.PhaseFieldCrystal3D); s.PhaseFieldCrystal = s.PhaseFieldCrystal with { Size = 32, SeedShape = seed, Mean = -.22123456789012345 }; return s; }
        static void EqualField(PhaseFieldCrystal3DField a,PhaseFieldCrystal3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Mean == b.Mean && a.Density.SequenceEqual(b.Density),"Exact ψ, mean or step mismatch.");
        static void CheckMass(PhaseFieldCrystal3DField field) => Check(Math.Abs(field.Values.Average(v => (double)v)-field.Mean) < 1e-6,"Density average drifted from the conserved zero mode.");
        static void AllInvalid(ParameterAtlasWindow a) => Check(!a.IsRendering && !E<Button>(a,"ApplyButton").IsEnabled && Tiles(a).All(b => !b.IsEnabled),"All-invalid range must finish without an applicable selection.");
        static void Mixed(ParameterAtlasWindow a) => Check(Tiles(a).Count(b => b.IsEnabled) == 6 && Tiles(a).Count(b => !b.IsEnabled) == 3,"Mixed range must disable only invalid cells.");
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
        static void Apply(PhaseFieldCrystal3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(PhaseFieldCrystal3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
