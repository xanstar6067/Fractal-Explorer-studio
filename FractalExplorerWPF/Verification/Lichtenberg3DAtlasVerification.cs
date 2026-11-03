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
    private static async Task VerifyLichtenberg3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Lichtenberg3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Lichtenberg3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        {
            source.Lichtenberg = source.Lichtenberg with { Eta = 2.1234567890123457, Field = InitialField(source.Lichtenberg) };
            var main = new Fractal3DWindow(kind); Lichtenberg3DAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                Check(E<TextBox>(main, "LichtenbergEtaBox").Text == source.Lichtenberg.Eta.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Loading must not round the editable applied eta.");
                E<TextBox>(main, "LichtenbergEtaBox").Text = "7.75"; E<TextBox>(main, "LichtenbergSeedBox").Text = "999";
                Set(main, "_lichtenbergRunning", true); Set(main, "_lichtenbergRequested", source.Lichtenberg.SegmentCount + 25);
                atlas = Create(main);
                Check(!(bool)Field(main, "_lichtenbergRunning") && (int)Field(main, "_lichtenbergRequested") == source.Lichtenberg.SegmentCount,
                    "Opening must freeze the shown count and discard the pending batch.");
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source) && ReferenceEquals(target.Lichtenberg.Field, source.Lichtenberg.Field),
                    "Unchanged parameters must preserve the exact checkpoint, applied settings and full stage, ignoring the UI draft.");
                var preview = (Fractal3DState)typeof(Fractal3DParameterAtlasWindow).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
                Check(preview.Lichtenberg.Field is null && preview.Lichtenberg.SegmentCount == 32 && preview.Lichtenberg.Size == source.Lichtenberg.Size,
                    "Comparisons must restart at a capped stage without changing the selected grid resolution.");
                Check(Boxes(atlas).Length == 4 && Boxes(atlas).All(NumericInput.IsRegistered), "Four validated DBM parameters required.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas, "ComponentBounds", i)!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge; target = source.Clone(); Apply(atlas, target, p); target.Lichtenberg.Validate();
                        Check(target.Lichtenberg.SegmentCount == source.Lichtenberg.SegmentCount && target.Lichtenberg.TargetSegments == source.Lichtenberg.TargetSegments,
                            "Applying a parameter must preserve the full stage and goal.");
                        Check(target.Lichtenberg.SameGrowth(source.Lichtenberg) ? ReferenceEquals(target.Lichtenberg.Field, source.Lichtenberg.Field) : target.Lichtenberg.Field is null,
                            "Changed rules must discard the old potential, unchanged rules must preserve it.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                foreach (int i in new[] { 1, 2, 3 }) { var p = (decimal[])point.Clone(); p[i] += .5m; Reject(atlas, source.Clone(), p); }
                var unsupported = (decimal[])point.Clone(); unsupported[2] = 33; Reject(atlas, source.Clone(), unsupported);
                Boxes(atlas)[2].Text = "47"; NumericInput.CommitAll(atlas); Check(Boxes(atlas)[2].Text == "48", "Fixed grid sizes must snap to 32/48/64.");
                var before = main.CaptureState("view"); atlas.Close(); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = before.Timestamp;
                Check(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after) && ReferenceEquals(after.Lichtenberg.Field, before.Lichtenberg.Field), "Closing without application changed the checkpoint or view.");
                Check(E<TextBox>(main, "LichtenbergEtaBox").Text == "7.75" && E<TextBox>(main, "LichtenbergSeedBox").Text == "999", "Closing discarded unapplied editor text.");
            }
            finally { atlas?.Close(); main.Close(); }
        }
        Console.WriteLine("Lichtenberg atlas: presets, checkpoint identity, exact eta, applied/draft settings, grid steps, bounds and pause passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.Lichtenberg = state.Lichtenberg with { Size = 32, SegmentCount = 3, TargetSegments = 100 };
        var window = new Fractal3DWindow(kind); Lichtenberg3DAtlasWindow? grid = null;
        try
        {
            window.LoadState(state); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var before = window.CaptureState("view"); grid = Create(window);
            Check(E<Button>(window, "LichtenbergAtlasButton").Visibility == Visibility.Visible, "Lichtenberg must expose its atlas.");
            E<TextBox>(grid, "MinRealBox").Text = "2"; E<TextBox>(grid, "MaxRealBox").Text = "4";
            E<TextBox>(grid, "MinImaginaryBox").Text = "1"; E<TextBox>(grid, "MaxImaginaryBox").Text = "2";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel"); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(ready.Length >= 2 && tiles.Children.Count == 6 && tiles.Children.OfType<Button>().Any(b => b.ToolTip.ToString()!.Contains("плоскость")), "Named electrode sampling failed or repeated rows.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            byte[] first = Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source);
            Fractal3DState checkpointState;
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Lichtenberg = p.Lichtenberg with { Field = null, SessionId = 0 };
                p.Iterations = Math.Clamp(p.Iterations, 1, 32); p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(first.SequenceEqual(reference.Buffer) && renderer.LichtenbergDisplayedField?.Count == 3, "Preview changed the eta, electrodes, grid, seed or stage.");
                checkpointState = expected.Clone(); checkpointState.Lichtenberg = checkpointState.Lichtenberg with { Field = renderer.LichtenbergDisplayedField };
            }
            Click(ready[1]); Check(!first.SequenceEqual(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source)), "Electrode configurations must produce different views.");
            expected = before.Clone(); grid.ApplySelectionTo(expected); Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = window.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected) && after.Lichtenberg.Field is null && !(bool)Field(window, "_lichtenbergRunning"),
                "Applying changed physics must discard the checkpoint and preserve the stage, goal and view while paused.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(checkpointState);
            var loaded = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State;
            Check(loaded.Lichtenberg.Field!.GrowthOrder.SequenceEqual(checkpointState.Lichtenberg.Field!.GrowthOrder) &&
                loaded.Lichtenberg.Field.Potential.SequenceEqual(checkpointState.Lichtenberg.Field.Potential), "Checkpoint save/load lost exact cells or float32 potential.");
            window.LoadState(loaded); grid = Create(window); var unchanged = window.CaptureState("view"); var retained = unchanged.Clone(); grid.ApplySelectionTo(retained);
            Check(ReferenceEquals(retained.Lichtenberg.Field, unchanged.Lichtenberg.Field), "Applying the same parameters to a real GPU checkpoint must retain it.");
            Task closing = Build(grid); grid.Close(); await closing; grid = null;
            var closed = window.CaptureState("view"); closed.Timestamp = unchanged.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(unchanged) && ReferenceEquals(closed.Lichtenberg.Field, unchanged.Lichtenberg.Field), "Closing during comparison changed the live checkpoint.");
        }
        finally { grid?.Close(); window.Close(); }
        Console.WriteLine("PASS (lichtenberg3d-atlas): exact parameters/checkpoints, independent GPU trials, named axes, cancellation, apply, pause and save/load.");
        static Lichtenberg3DField InitialField(Lichtenberg3DSettings s)
        {
            int n = s.Size; var mask = Lichtenberg3DGpuSimulation.InitialMask(s); var potential = new float[n * n * n];
            for (int i = 0; i < potential.Length; i++) potential[i] = mask[i] == 2 ? 1 : mask[i] == 3 ? 0 : .5f;
            int cell = Lichtenberg3DGpuSimulation.SeedCell(s); potential[cell] = 0; return new(n, [cell], potential);
        }
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
        static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Lichtenberg3DAtlasWindow Create(Fractal3DWindow w) => (Lichtenberg3DAtlasWindow)Invoke(w, "CreateLichtenbergAtlas")!;
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Lichtenberg3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(Lichtenberg3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
