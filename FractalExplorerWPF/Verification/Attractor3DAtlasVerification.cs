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
    private static async Task VerifyAttractor3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Attractor3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.StrangeAttractor;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        {
            source.Attractor.StartX = .12345678901234567;
            source.Attractor.TimeStep = .004567890123456789;
            var main = new Fractal3DWindow(kind); Attractor3DAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                Check(JsonSerializer.Serialize(main.CaptureState("view").Attractor) == JsonSerializer.Serialize(source.Attractor), "Loading rounded attractor values.");
                atlas = Create(main);
                int count = Attractor3DSystems.ParameterNames(source.Attractor.System).Length;
                var boxes = Boxes(atlas);
                Check(boxes.Length == count + 4 && boxes.All(NumericInput.IsRegistered), "Only active coefficients, step and start coordinates must be registered.");
                Check(E<ComboBox>(atlas, "YAxisBox").SelectedIndex == (count > 1 ? 1 : count + 1), "One-parameter systems must compare a coefficient and an initial coordinate by default.");
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded source doubles.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    foreach (decimal edge in i == count ? new[] { .0001m, .05m } : new[] { -100m, 100m })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        var target = source.Clone(); Apply(atlas, target, p);
                        Check(target.Iterations == source.Iterations && target.Attractor.System == source.Attractor.System, "Applying a parameter changed the fixed point count or system.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = i == count ? .051m : 101;
                    Reject(atlas, source.Clone(), invalid);
                }
                var wrong = source.Clone(); wrong.Attractor.System = source.Attractor.System == Attractor3DSystem.Lorenz ? Attractor3DSystem.Chua : Attractor3DSystem.Lorenz;
                Reject(atlas, wrong, point);
                var before = main.CaptureState("view"); atlas.Close(); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = before.Timestamp;
                Check(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after), "Closing an atlas changed the main state.");
            }
            finally { atlas?.Close(); main.Close(); }
        }
        Console.WriteLine("Attractor atlas: seven systems, active parameters, precision, bounds and close passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind); state.Iterations = 150_000;
        state.Attractor.StartX = state.Attractor.StartY = state.Attractor.StartZ = 0;
        state.Attractor.C = 8d / 3;
        var window = new Fractal3DWindow(kind); Attractor3DAtlasWindow? grid = null;
        try
        {
            window.LoadState(state); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var before = window.CaptureState("view");
            Check(E<Button>(window, "AttractorAtlasButton").Visibility == Visibility.Visible, "The mode must expose an atlas button.");
            grid = Create(window);
            E<ComboBox>(grid, "XAxisBox").SelectedIndex = 4; E<ComboBox>(grid, "YAxisBox").SelectedIndex = 5;
            E<TextBox>(grid, "MinRealBox").Text = "-.1"; E<TextBox>(grid, "MaxRealBox").Text = ".1";
            E<TextBox>(grid, "MinImaginaryBox").Text = "-.1"; E<TextBox>(grid, "MaxImaginaryBox").Text = ".1";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel"); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray();
            Check(ready.Length >= 2 && !((Button)tiles.Children[4]).IsEnabled && ((Button)tiles.Children[4]).ToolTip.ToString()!.Contains("сошлась к точке"),
                "A collapsed central trajectory must report its reason while neighbours continue rendering.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Iterations = 100_000; p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source).SequenceEqual(reference.Buffer), "Atlas preview changed the step, start point, coefficients or preview duration.");
            }
            Click(ready[1]); expected = before.Clone(); grid.ApplySelectionTo(expected);
            Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = window.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected) && after.Iterations == 150_000,
                "Atlas application changed fixed settings, precision or the full point count.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            window.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
            Check(JsonSerializer.Serialize(window.CaptureState("saved").Attractor) == JsonSerializer.Serialize(after.Attractor), "Saving lost exact attractor settings.");

            // Every sample is a fixed point. No density allocation or GPU frames are needed.
            var zero = before.Clone(); window.LoadState(zero); grid = Create(window);
            E<TextBox>(grid, "MinRealBox").Text = "10"; E<TextBox>(grid, "MaxRealBox").Text = "11";
            E<TextBox>(grid, "MinImaginaryBox").Text = "28"; E<TextBox>(grid, "MaxImaginaryBox").Text = "29";
            await Build(grid);
            Check(!grid.IsRendering && !E<Button>(grid, "ApplyButton").IsEnabled && E<UniformGrid>(grid, "TilesPanel").Children.OfType<Button>().All(b => !b.IsEnabled) &&
                E<TextBlock>(grid, "StatusText").Text.StartsWith("Нет допустимых"), "All collapsed samples must finish with an explanation and no stale selection.");
            grid.Close(); grid = null;

            var divergent = before.Clone(); divergent.Attractor.StartX = divergent.Attractor.StartY = divergent.Attractor.StartZ = 100;
            divergent.Attractor.A = divergent.Attractor.B = 100; divergent.Attractor.C = -100; divergent.Attractor.TimeStep = .05;
            bool divergenceReported = false;
            try { Attractor3DVolume.Build(divergent, CancellationToken.None); }
            catch (Attractor3DTrajectoryException ex) { divergenceReported = ex.Message.Contains("разошлась"); }
            Check(divergenceReported, "Divergence must be a trajectory-specific failure, distinct from GPU errors.");
        }
        finally { grid?.Close(); window.Close(); }
        Console.WriteLine("PASS (attractor3d-atlas): seven systems, exact fields, CPU/GPU previews, failed samples, cancellation, apply and saves.");

        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Attractor3DAtlasWindow Create(Fractal3DWindow w) => (Attractor3DAtlasWindow)Invoke(w, "CreateAttractorAtlas")!;
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Attractor3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(Attractor3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
