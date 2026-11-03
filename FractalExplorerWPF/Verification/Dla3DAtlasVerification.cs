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
    private static async Task VerifyDla3DAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("Dla3DAtlas"); EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.Dla3D;
        foreach (var source in Fractal3DCatalog.GetPresets(kind))
        {
            source.Dla.Stickiness = .12345678901234567; source.Dla.FlowStrength = .23456789012345678;
            source.Dla.FlowYaw = 12.345678901234567; source.Dla.FlowPitch = -23.456789012345678;
            var main = new Fractal3DWindow(kind); Dla3DAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                Check(main.CaptureState("view").Dla == source.Dla, "Percent sliders rounded loaded DLA probabilities.");
                Set(main, "_dlaRunning", true); Set(main, "_dlaRequestedCount", source.Dla.ParticleCount + 100);
                atlas = Create(main);
                Check(!(bool)Field(main, "_dlaRunning") && (int)Field(main, "_dlaRequestedCount") == source.Dla.ParticleCount,
                    "Opening an atlas must freeze the displayed stage, not the pending batch.");
                var target = source.Clone(); atlas.ApplySelectionTo(target);
                Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(source), "Untouched atlas changed exact DLA settings or full stage.");
                Check(Boxes(atlas).Length == 7 && Boxes(atlas).All(NumericInput.IsRegistered), "Seven validated DLA parameters required.");
                var p = Selected(atlas);
                for (int i = 0; i < p.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))Invoke(atlas, "ComponentBounds", i)!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var values = (decimal[])p.Clone(); values[i] = edge;
                        target = source.Clone(); Apply(atlas, target, values);
                        Check(target.Dla == target.Dla.Normalized() && target.Dla.ParticleCount == source.Dla.ParticleCount && target.Dla.TargetParticles == source.Dla.TargetParticles,
                            "DLA bounds must be exact and must preserve the full stage/goal.");
                    }
                    var invalid = (decimal[])p.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                foreach (int integer in new[] { 0, 1, 6 })
                { var invalid = (decimal[])p.Clone(); invalid[integer] += .5m; Reject(atlas, source.Clone(), invalid); }
                var names = E<ComboBox>(atlas, "XAxisBox").Items.Cast<string>().ToArray();
                Check(names.Contains("Сила потока") && names.Contains("Случайное число"), "Growth choices must be available on both axes.");
                var before = main.CaptureState("view"); atlas.Close(); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = before.Timestamp;
                Check(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after), "Closing the atlas changed the paused stage or main view.");
            }
            finally { atlas?.Close(); main.Close(); }
        }
        Console.WriteLine("DLA atlas: presets, exact probabilities, named/integer axes, bounds and displayed-stage pause passed.");

        var state = Fractal3DCatalog.CreateDefaultState(kind); state.Dla.ParticleCount = 700; state.Dla.TargetParticles = 1200;
        var window = new Fractal3DWindow(kind); Dla3DAtlasWindow? grid = null;
        try
        {
            window.LoadState(state); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var before = window.CaptureState("view"); grid = Create(window);
            Check(E<Button>(window, "DlaAtlasButton").Visibility == Visibility.Visible, "DLA must expose the atlas.");
            E<ComboBox>(grid, "XAxisBox").SelectedIndex = 0; E<ComboBox>(grid, "YAxisBox").SelectedIndex = 2;
            E<TextBox>(grid, "MinRealBox").Text = "1"; E<TextBox>(grid, "MaxRealBox").Text = "2";
            E<TextBox>(grid, "MinImaginaryBox").Text = ".8"; E<TextBox>(grid, "MaxImaginaryBox").Text = "1";
            Task building = Build(grid); var tiles = E<UniformGrid>(grid, "TilesPanel"); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (grid.IsRendering && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) < 2 && watch.Elapsed.TotalSeconds < 90) await Task.Delay(20);
            Click(E<Button>(grid, "CancelAtlasButton")); await building;
            var ready = tiles.Children.OfType<Button>().Where(b => b.IsEnabled).ToArray(); Check(ready.Length >= 2, "DLA previews failed.");
            Check(tiles.Children.Count == 6 && tiles.Children.OfType<Button>().Any(b => b.ToolTip.ToString()!.Contains("Стержень")), "Named shape sampling must not duplicate integer columns.");
            Click(ready[0]); var expected = before.Clone(); grid.ApplySelectionTo(expected);
            byte[] first = Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source);
            using (var renderer = new Fractal3DRenderer())
            {
                var p = expected.Clone(); p.Dla.ParticleCount = 500; p.Iterations = 1; p.MaxSteps = Math.Min(p.MaxSteps, 160);
                p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail); p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
                var reference = await renderer.RenderPixelsAsync(p, 144, 108, null, null, CancellationToken.None);
                Check(first.SequenceEqual(reference.Buffer) && renderer.DlaParticleCount == 500, "Preview must use the chosen seed/rules and exactly the capped stage.");
            }
            Click(ready[1]); Check(!first.SequenceEqual(Pixels((BitmapSource)E<Image>(grid, "SelectedPreview").Source)), "Different DLA variants need different previews.");
            expected = before.Clone(); grid.ApplySelectionTo(expected);
            Click(E<Button>(grid, "ApplyButton")); grid = null;
            var after = window.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected) && after.Dla.ParticleCount == 700 && after.Dla.TargetParticles == 1200,
                "Application changed the original stage/goal, view settings or exact probabilities.");
            Check(!(bool)Field(window, "_dlaRunning"), "Applying a variant must keep growth paused.");
            var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
            window.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
            Check(window.CaptureState("saved").Dla == after.Dla, "DLA save/load lost the stage or parameters.");
            var zero = before.Clone(); zero.Dla.ParticleCount = 0; window.LoadState(zero);
            grid = Create(window); var unchanged = zero.Clone(); grid.ApplySelectionTo(unchanged);
            Check(unchanged.Dla == zero.Dla, "An ungrown seed must remain at zero particles.");
            Task closing = Build(grid); grid.Close(); await closing; grid = null;
            Check(window.CaptureState("closed").Dla == zero.Dla, "Closing during calculation changed the source cluster.");
        }
        finally { grid?.Close(); window.Close(); }
        Console.WriteLine("PASS (dla3d-atlas): exact growth parameters, capped CPU/GPU previews, displayed-stage pause, cancellation, apply and saves.");
        static T E<T>(FrameworkElement e, string name) where T : class => (T)e.FindName(name);
        static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
        static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Dla3DAtlasWindow Create(Fractal3DWindow w) => (Dla3DAtlasWindow)Invoke(w, "CreateDlaAtlas")!;
        static Task Build(ParameterAtlasWindow a) => (Task)typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(a, null)!;
        static decimal[] Selected(ParameterAtlasWindow a) => (decimal[])typeof(ParameterAtlasWindow).GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        static TextBox[] Boxes(ParameterAtlasWindow a) => E<WrapPanel>(a, "FixedPointPanel").Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(Dla3DAtlasWindow a, Fractal3DState s, decimal[] p) => Invoke(a, "ApplyComponents", s, p);
        static void Reject(Dla3DAtlasWindow a, Fractal3DState s, decimal[] p)
        { string before = JsonSerializer.Serialize(s); bool rejected = false; try { Apply(a,s,p); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; } Check(rejected && before == JsonSerializer.Serialize(s), "Invalid parameters must be rejected atomically."); }
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
