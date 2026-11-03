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
    private static async Task VerifyCahnHilliard3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("CahnHilliard3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.CahnHilliard3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int cut in new[] { 0,2 })
        {
            source.CahnHilliard = source.CahnHilliard with { Kappa = 1.1234567890123456, Mobility = .8123456789012345,
                TimeStep = .7123456789012345, Level = .12345678901234567, CutAxis = cut };
            var atlas = new CahnHilliard3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Mapping rounded coefficients or replaced the source field.");
                Check(Boxes(atlas).Length == (cut == 0 ? 5 : 6) && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),
                    "Only active equation/view fields and a validated duration should be exposed.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) continue;
                        target = source.Clone(); Apply(atlas,target,p); target.CahnHilliard.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                var fractional = (decimal[])point.Clone(); fractional[4] = .5m; Reject(atlas,source.Clone(),fractional);
                Check((bool)Invoke(atlas,"IsIntegerComponent",4)! && Invoke(atlas,"ComponentValueLabel",4,1m,"G")!.ToString()!.Contains("Вторая фаза"),"Phases need integer sampling and readable names.");
                var coupled = (decimal[])point.Clone(); coupled[1] = 2; coupled[2] = 2;
                Check(Invoke(atlas,"UnavailableSample",coupled) is string,"M·Δt > 2 must be disabled before GPU work."); Reject(atlas,source.Clone(),coupled);
                foreach (var settings in new[] { source.CahnHilliard with { Size = 32 }, source.CahnHilliard with { Mean = .4 }, source.CahnHilliard with { Noise = .1 },
                    source.CahnHilliard with { Seed = source.CahnHilliard.Seed + 1 }, source.CahnHilliard with { SeedShape = (CahnHilliard3DSeed)(((int)source.CahnHilliard.SeedShape + 1) % 3) } })
                { var wrong = source.Clone(); wrong.CahnHilliard = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "120","Invalid trial duration must recover without starting work.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Cahn–Hilliard atlas: presets, active cut, exact values, named phases, bounds, coupled constraint and fixed initial mixture passed.");

        // Fresh starts must skip the main window warmup and reset time for every Δt trial.
        foreach (var shape in Enum.GetValues<CahnHilliard3DSeed>())
        {
            var fresh = Fractal3DCatalog.CreateDefaultState(kind);
            fresh.CahnHilliard = fresh.CahnHilliard with { Size = 32, SeedShape = shape, TimeStep = .5 };
            var atlas = new CahnHilliard3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.CahnHilliard.Field!.Step == 2 && first.CahnHilliard.Field.Time == 1,"Fresh trial added warmup steps or lost physical time.");
                Boxes(atlas)[2].Text = ".25";
                typeof(ParameterAtlasWindow).GetMethod("ReadPointFields",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas,null);
                var second = await atlas.PrepareSelectionAsync(fresh);
                Check(second.CahnHilliard.Field!.Step == 2 && second.CahnHilliard.Field.Time == .5 && second.CahnHilliard.Field.Mean == first.CahnHilliard.Field.Mean,
                    "Changing Δt must reset to the common initial mixture and use the selected physical time.");
                using var renderer = new Fractal3DRenderer(); using var direct = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,second.CahnHilliard with { Field = null });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.CahnHilliard.Field!);
            }
            finally { atlas.Close(); }
        }

        var state = Fractal3DCatalog.CreateDefaultState(kind); state.ShadingStyle = Fractal3DShadingStyle.Density;
        state.CahnHilliard = state.CahnHilliard with { Size = 32, Mean = .12345678901234567, Noise = .08123456789012345,
            Kappa = 1.1234567890123456, Mobility = .8123456789012345, TimeStep = .25123456789012345, WarmupSteps = 0 };
        state.CahnHilliard = state.CahnHilliard with { Field = new CahnHilliard3DField(32,7,CahnHilliard3DGpuSimulation.InitialField(state.CahnHilliard),2.125,(float)state.CahnHilliard.Mean) };
        string original = JsonSerializer.Serialize(state.CahnHilliard.Field);
        var main = new Fractal3DWindow(kind); CahnHilliard3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<CahnHilliard3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"CahnKappaBox").Text = "3.999";
            grid = (CahnHilliard3DAtlasWindow)Invoke(main,"CreateCahnHilliardAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.CahnHilliard.Kappa == state.CahnHilliard.Kappa,"Atlas must pause and use applied settings, preserving an unapplied equation draft.");
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 1; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 2;
            E<TextBox>(grid,"MinRealBox").Text = "1.6"; E<TextBox>(grid,"MaxRealBox").Text = "2";
            E<TextBox>(grid,"MinImaginaryBox").Text = "1.6"; E<TextBox>(grid,"MaxImaginaryBox").Text = "2";
            await Build(grid);
            var tiles = E<UniformGrid>(grid,"TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => !b.IsEnabled) && !E<Button>(grid,"ApplyButton").IsEnabled && !grid.IsRendering,
                "An entirely invalid M/Δt grid must finish with disabled cells and no stale apply selection.");
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 4; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 0;
            E<TextBox>(grid,"MinRealBox").Text = "0"; E<TextBox>(grid,"MaxRealBox").Text = "1";
            E<TextBox>(grid,"MinImaginaryBox").Text = "1.1"; E<TextBox>(grid,"MaxImaginaryBox").Text = "1.3";
            // Tiles render from the centre outward; finish this tiny 2×3 grid before comparing one row.
            await Build(grid);
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(tiles.Columns == 2 && ready.Length == 6,"Two phase values should produce two unique columns and successful GPU trials.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.CahnHilliard.Field?.Step == 10 && result.CahnHilliard.Field.Mean == before.CahnHilliard.Field!.Mean && result.CahnHilliard.Live is null,
                "Trial must preserve the conserved composition and add exactly N steps.");
            using (var renderer = new Fractal3DRenderer())
            {
                using var direct = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,result.CahnHilliard with { Field = before.CahnHilliard.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.CahnHilliard.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred checkpoint must render exactly like the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            EqualField(result.CahnHilliard.Field!,second.CahnHilliard.Field!);
            Check(result.CahnHilliard.Invert != second.CahnHilliard.Invert,"Phase comparison should change display only.");
            Click(ready[0]); Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running,"Modal application must queue the selected checkpoint on pause.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.CahnHilliard = result.CahnHilliard; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact settings, mass, physical time or field.");
            Check(E<TextBox>(editor,"CahnMobilityBox").Text == after.CahnHilliard.Mobility.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded a loaded atlas coefficient.");
            Check(original == JsonSerializer.Serialize(state.CahnHilliard.Field),"Comparison mutated the starting field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.CahnHilliard.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.CahnHilliard.Field!);
            Click(E<Button>(editor,"AtlasUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.CahnHilliard.Field!,undone.CahnHilliard.Field!);
            Check(undone.CahnHilliard.Kappa == before.CahnHilliard.Kappa && !E<Button>(editor,"AtlasUndoButton").IsEnabled,"Undo failed to recover the previous rule/history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new CahnHilliard3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.CahnHilliard.Field),"Cancel/close changed the source.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (cahn-hilliard3d-atlas): common field, independent short GPU trials, exact mass/time, phase columns, invalid grid, transfer/render/save, undo and cancellation.");

        static void EqualField(CahnHilliard3DField a,CahnHilliard3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Time == b.Time && a.Mean == b.Mean && a.Concentrations.SequenceEqual(b.Concentrations),"Exact field, mass or physical time mismatch.");
        static async Task Cancelled(Task task) { bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; } Check(cancelled,"Operation must cancel before applying a result."); }
        static T E<T>(FrameworkElement e,string name) where T : class => (T)e.FindName(name);
        static void Set(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o,value);
        static object? Invoke(object o,string method,params object[] args) => o.GetType().GetMethod(method,BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a,null)!;
        static Task Display(Fractal3DWindow w) { var m = w.GetType().GetMethod("RenderFrameAsync",BindingFlags.Instance | BindingFlags.NonPublic)!; return (Task)m.Invoke(w,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!; }
        static async Task Wait(Func<bool> condition) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < 90) await Task.Delay(15); Check(condition(),"Timed out awaiting atlas/simulation work."); }
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a,"FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(CahnHilliard3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(CahnHilliard3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
