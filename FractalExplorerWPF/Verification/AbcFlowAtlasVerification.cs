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
    private static async Task VerifyAbcFlowAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("AbcFlowAtlas");
        EnsureWindowThemeStyles();
        const Fractal3DKind kind = Fractal3DKind.AbcFlow;
        var configurations = from seedGroup in Enum.GetValues<AbcSeedGroup>() from display in Enumerable.Range(0, 4)
            from lyapunov in new[] { false, true } select State(seedGroup, display, lyapunov);
        foreach (var source in configurations.Concat(Fractal3DCatalog.GetPresets(kind)))
        {
            var atlas = new AbcFlowAtlasWindow(source);
            try
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray(); var boxes = FixedBoxes(atlas); var s = source.AbcFlow;
                bool phase = s.ShowParticles && !s.SectionOnly, lyapunov = source.ColoringMode == Fractal3DColoringMode.OrbitTrap;
                Check(names.Length == 11 + (phase ? 1 : 0) + (s.SectionOnly ? 1 : 0) + (lyapunov ? 1 : 0) && boxes.All(NumericInput.IsRegistered),
                    "ABC atlas needs validated active numeric fields.");
                Check(names.Contains("Фаза частиц · доля времени") == phase && names.Contains("Положение сечения, рад") == s.SectionOnly &&
                    names.Contains("Диапазон окраски ±λ(t)") == lyapunov, "Inactive particle, section or coloring fields leaked into the atlas.");
                Check(x.SelectedIndex == 0 && y.SelectedIndex == 1, "Default axes must compare A and B.");
                for (int i = 2; i < names.Length; i++)
                {
                    x.SelectedIndex = i; NumericInput.CommitAll(atlas);
                    Check(boxes.Count(b => b.IsEnabled) == names.Length - 2 && x.SelectedIndex != y.SelectedIndex, "Axis changes lost fixed fields.");
                }
                var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas rounded source doubles or fixed integration settings.");
                var point = Selected(atlas);
                for (int i = 0; i < point.Length; i++)
                {
                    var bounds = ((decimal Minimum, decimal Maximum))typeof(AbcFlowAtlasWindow)
                        .GetMethod("ComponentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [i])!;
                    foreach (decimal edge in new[] { bounds.Minimum, bounds.Maximum })
                    {
                        var values = (decimal[])point.Clone(); values[i] = edge;
                        var target = source.Clone(); Apply(atlas, target, values); target.AbcFlow.Validate();
                        if (i == 8) Check(target.AbcFlow.Samples == (int)edge, "Explicit sample counts must survive preview limits.");
                    }
                    var invalid = (decimal[])point.Clone(); invalid[i] = bounds.Maximum + 1; Reject(atlas, source.Clone(), invalid);
                }
                foreach (int integer in new[] { 3, 8 })
                { var p = (decimal[])point.Clone(); p[integer] += .5m; Reject(atlas, source.Clone(), p); }
                foreach (AbcFlowSettings wrong in new[] { s with { Group = (AbcSeedGroup)(((int)s.Group + 1) % 4) },
                    s with { SectionAxis = (AbcSectionAxis)(((int)s.SectionAxis + 1) % 3) }, s with { PositiveCrossings = !s.PositiveCrossings },
                    s with { SectionOnly = !s.SectionOnly }, s with { ShowTrails = !s.ShowTrails }, s with { ShowParticles = !s.ShowParticles } })
                { var target = source.Clone(); target.AbcFlow = wrong; Reject(atlas, target, point); }
                var wrongColor = source.Clone(); wrongColor.ColoringMode = lyapunov ? Fractal3DColoringMode.Escape : Fractal3DColoringMode.OrbitTrap;
                Reject(atlas, wrongColor, point);
            }
            finally { atlas.Close(); }
        }

        // Playback advances the UI toward the next frame; the atlas freezes the accepted frame's phase.
        var animated = new Fractal3DWindow(kind);
        try
        {
            new System.Windows.Interop.WindowInteropHelper(animated).EnsureHandle();
            var editor = Element<AbcFlowEditor>(animated, "AbcFlowEditor"); editor.Start();
            var shown = animated.CaptureState("shown"); shown.AbcFlow = shown.AbcFlow with { Phase = .6123456789012345 };
            editor.OnFrameDisplayed([], shown);
            Check(editor.Capture().Phase != shown.AbcFlow.Phase, "Playback test must have a pending phase.");
            var atlas = CreateAtlas(animated);
            try
            {
                Check(!editor.IsPlaying && editor.Capture().Phase == shown.AbcFlow.Phase, "Opening the atlas must freeze the displayed phase exactly.");
                var selected = animated.CaptureState("selected"); atlas.ApplySelectionTo(selected);
                Check(selected.AbcFlow.Phase == shown.AbcFlow.Phase, "The atlas captured a future particle phase.");
            }
            finally { atlas.Close(); }
            editor.Load(shown.AbcFlow with { Phase = .2 }); editor.Start(); editor.StopAtDisplayedPhase();
            Check(editor.Capture().Phase == .2, "Loading a state must clear stale displayed phases.");
        }
        finally { animated.Close(); }
        Console.WriteLine("ABC atlas: parameter bounds/precision and displayed-phase pause passed.");

        // Small grids keep the real integration count and step. No long/full simulation suite.
        for (int scenario = 0; scenario < 3; scenario++)
        {
            Console.WriteLine($"ABC atlas: small GPU grid {scenario + 1}/3.");
            var source = State(AbcSeedGroup.Neighbours, scenario == 2 ? 3 : 2, false);
            source.AbcFlow = source.AbcFlow with { Samples = 64, SeedCount = 2, Spread = .15, TimeStep = .04,
                A = 1, B = scenario == 2 ? 0 : .7, C = scenario == 2 ? 0 : .43,
                SectionAxis = AbcSectionAxis.X, StartX = .2, StartY = .1, StartZ = .3, Thickness = .025 };
            string original = JsonSerializer.Serialize(source);
            var main = new Fractal3DWindow(kind); AbcFlowAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<AbcFlowEditor>(main, "AbcFlowEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible, "ABC editor must expose its atlas.");
                var before = main.CaptureState("view");
                Check(before.AbcFlow == source.AbcFlow, "Loading rounded ABC doubles.");
                atlas = CreateAtlas(main); Check(atlas.Owner == main, "Atlas lost its owner.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var names = x.Items.Cast<string>().ToArray();
                x.SelectedIndex = scenario == 0 ? 0 : scenario == 1 ? 3 : Array.IndexOf(names, "Положение сечения, рад");
                y.SelectedIndex = scenario == 0 ? 1 : scenario == 1 ? 8 : 5;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Range fields need NumericInput.");
                minX.Text = scenario == 0 ? ".4" : scenario == 1 ? "1" : "-2";
                maxX.Text = scenario == 0 ? "1.2" : scenario == 1 ? "2" : "2";
                minY.Text = scenario == 0 ? "0" : scenario == 1 ? "64" : "-.6";
                maxY.Text = scenario == 0 ? ".8" : scenario == 1 ? "65" : ".6";
                var fixedC = FixedBoxes(atlas)[2]; fixedC.Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(fixedC) && NumericInput.GetHasNotice(fixedC), "Invalid fixed coefficient must recover with feedback.");
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (scenario == 1 ? 4 : 9) && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "ABC previews are incomplete or repeat integer rows.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Atlas changed the selected integration horizon, seeds, phase or section.");
                    // Y grows upward: sample 65 is in the top row, seed count 2 in its right column.
                    Click((Button)tiles.Children[scenario == 1 ? 1 : tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "ABC variants produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed unrelated view settings or rounded ABC values.");
                Check(after.AbcFlow.TimeStep == before.AbcFlow.TimeStep && after.AbcFlow.PlaybackSpeed == before.AbcFlow.PlaybackSpeed && !editor.IsPlaying,
                    "Application changed the fixed step/speed or resumed playback.");
                if (scenario == 1) Check(after.AbcFlow.SeedCount == 2 && after.AbcFlow.Samples == 65, "Selected integer integration counts were approximated.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                main.LoadState(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State);
                Check(main.CaptureState("restored").AbcFlow == after.AbcFlow, "Save/load lost exact ABC parameters.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(main); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without application changed the view.");
                Check(JsonSerializer.Serialize(source) == original, "Rendering mutated the source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
        }
        Console.WriteLine("PASS (abc-flow-atlas): seed groups, display modes, active axes, exact integration parameters, displayed phase, GPU previews, apply, saves and close.");

        static Fractal3DState State(AbcSeedGroup group, int display, bool lyapunov)
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            state.ColoringMode = lyapunov ? Fractal3DColoringMode.OrbitTrap : Fractal3DColoringMode.IterationIndex;
            state.AbcFlow = new() { Group = group, ShowTrails = display is 0 or 2, ShowParticles = display is 1 or 2, SectionOnly = display == 3,
                A = .9123456789012345, B = -.7123456789012345, C = .4123456789012345,
                StartX = .3123456789012345, StartY = -.2123456789012345, StartZ = .1123456789012345,
                Spread = .012345678901234567, TimeStep = .012345678901234567, Thickness = .012345678901234567,
                Phase = .3123456789012345, PlaybackSpeed = 2.123456789012345, SectionPosition = .2123456789012345, LyapunovRange = .3123456789012345 };
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var preview = state.Clone(); preview.Iterations = Math.Clamp(preview.Iterations, 1, 32);
            preview.MaxSteps = Math.Min(preview.MaxSteps, 160); preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false; return preview;
        }
        static AbcFlowAtlasWindow CreateAtlas(Fractal3DWindow main) => (AbcFlowAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateAbcFlowAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static decimal[] Selected(ParameterAtlasWindow atlas) => (decimal[])typeof(ParameterAtlasWindow)
            .GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
        static TextBox[] FixedBoxes(ParameterAtlasWindow window) => Element<WrapPanel>(window, "FixedPointPanel").Children.OfType<StackPanel>()
            .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
        static void Apply(AbcFlowAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(AbcFlowAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(AbcFlowAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Invalid/incompatible values must be rejected atomically.");
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
