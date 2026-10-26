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
    private static async Task VerifyCubeRuleAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("CubeRuleAtlas");
        EnsureWindowThemeStyles();
        var kinds = Fractal3DCatalog.Modes.Where(m => m.HasCubeRule).Select(m => m.Kind).ToArray();
        Check(kinds.Length == 3, "Cube-rule atlas must cover all three registered cube modes.");
        const uint asymmetric = (1u << 26) | (1u << 13) | 1u;
        foreach (var kind in kinds)
        {
            var configurations = new uint?[] { null, 0, CubeRule.All, CubeRule.Classic(kind), asymmetric }
                .SelectMany(mask => new[] { 1, mask.HasValue ? 12 : 64 }.Select(level => State(kind, mask, level)))
                .Concat(Fractal3DCatalog.GetPresets(kind));
            foreach (var source in configurations)
            {
                var atlas = new CubeRuleAtlasWindow(source);
                try
                {
                    Check(atlas.Rules.Count is >= 7 and <= 8 && atlas.Rules.Select(r => r.Mask).Distinct().Count() == atlas.Rules.Count,
                        "Named rules must be unique without discarding a custom source.");
                    Check(atlas.Rules[0].Mask == source.CubeRuleMask && atlas.Rules.Any(r => r.Mask is null) &&
                        atlas.Rules.Any(r => r.Mask == 0) && atlas.Rules.Any(r => r.Mask == CubeRule.All), "Current, classic, empty and full rules must stay distinct.");
                    foreach (uint preset in new[] { CubeRule.Menger, CubeRule.Vicsek, CubeRule.Cantor, CubeRule.Cantor | (1u << 13), CubeRule.All ^ (1u << 13) })
                        Check(atlas.Rules.Any(r => r.Mask == (preset == CubeRule.Classic(kind) ? null : (uint?)preset)), "Missing named rule.");
                    var unchanged = source.Clone(); atlas.ApplySelectionTo(unchanged);
                    Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(source), "Untouched atlas changed precision or null/custom mask semantics.");
                    Check(new[] { "MinRealBox", "MaxRealBox", "MinImaginaryBox", "MaxImaginaryBox" }
                        .All(n => NumericInput.IsRegistered(Element<TextBox>(atlas, n))), "All rule/generation ranges need NumericInput.");
                    for (int rule = 0; rule < atlas.Rules.Count; rule++) foreach (int level in new[] { 1, 12, 13, 64 })
                    {
                        decimal[] point = [rule + 1, level]; var target = source.Clone();
                        if (atlas.Rules[rule].Mask.HasValue && level > 12) { Reject(atlas, target, point); continue; }
                        Apply(atlas, target, point);
                        var expected = source.Clone(); expected.CubeRuleMask = atlas.Rules[rule].Mask; expected.Iterations = level;
                        Check(JsonSerializer.Serialize(target) == JsonSerializer.Serialize(expected), "Rule/depth application changed unrelated properties or capped explicit depth.");
                    }
                    foreach (decimal[] invalid in new decimal[][] { [0, 1], [atlas.Rules.Count + 1, 1], [1.5m, 1], [1, 0], [1, 65], [1, 1.5m] })
                        Reject(atlas, source.Clone(), invalid);
                    var wrong = source.Clone(); wrong.Kind = Fractal3DKind.Mandelbulb; Reject(atlas, wrong, [1, 1]);
                }
                finally { atlas.Close(); }
            }
            var bad = State(kind, uint.MaxValue, 1); string original = JsonSerializer.Serialize(bad); bool rejected = false;
            try { new CubeRuleAtlasWindow(bad).Close(); } catch (ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(bad) == original, "Invalid source masks must be rejected without mutation.");
        }
        Console.WriteLine("Cube atlas: named variants, exact source, generation limits and atomic application passed.");

        // Four to six tiny cells per mode; no full geometry/renderer suite.
        foreach (var kind in kinds)
        {
            bool high = kind == Fractal3DKind.Vicsek;
            var source = State(kind, high ? null : asymmetric, high ? 64 : 2);
            var main = new Fractal3DWindow(kind); CubeRuleAtlasWindow? atlas = null;
            try
            {
                main.LoadState(source); new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<CubeRuleEditor>(main, "CubeEditor");
                Check(editor.Visibility == Visibility.Visible && Element<Button>(editor, "AtlasButton").Visibility == Visibility.Visible,
                    "All cube modes must expose the atlas in their shared editor.");
                Check(main.CaptureState("view").CubeThickness == source.CubeThickness, "Loading rounded the saved thickness.");
                // Existing undo entries and mirror choices must survive the atlas.
                editor.ApplyAtlasRule(CubeRule.All); editor.ApplyAtlasRule(source.CubeRuleMask);
                Element<CheckBox>(editor, "MirrorXBox").IsChecked = true;
                var before = main.CaptureState("view"); string original = JsonSerializer.Serialize(before);
                atlas = CreateAtlas(main); Check(atlas.Owner == main, "Atlas must belong to the original window.");
                int empty = atlas.Rules.Select(r => r.Mask).ToList().IndexOf(0) + 1;
                int full = atlas.Rules.Select(r => r.Mask).ToList().IndexOf(CubeRule.All) + 1;
                int classic = atlas.Rules.Select(r => r.Mask).ToList().IndexOf(null) + 1;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
                minX.Text = "-100"; maxX.Text = "999"; minY.Text = "-100"; maxY.Text = "999"; NumericInput.CommitAll(atlas);
                Check(minX.Text == "1" && maxX.Text == atlas.Rules.Count.ToString() && minY.Text == "1" && maxY.Text == "64", "Rule and generation bounds are independent.");
                SetRange(atlas, kind == Fractal3DKind.MengerSponge ? empty : high ? 1 : classic - 1,
                    kind == Fractal3DKind.MengerSponge ? full : high ? 2 : classic, high ? 11 : 1, high ? 13 : 2);
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == (high ? 6 : 4) && tiles.Children.OfType<Button>().Count(b => b.IsEnabled) == (high ? 5 : 4),
                    "Rule grids must retain disabled over-depth cells and render valid combinations.");
                Check(!high || !((Button)tiles.Children[1]).IsEnabled, "A custom rule above 12 must be unavailable without clamping.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(Element<TextBlock>(atlas, "SelectedText").Text.Contains(atlas.Rules[(int)Selected(atlas)[0] - 1].Name),
                    "Selection must show a rule name instead of an opaque numeric mask.");
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(Preview(expected), 144, 108, null, null, CancellationToken.None);
                    byte[] actual = Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source);
                    Check(actual.SequenceEqual(reference.Buffer), "Rule atlas differs from direct rendering of the selected mask and depth.");
                    Click((Button)tiles.Children[tiles.Children.Count - 1]);
                    Check(!actual.SequenceEqual(Pixels((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source)), "Different cube rules produced identical previews.");
                }
                expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Application changed camera, appearance, precision or quality.");
                Check(Element<StackPanel>(main, "CubeThicknessPanel").IsEnabled == !after.CubeRuleMask.HasValue &&
                    Element<CheckBox>(editor, "MirrorXBox").IsChecked == true, "Thickness activity or mirror choices were lost.");
                Check(EditorCells(editor).Count(c => c.IsChecked == true) == CubeRule.Count(after.CubeRuleMask ?? CubeRule.Classic(kind)), "Editor cells must follow the selected rule.");
                var store = new Fractal3DSaveStore(kind); var slot = store.Save(after);
                var saved = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State;
                Check(saved.CubeRuleMask == after.CubeRuleMask && saved.Iterations == after.Iterations && saved.CubeThickness == after.CubeThickness,
                    "Save/load lost rule, depth or precise thickness.");
                Click(Element<Button>(editor, "UndoButton"));
                Check(editor.Capture() == before.CubeRuleMask, "Selected rule must be one undo entry.");
                Click(Element<Button>(editor, "UndoButton")); Check(editor.Capture() == CubeRule.All, "Atlas discarded existing undo history.");
                Click(Element<Button>(editor, "UndoButton")); Check(editor.Capture() == source.CubeRuleMask, "Earlier history was lost.");
                main.LoadState(saved); var restored = main.CaptureState("restored");
                Check(restored.CubeRuleMask == saved.CubeRuleMask && restored.CubeThickness == saved.CubeThickness, "Window load changed saved geometry.");
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(main);
                // All combinations unavailable: no GPU work and no stale selection or spinner.
                Check(atlas.Rules.TakeLast(2).All(r => r.Mask.HasValue), "The final two variants must be explicit rules.");
                SetRange(atlas, atlas.Rules.Count - 1, atlas.Rules.Count, 13, 14); await Build(atlas);
                Check(!atlas.IsRendering && !Element<Button>(atlas, "ApplyButton").IsEnabled &&
                    Element<TextBlock>(atlas, "StatusText").Text.Contains("Нет допустимых"), "Unavailable ranges must explain why nothing can be applied.");
                SetRange(atlas, 1, 2, 1, 2); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing/canceling without application changed the view.");
                Check(JsonSerializer.Serialize(before) == original, "Atlas mutated the source snapshot.");
            }
            finally { atlas?.Close(); main.Close(); }
            Console.WriteLine($"Cube atlas: {kind} preview, apply, undo, save/load and cancellation passed.");
        }

        // The default hooks remain ordinary numeric labels and unrestricted samples, without GPU work.
        var numeric = new SierpinskiAtlasWindow(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.SierpinskiTetrahedron));
        try
        {
            var label = typeof(ParameterAtlasWindow).GetMethod("ComponentValueLabel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var available = typeof(ParameterAtlasWindow).GetMethod("UnavailableSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check((string)label.Invoke(numeric, [0, 1.25m, "G5"])! == "1.25" &&
                (string)label.Invoke(numeric, [1, 64m, "G29"])! == "64", "Default numeric labels changed.");
            Check(available.Invoke(numeric, [new decimal[] { 2, 64 }]) is null, "Default numeric samples became unavailable.");
        }
        finally { numeric.Close(); }
        Console.WriteLine("PASS (cube-rule-atlas): three modes, named rules, exact generations, limits, GPU frames, application, history, saves and common atlas defaults.");

        static Fractal3DState State(Fractal3DKind kind, uint? mask, int level)
        {
            var state = Fractal3DCatalog.CreateDefaultState(kind);
            state.CubeRuleMask = mask; state.Iterations = level; state.CubeThickness = .8765432109876543;
            return state;
        }
        static Fractal3DState Preview(Fractal3DState state)
        {
            var p = state.Clone(); p.Iterations = Math.Clamp(p.Iterations, 1, 32);
            p.MaxSteps = Math.Min(p.MaxSteps, 160); p.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(p.Detail);
            p.Ssaa = 1; p.SoftShadows = p.AmbientOcclusion = false;
            p.Iterations = state.Iterations; return p;
        }
        static ToggleButton[] EditorCells(CubeRuleEditor editor) => Element<UniformGrid>(editor, "Layers").Children.OfType<StackPanel>()
            .SelectMany(p => p.Children.OfType<UniformGrid>()).SelectMany(p => p.Children.OfType<ToggleButton>()).ToArray();
        static CubeRuleAtlasWindow CreateAtlas(Fractal3DWindow main) => (CubeRuleAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateCubeRuleAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static decimal[] Selected(ParameterAtlasWindow atlas) => (decimal[])typeof(ParameterAtlasWindow)
            .GetProperty("SelectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(atlas)!;
        static void Apply(CubeRuleAtlasWindow atlas, Fractal3DState target, decimal[] point) => typeof(CubeRuleAtlasWindow)
            .GetMethod("ApplyComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(atlas, [target, point]);
        static void Reject(CubeRuleAtlasWindow atlas, Fractal3DState target, decimal[] point)
        {
            string before = JsonSerializer.Serialize(target); bool rejected = false;
            try { Apply(atlas, target, point); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(target) == before, "Invalid combinations must be rejected atomically.");
        }
        static void SetRange(ParameterAtlasWindow atlas, int minX, int maxX, int minY, int maxY)
        {
            Element<TextBox>(atlas, "MinRealBox").Text = minX.ToString(); Element<TextBox>(atlas, "MaxRealBox").Text = maxX.ToString();
            Element<TextBox>(atlas, "MinImaginaryBox").Text = minY.ToString(); Element<TextBox>(atlas, "MaxImaginaryBox").Text = maxY.ToString();
        }
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

}
