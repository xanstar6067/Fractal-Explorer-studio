using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private static async Task VerifyCubeRulesAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("cube-rules");
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "CubeRuleVerification");
        Directory.CreateDirectory(output);
        EnsureWindowThemeStyles();
        Check(CubeRule.Count(CubeRule.Menger) == 20 && CubeRule.Count(CubeRule.Vicsek) == 7 && CubeRule.Count(CubeRule.Cantor) == 8,
            "Classic rules must contain 20, 7 and 8 cubes.");
        Check(CubeRule.Set(0, 0, 0, 0, true, true, true, true) == CubeRule.Cantor,
            "Mirrored corner edits must reach all eight corners.");
        using var renderer = new Fractal3DRenderer();
        Fractal3DKind[] kinds = [Fractal3DKind.MengerSponge, Fractal3DKind.Vicsek, Fractal3DKind.CantorDust];
        foreach (var kind in kinds)
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            Check(state.CubeRuleMask is null && !JsonSerializer.Serialize(state).Contains("CubeRuleMask"), "Legacy states must retain the classic shader path.");
            var classic = await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(classic), 128, 128), $"Empty classic {kind}.");
            WriteLSystemPng(Path.Combine(output, $"classic-{kind}.png"), classic);
            var window = new Fractal3DWindow(kind);
            try
            {
                var editor = (CubeRuleEditor)window.FindName("CubeEditor");
                Check(editor.Visibility == Visibility.Visible && editor.Capture() is null, "Cube editor must start with its own classic rule.");
                var layers = (UniformGrid)editor.FindName("Layers");
                var cells = layers.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<UniformGrid>())
                    .SelectMany(p => p.Children.OfType<ToggleButton>()).ToArray();
                Check(cells.Length == 27 && cells.Count(c => c.IsChecked == true) == CubeRule.Count(CubeRule.Classic(kind)), "Editor layer orientation/count mismatch.");
                Click((Button)editor.FindName("ClearButton"));
                Check(window.CaptureState("empty").CubeRuleMask == 0, "An empty rule must be distinct from legacy null.");
                ((CheckBox)editor.FindName("MirrorXBox")).IsChecked = true;
                ((CheckBox)editor.FindName("MirrorYBox")).IsChecked = true;
                ((CheckBox)editor.FindName("MirrorZBox")).IsChecked = true;
                cells.Single(c => (int)c.Tag == 0).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(editor.Capture() == CubeRule.Cantor, "Cell clicks must apply all selected mirrors.");
                Click((Button)editor.FindName("UndoButton"));
                Check(editor.Capture() == 0, "Undo must restore the entire mirrored edit.");
                Click((Button)editor.FindName("UndoButton"));
                Check(editor.Capture() is null, "Undo must restore the original legacy path.");
                for (int n = 0; n < 10; n++)
                {
                    Click((Button)editor.FindName("RandomButton"));
                    uint generated = editor.Capture()!.Value;
                    Check(generated != 0 && generated != CubeRule.All, "Random rules must be nontrivial.");
                    for (int i = 0; i < 27; i++)
                        if ((generated & (1u << i)) != 0)
                            Check((CubeRule.Set(0, i % 3, i / 3 % 3, i / 9, true, true, true, true) & generated) ==
                                CubeRule.Set(0, i % 3, i / 3 % 3, i / 9, true, true, true, true), "Randomization must honor all mirror axes.");
                }
                state.CubeRuleMask = (1u << 26) | (1u << 13) | 1u;
                state.CubeThickness = .87;
                window.LoadState(state);
                var captured = window.CaptureState("custom");
                Check(captured.CubeRuleMask == state.CubeRuleMask && captured.CubeThickness == .87, "Window must retain exact custom mask and thickness.");
                Click((Button)editor.FindName("NextLevelButton"));
                Check(window.CaptureState("next").Iterations == state.Iterations + 1, "Generation buttons must update the registered numeric field.");
                Click((Button)editor.FindName("PreviousLevelButton"));
                var store = new Fractal3DSaveStore(kind);
                captured.SaveName = "Rule";
                store.Save(captured);
                Check(store.Load().Single().CubeRuleMask == state.CubeRuleMask, "Custom rule save roundtrip failed.");
                Check(captured.Clone().CubeRuleMask == state.CubeRuleMask, "Snapshot lost custom rule.");
                state.CubeRuleMask = uint.MaxValue;
                Fractal3DCatalog.Mode(kind).Validate(state);
                Check(state.CubeRuleMask == CubeRule.All, "Imported masks must be restricted to 27 bits.");
                Click((Button)editor.FindName("ResetButton"));
                Check(editor.Capture() is null, "Reset must restore this mode's legacy formula.");
                ((ComboBox)editor.FindName("RuleBox")).SelectedIndex = kind == Fractal3DKind.MengerSponge ? 2 : 0;
                Check(editor.Capture().HasValue, "A different built-in rule must reach the custom shader.");
                var iterations = (TextBox)window.FindName("IterationsBox");
                iterations.Text = "999"; NumericInput.CommitAll(window);
                Check(window.CaptureState("bounded").Iterations == 12 && iterations.Text == "12",
                    "Custom depth must be limited through NumericInput.");
                Click((Button)editor.FindName("ResetButton"));
                iterations.Text = "64"; NumericInput.CommitAll(window);
                Check(window.CaptureState("classic depth").Iterations == 64,
                    "Returning to the classic rule must restore its original numeric range.");
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1240, 900)); root.Arrange(new Rect(0, 0, 1240, 900)); root.UpdateLayout();
                Check(cells.All(c => c.ActualWidth >= 20 && c.ActualHeight >= 20), "Layer cells must remain usable in the existing panel.");
                if (kind == Fractal3DKind.MengerSponge)
                {
                    var ui = new RenderTargetBitmap(300, (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    ui.Render(editor);
                    WriteLSystemPng(Path.Combine(output, "editor.png"), ui);
                }
            }
            finally { window.Close(); }
        }
        var cloud = CloudSaveRepository.ListLocal(out int unreadable);
        Check(unreadable == 0 && cloud.Count == 3 && cloud.All(s => s.JsonData.Contains("CubeRuleMask")),
            "All three categories must retain custom rules in cloud payloads.");
        // Independent construction: explicitly replace every retained cube with smaller boxes.
        // Intersections use only the XY footprint and the front Z face, without DE or periodic folding.
        uint asymmetric = (1u << 0) | (1u << 4) | (1u << 10) | (1u << 17) | (1u << 26);
        uint[] masks = [0, CubeRule.All, CubeRule.Menger, CubeRule.Vicsek, CubeRule.Cantor, asymmetric, 1u << 26];
        int rays = 0;
        foreach (var kind in kinds)
        foreach (uint mask in masks)
        foreach (int levels in new[] { 1, 2, 3 })
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            state.CubeRuleMask = mask; state.Iterations = levels; state.CubeThickness = 1;
            state.CameraYaw = state.CameraPitch = state.CameraRoll = 0;
            state.CameraDistance = 3; state.TargetZ = 0;
            state.Detail = .05; state.MaxSteps = 1024;
            var boxes = CubeReferenceBoxes(mask, levels);
            foreach ((double x, double y) in new[] { (-.79, -.61), (.14, .18), (.72, .79), (-.57, .75), (.61, -.79) })
            {
                state.TargetX = x; state.TargetY = y;
                double expected = boxes.Where(b => Math.Abs(x - b.X) < b.Half && Math.Abs(y - b.Y) < b.Half)
                    .Select(b => 3 - b.Z - b.Half).DefaultIfEmpty(double.NaN).Min();
                double actual = await renderer.ProbeDistanceAsync(state, 256, 256, 512, 512, CancellationToken.None);
                Check(double.IsNaN(expected) ? double.IsNaN(actual) : double.IsFinite(actual) && Math.Abs(expected - actual) < .004,
                    $"{kind} mask={mask:X7} level={levels} at {x},{y}: expected {expected}, GPU {actual}.");
                rays++;
            }
        }
        foreach (var angles in new[] { (90d, 0d), (180d, 0d), (-90d, 55d), (37d, -23d) })
        foreach (uint mask in new[] { asymmetric, CubeRule.Menger, CubeRule.Vicsek })
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.MengerSponge);
            state.CubeRuleMask = mask; state.Iterations = 2;
            state.CameraYaw = angles.Item1; state.CameraPitch = angles.Item2;
            state.CameraDistance = 3; state.Detail = .05; state.MaxSteps = 1024;
            var boxes = CubeReferenceBoxes(mask, 2);
            var position = Fractal3DCamera.Build(state).Position;
            foreach (int x in new[] { 190, 256, 300 })
            foreach (int y in new[] { 200, 256, 290 })
            {
                var direction = Fractal3DCamera.PixelRay(state, x + .5, y + .5, 512, 512);
                double nearest = double.PositiveInfinity;
                foreach (var box in boxes)
                {
                    double low = 0, high = double.PositiveInfinity;
                    Slab(position.X, direction.X, box.X, box.Half);
                    Slab(position.Y, direction.Y, box.Y, box.Half);
                    Slab(position.Z, direction.Z, box.Z, box.Half);
                    if (low <= high) nearest = Math.Min(nearest, low);
                    void Slab(double origin, double ray, double center, double half)
                    {
                        if (Math.Abs(ray) < 1e-12) { if (Math.Abs(origin - center) > half) high = -1; return; }
                        double a = (center - half - origin) / ray, b = (center + half - origin) / ray;
                        low = Math.Max(low, Math.Min(a, b)); high = Math.Min(high, Math.Max(a, b));
                    }
                }
                double actual = await renderer.ProbeDistanceAsync(state, x + .5, y + .5, 512, 512, CancellationToken.None);
                Check(double.IsPositiveInfinity(nearest) ? double.IsNaN(actual) : Math.Abs(actual - nearest) < .004,
                    $"Oblique {angles} mask={mask:X7}: expected {nearest}, GPU {actual}.");
                rays++;
            }
        }
        // A camera inside two face-adjacent kept cells must see the external wall, not their shared face.
        var inside = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.MengerSponge);
        inside.CubeRuleMask = (1u << 4) | (1u << 13); inside.Iterations = 1;
        inside.CameraYaw = inside.CameraPitch = inside.CameraRoll = 0;
        inside.CameraDistance = .1; inside.TargetX = inside.TargetY = inside.TargetZ = 0;
        inside.Detail = .05; inside.MaxSteps = 1024;
        double exit = await renderer.ProbeDistanceAsync(inside, 256, 256, 512, 512, CancellationToken.None);
        Check(Math.Abs(exit - 1.1) < .004, $"Adjacent kept cells must have no internal face: {exit}.");
        inside.CubeRuleMask = CubeRule.All; inside.Iterations = 64;
        Check(Math.Abs(await renderer.ProbeDistanceAsync(inside, 256, 256, 512, 512, CancellationToken.None) - 1.1) < .004,
            "A full rule must remain a solid cube at any requested depth, including from inside.");
        var custom = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.MengerSponge);
        custom.CubeRuleMask = asymmetric; custom.Iterations = 2;
        custom.SoftShadows = false;
        var bitmap = await renderer.RenderAsync(custom, 160, 160, null, CancellationToken.None);
        Check(HasFractal3DStructure(Pixels(bitmap), 160, 160), "Custom rule must produce a visible 3D frame.");
        WriteLSystemPng(Path.Combine(output, "custom.png"), bitmap);
        custom.CubeRuleMask = CubeRule.All ^ (1u << 13);
        custom.Iterations = 2;
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            custom.ShadingStyle = style;
            var styled = await renderer.RenderAsync(custom, 96, 96, null, CancellationToken.None);
            WriteLSystemPng(Path.Combine(output, $"style-{style}.png"), styled);
            if (style == Fractal3DShadingStyle.Mirror)
            {
                // A flat mirror reflects the dark default sky and need not have a bright red-channel range.
                // Compare it directly with an empty scene under the same lighting instead.
                var empty = custom.Clone(); empty.CubeRuleMask = 0;
                byte[] sky = Pixels(await renderer.RenderAsync(empty, 96, 96, null, CancellationToken.None));
                byte[] pixels = Pixels(styled);
                int changed = Enumerable.Range(0, 96 * 96).Count(i =>
                    Math.Abs(pixels[i * 4] - sky[i * 4]) + Math.Abs(pixels[i * 4 + 1] - sky[i * 4 + 1]) +
                    Math.Abs(pixels[i * 4 + 2] - sky[i * 4 + 2]) > 3);
                Check(changed > 96 * 96 / 30, "Mirror surface must differ from the empty sky.");
            }
            else Check(HasFractal3DStructure(Pixels(styled), 96, 96), $"Empty custom rule style {style}.");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await renderer.RenderAsync(custom, 2048, 2048, null, canceled.Token); throw new Exception("Canceled render succeeded."); }
        catch (OperationCanceledException) { }
        Console.WriteLine($"PASS (cube-rules): {rays} analytic GPU rays, internal faces, classic frames, editor, mirrors, undo, generations, saves and cancellation. PNG: {output}");

        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static List<(double X, double Y, double Z, double Half)> CubeReferenceBoxes(uint mask, int levels)
    {
        var boxes = new List<(double X, double Y, double Z, double Half)> { (0, 0, 0, 1) };
        for (int level = 0; level < levels; level++)
        {
            var next = new List<(double, double, double, double)>();
            foreach (var box in boxes)
            for (int z = 0; z < 3; z++)
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                if ((mask & (1u << (x + 3 * y + 9 * z))) != 0)
                    next.Add((box.X + (x - 1) * box.Half * 2 / 3, box.Y + (y - 1) * box.Half * 2 / 3,
                        box.Z + (z - 1) * box.Half * 2 / 3, box.Half / 3));
            boxes = next;
        }
        return boxes;
    }
}
