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
    private static async Task VerifyLenia3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Lenia3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Lenia3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        foreach (int shells in new[] { 1,2,3,4 })
        foreach (int cut in new[] { 0,2 })
        {
            source.Lenia = source.Lenia with { ShellCount = shells, Beta1 = .8123456789012345, Beta2 = 5.0/12, Beta3 = .3, Beta4 = .2,
                GrowthMean = .17123456789012345, GrowthWidth = .014123456789012345, TimeStep = .10123456789012345, CutAxis = cut };
            var atlas = new Lenia3DAtlasWindow(source);
            try
            {
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source),"Mapping rounded coefficients or inactive shell weights.");
                Check(Boxes(atlas).Length == 6 + shells + (cut > 0 ? 1 : 0) && Boxes(atlas).All(NumericInput.IsRegistered) && NumericInput.IsRegistered(E<TextBox>(atlas,"TrialStepsBox")),
                    "Only active shells/cut and validated trial duration should be exposed.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas,"ComponentBounds",i)!;
                    foreach (decimal edge in new[] { bounds.Minimum,bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (Invoke(atlas,"UnavailableSample",p) is not null) continue;
                        target = source.Clone(); Apply(atlas,target,p); target.Lenia.Validate();
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas,source.Clone(),invalid);
                }
                var zero = (decimal[])point.Clone(); for (int i = 0; i < shells; i++) zero[6+i] = 0;
                Check(Invoke(atlas,"UnavailableSample",zero) is string,"All active weights zero must disable the sample."); Reject(atlas,source.Clone(),zero);
                var fractional = (decimal[])point.Clone(); fractional[3] = .5m; Reject(atlas,source.Clone(),fractional);
                Check((bool)Invoke(atlas,"IsIntegerComponent",3)! && Invoke(atlas,"ComponentValueLabel",3,1m,"G")!.ToString() == "Гауссова","Growth functions need discrete samples and readable names.");
                Check(Invoke(atlas,"UnavailableSampleError",new InvalidOperationException("GPU failure")) is null,"GPU errors must not be mistaken for a narrow kernel.");
                foreach (var settings in new[] { source.Lenia with { Size = 32 }, source.Lenia with { ShellCount = shells % 4 + 1 },
                    source.Lenia with { SeedNoise = .1 }, source.Lenia with { Seed = source.Lenia.Seed + 1 } })
                { var wrong = source.Clone(); wrong.Lenia = settings; Reject(atlas,wrong,point); }
                E<TextBox>(atlas,"TrialStepsBox").Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(E<TextBox>(atlas,"TrialStepsBox").Text == "72","Invalid duration fallback failed.");
            }
            finally { atlas.Close(); }
        }
        Console.WriteLine("Lenia atlas: presets, 1–4 active shells, exact fractions, bounds, discrete growth, zero weights and duration passed.");

        foreach (var seed in Enum.GetValues<Lenia3DSeed>())
        {
            var fresh = Small(seed); var atlas = new Lenia3DAtlasWindow(fresh);
            try
            {
                E<TextBox>(atlas,"TrialStepsBox").Text = "2";
                var first = await atlas.PrepareSelectionAsync(fresh);
                Check(first.Lenia.Field!.Step == 2 && first.Lenia.Field.Time == .2,"Fresh trials must skip warmup and retain exact physical time.");
                Boxes(atlas)[2].Text = "10"; Boxes(atlas)[4].Text = ".05"; ReadPoint(atlas);
                var second = await atlas.PrepareSelectionAsync(fresh);
                Check(second.Lenia.Field!.Step == 2 && second.Lenia.Field.Time == .1,"Changing Δt accumulated prior trials or used the wrong time.");
                using var renderer = new Fractal3DRenderer();
                var common = new Lenia3DField(32,0,Lenia3DMath.InitialField(fresh.Lenia));
                using var direct = new Lenia3DGpuSimulation(renderer.DeviceHost,second.Lenia with { Field = common });
                direct.Advance(2,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),second.Lenia.Field!);
                Check(fresh.Lenia.Field is null && fresh.Lenia.Radius == 8,"Trial mutated the original seed/radius.");
            }
            finally { atlas.Close(); }
        }
        var sparse = Small(Lenia3DSeed.RandomCloud); sparse.Lenia = sparse.Lenia with { ShellCount = 4, Beta1 = 1, Beta2 = 0 };
        var narrow = new Lenia3DAtlasWindow(sparse);
        try
        {
            E<TextBox>(narrow,"TrialStepsBox").Text = "1";
            E<ComboBox>(narrow,"XAxisBox").SelectedIndex = 2; E<ComboBox>(narrow,"YAxisBox").SelectedIndex = 6;
            E<TextBox>(narrow,"MinRealBox").Text = "2"; E<TextBox>(narrow,"MaxRealBox").Text = "8";
            E<TextBox>(narrow,"MinImaginaryBox").Text = ".5"; E<TextBox>(narrow,"MaxImaginaryBox").Text = "1";
            await Build(narrow); var tiles = E<UniformGrid>(narrow,"TilesPanel").Children.OfType<Button>().ToArray();
            Check(tiles.Count(b => b.IsEnabled) == 6 && tiles.Where((_,i) => i % 3 == 0).All(b => !b.IsEnabled && b.ToolTip.ToString()!.Contains("Увеличьте радиус")),
                "A narrow discrete kernel must disable only its own column and allow later trials to finish.");
        }
        finally { narrow.Close(); }

        var state = Small(Lenia3DSeed.RandomCloud);
        state.Lenia = state.Lenia with { Field = new Lenia3DField(32,7,Lenia3DMath.InitialField(state.Lenia),1.25) };
        string original = JsonSerializer.Serialize(state.Lenia.Field);
        var main = new Fractal3DWindow(kind); Lenia3DAtlasWindow? grid = null;
        try
        {
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); Set(main,"_suspended",true); main.LoadState(state);
            var editor = SimulationEditor<Lenia3DEditor>(main); var live = editor.Simulation;
            await Wait(() => !live.Busy); Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            E<TextBox>(editor,"LeniaMeanBox").Text = ".49";
            grid = (Lenia3DAtlasWindow)Invoke(main,"CreateLeniaAtlas")!; var before = main.CaptureState("atlas");
            Check(!live.Running && before.Lenia.GrowthMean == state.Lenia.GrowthMean,"Atlas must use applied settings and pause playback, preserving the equation draft.");
            E<TextBox>(grid,"TrialStepsBox").Text = "3";
            E<ComboBox>(grid,"XAxisBox").SelectedIndex = 3; E<ComboBox>(grid,"YAxisBox").SelectedIndex = 0;
            E<TextBox>(grid,"MinRealBox").Text = "0"; E<TextBox>(grid,"MaxRealBox").Text = "1";
            E<TextBox>(grid,"MinImaginaryBox").Text = ".16"; E<TextBox>(grid,"MaxImaginaryBox").Text = ".18";
            await Build(grid); var tiles = E<UniformGrid>(grid,"TilesPanel"); var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(tiles.Columns == 2 && ready.Length == 6,"Growth functions must produce two unique columns.");
            Click(ready[0]); var result = await grid.PrepareSelectionAsync(before);
            Check(result.Lenia.Field!.Step == 10 && result.Lenia.Live is null,"Trial must add exactly N steps to the shown source.");
            using (var renderer = new Fractal3DRenderer())
            {
                using var direct = new Lenia3DGpuSimulation(renderer.DeviceHost,result.Lenia with { Field = before.Lenia.Field });
                direct.Advance(3,CancellationToken.None); EqualField(direct.ReadCheckpoint(direct.Publish()),result.Lenia.Field!);
                var p = result.Clone(); p.Iterations = Math.Clamp(p.Iterations,1,32); p.MaxSteps = Math.Min(p.MaxSteps,160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p,144,108,null,null,CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid,"SelectedPreview").Source).SequenceEqual(reference.Buffer),"Transferred checkpoint must render exactly like the selected tile.");
            }
            Click(ready[1]); var second = await grid.PrepareSelectionAsync(before);
            Check(second.Lenia.Field!.Step == 10 && second.Lenia.Field.Time == result.Lenia.Field!.Time && second.Lenia.Growth != result.Lenia.Growth,"Growth trials must reset the source/time independently.");
            Click(ready[0]);
            var history = (List<Lenia3DSettings>)editor.GetType().GetField("_searchHistory",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            history.Add(before.Lenia with { GrowthMean = .1234 });
            Set(main,"_suspended",true); Click(E<Button>(grid,"ApplyButton")); await Wait(() => live.Settings.Field?.Step == 10); grid = null;
            Check(live.ResetPending && !live.Running && history.Count == 2,"Modal application must queue a paused checkpoint and retain previous search history.");
            Set(main,"_suspended",false); live.Resume(); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var after = main.CaptureState("atlas"); var expected = before.Clone(); expected.Lenia = result.Lenia; after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected),"Application changed exact coefficients, cells, step or time.");
            Check(E<TextBox>(editor,"LeniaBeta2Box").Text == (5.0/12).ToString("R",System.Globalization.CultureInfo.InvariantCulture),"Editor rounded a fractional shell weight.");
            Check(original == JsonSerializer.Serialize(state.Lenia.Field),"Trials mutated the original field.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            EqualField(after.Lenia.Field!,store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Lenia.Field!);
            Click(E<Button>(editor,"LeniaSearchUndoButton")); await Wait(() => !live.Busy && live.Pending is not null); await Display(main);
            var undone = main.CaptureState("undo"); EqualField(before.Lenia.Field!,undone.Lenia.Field!);
            Check(undone.Lenia.GrowthMean == before.Lenia.GrowthMean,"Undo failed to recover the original rule.");
            Click(E<Button>(editor,"LeniaSearchUndoButton")); Check(live.Settings.GrowthMean == .1234 && history.Count == 0,"Atlas discarded previous search history.");
        }
        finally { grid?.Close(); main.Close(); }

        var cancel = new Lenia3DAtlasWindow(state);
        try
        {
            E<TextBox>(cancel,"TrialStepsBox").Text = "10000"; var task = cancel.PrepareSelectionAsync(state);
            Click(E<Button>(cancel,"CancelAtlasButton")); await Cancelled(task); Check(!cancel.IsApplying,"Cancel did not release the apply gate.");
            task = cancel.PrepareSelectionAsync(state); cancel.Close(); await Cancelled(task);
            Check(original == JsonSerializer.Serialize(state.Lenia.Field),"Cancel/close changed the starting field.");
        }
        finally { cancel.Close(); }
        Console.WriteLine("PASS (lenia3d-atlas): frozen seeds, active weights, narrow kernels, both growth functions, short independent trials, exact time/transfer/render/save, undo/history and cancellation.");

        static Fractal3DState Small(Lenia3DSeed seed)
        {
            var s = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Lenia3D);
            s.Lenia = s.Lenia with { Size = 32, Radius = 8, SeedShape = seed, ShellCount = 2, Beta1 = 1, Beta2 = 5.0/12, Beta3 = 0, Beta4 = 0, TimeStep = .1 };
            return s;
        }
        static void EqualField(Lenia3DField a,Lenia3DField b) => Check(a.Size == b.Size && a.Step == b.Step && a.Time == b.Time && a.Cells.SequenceEqual(b.Cells),"Exact cells, step or physical time mismatch.");
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
        static void Apply(Lenia3DAtlasWindow a,Fractal3DState s,decimal[] p) => Invoke(a,"ApplyComponents",s,p);
        static void Reject(Lenia3DAtlasWindow a,Fractal3DState s,decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s),"Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
