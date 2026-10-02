using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyLSystem3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("lsystem3d");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "LSystem3D");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.LSystem3D;
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "Spatial L-systems must be in the shared 3D catalog.");
        var presets = Fractal3DCatalog.GetPresets(kind);
        Check(FractalExplorerWPF.Core.Rendering.LSystemEngine.ParseRules("F → F+F // comment")['F'] == "F+F",
            "The planar laboratory must keep its existing comment syntax.");
        var spatialRules = FractalExplorerWPF.Core.Rendering.LSystemEngine.ParseRules("F = F//F # comment\nA =", spatialCommands: true);
        Check(spatialRules['F'] == "F//F" && spatialRules['A'] == "", "Spatial rolls, comments, and deletion.");
        foreach (var preset in presets)
        {
            var geometry = LSystem3DGeometry.Build(preset.LSystem, CancellationToken.None);
            Check(geometry.Segments.Length > 10, $"Empty preset {preset.SaveName}.");
            Check(geometry.Segments.All(s => s.Radius > 0 && Vector3.Distance(s.Start, s.End) > 0 &&
                Math.Abs(s.Start.X) <= 1.001 && Math.Abs(s.Start.Y) <= 1.001 && Math.Abs(s.Start.Z) <= 1.001), "Fit and thickness bounds.");
            Check(geometry.Capsules.SequenceEqual(LSystem3DGeometry.Build(preset.LSystem, CancellationToken.None).Capsules), "Deterministic geometry.");
            Console.WriteLine($"{preset.SaveName}: {geometry.Segments.Length} segments.");
        }
        var hilbert = presets.Single(p => p.SaveName.Contains("Гильберт")).LSystem;
        for (int n = 1; n <= 4; n++)
        {
            var geometry = LSystem3DGeometry.Build(hilbert with { Generations = n }, CancellationToken.None);
            int points = 1 << (3 * n);
            Check(geometry.Segments.Length == points - 1, "Hilbert segment count.");
            var vertices = geometry.Segments.Select(s => s.Start).Append(geometry.Segments[^1].End).ToArray();
            float step = Vector3.Distance(geometry.Segments[0].Start, geometry.Segments[0].End);
            Vector3 minimum = vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var lattice = vertices.Select(p => (p - minimum) / step).ToArray();
            var grid = lattice.Select(p => (Math.Round(p.X), Math.Round(p.Y), Math.Round(p.Z))).ToHashSet();
            Check(lattice.All(v => Enumerable.Range(0, 3).All(a => Math.Abs(v[a] - Math.Round(v[a])) < .002)), "Hilbert vertices must lie on the lattice.");
            Check(grid.Count == points, $"Hilbert generation {n} revisits lattice vertices.");
            foreach (int axis in new[] { 0, 1, 2 })
                Check(lattice.Select(v => Math.Round(v[axis])).Distinct().Count() == 1 << n, $"Hilbert generation {n} must fill a cube.");
            Check(geometry.Segments.All(s => new[] { Math.Abs(s.End.X - s.Start.X), Math.Abs(s.End.Y - s.Start.Y), Math.Abs(s.End.Z - s.Start.Z) }
                .Count(d => d > 1e-5) == 1), "Hilbert steps must follow one grid axis.");
        }
        // Restoring a branch restores position, rotation, radius and step.
        var simple = new LSystem3DSettings { Axiom = "F[&>!F]F", RulesText = "", Generations = 0 };
        var branch = LSystem3DGeometry.Build(simple, CancellationToken.None).Segments;
        Check(Vector3.Distance(branch[0].End, branch[2].Start) < 1e-6 &&
            Vector3.Distance(branch[0].End - branch[0].Start, branch[2].End - branch[2].Start) < 1e-6 &&
            branch[0].Radius == branch[2].Radius && branch[1].Radius < branch[0].Radius, "Complete turtle stack restoration.");
        foreach (var bad in new[]
        {
            simple with { Axiom = "F]" }, simple with { Axiom = "[F" }, simple with { DrawSymbols = "+" },
            simple with { RulesText = "FF → F" }, simple with { Axiom = "A", RulesText = "A → AAAAAAAAAA", Generations = 12 },
            simple with { Radius = double.NaN }
        })
        {
            bool rejected = false;
            try { LSystem3DGeometry.Build(bad, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Invalid grammar or complexity must fail with a user-facing error.");
        }
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool stopped = false;
            try { LSystem3DGeometry.Build(hilbert, cts.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Geometry cancellation.");
        }

        using var renderer = new Fractal3DRenderer();
        foreach (var preset in presets)
        {
            var bitmap = await renderer.RenderAsync(preset, 256, 256, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 256, 256), $"Blank GPU preset {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output, $"preset-{Array.IndexOf(presets.ToArray(), preset)}.png"), bitmap);
        }
        var state = presets[0].Clone();
        var full = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        object? cached = typeof(Fractal3DRenderer).GetField("_lSystemGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
        state.LSystem = state.LSystem with { Growth = 0 };
        var empty = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        state.LSystem = state.LSystem with { Growth = .45 };
        var half = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Check(!empty.SequenceEqual(half) && !full.SequenceEqual(half), "Growth must change displayed geometry.");
        Check(ReferenceEquals(cached, typeof(Fractal3DRenderer).GetField("_lSystemGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)),
            "Growth must reuse the full geometry and BVH.");
        foreach (var source in Enum.GetValues<LSystem3DColorSource>())
        {
            state.LSystem = state.LSystem with { ColorSource = source, Growth = 1 };
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None)), 128, 128), "L-system color source.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = presets.Single(p => p.SaveName.Contains("Гильберт")).Clone();
            styled.ShadingStyle = style;
            var bitmap = await renderer.RenderAsync(styled, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 128, 128), $"L-system style {style}.");
            WriteLSystemPng(Path.Combine(output, $"style-{style}.png"), bitmap);
        }
        state = presets[0].Clone();
        state.SoftShadows = true;
        Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None)), 128, 128), "Capsule shadows.");
        await VerifyLSystemProbes(renderer, state);
        state.CameraDistance = 5; state.LSystem = state.LSystem with { Growth = .375 };
        var store = new Fractal3DSaveStore(kind); state.SaveName = "spatial-rules-test";
        store.Save(state);
        var saved = store.Load().Single();
        Check(saved.LSystem == state.LSystem && saved.CameraDistance == 5, "Save grammar, growth, and view.");
        var local = CloudSaveRepository.ListLocal(out int unreadable).Single(s => s.Category == Fractal3DCatalog.GetDefinition(kind).SaveCategory);
        Check(unreadable == 0 && local.JsonData.Contains("RulesText"), "Cloud envelope must include grammar.");

        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Absolute);
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(saved);
            Check(window.CaptureState("test").LSystem == saved.LSystem, "WPF fields must roundtrip.");
            var editor = (LSystem3DEditor)window.FindName("LSystemEditor");
            Check(editor.Visibility == Visibility.Visible, "Editor must be visible.");
            var generations = (Slider)editor.FindName("GenerationSlider");
            generations.Value = 3;
            Check(window.CaptureState("test").LSystem.Generations == 3, "Live generation control.");
            var growth = (Slider)editor.FindName("GrowthSlider");
            growth.Value = 50;
            Check(window.CaptureState("test").LSystem.Growth == .5, "Live growth control.");
            var yaw = (Slider)editor.FindName("YawSlider"); yaw.Value = 72;
            Check(window.CaptureState("test").LSystem.Yaw == 72, "Local angle dial binding.");
            var rules = (TextBox)editor.FindName("RulesBox");
            var apply = (Button)editor.FindName("ApplyButton");
            rules.Text = "A = F//A # spatial rolls must survive";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int wait = 0; wait < 200 && !apply.IsEnabled; wait++) await Task.Delay(10);
            Check(editor.Capture().RulesText == rules.Text, "Apply must accept the checked grammar.");
            rules.Text = "A → [F";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int wait = 0; wait < 200 && !apply.IsEnabled; wait++) await Task.Delay(10);
            Check(editor.Capture().RulesText != rules.Text && ((TextBlock)editor.FindName("RuleStatus")).Text.Contains("незакрытые"),
                "Invalid edited rules must retain the previous applied grammar with an inline error.");
            ((Button)editor.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture().RulesText == saved.LSystem.RulesText, "Rules undo.");
            var play = (Button)editor.FindName("PlayButton");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.IsPlaying, "Playback must start.");
            double beforeGrowth = editor.Capture().Growth;
            await Task.Delay(25); editor.OnFrameDisplayed(364, 1000);
            Check(editor.Capture().Growth > beforeGrowth && editor.IsPlaying, "Playback advances after a displayed frame.");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!editor.IsPlaying, "Playback pause.");
            editor.Load(presets[0].LSystem);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240, 800)); root.Arrange(new Rect(0, 0, 1240, 800)); root.UpdateLayout();
            // A static view of the real controls with a representative GPU image, without showing a window.
            ((Image)window.FindName("CanvasImage")).Source = await renderer.RenderAsync(presets[0], 800, 740, null, CancellationToken.None);
            root.UpdateLayout();
            var image = new RenderTargetBitmap(1240, 800, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            WriteLSystemPng(Path.Combine(output, "editor.png"), image);
            // Also verify the three graphic angle inputs further down the real scrollable panel.
            var turn = (Slider)editor.FindName("PitchSlider");
            turn.BringIntoView(); root.UpdateLayout();
            var angles = new RenderTargetBitmap(1240, 800, 96, 96, PixelFormats.Pbgra32); angles.Render(root);
            WriteLSystemPng(Path.Combine(output, "editor-angles.png"), angles);
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (lsystem3d): grammar, turtle stack, Hilbert lattice, caps/BVH probes, six GPU presets, nine styles, growth/cache, cancellation, saves/cloud, WPF controls. Images: {output}");
    }

    private static void WriteLSystemPng(string path, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static async Task VerifyLSystemProbes(Fractal3DRenderer renderer, Fractal3DState state)
    {
        var geometry = LSystem3DGeometry.Build(state.LSystem, CancellationToken.None);
        var camera = Fractal3DCamera.Build(state);
        for (int y = 24; y < 120; y += 12)
        for (int x = 24; x < 120; x += 12)
        {
            Vector3 direction = Fractal3DCamera.PixelRay(state, x + .5, y + .5, 144, 144);
            // Independent brute force: solve perpendicular cylinder quadratic and clip spherical caps.
            double nearest = double.PositiveInfinity;
            foreach (var s in geometry.Segments)
            {
                Vector3 axis = Vector3.Normalize(s.End - s.Start);
                Vector3 offset = camera.Position - s.Start;
                double along = Vector3.Dot(offset, axis), rayAlong = Vector3.Dot(direction, axis);
                Vector3 perpendicular = offset - axis * (float)along;
                Vector3 rayPerp = direction - axis * (float)rayAlong;
                double length = Vector3.Distance(s.Start, s.End);
                Roots(Vector3.Dot(rayPerp, rayPerp), 2 * Vector3.Dot(perpendicular, rayPerp), Vector3.Dot(perpendicular, perpendicular) - s.Radius * s.Radius,
                    t => along + t * rayAlong >= 0 && along + t * rayAlong <= length);
                Cap(s.Start, true); Cap(s.End, false);
                void Cap(Vector3 center, bool first)
                {
                    Vector3 o = camera.Position - center;
                    Roots(1, 2 * Vector3.Dot(o, direction), Vector3.Dot(o, o) - s.Radius * s.Radius,
                        t => first ? along + t * rayAlong <= 0 : along + t * rayAlong >= length);
                }
                void Roots(double a, double b, double c, Func<double, bool> accept)
                {
                    if (a < 1e-14) return;
                    double disc = b * b - 4 * a * c; if (disc < 0) return;
                    foreach (double t in new[] { (-b - Math.Sqrt(disc)) / (2 * a), (-b + Math.Sqrt(disc)) / (2 * a) })
                        if (t > 0 && accept(t)) nearest = Math.Min(nearest, t);
                }
            }
            double probe = await renderer.ProbeDistanceAsync(state, x + .5, y + .5, 144, 144, CancellationToken.None);
            Check(double.IsPositiveInfinity(nearest) ? double.IsNaN(probe) : double.IsFinite(probe) && Math.Abs(probe - nearest) < .002,
                $"Capsule BVH probe disagrees at {x},{y}: {probe} vs {nearest}.");
        }
    }
}
