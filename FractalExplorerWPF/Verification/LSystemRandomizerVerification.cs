using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyLSystemRandomizerAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("lsystemrandom");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "LSystemRandomizer");
        Directory.CreateDirectory(output);
        var planar = LSystemPresets.All[3].Definition;
        var spatial = new LSystem3DSettings();
        var distinctRules = new HashSet<string>();
        foreach (var family in Enum.GetValues<LSystemShapeFamily>())
        foreach (bool regular in new[] { false, true })
        foreach (int seed in Enumerable.Range(0, 5))
        {
            var options = new LSystemRandomizationSettings { Family = family, Seed = seed, Symmetric = regular,
                Detail = seed % 5 + 1, Branching = seed % 4 + 2 };
            var a = LSystemRandomizer.Create2D(options, planar, false, CancellationToken.None);
            var b = LSystemRandomizer.Create3D(options, spatial, false, CancellationToken.None);
            Check(a.Segments is >= 2 and <= 50_000 && b.Segments is >= 2 and <= 20_000, "Interactive segment budgets.");
            Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(LSystemRandomizer.Create2D(options, planar, false, CancellationToken.None)) &&
                b == LSystemRandomizer.Create3D(options, spatial, false, CancellationToken.None), "Repeatable seed and settings.");
            Check(a.Planar!.StartColor == planar.StartColor && a.Planar.BackgroundColor == planar.BackgroundColor, "Keep 2D appearance.");
            distinctRules.Add(b.Spatial!.RulesText);
        }
        Check(distinctRules.Count > 12, "The generator must create a variety of actual grammars.");
        foreach (var kind in Enum.GetValues<LSystemCurveKind>())
        {
            var curveRules2 = new HashSet<string>();
            var curveRules3 = new HashSet<string>();
            foreach (int seed in Enumerable.Range(0, 12))
            {
                var options = new LSystemRandomizationSettings { Family = LSystemShapeFamily.Curves,
                    CurveKind = kind, Seed = seed, RuleComplexity = seed % 4 + 1, StemLength = 4,
                    Detail = seed % 5 + 1, Symmetric = seed % 2 == 0 };
                var a = LSystemRandomizer.Create2D(options, planar, false, CancellationToken.None);
                var b = LSystemRandomizer.Create3D(options, spatial, false, CancellationToken.None);
                Check(a.Segments is >= 2 and <= 50_000 && b.Segments is >= 2 and <= 20_000, "Curve budgets.");
                curveRules2.Add(a.Planar!.RulesText); curveRules3.Add(b.Spatial!.RulesText);
                Check(a.Planar.AngleDegrees >= options.AngleMinimum && a.Planar.AngleDegrees <= options.AngleMaximum &&
                    b.Spatial.Yaw >= options.AngleMinimum && b.Spatial.Yaw <= options.AngleMaximum, "Curve angle ranges.");
            }
            Check(curveRules2.Count >= 3 && curveRules3.Count >= 3, $"Structural variety within {kind}.");
        }
        var constrained = new LSystemRandomizationSettings { AngleMinimum = 36, AngleMaximum = 36,
            PitchMinimum = 47, PitchMaximum = 47, RollMinimum = 53, RollMaximum = 53,
            Radius = .23, BranchTaper = .61, StepDecay = .54, RuleComplexity = 4, StemLength = 4 };
        var constrained3 = LSystemRandomizer.Create3D(constrained, spatial, false, CancellationToken.None).Spatial!;
        Check(constrained3.Yaw == 36 && constrained3.Pitch == 47 && constrained3.Roll == 53 &&
            constrained3.Radius == .23 && constrained3.BranchTaper == .61 && constrained3.StepDecay == .54, "Spatial proportions and ranges.");
        Check(LSystemRandomizer.Create2D(constrained, planar, false, CancellationToken.None).Planar!.AngleDegrees == 36,
            "Planar angle range.");
        foreach (var invalid in new[] { constrained with { AngleMinimum = 80, AngleMaximum = 20 },
            constrained with { PitchMinimum = double.NaN }, constrained with { Radius = 0 } })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Invalid generator ranges are rejected.");
        }
        foreach (var preset in LSystemPresets.All)
        {
            var result = LSystemRandomizer.Create2D(new() { Seed = 72 }, preset.Definition, true, CancellationToken.None);
            Check(result.Planar!.Axiom == preset.Definition.Axiom && result.Planar.RulesText != preset.Definition.RulesText &&
                result.Planar.StartColor == preset.Definition.StartColor, "2D nearby mutation preserves axiom/style and changes rules.");
        }
        foreach (var preset in LSystem3DPresets.All)
        {
            var result = LSystemRandomizer.Create3D(new() { Seed = 72 }, preset.Settings, true, CancellationToken.None);
            Check(result.Spatial!.Axiom == preset.Settings.Axiom && result.Spatial.RulesText != preset.Settings.RulesText, "3D nearby rules mutation.");
        }
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool cancelled = false;
            try { LSystemRandomizer.Create3D(new(), spatial, false, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Generation cancellation.");
        }
        using var renderer = new Fractal3DRenderer();
        var curveImages = new HashSet<string>();
        foreach (var kind in Enum.GetValues<LSystemCurveKind>().Where(k => k != LSystemCurveKind.Mixed))
        {
            var options = new LSystemRandomizationSettings { Family = LSystemShapeFamily.Curves,
                CurveKind = kind, Seed = 73, Detail = 3 };
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.LSystem3D);
            state.LSystem = LSystemRandomizer.Create3D(options, spatial, false, CancellationToken.None).Spatial!;
            var image = await renderer.RenderAsync(state, 400, 400, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image), 400, 400), $"Blank curve {kind}.");
            curveImages.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(image))));
            WriteLSystemPng(Path.Combine(output, $"curve-3d-{kind}.png"), image);
            var d = LSystemRandomizer.Create2D(options, planar, false, CancellationToken.None).Planar!;
            var scene = LSystemEngine.BuildScene(d, CancellationToken.None);
            var pixels = LSystemRasterizer.Render(scene, d, 400, 400, scene.Segments.Count, 1, 0, 0, CancellationToken.None)!;
            WriteLSystemPng(Path.Combine(output, $"curve-2d-{kind}.png"),
                BitmapSource.Create(400, 400, 96, 96, PixelFormats.Bgra32, null, pixels, 1600));
        }
        Check(curveImages.Count == 5, "The five curve types must produce different images.");
        foreach (var family in Enum.GetValues<LSystemShapeFamily>())
        {
            var options = new LSystemRandomizationSettings { Family = family, Seed = 314, Detail = 3 };
            var d = LSystemRandomizer.Create2D(options, planar, false, CancellationToken.None).Planar!;
            var scene = LSystemEngine.BuildScene(d, CancellationToken.None);
            var pixels = LSystemRasterizer.Render(scene, d, 320, 320, scene.Segments.Count, 1, 0, 0, CancellationToken.None)!;
            Check(pixels.Where((_, i) => i % 4 == 2).Count(v => v != d.BackgroundColor.R) > 100, $"Blank planar family {family}.");
            var bitmap = BitmapSource.Create(320, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 1280);
            WriteLSystemPng(Path.Combine(output, $"2d-{family}.png"), bitmap);
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.LSystem3D);
            state.LSystem = LSystemRandomizer.Create3D(options, spatial, false, CancellationToken.None).Spatial!;
            var image = await renderer.RenderAsync(state, 256, 256, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image), 256, 256), $"Blank spatial family {family}.");
            WriteLSystemPng(Path.Combine(output, $"3d-{family}.png"), image);
        }
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var two = new LSystemWindow();
        var three = new Fractal3DWindow(Fractal3DKind.LSystem3D);
        try
        {
            foreach (Window owner in new Window[] { two, three })
            {
                owner.WindowStartupLocation = WindowStartupLocation.Manual;
                owner.Left = -10000; owner.Top = -10000; owner.ShowInTaskbar = false; owner.Show();
            }
            var editor = (LSystem3DEditor)three.FindName("LSystemEditor");
            LSystemRandomizerPanel Panel(object owner) => (LSystemRandomizerPanel)owner.GetType()
                .GetField("Randomizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
            var panel3 = Panel(editor);
            var panel2 = Panel(two);
            var before3 = editor.Capture();
            string before2 = ((TextBox)two.FindName("RulesBox")).Text;
            foreach (var panel in new[] { panel2, panel3 })
            {
                ((CheckBox)panel.FindName("FreshSeedBox")).IsChecked = false;
                ((TextBox)panel.FindName("SeedBox")).Text = "314";
                ((Button)panel.FindName("NewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var button = (Button)panel.FindName("NewButton");
                for (int i = 0; i < 500 && !button.IsEnabled; i++) await Task.Delay(10);
                Check(button.IsEnabled && ((TextBlock)panel.FindName("ResultText")).Text.Contains("314"), "WPF generate action.");
                Check(((Button)panel.FindName("UndoButton")).IsEnabled, "Generated form must be undoable.");
            }
            foreach (var (owner, buttonOwner, panel, windowType, name) in new[]
            {
                (two as Window, two as FrameworkElement, panel2, typeof(LSystemRandomizerWindow), "randomizer-window-2d"),
                (three as Window, editor as FrameworkElement, panel3, typeof(LSystem3DRandomizerWindow), "randomizer-window-3d")
            })
            {
                ((Button)buttonOwner.FindName("RandomizerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = owner.OwnedWindows.Cast<Window>().Single(w => w.GetType() == windowType);
                Check(panel.OpenWindow(owner) == dialog, "Repeated launch must reuse the generator window.");
                Check(panel.Content != null && dialog.Content == panel, "Generator settings live in the separate window.");
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(800, 720)); content.Arrange(new Rect(0, 0, 800, 720)); content.UpdateLayout();
                var shot = new RenderTargetBitmap(800, 720, 96, 96, PixelFormats.Pbgra32); shot.Render(content);
                WriteLSystemPng(Path.Combine(output, name + ".png"), shot);
                dialog.Close();
                var reopened = panel.OpenWindow(owner);
                Check(((TextBox)panel.FindName("SeedBox")).Text == "314", "Reopening must preserve generator settings.");
                reopened.Close();
            }
            Check(editor.Capture() != before3 && ((TextBox)two.FindName("RulesBox")).Text != before2, "Generator must fill both editors.");
            var state = three.CaptureState("generated");
            ((Image)three.FindName("CanvasImage")).Source = await renderer.RenderAsync(state, 800, 740, null, CancellationToken.None);
            var generated = LSystemRandomizer.Create2D(new() { Seed = 314 }, planar, false, CancellationToken.None).Planar!;
            var p = LSystemEngine.BuildScene(generated, CancellationToken.None);
            var bytes = LSystemRasterizer.Render(p, generated, 800, 740, p.Segments.Count, 1, 0, 0, CancellationToken.None)!;
            ((Image)two.FindName("CanvasImage")).Source = BitmapSource.Create(800, 740, 96, 96, PixelFormats.Bgra32, null, bytes, 3200);
            foreach (var (window, name) in new[] { (two as Window, "generator-2d"), (three as Window, "generator-3d") })
            {
                var root = (FrameworkElement)window.Content; root.Measure(new Size(1280, 800)); root.Arrange(new Rect(0, 0, 1280, 800)); root.UpdateLayout();
                var shot = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32); shot.Render(root);
                WriteLSystemPng(Path.Combine(output, name + ".png"), shot);
            }
            ((Button)panel2.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)panel3.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture() == before3 && ((TextBox)two.FindName("RulesBox")).Text == before2, "Exact undo in both windows.");
            Check(((Button)two.FindName("SavesButton")).Content.ToString() == "Менеджер сохранений", "2D save manager button.");
            var saved = two.CaptureState("L-system round trip");
            saved.ViewZoom = 2.3; saved.PanX = .12; saved.PanY = -.08;
            saved.AnimationDurationSeconds = 9.5;
            saved.Definition.RulesText = "F → F+F--F+F";
            saved.Definition.Axiom = "F"; saved.Definition.Depth = 3;
            saved.Definition.StartColor = Colors.Coral;
            var store = new LSystemSaveStore();
            store.Save(saved);
            var loaded = store.Load().Single();
            Check(JsonSerializer.Serialize(saved, JsonOptionsFactory.Create()) == JsonSerializer.Serialize(loaded, JsonOptionsFactory.Create()),
                "2D saved grammar, colors and viewport JSON round trip.");
            two.LoadState(loaded);
            var busy = typeof(LSystemWindow).GetField("_isBuilding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            for (int i = 0; i < 500 && (bool)busy.GetValue(two)!; i++) await Task.Delay(10);
            var restored = two.CaptureState(loaded.SaveName);
            restored.Timestamp = loaded.Timestamp;
            Check(JsonSerializer.Serialize(restored, JsonOptionsFactory.Create()) == JsonSerializer.Serialize(loaded, JsonOptionsFactory.Create()),
                "2D window restores full saved parameters and viewport.");
            var originalPreview = await two.RenderStatePreviewAsync(saved, 320, 240, CancellationToken.None);
            var restoredPreview = await two.RenderStatePreviewAsync(loaded, 320, 240, CancellationToken.None);
            Check(Pixels(originalPreview).SequenceEqual(Pixels(restoredPreview)), "2D save preview reproduces saved image.");
            WriteLSystemPng(Path.Combine(output, "2d-saved-state.png"), restoredPreview);
        }
        finally { two.Close(); three.Close(); }
        await VerifyLSystemCancellationRaceAsync();
        Console.WriteLine($"PASS (lsystemrandom): seeded families and five curve types, structural variety, parameter ranges, budgets, preset mutations, cancellation, CPU/GPU images, separate generator windows and reopening, undo, saves, overlapping builds and close during build. Images: {output}");
    }

    private static async Task VerifyLSystemCancellationRaceAsync()
    {
        var window = new LSystemWindow();
        bool closed = false;
        try
        {
            ((TextBox)window.FindName("AxiomBox")).Text = "F";
            ((TextBox)window.FindName("RulesBox")).Text = "F → FF";
            var depth = (TextBox)window.FindName("DepthBox");
            depth.Text = "19";
            var method = typeof(LSystemWindow).GetMethod("BuildSceneAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var field = typeof(LSystemWindow).GetField("_buildCts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Task Start() => (Task)method.Invoke(window, [false])!;
            Task first = Start();
            var firstSource = (CancellationTokenSource)field.GetValue(window)!;
            Task second = Start();
            Check(firstSource.Token.IsCancellationRequested, "A superseded worker must be cancelled without disposing its source early.");
            depth.Text = "3";
            Task third = Start();
            await Task.WhenAll(first, second, third);
            Check(((TextBlock)window.FindName("StatusText")).Text.StartsWith("Готово"), "Latest overlapping build must finish successfully.");
            Check(field.GetValue(window) is null, "Completed build must release its shared reference.");
            depth.Text = "19";
            Task closing = Start();
            var closingSource = (CancellationTokenSource)field.GetValue(window)!;
            window.Close(); closed = true;
            Check(closingSource.Token.IsCancellationRequested, "Closing must cancel while the source remains alive for the worker.");
            await closing;
            bool disposed = false;
            try { _ = closingSource.Token; } catch (ObjectDisposedException) { disposed = true; }
            Check(disposed && field.GetValue(window) is null, "The owning operation must dispose its source after completion.");
        }
        finally { if (!closed) window.Close(); }
    }
}
