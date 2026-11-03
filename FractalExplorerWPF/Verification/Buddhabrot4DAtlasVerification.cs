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
    private static async Task VerifyBuddhabrot4DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Buddhabrot4DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Buddhabrot4D;
        var sculpture = Fractal3DCatalog.CreateDefaultState(kind); sculpture.ShadingStyle = Fractal3DShadingStyle.Clay;
        foreach (var source in Fractal3DCatalog.GetPresets(kind).Append(sculpture))
        {
            source.Buddhabrot.ZrCr = 12.345678901234567; source.Buddhabrot.ZiCi = -23.456789012345678;
            source.Buddhabrot.Exposure = 1.2345678901234567; source.Buddhabrot.Gamma = 1.5123456789012345;
            var main = new Fractal3DWindow(kind); Buddhabrot4DAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = E<Buddhabrot4DEditor>(main, "BuddhabrotEditor");
                Check(main.CaptureState("view").Buddhabrot == source.Buddhabrot, "Loading rounded 4D angles or tone values.");
                Check(NumericInput.IsRegistered(E<TextBox>(editor, "ZrCrBox")), "Editor angles must use NumericInput before being captured by the atlas.");
                E<TextBox>(editor, "ZrCrBox").Text = "NaN"; NumericInput.CommitAll(editor);
                Check(editor.Capture().ZrCr == source.Buddhabrot.ZrCr, "Invalid angle recovery must preserve the applied double.");
                atlas = Create(main); var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Untouched atlas changed exact fields or sample count.");
                bool cloud = source.ShadingStyle is Fractal3DShadingStyle.Glow or Fractal3DShadingStyle.Density;
                Check(Boxes(atlas).Length == (cloud ? 17 : 16) && Boxes(atlas).All(NumericInput.IsRegistered), "Only active density and validated parameters should be exposed.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas, "ComponentBounds", i)!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var p = (decimal[])point.Clone(); p[i] = edge;
                        if (i == 7 && edge == 9999) { p[8] = 10000; p[11] = 10000; }
                        if (i == 8 && edge == 2) p[7] = 1;
                        target = source.Clone(); Apply(atlas, target, p); target.Buddhabrot.Validate();
                        Check(target.Buddhabrot.SampleCount == source.Buddhabrot.SampleCount && target.ShadingStyle == source.ShadingStyle, "Bounds application changed fixed sampling quality/style.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                foreach (int i in new[] { 0, 7, 8, 9, 10, 11, 12 })
                { var p = (decimal[])point.Clone(); p[i] += .5m; Reject(atlas, source.Clone(), p); }
                var crossed = (decimal[])point.Clone(); crossed[7] = crossed[8]; Reject(atlas, source.Clone(), crossed);
                var empty = (decimal[])point.Clone(); empty[9] = empty[10] = empty[11] = 1; Reject(atlas, source.Clone(), empty);
                for (int projection = 1; projection <= 4; projection++)
                { var p = (decimal[])point.Clone(); p[0] = projection; target = source.Clone(); Apply(atlas, target, p); Check((int)target.Buddhabrot.Projection == projection - 1, "Named projection mapping changed."); }
                var layout = (FrameworkElement)atlas.Content; layout.Measure(new Size(832, 600)); layout.Arrange(new Rect(0, 0, 832, 600)); layout.UpdateLayout();
                var fixedScroll = (ScrollViewer)E<WrapPanel>(atlas, "FixedPointPanel").Parent;
                Check(fixedScroll.ActualHeight <= 180 && fixedScroll.ExtentHeight > fixedScroll.ViewportHeight,
                    "Many fixed fields must scroll instead of consuming the preview area at minimum window size.");
            }
            finally { atlas?.Close(); main.Close(); }
        }
        bool sparseRejected = false;
        try { await Task.Run(() => Buddhabrot4DOrbitCloud.Build(new() { SampleCount = 1000, MinIterations = 9999, MaxIterations = 10000 }, CancellationToken.None)); }
        catch (Buddhabrot4DSampleException) { sparseRejected = true; }
        Check(sparseRejected, "Insufficient orbit samples must have a domain failure distinct from GPU errors.");
        Console.WriteLine("Buddhabrot atlas: four projections, six rotations, channels, active tone fields, bounds, precision and compact layout passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.Buddhabrot.SampleCount = 12000; state.Buddhabrot.MinIterations = 4; state.Buddhabrot.MaxIterations = 100;
        state.Buddhabrot.RedLimit = 15; state.Buddhabrot.GreenLimit = 40; state.Buddhabrot.BlueLimit = 100;
        var window = new Fractal3DWindow(kind); Buddhabrot4DAtlasWindow? grid = null;
        try
        {
            window.LoadState(state); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var before = window.CaptureState("view"); grid = Create(window);
            E<TextBox>(grid, "MinRealBox").Text = "0"; E<TextBox>(grid, "MaxRealBox").Text = "60";
            E<TextBox>(grid, "MinImaginaryBox").Text = "0"; E<TextBox>(grid, "MaxImaginaryBox").Text = "60";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel"); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2, "4D rotation previews failed.");
            var atlasRenderer = (Fractal3DRenderer)typeof(Fractal3DParameterAtlasWindow).GetField("_renderer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
            Check(atlasRenderer.BuddhabrotSamplingBuilds == 1, "All rotation variants must reuse the same sampled cloud.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            byte[] first = Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source);
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Buddhabrot.SampleCount = 10000; p.Iterations = Math.Clamp(p.Iterations, 1, 32); p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(first.SequenceEqual(reference.Buffer), "Preview differs from the exact selected projection, channels or capped seed count.");
            }
            Click(ready[1]); Check(!first.SequenceEqual(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source)), "Mixed 4D rotations need different views.");
            expected = before.Clone(); grid.ApplySelectionTo(expected); Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = window.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected) && after.Buddhabrot.SampleCount == 12000, "Application changed the full seed count or unrelated view settings.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            window.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
            Check(window.CaptureState("saved").Buddhabrot == after.Buddhabrot, "Save/load lost exact projection or sampling fields.");
            grid = Create(window); E<ComboBox>(grid, "XAxisBox").SelectedIndex = 7; E<ComboBox>(grid, "YAxisBox").SelectedIndex = 8;
            E<TextBox>(grid, "MinRealBox").Text = "500"; E<TextBox>(grid, "MaxRealBox").Text = "501";
            E<TextBox>(grid, "MinImaginaryBox").Text = "2"; E<TextBox>(grid, "MaxImaginaryBox").Text = "3";
            await Build(grid);
            Check(!grid.IsRendering && !E<Button>(grid, "ApplyButton").IsEnabled && E<TextBlock>(grid, "StatusText").Text.StartsWith("Нет допустимых"),
                "Crossed orbit ranges must finish without rendering or stale application.");
            grid.Close(); grid = Create(window); var unchanged = window.CaptureState("view");
            Task closing = Build(grid); grid.Close(); await closing; grid = null;
            var closed = window.CaptureState("view"); closed.Timestamp = unchanged.Timestamp;
            Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(unchanged), "Closing during calculation changed the source view.");
        }
        finally { grid?.Close(); window.Close(); }
        Console.WriteLine("PASS (buddhabrot4d-atlas): named projections, rotations, orbit/channel ranges, tone fields, shared samples, CPU/GPU previews, apply, cancellation and saves.");
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Buddhabrot4DAtlasWindow Create(Fractal3DWindow w) => (Buddhabrot4DAtlasWindow)Invoke(w, "CreateBuddhabrotAtlas")!;
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Buddhabrot4DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(Buddhabrot4DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
