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
    private static async Task VerifySwiftHohenberg3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("SwiftHohenberg3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.SwiftHohenberg3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,3 })
        foreach (var view in Enum.GetValues<SwiftHohenberg3DView>())
        {
            source.SwiftHohenberg = source.SwiftHohenberg with { R = .35123456789012345, Quadratic = -.7123456789012345,
                WaveNumber = 5.0/6, Spacing = 1.1234567890123456, TimeStep = .021234567890123456,
                Noise = .12123456789012345, DisplayRange = 1.5123456789012345, Threshold = .15123456789012345,
                CutAxis = cut, CutPosition = .1234567890123456, View = view };
            var atlas = new SwiftHohenberg3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Atlas rounded equation, fixed noise or view settings.");
                Check(Boxes(atlas).Length == 8 + (cut > 0 ? 1 : 0) && Boxes(atlas).All(NumericInput.IsRegistered) &&
                    NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),"Only active cut should appear, with validated duration.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) { Reject(atlas,source.Clone(),p); continue; }
                        target = source.Clone(); Apply(atlas,target,p); target.SwiftHohenberg.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                var fractional = (decimal[])point.Clone(); fractional[7] = .5m; Reject(atlas,source.Clone(),fractional);
                Check((bool)Invoke(atlas,"IsIntegerComponent",7)! && Invoke(atlas,"ComponentValueLabel",7,2m,"G")!.ToString() == "Границы φ ≈ 0","Views must have distinct integer samples and readable names.");
                var band = (decimal[])point.Clone(); band[2] = 1.5m; band[3] = 2;
                Check(Invoke(atlas,"UnavailableSample",band) is string,"A preferred frequency outside the band must disable the sample."); Reject(atlas,source.Clone(),band);
                band[2] = 1.2m; band[3] = 1;
                Check(Invoke(atlas,"UnavailableSample",band) is null,"Exact band boundary must remain valid."); Apply(atlas,source.Clone(),band);
                band[2] += .000000000002m; Reject(atlas,source.Clone(),band);
                foreach (var settings in new[] { source.SwiftHohenberg with { Size = 32 }, source.SwiftHohenberg with { Seed = source.SwiftHohenberg.Seed + 1 },
                    source.SwiftHohenberg with { Noise = .2 }, source.SwiftHohenberg with { SeedShape = SwiftHohenberg3DSeed.Uniform }, source.SwiftHohenberg with { CutAxis = (cut+1)%4 } })
                { var wrong = source.Clone(); wrong.SwiftHohenberg = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "48","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Swift–Hohenberg atlas: seven presets, three views, active cuts, exact parameters, band limits and duration passed.");

        foreach (var seed in Enum.GetValues<SwiftHohenberg3DSeed>())
        {
            var fresh = Small(seed); var atlas = new SwiftHohenberg3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.SwiftHohenberg.Field!.Step == 2,"Fresh trial must skip warmup.");
                Boxes(atlas)[0].Text = "-.1"; Boxes(atlas)[1].Text = "-.8"; Boxes(atlas)[2].Text = ".4";
                Boxes(atlas)[3].Text = ".5"; Boxes(atlas)[4].Text = ".01"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                using var renderer = new Fractal3DRenderer();
                using var direct = new SwiftHohenberg3DGpuSimulation(renderer.DeviceHost,fresh.SwiftHohenberg);
                // The common start is the original seed after its one Galerkin projection, not raw InitialField.
                var common = direct.ReadCheckpoint(direct.Publish());
                direct.Reset(first.SwiftHohenberg with { Field = common }); direct.Advance(2,CancellationToken.None);
                EqualField(direct.ReadCheckpoint(direct.Publish()),first.SwiftHohenberg.Field!);
                direct.Reset(second.SwiftHohenberg with { Field = common }); direct.Advance(2,CancellationToken.None);
                EqualField(direct.ReadCheckpoint(direct.Publish()),second.SwiftHohenberg.Field!);
                Check(second.SwiftHohenberg.Field!.Step == 2 && fresh.SwiftHohenberg.Field is null && fresh.SwiftHohenberg.WaveNumber == 1 && fresh.SwiftHohenberg.Spacing == 1,"Trial accumulated steps or rebuilt the original seed at a different q₀/Δx.");
                var restored = JsonSerializer.Deserialize<SwiftHohenberg3DSettings>(JsonSerializer.Serialize(second.SwiftHohenberg))!;
                EqualField(second.SwiftHohenberg.Field!,restored.Field!);
            }
            finally { atlas.Close(); }
        }

        var invalidAtlas = new SwiftHohenberg3DAtlasWindow(Small(SwiftHohenberg3DSeed.Layers));
        try
        {
            E<TextBox>(invalidAtlas,"TrialStepsBox").Text = "1";
            E<ComboBox>(invalidAtlas,"XAxisBox").SelectedIndex = 2; E<ComboBox>(invalidAtlas,"YAxisBox").SelectedIndex = 3;
            Range(invalidAtlas,"1.3","1.5","1.1","2"); await Build(invalidAtlas);
            Check(!invalidAtlas.IsRendering && !E<Button>(invalidAtlas,"ApplyButton").IsEnabled && Tiles(invalidAtlas).All(b => !b.IsEnabled),"All-invalid band range must finish without an applicable selection.");
            Range(invalidAtlas,".4","1.5",".5","2"); await Build(invalidAtlas);
            Check(Tiles(invalidAtlas).Count(b => b.IsEnabled) == 6 && Tiles(invalidAtlas).Count(b => !b.IsEnabled) == 3,"Mixed band range must disable only invalid cells.");
        }
        finally { invalidAtlas.Close(); }

        var state = Small(SwiftHohenberg3DSeed.Localized);
        using (var renderer = new Fractal3DRenderer())
        using (var sourceGpu = new SwiftHohenberg3DGpuSimulation(renderer.DeviceHost,state.SwiftHohenberg))
        {
            var initial = sourceGpu.ReadCheckpoint(sourceGpu.Publish());
            state.SwiftHohenberg = state.SwiftHohenberg with { WaveNumber = 5.0/6, Quadratic = -.7123456789012345,
                Spacing = 1.1234567890123456, Noise = .12123456789012345, CutAxis = 2,
                CutPosition = .1234567890123456, DisplayRange = 1.5123456789012345, Threshold = .2345678901234567,
                Field = new SwiftHohenberg3DField(32,7,initial.Samples) };
        }
        string original = JsonSerializer.Serialize(state.SwiftHohenberg.Field);
        Check(state.SwiftHohenberg.Field!.Values.Any(v => v < 0),"Test checkpoint must exercise signed scalar values.");
        var main = new Fractal3DWindow(kind); SwiftHohenberg3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<SwiftHohenberg3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"ShRBox").Text = ".99"; E<ComboBox>(editor,"ShSeedBox").SelectedIndex = 6;
            grid = (SwiftHohenberg3DAtlasWindow)Invoke(main,"CreateSwiftHohenbergAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.SwiftHohenberg.R == state.SwiftHohenberg.R && before.SwiftHohenberg.SeedShape == SwiftHohenberg3DSeed.Localized,"Atlas must pause and ignore unapplied equation/seed drafts.");
            EqualField(state.SwiftHohenberg.Field!,before.SwiftHohenberg.Field!);
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 7; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 5;
            Boxes(grid)[4].Text = ".01"; Range(grid,"0","2","1","2");
            await Build(grid); var ready = Tiles(grid).Where(b => b.IsEnabled).ToArray();
            Check(E<UniformGrid>(grid,"TilesPanel").Columns == 3 && ready.Length == 9,"View axis must create three unique columns.");
            Fractal3DState? result = null;
            foreach (int index in new[] { 0,1,2 })
            {
                Click(ready[index]); result = await grid.PrepareSelectionAsync(before);
                Check(result.SwiftHohenberg.Field!.Step == 10 && result.SwiftHohenberg.Live is null && result.SwiftHohenberg.TimeStep == .01,"Trial must add N steps and retain the selected Δt.");
                using var renderer = new Fractal3DRenderer();
                using var direct = new SwiftHohenberg3DGpuSimulation(renderer.DeviceHost,result.SwiftHohenberg with { Field = before.SwiftHohenberg.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.SwiftHohenberg.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred φ must render exactly like the selected tile in all three views.");
            }
            Click(ready[3]); var second = await grid.PrepareSelectionAsync(before);
            EqualField(result!.SwiftHohenberg.Field!,second.SwiftHohenberg.Field!);
            Check(second.SwiftHohenberg.DisplayRange != result.SwiftHohenberg.DisplayRange,"Display-scale trial must change only the view, not evolve from the preceding trial.");
            Click(ready[2]); Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running,"Modal application must queue a paused checkpoint.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.SwiftHohenberg = result.SwiftHohenberg; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact parameters, signed φ, step or view.");
            Check(E<TextBox>(editor,"ShQBox").Text == (5.0/6).ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded the fractional wave number.");
            Check(original == JsonSerializer.Serialize(state.SwiftHohenberg.Field),"Trials mutated the original scalar field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.SwiftHohenberg.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.SwiftHohenberg.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.SwiftHohenberg.Field!,undone.SwiftHohenberg.Field!);
            Check(undone.SwiftHohenberg.TimeStep == before.SwiftHohenberg.TimeStep && undone.SwiftHohenberg.View == before.SwiftHohenberg.View &&
                undone.SwiftHohenberg.DisplayRange == before.SwiftHohenberg.DisplayRange && !E<Button>(editor,"AtlasUndoButton").IsEnabled,"Undo failed to recover the equation/display and consume history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new SwiftHohenberg3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.SwiftHohenberg.Field),"Cancel/close changed the starting scalar field.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (swift-hohenberg3d-atlas): seven projected frozen seeds, band ranges, three views/display scale, exact signed transfer/render/save, paused apply/undo and cancellation.");

        static Fractal3DState Small(SwiftHohenberg3DSeed seed)
        { var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.SwiftHohenberg3D); s.SwiftHohenberg = s.SwiftHohenberg with { Size = 32, SeedShape = seed }; return s; }
        static void EqualField(SwiftHohenberg3DField a,SwiftHohenberg3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Samples.SequenceEqual(b.Samples),"Exact signed φ or step mismatch.");
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
        static void Apply(SwiftHohenberg3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(SwiftHohenberg3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
